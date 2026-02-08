using System.Collections.Concurrent;
using System.Text.Json;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.Office.Events;
using CopilotAgent.Office.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Core orchestration service — owns the Manager state machine and iteration loop.
/// Phase 1: Uses hardcoded stubs for LLM calls (plan, event fetching, aggregation).
/// Phase 2 (Step 18): Replaces stubs with real LLM-driven Manager behavior.
/// </summary>
public sealed class OfficeManagerService : IOfficeManagerService
{
    private readonly ICopilotService _copilotService;
    private readonly CopilotSdkService? _sdkService;
    private readonly IReasoningStream _reasoningStream;
    private readonly IOfficeEventLog _eventLog;
    private readonly IIterationScheduler _scheduler;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<OfficeManagerService> _logger;

    private OfficeConfig? _config;
    private ManagerContext _context = new();
    private CancellationTokenSource? _runCts;
    private TaskCompletionSource? _pauseGate;
    private Task? _iterationLoopTask;
    private readonly ConcurrentBag<string> _injectedInstructions = [];
    private readonly SemaphoreSlim _managerLock = new(1, 1);
    private int _checkIntervalMinutes;

    // Manager LLM session — persistent session for plan/task/aggregation calls
    private Session? _managerSession;

    // Clarification gate — set when Manager asks user a question, awaited until user responds
    private TaskCompletionSource<string>? _clarificationGate;

    // Stored delegate for countdown tick to enable proper unsubscription (Bug #8 fix)
    private Action<RestCountdownEvent>? _countdownTickHandler;

    // Stored delegate for SDK session event handler (tool call detection for Manager)
    private EventHandler<SdkSessionEventArgs>? _sdkEventHandler;

    // JSON serializer options for structured LLM responses
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <inheritdoc />
    public ManagerPhase CurrentPhase => _context.CurrentPhase;

    /// <inheritdoc />
    public int CurrentIteration => _context.CurrentIteration;

    /// <inheritdoc />
    public bool IsRunning => _context.CurrentPhase is not (ManagerPhase.Idle or ManagerPhase.Stopped or ManagerPhase.Error);

    /// <inheritdoc />
    public bool IsWaitingForClarification => _context.CurrentPhase == ManagerPhase.Clarifying;

    /// <inheritdoc />
    public bool IsPlanAwaitingApproval => _context.CurrentPhase == ManagerPhase.AwaitingApproval;

    /// <inheritdoc />
    public string? CurrentPlan => _context.ApprovedPlan;

    /// <inheritdoc />
    public event Action<OfficeEvent>? OnEvent;

    public OfficeManagerService(
        ICopilotService copilotService,
        IReasoningStream reasoningStream,
        IOfficeEventLog eventLog,
        IIterationScheduler scheduler,
        ILoggerFactory loggerFactory)
    {
        _copilotService = copilotService;
        _sdkService = copilotService as CopilotSdkService;
        _reasoningStream = reasoningStream;
        _eventLog = eventLog;
        _scheduler = scheduler;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<OfficeManagerService>();
    }

