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
        IOfficeEventLog eventLog,
        IIterationScheduler scheduler,
        ILoggerFactory loggerFactory)
    {
        _copilotService = copilotService;
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

        // Create manager session
        _managerSession = new Session
        {
            SessionId = $"office-manager-{Guid.NewGuid():N}",
            DisplayName = "Office Manager",
            ModelId = config.ManagerModel,
            WorkingDirectory = config.WorkspacePath,
            SystemPrompt = config.ManagerSystemPrompt ?? BuildDefaultManagerSystemPrompt(config),
            AutonomousMode = new AutonomousModeSettings { AllowAll = true }
        };

        // Transition to Planning
        TransitionTo(ManagerPhase.Planning);

        // Generate plan (stub for now — Step 18 replaces with LLM)
        var plan = await GeneratePlanAsync(ct).ConfigureAwait(false);
        _context.ApprovedPlan = plan;

        if (config.RequirePlanApproval)
        {
            TransitionTo(ManagerPhase.AwaitingApproval);
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

        RaiseEvent(new ChatMessageEvent
        {
            Message = new OfficeChatMessage
            {
                Role = OfficeChatRole.User,
                SenderName = "User",
                Content = "✅ Plan approved. Starting execution.",
                AccentColor = OfficeColorScheme.UserColor
            },
            Description = "User approved the plan"
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

        // Re-generate plan with feedback (stub — Step 18 uses LLM)
        var plan = await GeneratePlanAsync(ct).ConfigureAwait(false);
        _context.ApprovedPlan = plan;

        TransitionTo(ManagerPhase.AwaitingApproval);

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

        RaiseEvent(new ChatMessageEvent
        {
            Message = new OfficeChatMessage
            {
                Role = OfficeChatRole.User,
                SenderName = "User",
                Content = response,
                AccentColor = OfficeColorScheme.UserColor
            },
            Description = "User responded to clarification"
        });

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

        RaiseEvent(new RunStoppedEvent
        {
            Reason = "User requested stop",
            Description = "Office stopped by user"
        });

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
        _logger.LogInformation("Office reset to Idle");
    }

    /// <inheritdoc />
    public void UpdateCheckInterval(int minutes)
    {
        _checkIntervalMinutes = Math.Max(1, minutes);
        _logger.LogInformation("Check interval updated to {Minutes} minutes", _checkIntervalMinutes);
    }

    // ─── Private: Iteration Loop ────────────────────────────

    private Task BeginIterationLoopAsync()
    {
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

                // Absorb injected instructions
                var instructions = AbsorbInjectedInstructions();

                // Phase: FetchingEvents
                TransitionTo(ManagerPhase.FetchingEvents);
                EmitCommentary(CommentaryType.ManagerThinking, "Manager", "🔍 Checking for events and generating tasks...");

                var tasks = await FetchEventsAndCreateTasksAsync(ct).ConfigureAwait(false);

                if (tasks.Count == 0)
                {
                    _logger.LogInformation("No tasks for iteration {N}, moving to rest", _context.CurrentIteration);
                    EmitCommentary(CommentaryType.System, "System", "No tasks generated. Resting...");
                }
                else
                {
                    // Phase: Scheduling
                    TransitionTo(ManagerPhase.Scheduling);
                    EmitCommentary(CommentaryType.SchedulingDecision, "Manager",
                        $"📋 Scheduling {tasks.Count} tasks for execution");

                    // Phase: Executing
                    TransitionTo(ManagerPhase.Executing);

                    var pool = new AssistantPool(
                        _copilotService,
                        _loggerFactory.CreateLogger<AssistantPool>(),
                        _loggerFactory);

                    // Wire pool events to our event system
                    pool.OnAssistantEvent += evt => { RaiseEvent(evt); _eventLog.Log(evt); };
                    pool.OnSchedulingEvent += evt => { RaiseEvent(evt); _eventLog.Log(evt); };

                    var results = await pool.ExecuteTasksAsync(tasks, _config!, ct).ConfigureAwait(false);

                    // Phase: Aggregating
                    TransitionTo(ManagerPhase.Aggregating);
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

                    // Post aggregation summary to chat
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
                }

                // Phase: Resting
                TransitionTo(ManagerPhase.Resting);

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

                // Wire countdown events
                _scheduler.OnCountdownTick += tick =>
                {
                    RaiseEvent(tick);
                    _eventLog.Log(tick);
                };

                await _scheduler.WaitForNextIterationAsync(_checkIntervalMinutes, ct).ConfigureAwait(false);

                // Unwire to avoid accumulation
                _scheduler.OnCountdownTick -= tick =>
                {
                    RaiseEvent(tick);
                    _eventLog.Log(tick);
                };
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

            RaiseEvent(new ErrorEvent
            {
                ErrorMessage = ex.Message,
                Exception = ex,
                IterationNumber = _context.CurrentIteration,
                Description = $"Error: {ex.Message}"
            });
        }
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
            If you need clarification from the user, start your response with [CLARIFICATION_NEEDED] 
            followed by your question. Otherwise, provide the plan directly.
            """;

        try
        {
            EmitCommentary(CommentaryType.ManagerThinking, "Manager", "🧠 Generating execution plan...");
            var response = await _copilotService.SendMessageAsync(_managerSession, prompt, ct).ConfigureAwait(false);
            var content = response.Content.Trim();

            // Check if the Manager needs clarification
            if (content.StartsWith("[CLARIFICATION_NEEDED]", StringComparison.OrdinalIgnoreCase))
            {
                var question = content["[CLARIFICATION_NEEDED]".Length..].Trim();
                var userAnswer = await RequestClarificationAsync(question, ct).ConfigureAwait(false);

                // Re-generate plan with the clarification
                var followUp = $"The user answered your question: \"{userAnswer}\"\n\nNow produce the execution plan.";
                var retry = await _copilotService.SendMessageAsync(_managerSession, followUp, ct).ConfigureAwait(false);
                return retry.Content.Trim();
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

    private async Task<IReadOnlyList<AssistantTask>> FetchEventsAndCreateTasksAsync(CancellationToken ct)
    {
        if (_managerSession is null || _config is null)
        {
            return BuildFallbackTasks();
        }

        var previousReport = _context.IterationReports.Count > 0
            ? $"\n\n## Previous Iteration Summary\n{_context.IterationReports[^1].AggregatedSummary}"
            : "";

        var injectedContext = "";
        var absorbed = AbsorbInjectedInstructions();
        if (absorbed.Count > 0)
        {
            injectedContext = $"\n\n## New User Instructions\n{string.Join("\n- ", absorbed)}";
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
            EmitCommentary(CommentaryType.ManagerThinking, "Manager", "🔍 Analyzing workspace and creating tasks...");
            var response = await _copilotService.SendMessageAsync(_managerSession, prompt, ct).ConfigureAwait(false);
            return ParseTasksFromLlmResponse(response.Content);
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
        if (_managerSession is null || results.Count == 0)
        {
            return BuildFallbackAggregation(results);
        }

        var resultsSummary = string.Join("\n\n", results.Select((r, i) =>
        {
            var status = r.Success ? "✅ Success" : "❌ Failed";
            var error = r.ErrorMessage is not null ? $"\nError: {r.ErrorMessage}" : "";
            return $"### Task {i + 1}: {r.TaskId} ({status}, {r.Duration.TotalSeconds:F1}s)\n{r.Summary}{error}";
        }));

        var prompt = $"""
            You are the Manager agent. Iteration {_context.CurrentIteration} has completed.
            
            ## Objective
            {_config?.Objective}
            
            ## Task Results
            {resultsSummary}
            
            ## Instructions
            Synthesize these results into a coherent iteration summary.
            Highlight key findings, any issues, and recommended next steps.
            Be concise but thorough. Use Markdown formatting.
            """;

        try
        {
            EmitCommentary(CommentaryType.Aggregation, "Manager", "📊 Synthesizing results...");
            var response = await _copilotService.SendMessageAsync(_managerSession, prompt, ct).ConfigureAwait(false);
            return response.Content.Trim();
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
            parts.Add($"  {result.Summary}");
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