    /// <inheritdoc />
    public async Task StartAsync(OfficeConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (IsRunning)
        {
            _logger.LogWarning("Office is already running. Stop first before starting a new run.");
            return;
        }

        _config = config;
        _checkIntervalMinutes = config.CheckIntervalMinutes;
        _context = new ManagerContext { Config = config, StartedAt = DateTimeOffset.UtcNow };
        _eventLog.Clear();
        _runCts = new CancellationTokenSource();

        RaiseEvent(new RunStartedEvent
        {
            Config = config,
            Description = $"Office started with objective: {config.Objective}"
        });

        _logger.LogInformation("Office started. Objective: {Objective}", config.Objective);

        // Unsubscribe any stale SDK event handler from a previous run
        UnsubscribeManagerSdkEvents();

        // Create manager session with full tool/skill configuration
        _managerSession = new Session
        {
            SessionId = $"office-manager-{Guid.NewGuid():N}",
            DisplayName = "Office Manager",
            ModelId = config.ManagerModel,
            WorkingDirectory = config.WorkspacePath,
            SystemPrompt = config.ManagerSystemPrompt ?? BuildDefaultManagerSystemPrompt(config),
            AutonomousMode = new AutonomousModeSettings { AllowAll = true },

            // Issue #5/#6: Propagate MCP servers and skills so the Manager
            // session can use configured tools and skills for planning/aggregation.
            EnabledMcpServers = config.EnabledMcpServers,
            DisabledSkills = config.DisabledSkills,
            SkillDirectories = config.SkillDirectories
        };

        // Subscribe to SDK session events for Manager tool call commentary
        SubscribeManagerSdkEvents();

        // Transition to Planning
        TransitionTo(ManagerPhase.Planning);
        EmitActivityStatus(ActivityStatusType.ManagerThinking, "Manager thinking...");

        // Generate plan (stub for now — Step 18 replaces with LLM)
        var plan = await GeneratePlanAsync(ct).ConfigureAwait(false);
        _context.ApprovedPlan = plan;

        if (config.RequirePlanApproval)
        {
            TransitionTo(ManagerPhase.AwaitingApproval);
            EmitActivityStatus(ActivityStatusType.AwaitingApproval, "Awaiting plan approval...");
            RaiseEvent(new ChatMessageEvent
            {
                Message = new OfficeChatMessage
                {
                    Role = OfficeChatRole.Manager,
                    SenderName = "Manager",
                    Content = $"📋 **Proposed Plan**\n\n{plan}\n\n*Please approve or reject this plan.*",
                    AccentColor = OfficeColorScheme.ManagerColor,
                    IsCollapsible = true
                },
                Description = "Manager proposed a plan"
            });
        }
        else
        {
            await BeginIterationLoopAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task ApprovePlanAsync(CancellationToken ct = default)
    {
        if (_context.CurrentPhase != ManagerPhase.AwaitingApproval)
        {
            _logger.LogWarning("Cannot approve plan — not in AwaitingApproval phase (current: {Phase})", _context.CurrentPhase);
            return Task.CompletedTask;
        }

        // Issue #6 fix: Attribution comes from Manager acknowledging the approval, not User echoing it.
        // The ViewModel already shows the user's click action; the chat message should be the Manager's reaction.
        RaiseEvent(new ChatMessageEvent
        {
            Message = new OfficeChatMessage
            {
                Role = OfficeChatRole.Manager,
                SenderName = "Manager",
                Content = "✅ Plan approved. Starting execution.",
                AccentColor = OfficeColorScheme.ManagerColor
            },
            Description = "Manager acknowledged plan approval"
        });

        return BeginIterationLoopAsync();
    }

    /// <inheritdoc />
    public async Task RejectPlanAsync(string? feedback = null, CancellationToken ct = default)
    {
        if (_context.CurrentPhase != ManagerPhase.AwaitingApproval)
        {
            _logger.LogWarning("Cannot reject plan — not in AwaitingApproval phase");
            return;
        }

        RaiseEvent(new ChatMessageEvent
        {
            Message = new OfficeChatMessage
            {
                Role = OfficeChatRole.User,
                SenderName = "User",
                Content = $"❌ Plan rejected.{(feedback is not null ? $" Feedback: {feedback}" : "")}",
                AccentColor = OfficeColorScheme.UserColor
            },
            Description = "User rejected the plan"
        });

        TransitionTo(ManagerPhase.Planning);
        EmitActivityStatus(ActivityStatusType.ManagerThinking, "Manager revising plan...");

        // Re-generate plan with feedback (stub — Step 18 uses LLM)
        var plan = await GeneratePlanAsync(ct).ConfigureAwait(false);
        _context.ApprovedPlan = plan;

        TransitionTo(ManagerPhase.AwaitingApproval);
        EmitActivityStatus(ActivityStatusType.AwaitingApproval, "Awaiting plan approval...");

        RaiseEvent(new ChatMessageEvent
        {
            Message = new OfficeChatMessage
            {
                Role = OfficeChatRole.Manager,
                SenderName = "Manager",
                Content = $"📋 **Revised Plan**\n\n{plan}\n\n*Please approve or reject this plan.*",
                AccentColor = OfficeColorScheme.ManagerColor,
                IsCollapsible = true
            },
            Description = "Manager revised the plan"
        });
    }

    /// <inheritdoc />
    public Task InjectInstructionAsync(string instruction, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);

        _injectedInstructions.Add(instruction);

        RaiseEvent(new InstructionInjectedEvent
        {
            Instruction = instruction,
            IterationNumber = _context.CurrentIteration,
            Description = $"Instruction injected: {instruction[..Math.Min(50, instruction.Length)]}..."
        });

        _logger.LogInformation("Instruction injected: {Instruction}", instruction);

        // Issue #4: If currently resting, wake up immediately so the next iteration
        // absorbs this instruction without waiting for the full rest period.
        if (_context.CurrentPhase == ManagerPhase.Resting)
        {
            _logger.LogInformation("Interrupting rest period — instruction received during rest");
            EmitCommentary(CommentaryType.System, "System",
                "⚡ Rest interrupted — new instruction received, starting next iteration...");
            _scheduler.CancelRest();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RespondToClarificationAsync(string response, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(response);

        if (_context.CurrentPhase != ManagerPhase.Clarifying || _clarificationGate is null)
        {
            _logger.LogWarning("Not in Clarifying phase — ignoring response");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Clarification response received: {Response}", response);

        // Issue fix: Do NOT echo the user's message here — the ViewModel already adds it
        // to the chat before calling this method. Instead, emit a Manager acknowledgment
        // so the user sees the Manager is processing their input.
        RaiseEvent(new ChatMessageEvent
        {
            Message = new OfficeChatMessage
            {
                Role = OfficeChatRole.Manager,
                SenderName = "Manager",
                Content = "📝 Received your input. Analyzing... give me a moment.",
                AccentColor = OfficeColorScheme.ManagerColor
            },
            Description = "Manager acknowledged clarification response"
        });

        // Bug #1 fix: Transition back to Planning before resolving the gate,
        // so the awaiter in RequestClarificationAsync resumes in the correct phase.
        TransitionTo(ManagerPhase.Planning);

        _clarificationGate.TrySetResult(response);
        _clarificationGate = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PauseAsync(CancellationToken ct = default)
    {
        if (_context.CurrentPhase == ManagerPhase.Paused)
        {
            return Task.CompletedTask;
        }

        _pauseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TransitionTo(ManagerPhase.Paused);
        _logger.LogInformation("Office paused");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ResumeAsync(CancellationToken ct = default)
    {
        if (_context.CurrentPhase != ManagerPhase.Paused)
        {
            return Task.CompletedTask;
        }

        _pauseGate?.TrySetResult();
        _pauseGate = null;
        _logger.LogInformation("Office resumed");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsRunning)
        {
            return;
        }

        _logger.LogInformation("Stopping Office...");

        // Cancel the iteration loop
        _runCts?.Cancel();
        _pauseGate?.TrySetResult();
        _scheduler.CancelRest();

        // Bug #4 fix: Cancel clarification gate so RequestClarificationAsync unblocks
        _clarificationGate?.TrySetCanceled();
        _clarificationGate = null;

        // Wait for loop to finish
        if (_iterationLoopTask is not null)
        {
            try
            {
                await _iterationLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _context.EndedAt = DateTimeOffset.UtcNow;
        TransitionTo(ManagerPhase.Stopped);
        EmitActivityStatus(ActivityStatusType.Idle, string.Empty);

        RaiseEvent(new RunStoppedEvent
        {
            Reason = "User requested stop",
            Description = "Office stopped by user"
        });

        // Unsubscribe from SDK events before terminating the session
        UnsubscribeManagerSdkEvents();

        // Cleanup manager session
        if (_managerSession is not null)
        {
            try
            {
                _copilotService.TerminateSessionProcess(_managerSession.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to terminate manager session");
            }
        }

        _runCts?.Dispose();
        _runCts = null;
    }

    /// <inheritdoc />
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await StopAsync(ct).ConfigureAwait(false);

        _context = new ManagerContext();
        _eventLog.Clear();
        while (_injectedInstructions.TryTake(out _)) { }

        TransitionTo(ManagerPhase.Idle);
        EmitActivityStatus(ActivityStatusType.Idle, string.Empty);
        _logger.LogInformation("Office reset to Idle");
    }

    /// <inheritdoc />
    public void UpdateCheckInterval(int minutes)
    {
        _checkIntervalMinutes = Math.Max(1, minutes);
        _logger.LogInformation("Check interval updated to {Minutes} minutes", _checkIntervalMinutes);
    }

    /// <inheritdoc />
    public void SkipRest()
    {
        if (_context.CurrentPhase != ManagerPhase.Resting)
        {
            _logger.LogWarning("Cannot skip rest — not in Resting phase (current: {Phase})", _context.CurrentPhase);
            return;
        }

        _logger.LogInformation("Skipping rest period — user requested");
        EmitCommentary(CommentaryType.System, "System", "⏭️ Rest skipped — starting next iteration...");
        _scheduler.CancelRest();
    }

    // ─── Private: Iteration Loop ────────────────────────────

    private Task BeginIterationLoopAsync()
    {
        // Bug #2 fix: Explicitly transition out of AwaitingApproval before starting the loop
        if (_context.CurrentPhase == ManagerPhase.AwaitingApproval)
        {
            TransitionTo(ManagerPhase.FetchingEvents);
        }

        _iterationLoopTask = Task.Run(() => RunIterationLoopAsync(_runCts!.Token));
        return Task.CompletedTask;
    }

    private async Task RunIterationLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Check for pause
                if (_pauseGate is not null)
                {
                    await _pauseGate.Task.WaitAsync(ct).ConfigureAwait(false);
                }

                _context.CurrentIteration++;
                var iterationStart = DateTimeOffset.UtcNow;

                _logger.LogInformation("=== Iteration {N} starting ===", _context.CurrentIteration);

                // Phase: FetchingEvents
                TransitionTo(ManagerPhase.FetchingEvents);
                EmitActivityStatus(ActivityStatusType.ManagerThinking, "Manager thinking...");

                // Absorb injected instructions once (Bug #10 fix: was being drained twice)
                var instructions = AbsorbInjectedInstructions();
                EmitCommentary(CommentaryType.ManagerThinking, "Manager", "🔍 Checking for events and generating tasks...");

                var tasks = await FetchEventsAndCreateTasksAsync(instructions, ct).ConfigureAwait(false);

                IReadOnlyList<AssistantResult> results;

                if (tasks.Count == 0)
                {
                    _logger.LogInformation("No tasks for iteration {N}", _context.CurrentIteration);
                    EmitCommentary(CommentaryType.System, "System", "No tasks generated for this iteration.");
                    results = Array.Empty<AssistantResult>();
                }
                else
                {
                    // Phase: Scheduling
                    TransitionTo(ManagerPhase.Scheduling);
                    EmitCommentary(CommentaryType.SchedulingDecision, "Manager",
                        $"📋 Scheduling {tasks.Count} tasks for execution");

                    // Phase: Executing
                    TransitionTo(ManagerPhase.Executing);
                    EmitActivityStatus(ActivityStatusType.Delegated,
                        $"Manager delegated to {tasks.Count} assistant{(tasks.Count > 1 ? "s" : "")}...",
                        totalDispatched: tasks.Count);

                    var pool = new AssistantPool(
                        _copilotService,
                        _loggerFactory.CreateLogger<AssistantPool>(),
                        _loggerFactory);

                    // Wire pool events to our event system
                    pool.OnAssistantEvent += evt => { RaiseEvent(evt); _eventLog.Log(evt); };
                    pool.OnSchedulingEvent += evt => { RaiseEvent(evt); _eventLog.Log(evt); };
                    pool.OnCommentaryEvent += evt => { RaiseEvent(evt); _eventLog.Log(evt); };

                    results = await pool.ExecuteTasksAsync(tasks, _config!, ct).ConfigureAwait(false);
                }

                // Phase: Aggregating — ALWAYS runs, even when no tasks were generated.
                // The iteration is NOT complete until the Manager has responded to the user
                // with an aggregation report. This ensures Manager LLM streaming finishes
                // before the rest timer starts (fixes rest-timer-during-streaming bug).
                TransitionTo(ManagerPhase.Aggregating);
                EmitActivityStatus(ActivityStatusType.ManagerAggregating, "Manager preparing response...");
                EmitCommentary(CommentaryType.Aggregation, "Manager", "📊 Aggregating results...");

                var summary = await AggregateResultsAsync(results, ct).ConfigureAwait(false);

                // Build iteration report
                var report = new IterationReport
                {
                    IterationNumber = _context.CurrentIteration,
                    StartedAt = iterationStart,
                    CompletedAt = DateTimeOffset.UtcNow,
                    TasksDispatched = tasks.Count,
                    TasksSucceeded = results.Count(r => r.Success),
                    TasksFailed = results.Count(r => !r.Success),
                    TasksCancelled = results.Count(r => r.ErrorMessage == "Task was cancelled"),
                    Results = results,
                    AggregatedSummary = summary,
                    InjectedInstructions = instructions,
                    SchedulingDecisions = _eventLog
                        .GetByIteration(_context.CurrentIteration)
                        .OfType<SchedulingEvent>()
                        .Select(e => e.Decision)
                        .ToList()
                };

                _context.IterationReports.Add(report);
                _context.TotalTasksCompleted += report.TasksSucceeded;
                _context.TotalTasksFailed += report.TasksFailed;

                RaiseEvent(new IterationCompletedEvent
                {
                    Report = report,
                    IterationNumber = _context.CurrentIteration,
                    Description = $"Iteration {_context.CurrentIteration}: {report.TasksSucceeded}/{report.TasksDispatched} succeeded"
                });

                // Post aggregation summary to chat — Manager's response marks iteration as complete
                RaiseEvent(new ChatMessageEvent
                {
                    Message = new OfficeChatMessage
                    {
                        Role = OfficeChatRole.Manager,
                        SenderName = "Manager",
                        Content = $"📊 **Iteration {_context.CurrentIteration} Summary**\n\n{summary}",
                        IterationNumber = _context.CurrentIteration,
                        AccentColor = OfficeColorScheme.ManagerColor,
                        IsCollapsible = true
                    },
                    IterationNumber = _context.CurrentIteration,
                    Description = $"Iteration {_context.CurrentIteration} summary"
                });

                _logger.LogInformation("=== Iteration {N} complete — entering rest ===", _context.CurrentIteration);

                // Emit completion marker so the UI can render the summary before phase changes
                EmitCommentary(CommentaryType.System, "Manager",
                    $"✅ Iteration {_context.CurrentIteration} complete");

                // Yield to allow the WPF Dispatcher to process queued events (ChatMessageEvent,
                // IterationCompletedEvent, commentary) before the PhaseChangedEvent for Resting
                // overwrites the header. Without this barrier the async InvokeAsync dispatch
                // can reorder the Resting phase-change ahead of the aggregation chat message.
                await Task.Delay(150, ct).ConfigureAwait(false);

                // Phase: Resting — safe to transition now that UI has caught up
                TransitionTo(ManagerPhase.Resting);
                EmitActivityStatus(ActivityStatusType.Resting,
                    $"Resting for {_checkIntervalMinutes} min before next iteration...");

                RaiseEvent(new ChatMessageEvent
                {
                    Message = new OfficeChatMessage
                    {
                        Role = OfficeChatRole.RestCountdown,
                        SenderName = "System",
                        Content = $"⏳ Resting for {_checkIntervalMinutes} minutes before next iteration...",
                        IterationNumber = _context.CurrentIteration,
                        AccentColor = OfficeColorScheme.RestColor
                    },
                    Description = $"Rest period: {_checkIntervalMinutes} minutes"
                });

                // Bug #8 fix: Store delegate reference for proper unsubscription
                // Issue fix: Only log countdown to event log every 60 seconds to reduce noise.
                // UI still gets every tick via RaiseEvent for smooth progress bar updates.
                _countdownTickHandler = tick =>
                {
                    RaiseEvent(tick);

                    // Only log to event log at 60-second intervals (or when rest completes) to avoid bloat
                    if (tick.SecondsRemaining % 60 == 0 || tick.SecondsRemaining <= 0)
                    {
                        _eventLog.Log(tick);
                    }
                };
                _scheduler.OnCountdownTick += _countdownTickHandler;

                await _scheduler.WaitForNextIterationAsync(_checkIntervalMinutes, ct).ConfigureAwait(false);

                // Unwire using the same delegate reference
                _scheduler.OnCountdownTick -= _countdownTickHandler;
                _countdownTickHandler = null;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Iteration loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Iteration loop failed");
            _context.LastError = ex.Message;
            TransitionTo(ManagerPhase.Error);
            EmitActivityStatus(ActivityStatusType.Idle, string.Empty);

            RaiseEvent(new ErrorEvent
            {
                ErrorMessage = ex.Message,
                Exception = ex,
                IterationNumber = _context.CurrentIteration,
                Description = $"Error: {ex.Message}"
            });
        }
    }

    // ─── Private: LLM Call Helper ───────────────────────────

    /// <summary>
    /// Sends a prompt to the Manager LLM, emits an action label commentary,
    /// delegates streaming/reasoning to <see cref="IReasoningStream"/>,
    /// and returns the final complete response text.
    ///
    /// The Manager only cares about:
    ///   1. Announcing what it is about to do (action label)
    ///   2. Getting the final response text for its state machine logic
    ///
    /// All streaming delta extraction and live commentary emission is handled
    /// by <see cref="IReasoningStream"/> (single responsibility).
    /// </summary>
    private async Task<string> SendManagerPromptAsync(
        Session session,
        string prompt,
        CommentaryType actionType,
        string agentName,
        string actionLabel,
        CancellationToken ct)
    {
        // Step 1: Announce what the Manager is about to do
        EmitCommentary(actionType, agentName, actionLabel);

        // Step 2: Delegate streaming to ReasoningStream with delta callback
        // for live word-by-word commentary in the sidebar
        var response = await _reasoningStream.StreamAsync(
            _copilotService.SendMessageStreamingAsync(session, prompt, ct),
            agentName,
            _context.CurrentIteration,
            delta => EmitCommentary(CommentaryType.ManagerThinking, agentName, delta),
            ct).ConfigureAwait(false);

        return response;
    }


    // ─── Private: LLM-Driven Manager Intelligence ──────────

    private async Task<string> GeneratePlanAsync(CancellationToken ct)
    {
        if (_managerSession is null || _config is null)
        {
            return BuildFallbackPlan();
        }

        var previousReports = BuildPreviousIterationsSummary();
        var injectedContext = _injectedInstructions.IsEmpty
            ? ""
            : $"\n\n## User Instructions\n{string.Join("\n- ", _injectedInstructions)}";

        var prompt = $"""
            You are the Manager agent. Create an execution plan for the following objective.
            
            ## Objective
            {_config.Objective}
            
            ## Workspace
            {_config.WorkspacePath}
            
            ## Constraints
            - Maximum {_config.MaxAssistants} concurrent assistant agents
            - Check interval: {_config.CheckIntervalMinutes} minutes
            - Each assistant gets a single focused task with a clear prompt
            {injectedContext}
            {previousReports}
            
            ## Instructions
            Produce a clear, numbered execution plan in Markdown format.
            Focus on actionable steps that assistant agents can execute independently.
            ## Vague Objective Detection
            CRITICAL: Before creating a plan, evaluate whether the objective is specific enough to act on.
            An objective is TOO VAGUE if it:
            - Is a greeting or casual message (e.g., "Hi", "Hello", "Hey there")
            - Lacks any actionable intent (e.g., "test", "help", "something")
            - Has no clear deliverable or measurable outcome
            - Is fewer than 5 words and doesn't describe a concrete task
            
            If the objective is vague, ambiguous, or lacks sufficient detail to create a meaningful plan,
            you MUST start your response with [CLARIFICATION_NEEDED] followed by a helpful question
            that guides the user toward providing a specific, actionable objective.
            
            If the objective IS clear enough, provide the plan directly.
            Do NOT create a generic/placeholder plan for vague objectives — always ask for clarification instead.
            """;

        try
        {
            var content = await SendManagerPromptAsync(
                _managerSession, prompt,
                CommentaryType.ManagerThinking, "Manager",
                "🧠 Generating execution plan...", ct).ConfigureAwait(false);

            // Check if the Manager needs clarification
            if (content.StartsWith("[CLARIFICATION_NEEDED]", StringComparison.OrdinalIgnoreCase))
            {
                var question = content["[CLARIFICATION_NEEDED]".Length..].Trim();
                var userAnswer = await RequestClarificationAsync(question, ct).ConfigureAwait(false);

                // Re-generate plan with the clarification
                var followUp = $"The user answered your question: \"{userAnswer}\"\n\nNow produce the execution plan.";
                var retryContent = await SendManagerPromptAsync(
                    _managerSession, followUp,
                    CommentaryType.ManagerThinking, "Manager",
                    "🧠 Refining plan with your clarification...", ct).ConfigureAwait(false);
                return retryContent;
            }

            return content;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM plan generation failed — using fallback plan");
            return BuildFallbackPlan();
        }
    }

    private async Task<IReadOnlyList<AssistantTask>> FetchEventsAndCreateTasksAsync(
        IReadOnlyList<string> absorbedInstructions, CancellationToken ct)
    {
        if (_managerSession is null || _config is null)
        {
            return BuildFallbackTasks();
        }

        var previousReport = _context.IterationReports.Count > 0
            ? $"\n\n## Previous Iteration Summary\n{_context.IterationReports[^1].AggregatedSummary}"
            : "";

        // Bug #10 fix: Use pre-absorbed instructions instead of draining again
        var injectedContext = "";
        if (absorbedInstructions.Count > 0)
        {
            injectedContext = $"\n\n## New User Instructions\n{string.Join("\n- ", absorbedInstructions)}";
        }

        var prompt = $$"""
            You are the Manager agent. It is iteration {{_context.CurrentIteration}}.
            
            ## Objective
            {{_config.Objective}}
            
            ## Workspace
            {{_config.WorkspacePath}}
            
            ## Current Plan
            {{_context.ApprovedPlan}}
            {{previousReport}}
            {{injectedContext}}
            
            ## Instructions
            Based on the objective, plan, and any previous results, generate a list of tasks 
            for your assistant agents to execute in this iteration.
            
            Respond ONLY with a JSON array of task objects. Each task must have:
            - "title": short descriptive title
            - "prompt": detailed instructions for the assistant agent
            - "priority": integer (0 = highest priority)
            
            Example:
            [
              {"title": "Check for new issues", "prompt": "List open issues in the repository...", "priority": 0},
              {"title": "Review PR #42", "prompt": "Review the changes in pull request...", "priority": 1}
            ]
            
            If there is nothing to do this iteration, return an empty array: []
            Do not include any text outside the JSON array.
            """;

        try
        {
            var content = await SendManagerPromptAsync(
                _managerSession, prompt,
                CommentaryType.ManagerThinking, "Manager",
                "🔍 Analyzing workspace and creating tasks...", ct).ConfigureAwait(false);
            return ParseTasksFromLlmResponse(content);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM task generation failed — using fallback tasks");
            return BuildFallbackTasks();
        }
    }

    private async Task<string> AggregateResultsAsync(IReadOnlyList<AssistantResult> results, CancellationToken ct)
    {
        // Only fall back if we have no LLM session at all.
        // When results.Count == 0 we STILL call the LLM so it can explain why no work
        // was generated and provide a meaningful summary. This also ensures the LLM
        // streaming await blocks the iteration loop, preventing the Resting phase
        // from starting before the Manager's response has been fully rendered.
        if (_managerSession is null)
        {
            return BuildFallbackAggregation(results);
        }

        // Bug #1 fix: Provide clean summaries without verbose metadata (task IDs, durations)
        // so the LLM focuses on synthesis rather than echoing structural noise.
        string completedWorkSection;

        if (results.Count == 0)
        {
            completedWorkSection = "No tasks were generated or executed this iteration.";
        }
        else
        {
            var succeeded = results.Where(r => r.Success && !string.IsNullOrWhiteSpace(r.Content)).ToList();
            var failed = results.Where(r => !r.Success).ToList();

            var summariesText = string.Join("\n\n", succeeded.Select((r, i) =>
                $"- {r.Content}"));

            var failureText = failed.Count > 0
                ? $"\n\n**Failures ({failed.Count}):**\n" + string.Join("\n", failed.Select(r =>
                    $"- {r.ErrorMessage ?? "Unknown error"}"))
                : "";

            completedWorkSection = $"{summariesText}{failureText}";
        }

        var prompt = $"""
            You are the Manager agent. Iteration {_context.CurrentIteration} has completed.
            
            ## Objective
            {_config?.Objective}
            
            ## Current Plan
            {_context.ApprovedPlan}
            
            ## Completed Work
            {completedWorkSection}
            
            ## Instructions
            Synthesize the above into a CONCISE executive summary (max 150 words).
            
            CRITICAL RULES:
            - DO NOT list individual tasks or repeat their summaries verbatim
            - DO NOT include task IDs, durations, or internal metadata
            - If no tasks were executed, explain what you observed and what you plan to do next
            - Focus on INSIGHTS and PATTERNS across results
            - Structure as bullet points: Key Findings → Issues (if any) → Next Steps
            - Be direct and actionable — this summary is shown to end users
            """;

        try
        {
            return await SendManagerPromptAsync(
                _managerSession, prompt,
                CommentaryType.Aggregation, "Manager",
                "📊 Synthesizing results...", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM aggregation failed — using fallback");
            return BuildFallbackAggregation(results);
        }
    }

    // ─── Private: Clarification Flow ────────────────────────

    /// <summary>
    /// Transitions to Clarifying phase, posts the question to chat, 
    /// and waits for the user to respond via RespondToClarificationAsync.
    /// </summary>
    private async Task<string> RequestClarificationAsync(string question, CancellationToken ct)
    {
        TransitionTo(ManagerPhase.Clarifying);
        EmitActivityStatus(ActivityStatusType.ManagerClarifying, "Waiting for your response...");

        RaiseEvent(new ClarificationRequestedEvent
        {
            Question = question,
            IterationNumber = _context.CurrentIteration,
            Description = $"Manager needs clarification: {question[..Math.Min(80, question.Length)]}"
        });

        RaiseEvent(new ChatMessageEvent
        {
            Message = new OfficeChatMessage
            {
                Role = OfficeChatRole.Manager,
                SenderName = "Manager",
                Content = $"❓ **Clarification Needed**\n\n{question}",
                AccentColor = OfficeColorScheme.ManagerColor
            },
            Description = "Manager asked for clarification"
        });

        _clarificationGate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Wait for user response or cancellation
        using var reg = ct.Register(() => _clarificationGate.TrySetCanceled(ct));
        var response = await _clarificationGate.Task.ConfigureAwait(false);

        _logger.LogInformation("Clarification received: {Response}", response);
        return response;
    }

    // ─── Private: LLM Response Parsing ──────────────────────

    private IReadOnlyList<AssistantTask> ParseTasksFromLlmResponse(string llmResponse)
    {
        try
        {
            // Extract JSON array from the response (handle markdown code fences)
            var json = ExtractJsonArray(llmResponse);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("No JSON array found in LLM response, using fallback");
                return BuildFallbackTasks();
            }

            var rawTasks = JsonSerializer.Deserialize<List<LlmTaskDto>>(json, s_jsonOptions);
            if (rawTasks is null || rawTasks.Count == 0)
            {
                return [];
            }

            return rawTasks
                .Where(t => !string.IsNullOrWhiteSpace(t.Title) && !string.IsNullOrWhiteSpace(t.Prompt))
                .Select(t => new AssistantTask
                {
                    Title = t.Title!.Trim(),
                    Prompt = t.Prompt!.Trim(),
                    IterationNumber = _context.CurrentIteration,
                    Priority = t.Priority
                })
                .OrderBy(t => t.Priority)
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse tasks JSON from LLM response");
            return BuildFallbackTasks();
        }
    }

    private static string? ExtractJsonArray(string text)
    {
        // Try to find JSON array, possibly wrapped in ```json ... ``` code fence
        var trimmed = text.Trim();

        // Remove markdown code fences
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }
            if (trimmed.EndsWith("```"))
            {
                trimmed = trimmed[..^3];
            }
            trimmed = trimmed.Trim();
        }

        // Find the first '[' and last ']'
        var start = trimmed.IndexOf('[');
        var end = trimmed.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        return null;
    }

    // ─── Private: Fallback Implementations ──────────────────

    private string BuildFallbackPlan()
    {
        return $"""
            ## Execution Plan for: {_config?.Objective}
            
            1. **Monitor** workspace for changes and events
            2. **Analyze** any new issues, PRs, or code changes
            3. **Execute** relevant tasks based on findings
            4. **Report** results and continue monitoring
            
            *Iterations every {_config?.CheckIntervalMinutes} minutes with up to {_config?.MaxAssistants} concurrent assistants.*
            """;
    }

    private IReadOnlyList<AssistantTask> BuildFallbackTasks()
    {
        return
        [
            new()
            {
                Title = "Check workspace status",
                Prompt = $"Check the current status of the workspace at '{_config?.WorkspacePath ?? "current directory"}'. Report any recent changes, open issues, or pending work.",
                IterationNumber = _context.CurrentIteration,
                Priority = 0
            },
            new()
            {
                Title = "Review recent activity",
                Prompt = $"Review recent activity related to: {_config?.Objective}. Summarize findings and suggest next actions.",
                IterationNumber = _context.CurrentIteration,
                Priority = 1
            }
        ];
    }

    private static string BuildFallbackAggregation(IReadOnlyList<AssistantResult> results)
    {
        var succeeded = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);

        var parts = new List<string>
        {
            $"**Results**: {succeeded} succeeded, {failed} failed",
            ""
        };

        foreach (var result in results)
        {
            var status = result.Success ? "✅" : "❌";
            parts.Add($"{status} **Task {result.TaskId}** ({result.Duration.TotalSeconds:F1}s)");
            parts.Add($"  {result.Content}");
            parts.Add("");
        }

        return string.Join("\n", parts);
    }

    private string BuildPreviousIterationsSummary()
    {
        if (_context.IterationReports.Count == 0)
        {
            return "";
        }

        // Include last 3 iteration summaries for context
        var recent = _context.IterationReports
            .TakeLast(3)
            .Select(r => $"### Iteration {r.IterationNumber}\n{r.AggregatedSummary}");

        return $"\n\n## Previous Iterations\n{string.Join("\n\n", recent)}";
    }

    /// <summary>DTO for deserializing LLM task JSON responses.</summary>
    private sealed class LlmTaskDto
    {
        public string? Title { get; set; }
        public string? Prompt { get; set; }
        public int Priority { get; set; }
    }

    // ─── Private: Helpers ───────────────────────────────────

    private void TransitionTo(ManagerPhase newPhase)
    {
        var previous = _context.CurrentPhase;
        _context.CurrentPhase = newPhase;

        var evt = new PhaseChangedEvent
        {
            PreviousPhase = previous,
            NewPhase = newPhase,
            IterationNumber = _context.CurrentIteration,
            Description = $"Phase: {previous} → {newPhase}"
        };

        _eventLog.Log(evt);
        RaiseEvent(evt);

        _logger.LogDebug("Phase transition: {Previous} → {New}", previous, newPhase);
    }

    private List<string> AbsorbInjectedInstructions()
    {
        var instructions = new List<string>();
        while (_injectedInstructions.TryTake(out var instruction))
        {
            instructions.Add(instruction);
            _context.InjectedInstructions.Add(instruction);
            _logger.LogInformation("Absorbed injected instruction: {Instruction}", instruction);
        }

        return instructions;
    }

    private void EmitCommentary(CommentaryType type, string agentName, string message)
    {
        var commentary = new LiveCommentary
        {
            Type = type,
            AgentName = agentName,
            Message = message,
            IterationNumber = _context.CurrentIteration,
            Emoji = type switch
            {
                CommentaryType.ManagerThinking => "🤔",
                CommentaryType.ManagerEvaluating => "🧐",
                CommentaryType.AssistantStarted => "🚀",
                CommentaryType.AssistantProgress => "⚡",
                CommentaryType.AssistantCompleted => "✅",
                CommentaryType.AssistantError => "❌",
                CommentaryType.SchedulingDecision => "📋",
                CommentaryType.Aggregation => "📊",
                CommentaryType.System => "ℹ️",
                CommentaryType.ToolCallStarted => "🔧",
                CommentaryType.ToolCallCompleted => "✅",
                _ => "💭"
            }
        };

        var evt = new CommentaryEvent
        {
            Commentary = commentary,
            IterationNumber = _context.CurrentIteration,
            Description = $"{agentName}: {message}"
        };

        _eventLog.Log(evt);
        RaiseEvent(evt);
    }

    private void RaiseEvent(OfficeEvent evt)
    {
        try
        {
            OnEvent?.Invoke(evt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in event handler for {EventType}", evt.EventType);
        }
    }

    /// <summary>
    /// Emits an <see cref="ActivityStatusEvent"/> so the ViewModel can drive the live
    /// activity status panel. Called at every meaningful state transition.
    /// </summary>
    private void EmitActivityStatus(
        ActivityStatusType type,
        string message,
        IReadOnlyList<int>? activeIndices = null,
        int totalDispatched = 0)
    {
        var evt = new ActivityStatusEvent
        {
            StatusType = type,
            StatusMessage = message,
            ActiveAssistantIndices = activeIndices ?? [],
            TotalAssistantsDispatched = totalDispatched,
            IterationNumber = _context.CurrentIteration,
            Description = $"Activity: {message}"
        };

        _eventLog.Log(evt);
        RaiseEvent(evt);
    }

    // ─── Private: SDK Event Subscription (Manager Tool Calls) ──

    /// <summary>
    /// Subscribes to <see cref="CopilotSdkService.SessionEventReceived"/> so the Manager
    /// can emit tool call commentary when the LLM invokes tools (e.g., during planning,
    /// task generation, or aggregation). Mirrors the pattern used by <see cref="AssistantAgent"/>.
    /// </summary>
    private void SubscribeManagerSdkEvents()
    {
        if (_sdkService is null || _managerSession is null)
            return;

        _sdkEventHandler = (_, args) => OnManagerSdkSessionEvent(args);
        _sdkService.SessionEventReceived += _sdkEventHandler;

        _logger.LogDebug("Manager subscribed to SDK session events for session {SessionId}",
            _managerSession.SessionId);
    }

    /// <summary>
    /// Unsubscribes the stored SDK event handler to prevent leaks across runs.
    /// </summary>
    private void UnsubscribeManagerSdkEvents()
    {
        if (_sdkService is not null && _sdkEventHandler is not null)
        {
            _sdkService.SessionEventReceived -= _sdkEventHandler;
            _sdkEventHandler = null;
            _logger.LogDebug("Manager unsubscribed from SDK session events");
        }
    }

    /// <summary>
    /// Handles SDK session events for the Manager session, extracting tool call
    /// start/complete events via reflection (same approach as <see cref="AssistantAgent"/>)
    /// and emitting live commentary so the user sees Manager tool usage in real time.
    /// </summary>
    private void OnManagerSdkSessionEvent(SdkSessionEventArgs args)
    {
        if (_managerSession is null || args.SessionId != _managerSession.SessionId)
            return;

        try
        {
            var eventTypeName = args.Event.GetType().Name;

            switch (eventTypeName)
            {
                case "ToolExecutionStartEvent":
                {
                    var data = args.Event.GetType().GetProperty("Data")?.GetValue(args.Event);
                    if (data is not null)
                    {
                        var toolName = data.GetType().GetProperty("ToolName")?.GetValue(data) as string ?? "unknown";
                        EmitCommentary(CommentaryType.ToolCallStarted, "Manager", $"🔧 Calling tool: {toolName}");
                        _logger.LogDebug("Manager tool call started: {ToolName}", toolName);
                    }
                    break;
                }

                case "ToolExecutionCompleteEvent":
                {
                    var data = args.Event.GetType().GetProperty("Data")?.GetValue(args.Event);
                    if (data is not null)
                    {
                        var toolCallId = data.GetType().GetProperty("ToolCallId")?.GetValue(data) as string ?? "unknown";
                        EmitCommentary(CommentaryType.ToolCallCompleted, "Manager", $"✅ Tool call completed ({toolCallId})");
                        _logger.LogDebug("Manager tool call completed: {ToolCallId}", toolCallId);
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error processing SDK session event for Manager tool commentary");
        }
    }

    private static string BuildDefaultManagerSystemPrompt(OfficeConfig config)
    {
        return $"""
            You are an AI Manager orchestrating a team of assistant agents.
            
            ## Objective
            {config.Objective}
            
            ## Your Responsibilities
            1. Analyze the workspace and identify tasks that need to be done
            2. Create clear, focused task prompts for your assistant agents
            3. Aggregate results from completed tasks into coherent summaries
            4. Continuously monitor for new events and changes
            
            ## Guidelines
            - Break work into small, independent tasks suitable for parallel execution
            - Each task should be self-contained with clear instructions
            - Prioritize tasks by urgency and impact
            - Provide structured JSON responses when asked for task lists
            - When aggregating, synthesize findings — don't just concatenate
            """;
    }
}