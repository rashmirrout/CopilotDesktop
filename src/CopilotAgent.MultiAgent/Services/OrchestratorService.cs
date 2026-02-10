using System.Diagnostics;
using System.Text.Json;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.MultiAgent.Events;
using CopilotAgent.MultiAgent.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Core orchestration service implementing the state machine:
/// Idle → Clarifying → Planning → AwaitingApproval → Executing → Aggregating → Completed.
/// Manages a long-lived orchestrator LLM session and delegates work to short-lived worker agents.
/// </summary>
public sealed class OrchestratorService : IOrchestratorService, IDisposable
{
    private readonly ICopilotService _copilotService;
    private readonly ISessionManager _sessionManager;
    private readonly ITaskDecomposer _taskDecomposer;
    private readonly IDependencyScheduler _dependencyScheduler;
    private readonly IAgentPool _agentPool;
    private readonly IResultAggregator _resultAggregator;
    private readonly ITaskLogStore _taskLogStore;
    private readonly ILogger<OrchestratorService> _logger;

    private OrchestratorContext _context = new();
    private MultiAgentConfig? _activeConfig;
    private Session? _orchestratorSession;
    private CancellationTokenSource? _cts;
    private OrchestrationPhase _currentPhase = OrchestrationPhase.Idle;
    private OrchestrationPlan? _currentPlan;
    private bool _isRunning;
    private bool _disposed;

    public OrchestratorService(
        ICopilotService copilotService,
        ISessionManager sessionManager,
        ITaskDecomposer taskDecomposer,
        IDependencyScheduler dependencyScheduler,
        IAgentPool agentPool,
        IResultAggregator resultAggregator,
        ITaskLogStore taskLogStore,
        ILogger<OrchestratorService> logger)
    {
        _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _taskDecomposer = taskDecomposer ?? throw new ArgumentNullException(nameof(taskDecomposer));
        _dependencyScheduler = dependencyScheduler ?? throw new ArgumentNullException(nameof(dependencyScheduler));
        _agentPool = agentPool ?? throw new ArgumentNullException(nameof(agentPool));
        _resultAggregator = resultAggregator ?? throw new ArgumentNullException(nameof(resultAggregator));
        _taskLogStore = taskLogStore ?? throw new ArgumentNullException(nameof(taskLogStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Forward worker progress events
        _agentPool.WorkerProgress += (_, e) => EventReceived?.Invoke(this, e);
    }

    // ── IOrchestratorService properties ─────────────────────────

    /// <inheritdoc />
    public OrchestrationPhase CurrentPhase => _currentPhase;

    /// <inheritdoc />
    public OrchestrationPlan? CurrentPlan => _currentPlan;

    /// <inheritdoc />
    public bool IsRunning => _isRunning;

    /// <inheritdoc />
    public string? OrchestratorSessionId => _orchestratorSession?.SessionId;

    /// <inheritdoc />
    public event EventHandler<OrchestratorEvent>? EventReceived;

    // ── IOrchestratorService methods ────────────────────────────

    /// <inheritdoc />
    public async Task<OrchestratorResponse> SubmitTaskAsync(
        string taskPrompt, MultiAgentConfig config, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        if (_isRunning)
        {
            _logger.LogWarning("SubmitTaskAsync rejected — orchestration already running in phase {Phase}", _currentPhase);
            throw new InvalidOperationException("An orchestration task is already running. Cancel it first.");
        }

        _activeConfig = config ?? throw new ArgumentNullException(nameof(config));
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isRunning = true;
        _currentPlan = null;

        _logger.LogInformation(
            "SubmitTaskAsync started. Model={Model}, MaxParallel={MaxParallel}, WorkDir={WorkDir}, PromptLength={Len}",
            config.OrchestratorModelId, config.MaxParallelSessions, config.WorkingDirectory, taskPrompt.Length);

        try
        {
            await EnsureOrchestratorSessionAsync(config, _cts.Token).ConfigureAwait(false);

            // Persist the original, unmodified task prompt so we can enrich it
            // with clarification context later (the evaluation prompt wrapper
            // would pollute ConversationHistory's first User entry).
            _context.OriginalTaskPrompt = taskPrompt;

            await LogAsync(null, null, OrchestrationLogLevel.Info, "Orchestrator",
                $"Task submitted: {Truncate(taskPrompt, 200)}",
                OrchestratorEventType.OrchestratorCommentary).ConfigureAwait(false);

            // Ask the orchestrator LLM to evaluate whether clarification is needed
            _logger.LogDebug("Sending evaluation prompt to orchestrator LLM ({Len} chars)", taskPrompt.Length);
            var evaluationPrompt = BuildEvaluationPrompt(taskPrompt);
            var llmResponse = await SendToOrchestratorLlmAsync(evaluationPrompt, _cts.Token)
                .ConfigureAwait(false);

            _logger.LogDebug("Evaluation LLM response received ({Len} chars)", llmResponse.Length);

            // Parse LLM response to determine next phase
            var parsed = ParseEvaluationResponse(llmResponse);

            if (parsed.NeedsClarification && parsed.Questions is { Count: > 0 })
            {
                _logger.LogInformation(
                    "LLM requested clarification with {QuestionCount} questions", parsed.Questions.Count);
                TransitionTo(OrchestrationPhase.Clarifying);
                return new OrchestratorResponse
                {
                    Phase = OrchestrationPhase.Clarifying,
                    Message = "I need some clarifications before proceeding.",
                    ClarifyingQuestions = parsed.Questions,
                    RequiresUserInput = true
                };
            }

            // Handle conversational / non-task responses (e.g., "hi", greetings, casual chat).
            // The LLM returns {"action":"respond"} — this is NOT an actionable task, so we
            // must NOT proceed to planning. Instead, return the LLM's conversational response
            // and go back to idle so the user can submit a real task.
            if (!parsed.IsNewTask && !string.IsNullOrWhiteSpace(parsed.Message))
            {
                _logger.LogInformation("LLM returned conversational response (not a task) — returning to idle.");
                _isRunning = false;
                TransitionTo(OrchestrationPhase.Idle, "ConversationalResponse");
                return new OrchestratorResponse
                {
                    Phase = OrchestrationPhase.Idle,
                    Message = parsed.Message,
                    RequiresUserInput = true
                };
            }

            _logger.LogInformation("No clarification needed — proceeding to planning");
            // Proceed directly to planning
            return await PlanTaskAsync(taskPrompt, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("SubmitTaskAsync cancelled by user or token");
            TransitionTo(OrchestrationPhase.Cancelled);
            _isRunning = false;
            return CancelledResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during task submission. Phase was {Phase}", _currentPhase);
            _isRunning = false;
            TransitionTo(OrchestrationPhase.Idle);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<OrchestratorResponse> RespondToClarificationAsync(
        string userResponse, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        if (_currentPhase != OrchestrationPhase.Clarifying)
        {
            _logger.LogWarning("RespondToClarificationAsync rejected — current phase is {Phase}, expected Clarifying", _currentPhase);
            throw new InvalidOperationException($"Cannot respond to clarification in phase {_currentPhase}.");
        }

        // Generate a correlation ID for this entire clarification → planning flow.
        // The UI uses this to distinguish "expected" phase transitions (caused by our Send)
        // from "external" transitions (timeouts, cancellations, etc.).
        var correlationId = $"clarify-{Guid.NewGuid():N}";

        _logger.LogInformation("Clarification response received ({Len} chars), correlationId={CorrelationId}",
            userResponse.Length, correlationId);

        // Emit acknowledgment IMMEDIATELY so the UI knows we received the user's input.
        EmitCorrelatedEvent(OrchestratorEventType.ClarificationReceived,
            "Clarification received. Analyzing your response...", correlationId);

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _cts?.Token ?? CancellationToken.None);

        try
        {
            // Emit processing event — the UI can show "Processing..." feedback.
            EmitCorrelatedEvent(OrchestratorEventType.ClarificationProcessing,
                "Sending your response to the orchestrator...", correlationId);

            // Send user's clarification response to orchestrator LLM
            var llmResponse = await SendToOrchestratorLlmAsync(userResponse, linkedCts.Token)
                .ConfigureAwait(false);

            var parsed = ParseEvaluationResponse(llmResponse);

            if (parsed.NeedsClarification && parsed.Questions is { Count: > 0 })
            {
                // Still needs more clarification
                return new OrchestratorResponse
                {
                    Phase = OrchestrationPhase.Clarifying,
                    Message = "I have a few more questions.",
                    ClarifyingQuestions = parsed.Questions,
                    RequiresUserInput = true
                };
            }

            // Build an enriched task description that combines the original user task
            // with the clarification Q&A. This ensures the task decomposer has full
            // context — the raw task alone would ignore the user's clarification answers.
            var enrichedTask = BuildEnrichedTaskDescription(userResponse);

            _logger.LogDebug("Enriched task for planning ({Len} chars): {Preview}",
                enrichedTask.Length, Truncate(enrichedTask, 200));

            // Pass the correlationId through PlanTaskAsync so all downstream phase
            // transitions are tagged as consequences of the user's clarification.
            return await PlanTaskAsync(enrichedTask, linkedCts.Token, correlationId).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TransitionTo(OrchestrationPhase.Cancelled, "UserCancelled", correlationId);
            _isRunning = false;
            return CancelledResponse();
        }
    }

    /// <inheritdoc />
    public async Task<OrchestratorResponse> ApprovePlanAsync(
        PlanApprovalDecision decision, string? feedback = null, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        if (_currentPhase != OrchestrationPhase.AwaitingApproval)
            throw new InvalidOperationException($"Cannot approve plan in phase {_currentPhase}.");

        if (_currentPlan is null || _activeConfig is null)
            throw new InvalidOperationException("No active plan or config.");

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _cts?.Token ?? CancellationToken.None);

        try
        {
            switch (decision)
            {
                case PlanApprovalDecision.Approve:
                    return await ExecutePlanAsync(linkedCts.Token).ConfigureAwait(false);

                case PlanApprovalDecision.RequestChanges:
                    // Send feedback to LLM for plan revision
                    var revisedPrompt = $"The user wants changes to the plan: {feedback}\n\nPlease revise the plan.";
                    var llmResponse = await SendToOrchestratorLlmAsync(revisedPrompt, linkedCts.Token)
                        .ConfigureAwait(false);

                    // Re-plan with the revised context
                    var taskDesc = _currentPlan.TaskDescription;
                    return await PlanTaskAsync(taskDesc, linkedCts.Token).ConfigureAwait(false);

                case PlanApprovalDecision.Reject:
                    TransitionTo(OrchestrationPhase.Idle);
                    _isRunning = false;
                    _currentPlan = null;
                    return new OrchestratorResponse
                    {
                        Phase = OrchestrationPhase.Idle,
                        Message = "Plan rejected. You can submit a new task.",
                        RequiresUserInput = true
                    };

                default:
                    throw new ArgumentOutOfRangeException(nameof(decision));
            }
        }
        catch (OperationCanceledException)
        {
            TransitionTo(OrchestrationPhase.Cancelled);
            _isRunning = false;
            return CancelledResponse();
        }
    }

    /// <inheritdoc />
    public async Task<OrchestratorResponse> InjectInstructionAsync(
        string instruction, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        if (_currentPhase != OrchestrationPhase.Executing)
        {
            _logger.LogWarning("Injection ignored — not in Executing phase (current: {Phase})", _currentPhase);
            return new OrchestratorResponse
            {
                Phase = _currentPhase,
                Message = $"Cannot inject instructions in phase {_currentPhase}. Only available during execution.",
                RequiresUserInput = false
            };
        }

        EmitEvent(OrchestratorEventType.InjectionReceived, $"User instruction: {Truncate(instruction, 150)}");

        // Forward the injection to the orchestrator LLM for context awareness
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _cts?.Token ?? CancellationToken.None);

        await SendToOrchestratorLlmAsync(
            $"[User Injection During Execution] {instruction}",
            linkedCts.Token).ConfigureAwait(false);

        EmitEvent(OrchestratorEventType.InjectionProcessed, "Instruction acknowledged.");

        return new OrchestratorResponse
        {
            Phase = OrchestrationPhase.Executing,
            Message = "Instruction received and acknowledged. Execution continues.",
            RequiresUserInput = false
        };
    }

    /// <inheritdoc />
    public async Task<OrchestratorResponse> SendFollowUpAsync(
        string followUpPrompt, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        if (_currentPhase != OrchestrationPhase.Completed && _currentPhase != OrchestrationPhase.Idle)
            throw new InvalidOperationException($"Follow-up only available after completion or idle (current: {_currentPhase}).");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isRunning = true;

        try
        {
            await EnsureOrchestratorSessionAsync(_activeConfig!, _cts.Token).ConfigureAwait(false);

            EmitEvent(OrchestratorEventType.FollowUpSent, $"Follow-up: {Truncate(followUpPrompt, 150)}");

            var llmResponse = await SendToOrchestratorLlmAsync(followUpPrompt, _cts.Token)
                .ConfigureAwait(false);

            _context.LastActivityUtc = DateTime.UtcNow;

            EmitEvent(OrchestratorEventType.FollowUpReceived, Truncate(llmResponse, 300));

            // Check if the follow-up requires a new orchestration
            var parsed = ParseEvaluationResponse(llmResponse);

            if (parsed.IsNewTask)
            {
                return await PlanTaskAsync(followUpPrompt, _cts.Token).ConfigureAwait(false);
            }

            _isRunning = false;
            return new OrchestratorResponse
            {
                Phase = OrchestrationPhase.Completed,
                Message = llmResponse,
                RequiresUserInput = true
            };
        }
        catch (OperationCanceledException)
        {
            TransitionTo(OrchestrationPhase.Cancelled);
            _isRunning = false;
            return CancelledResponse();
        }
    }

    /// <inheritdoc />
    public async Task CancelAsync()
    {
        if (_cts is not null && !_cts.IsCancellationRequested)
        {
            _logger.LogInformation("Cancelling orchestration...");
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        TransitionTo(OrchestrationPhase.Cancelled);
        _isRunning = false;

        EmitEvent(OrchestratorEventType.TaskAborted, "Orchestration cancelled by user.");

        // Terminate orchestrator session process and clear the reference so
        // EnsureOrchestratorSessionAsync creates a fresh session on next submit.
        if (_orchestratorSession is not null)
        {
            _copilotService.TerminateSessionProcess(_orchestratorSession.SessionId);
            _orchestratorSession = null;
        }
    }

    /// <inheritdoc />
    public void ResetContext()
    {
        _context = new OrchestratorContext();
        _currentPlan = null;
        _currentPhase = OrchestrationPhase.Idle;
        _isRunning = false;
        _activeConfig = null;

        if (_orchestratorSession is not null)
        {
            _copilotService.TerminateSessionProcess(_orchestratorSession.SessionId);
            _orchestratorSession = null;
        }

        _logger.LogInformation("Orchestrator context reset.");
    }

    // ── Private: State machine transitions ──────────────────────

    private void TransitionTo(OrchestrationPhase newPhase, string reason = "", string? correlationId = null)
    {
        var oldPhase = _currentPhase;
        _currentPhase = newPhase;

        _logger.LogInformation("Phase transition: {OldPhase} → {NewPhase} (reason={Reason}, correlationId={CorrelationId})",
            oldPhase, newPhase, reason, correlationId ?? "(none)");

        EventReceived?.Invoke(this, new PhaseTransitionEvent
        {
            EventType = OrchestratorEventType.PhaseChanged,
            Message = $"{oldPhase} → {newPhase}",
            TimestampUtc = DateTime.UtcNow,
            FromPhase = oldPhase,
            ToPhase = newPhase,
            Reason = reason,
            CorrelationId = correlationId
        });
    }

    // ── Private: Planning ───────────────────────────────────────

    /// <summary>
    /// Overload that carries a correlation ID through all downstream phase transitions,
    /// allowing the UI to recognize these transitions as consequences of a prior user command
    /// (e.g., the user's clarification response triggering Planning → AwaitingApproval).
    /// </summary>
    private Task<OrchestratorResponse> PlanTaskAsync(string taskDescription, CancellationToken ct, string correlationId)
        => PlanTaskAsyncCore(taskDescription, ct, correlationId);

    private Task<OrchestratorResponse> PlanTaskAsync(string taskDescription, CancellationToken ct)
        => PlanTaskAsyncCore(taskDescription, ct, correlationId: null);

    private async Task<OrchestratorResponse> PlanTaskAsyncCore(string taskDescription, CancellationToken ct, string? correlationId)
    {
        TransitionTo(OrchestrationPhase.Planning, "PlanRequested", correlationId);

        // Defensive: ensure orchestrator session ID is valid before calling downstream services.
        // This can happen if CancelAsync was called between SubmitTask and PlanTask, leaving
        // _context.OrchestratorSessionId as empty (its default).
        if (string.IsNullOrWhiteSpace(_context.OrchestratorSessionId))
        {
            _logger.LogWarning("OrchestratorSessionId is empty at PlanTaskAsyncCore entry — recreating session.");
            if (_activeConfig is not null)
            {
                await EnsureOrchestratorSessionAsync(_activeConfig, ct).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(_context.OrchestratorSessionId))
            {
                _isRunning = false;
                TransitionTo(OrchestrationPhase.Idle, "SessionCreationFailed", correlationId);
                return new OrchestratorResponse
                {
                    Phase = OrchestrationPhase.Idle,
                    Message = "Failed to establish orchestrator session. Please try again.",
                    RequiresUserInput = true
                };
            }
        }

        await LogAsync(null, null, OrchestrationLogLevel.Info, "Orchestrator",
            "Decomposing task into work chunks...",
            OrchestratorEventType.OrchestratorCommentary, ct).ConfigureAwait(false);

        var plan = await _taskDecomposer
            .DecomposeAsync(taskDescription, _context.OrchestratorSessionId, _activeConfig!, ct)
            .ConfigureAwait(false);

        // Validate dependencies
        var validation = _dependencyScheduler.ValidateDependencies(plan);
        if (!validation.IsValid)
        {
            _logger.LogWarning("Plan has dependency errors: {Errors}", string.Join("; ", validation.Errors));
            // Auto-fix by removing invalid dependencies
            foreach (var chunk in plan.Chunks)
            {
                var validIds = new HashSet<string>(plan.Chunks.Select(c => c.ChunkId));
                chunk.DependsOnChunkIds = chunk.DependsOnChunkIds.Where(d => validIds.Contains(d)).ToList();
            }
        }

        _currentPlan = plan;
        _context.ExecutedPlans.Add(plan);

        EmitEvent(OrchestratorEventType.PlanCreated,
            $"Plan '{plan.PlanId}' created with {plan.Chunks.Count} chunks.");

        await LogAsync(plan.PlanId, null, OrchestrationLogLevel.Info, "Orchestrator",
            $"Plan created: {plan.Chunks.Count} chunks, summary: {Truncate(plan.PlanSummary, 200)}",
            OrchestratorEventType.PlanCreated, ct).ConfigureAwait(false);

        TransitionTo(OrchestrationPhase.AwaitingApproval, "PlanCreated", correlationId);

        return new OrchestratorResponse
        {
            Phase = OrchestrationPhase.AwaitingApproval,
            Message = plan.PlanSummary,
            Plan = plan,
            RequiresUserInput = true
        };
    }

    // ── Private: Execution ──────────────────────────────────────

    private async Task<OrchestratorResponse> ExecutePlanAsync(CancellationToken ct)
    {
        // Defensive: validate session ID before execution
        if (string.IsNullOrWhiteSpace(_context.OrchestratorSessionId))
        {
            _logger.LogError("OrchestratorSessionId is empty at ExecutePlanAsync entry — aborting execution.");
            _isRunning = false;
            TransitionTo(OrchestrationPhase.Idle, "MissingSessionId");
            return new OrchestratorResponse
            {
                Phase = OrchestrationPhase.Idle,
                Message = "Orchestrator session was lost. Please submit the task again.",
                RequiresUserInput = true
            };
        }

        TransitionTo(OrchestrationPhase.Executing);
        var plan = _currentPlan!;
        var config = _activeConfig!;
        var sw = Stopwatch.StartNew();
        var allResults = new List<AgentResult>();

        _logger.LogInformation(
            "ExecutePlanAsync starting. PlanId={PlanId}, Chunks={ChunkCount}, MaxParallel={MaxParallel}",
            plan.PlanId, plan.Chunks.Count, config.MaxParallelSessions);

        await LogAsync(plan.PlanId, null, OrchestrationLogLevel.Info, "Orchestrator",
            "Execution started.", OrchestratorEventType.StageStarted, ct).ConfigureAwait(false);

        try
        {
            // Build execution schedule (DAG topological sort by layers)
            var stages = _dependencyScheduler.BuildSchedule(plan);

            for (var i = 0; i < stages.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var stage = stages[i];

                EmitEvent(OrchestratorEventType.StageStarted,
                    $"Stage {stage.StageIndex + 1}/{stages.Count}: {stage.Chunks.Count} chunks");

                await LogAsync(plan.PlanId, null, OrchestrationLogLevel.Info, "Orchestrator",
                    $"Stage {stage.StageIndex + 1}/{stages.Count} started with {stage.Chunks.Count} parallel chunks.",
                    OrchestratorEventType.StageStarted, ct).ConfigureAwait(false);

                // Inject dependency outputs into chunk contexts
                InjectDependencyOutputs(stage.Chunks, allResults);

                // Dispatch all chunks in this stage in parallel
                var stageSw = Stopwatch.StartNew();
                var stageResults = await _agentPool
                    .DispatchBatchAsync(stage.Chunks, _context.OrchestratorSessionId, config, ct)
                    .ConfigureAwait(false);
                stageSw.Stop();

                allResults.AddRange(stageResults);

                var succeeded = stageResults.Count(r => r.IsSuccess);
                var failed = stageResults.Count(r => !r.IsSuccess);

                _logger.LogInformation(
                    "Stage {StageIdx}/{Total} finished in {Elapsed:F1}s — succeeded={Succeeded}, failed={Failed}",
                    i + 1, stages.Count, stageSw.Elapsed.TotalSeconds, succeeded, failed);

                EmitEvent(OrchestratorEventType.StageCompleted,
                    $"Stage {stage.StageIndex + 1} completed: {succeeded} succeeded, {failed} failed");

                await LogAsync(plan.PlanId, null, OrchestrationLogLevel.Info, "Orchestrator",
                    $"Stage {stage.StageIndex + 1} completed. Succeeded: {succeeded}, Failed: {failed}",
                    OrchestratorEventType.StageCompleted, ct).ConfigureAwait(false);

                // Abort if critical chunks failed and downstream chunks depend on them
                if (failed > 0 && HasCriticalFailure(stage.Chunks, stageResults, stages, i))
                {
                    _logger.LogWarning("Critical chunk failure detected — aborting remaining stages.");
                    await LogAsync(plan.PlanId, null, OrchestrationLogLevel.Warning, "Orchestrator",
                        "Critical chunk failure detected. Aborting remaining stages.",
                        OrchestratorEventType.TaskFailed, ct).ConfigureAwait(false);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            TransitionTo(OrchestrationPhase.Cancelled);
            _isRunning = false;
            EmitEvent(OrchestratorEventType.TaskAborted, "Execution cancelled.");
            return CancelledResponse();
        }

        sw.Stop();

        _logger.LogInformation(
            "All execution stages completed in {Elapsed:F1}s. Total results={ResultCount}, Succeeded={Succeeded}, Failed={Failed}",
            sw.Elapsed.TotalSeconds, allResults.Count,
            allResults.Count(r => r.IsSuccess), allResults.Count(r => !r.IsSuccess));

        // ── Aggregation phase ──
        return await AggregateResultsAsync(plan, allResults, sw.Elapsed, ct).ConfigureAwait(false);
    }

    private async Task<OrchestratorResponse> AggregateResultsAsync(
        OrchestrationPlan plan, List<AgentResult> results, TimeSpan duration, CancellationToken ct)
    {
        // Defensive: validate session ID before aggregation
        if (string.IsNullOrWhiteSpace(_context.OrchestratorSessionId))
        {
            _logger.LogError("OrchestratorSessionId is empty at AggregateResultsAsync entry — returning partial results.");
            _isRunning = false;
            TransitionTo(OrchestrationPhase.Completed, "MissingSessionId");
            return new OrchestratorResponse
            {
                Phase = OrchestrationPhase.Completed,
                Message = "Orchestrator session was lost during execution. Results may be incomplete.",
                RequiresUserInput = true
            };
        }

        TransitionTo(OrchestrationPhase.Aggregating);

        _logger.LogInformation(
            "AggregateResultsAsync starting for PlanId={PlanId}. ResultCount={Count}, Duration={Duration:F1}s",
            plan.PlanId, results.Count, duration.TotalSeconds);

        EmitEvent(OrchestratorEventType.AggregationStarted, "Aggregating worker results...");

        await LogAsync(plan.PlanId, null, OrchestrationLogLevel.Info, "Orchestrator",
            "Aggregation started.", OrchestratorEventType.AggregationStarted, ct).ConfigureAwait(false);

        var report = await _resultAggregator
            .AggregateAsync(plan, results, _context.OrchestratorSessionId, _activeConfig!, ct)
            .ConfigureAwait(false);

        report.Stats.TotalDuration = duration;
        _context.Reports.Add(report);

        EmitEvent(OrchestratorEventType.AggregationCompleted,
            $"Aggregation complete. {report.Stats.SucceededChunks}/{report.Stats.TotalChunks} succeeded.");

        await LogAsync(plan.PlanId, null, OrchestrationLogLevel.Info, "Orchestrator",
            $"Aggregation complete. Duration: {duration.TotalSeconds:F1}s",
            OrchestratorEventType.AggregationCompleted, ct).ConfigureAwait(false);

        TransitionTo(OrchestrationPhase.Completed);
        _isRunning = false;

        var completedEvent = new OrchestrationCompletedEvent
        {
            EventType = OrchestratorEventType.TaskCompleted,
            Message = "Orchestration completed.",
            Report = report,
            WasAborted = false
        };
        EventReceived?.Invoke(this, completedEvent);

        return new OrchestratorResponse
        {
            Phase = OrchestrationPhase.Completed,
            Message = report.ConversationalSummary,
            Report = report,
            RequiresUserInput = true
        };
    }

    // ── Private: Orchestrator session management ────────────────

    private Task EnsureOrchestratorSessionAsync(MultiAgentConfig config, CancellationToken ct)
    {
        if (_orchestratorSession is not null && _copilotService.HasActiveSession(_orchestratorSession.SessionId))
            return Task.CompletedTask;

        var session = new Session
        {
            SessionId = $"orchestrator-{Guid.NewGuid():N}",
            DisplayName = "Multi-Agent Orchestrator",
            ModelId = config.OrchestratorModelId ?? "gpt-4",
            WorkingDirectory = config.WorkingDirectory,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            EnabledMcpServers = config.EnabledMcpServers?.ToList(),
            DisabledSkills = config.DisabledSkills?.ToList(),
            SystemPrompt = BuildOrchestratorSystemPrompt()
        };

        _orchestratorSession = session;
        _context.OrchestratorSessionId = session.SessionId;

        _logger.LogInformation("Created orchestrator session: {SessionId}", session.SessionId);

        return Task.CompletedTask;
    }

    private async Task<string> SendToOrchestratorLlmAsync(string message, CancellationToken ct)
    {
        if (_orchestratorSession is null)
            throw new InvalidOperationException("Orchestrator session not initialized.");

        // Track conversation history
        _context.ConversationHistory.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Content = message,
            Timestamp = DateTime.UtcNow
        });

        var timeout = _activeConfig?.OrchestratorLlmTimeout ?? TimeSpan.FromMinutes(5);
        const int maxAttempts = 2; // 1 initial + 1 retry on connection loss

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            try
            {
                var response = await _copilotService
                    .SendMessageAsync(_orchestratorSession, message, timeoutCts.Token)
                    .ConfigureAwait(false);

                _context.ConversationHistory.Add(response);
                _context.LastActivityUtc = DateTime.UtcNow;

                return response.Content ?? string.Empty;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout (not user cancellation)
                _logger.LogWarning(
                    "Orchestrator LLM call timed out after {Timeout}s (attempt {Attempt}/{Max})",
                    timeout.TotalSeconds, attempt, maxAttempts);

                EmitEvent(OrchestratorEventType.OrchestratorCommentary,
                    $"⏱ LLM call timed out after {timeout.TotalSeconds:F0}s (attempt {attempt}/{maxAttempts})");

                if (attempt < maxAttempts)
                {
                    await RecreateOrchestratorSessionAsync(ct).ConfigureAwait(false);
                    continue;
                }

                throw new TimeoutException(
                    $"Orchestrator LLM call timed out after {timeout.TotalSeconds:F0}s. " +
                    "The task may be too complex or the connection was lost. Please try again.");
            }
            catch (OperationCanceledException)
            {
                // User-initiated cancellation — propagate immediately
                throw;
            }
            catch (Exception ex) when (IsConnectionLossException(ex) && attempt < maxAttempts)
            {
                _logger.LogWarning(ex,
                    "Connection lost to orchestrator LLM (attempt {Attempt}/{Max}). Recreating session...",
                    attempt, maxAttempts);

                EmitEvent(OrchestratorEventType.OrchestratorCommentary,
                    $"⚠ Connection lost ({ex.GetType().Name}). Reconnecting...");

                await RecreateOrchestratorSessionAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsConnectionLossException(ex))
            {
                // Final attempt also failed with connection loss
                _logger.LogError(ex, "Orchestrator LLM connection lost after {Max} attempts", maxAttempts);

                throw new InvalidOperationException(
                    $"Lost connection to the Copilot service ({ex.GetType().Name}). " +
                    "This may happen with complex tasks or long-running operations. " +
                    "Please reset the orchestrator and try again.", ex);
            }
        }

        // Should not reach here, but be safe
        throw new InvalidOperationException("Orchestrator LLM call failed after all retry attempts.");
    }

    /// <summary>
    /// Determines whether an exception indicates a JSON-RPC or transport-level connection loss.
    /// </summary>
    private static bool IsConnectionLossException(Exception ex)
    {
        // Check exception type hierarchy
        if (ex is System.IO.IOException or System.Net.Sockets.SocketException)
            return true;

        // Check for StreamJsonRpc or JSON-RPC related exceptions by name
        // (avoids hard dependency on StreamJsonRpc assembly)
        var typeName = ex.GetType().FullName ?? string.Empty;
        if (typeName.Contains("JsonRpc", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("RemoteInvocation", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("ConnectionLost", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check exception message for connection-related keywords
        var message = ex.Message ?? string.Empty;
        if (message.Contains("JSON RPC", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("JSON-RPC", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("disconnected", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("pipe", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("broken", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("stream", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check inner exception
        if (ex.InnerException is not null)
            return IsConnectionLossException(ex.InnerException);

        return false;
    }

    /// <summary>
    /// Tears down the current orchestrator session and creates a fresh one for retry.
    /// </summary>
    private async Task RecreateOrchestratorSessionAsync(CancellationToken ct)
    {
        if (_orchestratorSession is not null)
        {
            _copilotService.TerminateSessionProcess(_orchestratorSession.SessionId);
            _orchestratorSession = null;
        }

        if (_activeConfig is not null)
        {
            await EnsureOrchestratorSessionAsync(_activeConfig, ct).ConfigureAwait(false);
        }

        EmitEvent(OrchestratorEventType.OrchestratorCommentary, "🔄 Orchestrator session recreated.");
    }

    // ── Private: Task context enrichment ────────────────────────

    /// <summary>
    /// Builds an enriched task description by combining the original user task
    /// with the clarification Q&A from conversation history.
    /// 
    /// The conversation history at this point contains:
    ///   [0] User   → evaluation prompt (wrapped original task)
    ///   [1] Assistant → clarification questions (JSON: {"action":"clarify",...})
    ///   [2] User   → user's clarification response
    ///   [3] Assistant → LLM reply (JSON: {"action":"proceed"} or more questions)
    ///   ... (may repeat for multiple clarification rounds)
    ///
    /// We extract the clarification exchanges and append them to the original task
    /// so the task decomposer has full context.
    /// </summary>
    private string BuildEnrichedTaskDescription(string latestClarificationResponse)
    {
        var originalTask = _context.OriginalTaskPrompt;

        // Safety: if original task was never stored, fall back to first user message
        // (which may be the evaluation-wrapped prompt — better than nothing).
        if (string.IsNullOrWhiteSpace(originalTask))
        {
            originalTask = _context.ConversationHistory
                .FirstOrDefault(m => m.Role == MessageRole.User)?.Content ?? latestClarificationResponse;
            _logger.LogWarning("OriginalTaskPrompt was empty — falling back to first conversation entry.");
        }

        // Extract clarification exchanges from history.
        // Skip the first User+Assistant pair (evaluation prompt + clarify response),
        // then collect all subsequent User/Assistant pairs as clarification rounds.
        var history = _context.ConversationHistory;
        var clarificationPairs = new List<(string Question, string Answer)>();

        // Walk history starting from index 1 (first assistant response = clarification questions)
        // to find clarification rounds.
        for (var i = 1; i < history.Count - 1; i += 2)
        {
            if (history[i].Role == MessageRole.Assistant && i + 1 < history.Count
                && history[i + 1].Role == MessageRole.User)
            {
                var assistantMsg = history[i].Content ?? "";
                var userMsg = history[i + 1].Content ?? "";

                // Only include if the assistant message was a clarification request
                // (contains "clarify" action or question-like content)
                if (!string.IsNullOrWhiteSpace(userMsg))
                {
                    clarificationPairs.Add((
                        Question: ExtractQuestionsDisplay(assistantMsg),
                        Answer: userMsg
                    ));
                }
            }
        }

        // If no clarification pairs were found, just return the original task
        // enriched with the latest response
        if (clarificationPairs.Count == 0)
        {
            _logger.LogDebug("No clarification pairs found in history — using original task + latest response.");
            return $"{originalTask}\n\nAdditional context from user:\n{latestClarificationResponse}";
        }

        // Build the enriched description
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Original Task");
        sb.AppendLine(originalTask);
        sb.AppendLine();
        sb.AppendLine("## Clarifications Provided");

        for (var i = 0; i < clarificationPairs.Count; i++)
        {
            var (question, answer) = clarificationPairs[i];
            sb.AppendLine($"### Round {i + 1}");
            sb.AppendLine($"**Questions asked:** {Truncate(question, 500)}");
            sb.AppendLine($"**User's response:** {answer}");
            sb.AppendLine();
        }

        var result = sb.ToString();
        _logger.LogInformation("Built enriched task: {ClarificationRounds} clarification round(s), total {Len} chars.",
            clarificationPairs.Count, result.Length);

        return result;
    }

    /// <summary>
    /// Extracts a human-readable summary of questions from an LLM clarification response.
    /// Tries to parse JSON {"action":"clarify","questions":[...]} first, falls back to raw text.
    /// </summary>
    private static string ExtractQuestionsDisplay(string assistantMessage)
    {
        try
        {
            var jsonStart = assistantMessage.IndexOf('{');
            var jsonEnd = assistantMessage.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = assistantMessage[jsonStart..(jsonEnd + 1)];
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("questions", out var qProp))
                {
                    var questions = qProp.EnumerateArray()
                        .Select(q => q.GetString() ?? "")
                        .Where(q => !string.IsNullOrWhiteSpace(q))
                        .ToList();
                    if (questions.Count > 0)
                    {
                        return string.Join("; ", questions);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not valid JSON — fall through to raw text
        }

        return assistantMessage;
    }

    // ── Private: LLM response parsing ───────────────────────────

    private static string BuildEvaluationPrompt(string taskPrompt)
    {
        return
            """
            You are the **Agent Team Orchestrator** — a general-purpose multi-agent task executor that coordinates multiple parallel worker agents to accomplish ANY type of complex task.

            YOUR CAPABILITIES:
            - Decompose ANY complex task into parallel work chunks (coding, research, analysis, writing, data processing, web searches, creative work, etc.)
            - Coordinate multiple worker agents executing simultaneously
            - Handle multi-step workflows with dependencies between tasks
            - Aggregate results from all workers into a unified report
            - Support plan review, clarification rounds, and mid-execution instructions

            IMPORTANT: You are NOT limited to software engineering. You can handle research, analysis, writing, data tasks, web lookups, comparisons, creative projects, and any other task that can be broken into steps.

            Evaluate the following user request and determine the correct next action.

            RULES (follow strictly):
            1. If the request is a greeting, casual chat, or conversational message (e.g., "hi", "hello", "hey", "thanks", "how are you"):
               - Respond with a friendly greeting
               - Briefly introduce yourself and your capabilities (emphasize you handle ALL types of tasks, not just coding)
               - Encourage the user to submit a task
               - Do NOT plan or clarify
               Example: {"action": "respond", "message": "👋 Hello! I'm the Agent Team Orchestrator — I coordinate multiple AI worker agents in parallel to tackle complex tasks of any kind: coding, research, analysis, writing, data processing, web lookups, and more. Give me a detailed task and I'll break it down, get your approval on the plan, and execute it with a team of workers!"}
            2. If the request is vague, ambiguous, incomplete, or lacks enough detail to create a concrete execution plan (e.g., single words, unclear scope, missing context), you MUST ask clarifying questions. Do NOT proceed to planning with insufficient information.
            3. Use "proceed" when the request is a clear, specific, actionable task with enough detail to decompose into work chunks. This applies to ANY domain — coding, research, analysis, writing, etc.

            RESPOND WITH EXACTLY ONE OF THESE JSON FORMATS:

            If the request is conversational / not a task:
            {"action": "respond", "message": "your friendly response including who you are and what you can do"}

            If clarification is needed (vague, ambiguous, or incomplete request):
            {"action": "clarify", "questions": ["specific question 1", "specific question 2"]}

            If the request is a clear, actionable task ready for planning:
            {"action": "proceed"}

            USER REQUEST:
            """ + "\n" + taskPrompt;
    }

    private static string BuildOrchestratorSystemPrompt()
    {
        return
            """
            You are a general-purpose multi-agent orchestrator. Your role is to:
            1. Evaluate user tasks and determine if clarification is needed
            2. Break complex tasks into parallel work chunks — tasks can be of ANY type: coding, research, analysis, writing, data processing, web lookups, comparisons, creative work, etc.
            3. Coordinate worker agents and synthesize their results
            4. Maintain context across follow-up interactions

            IMPORTANT: You are NOT limited to software engineering tasks. You handle ALL domains and types of work. Never reject a task because it is "not coding" or "not software engineering". Any task that can be decomposed into steps is valid.

            Always respond with valid JSON when asked for structured output.
            Be concise and focused on task decomposition and coordination.
            """;
    }

    private sealed class EvaluationResult
    {
        public bool NeedsClarification { get; set; }
        public List<string>? Questions { get; set; }
        public bool IsNewTask { get; set; }
        public string? Message { get; set; }
    }

    private EvaluationResult ParseEvaluationResponse(string llmResponse)
    {
        _logger.LogDebug("Parsing evaluation response ({Len} chars): {Preview}",
            llmResponse.Length, Truncate(llmResponse, 120));

        try
        {
            // Try to extract JSON from the response
            var jsonStart = llmResponse.IndexOf('{');
            var jsonEnd = llmResponse.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = llmResponse[jsonStart..(jsonEnd + 1)];
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var action = root.TryGetProperty("action", out var actionProp)
                    ? actionProp.GetString()
                    : null;

                return action switch
                {
                    "clarify" => new EvaluationResult
                    {
                        NeedsClarification = true,
                        Questions = root.TryGetProperty("questions", out var qProp)
                            ? qProp.EnumerateArray().Select(q => q.GetString() ?? "").ToList()
                            : new List<string>()
                    },
                    "respond" => new EvaluationResult
                    {
                        NeedsClarification = false,
                        IsNewTask = false,
                        Message = root.TryGetProperty("message", out var msgProp)
                            ? msgProp.GetString()
                            : llmResponse
                    },
                    "proceed" or "plan" => new EvaluationResult
                    {
                        NeedsClarification = false,
                        IsNewTask = true
                    },
                    _ => new EvaluationResult { NeedsClarification = false, IsNewTask = true }
                };
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Could not parse LLM evaluation response as JSON — defaulting to proceed. Response preview: {Preview}",
                Truncate(llmResponse, 200));
        }

        // Default: proceed to planning (LLM didn't return structured JSON)
        _logger.LogDebug("Evaluation parse fallback: treating response as 'proceed to planning'");
        return new EvaluationResult { NeedsClarification = false, IsNewTask = true };
    }

    // ── Private: Dependency injection for stages ────────────────

    /// <summary>
    /// Inject outputs from completed dependency chunks into the dependent chunks'
    /// DependencyContext fields so workers can access upstream results.
    /// </summary>
    private static void InjectDependencyOutputs(List<WorkChunk> stageChunks, List<AgentResult> completedResults)
    {
        var resultMap = completedResults.ToDictionary(r => r.ChunkId, r => r);

        foreach (var chunk in stageChunks)
        {
            if (chunk.DependsOnChunkIds is not { Count: > 0 }) continue;

            var depOutputs = new List<string>();
            foreach (var depId in chunk.DependsOnChunkIds)
            {
                if (resultMap.TryGetValue(depId, out var depResult))
                {
                    depOutputs.Add($"[Output from '{depId}']: {Truncate(depResult.Response ?? "(no output)", 500)}");
                }
            }

            if (depOutputs.Count > 0)
            {
                chunk.Prompt = $"DEPENDENCY OUTPUTS:\n{string.Join("\n", depOutputs)}\n\nTASK:\n{chunk.Prompt}";
            }
        }
    }

    /// <summary>
    /// Determine if a stage failure is critical — i.e., any failed chunk has
    /// downstream dependents in later stages.
    /// </summary>
    private static bool HasCriticalFailure(
        List<WorkChunk> stageChunks,
        List<AgentResult> stageResults,
        List<ExecutionStage> allStages,
        int currentStageIndex)
    {
        var failedChunkIds = new HashSet<string>(
            stageResults.Where(r => !r.IsSuccess).Select(r => r.ChunkId));

        if (failedChunkIds.Count == 0) return false;

        // Check if any later stage depends on a failed chunk
        for (var i = currentStageIndex + 1; i < allStages.Count; i++)
        {
            foreach (var chunk in allStages[i].Chunks)
            {
                if (chunk.DependsOnChunkIds.Any(d => failedChunkIds.Contains(d)))
                    return true;
            }
        }

        return false;
    }

    // ── Private: Logging & events ───────────────────────────────

    private async Task LogAsync(
        string? planId, string? chunkId, OrchestrationLogLevel level, string source,
        string message, OrchestratorEventType? eventType = null, CancellationToken ct = default)
    {
        var entry = new LogEntry
        {
            Level = level,
            Source = source,
            Message = message,
            PlanId = planId,
            ChunkId = chunkId,
            EventType = eventType
        };

        if (planId is not null)
        {
            try
            {
                await _taskLogStore.SaveLogEntryAsync(planId, chunkId, entry, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist log entry");
            }
        }

        if (eventType.HasValue)
        {
            EmitEvent(eventType.Value, message);
        }
    }

    private void EmitEvent(OrchestratorEventType eventType, string message)
    {
        EventReceived?.Invoke(this, new OrchestratorEvent
        {
            EventType = eventType,
            Message = message,
            TimestampUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Emits an event tagged with a correlation ID so the UI can associate it
    /// with the user command that triggered this flow.
    /// </summary>
    private void EmitCorrelatedEvent(OrchestratorEventType eventType, string message, string correlationId)
    {
        EventReceived?.Invoke(this, new OrchestratorEvent
        {
            EventType = eventType,
            Message = message,
            TimestampUtc = DateTime.UtcNow,
            CorrelationId = correlationId
        });
    }

    // ── Private: Helpers ────────────────────────────────────────

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength] + "…";
    }

    private static OrchestratorResponse CancelledResponse() => new()
    {
        Phase = OrchestrationPhase.Cancelled,
        Message = "Orchestration was cancelled.",
        RequiresUserInput = true
    };

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();

        if (_orchestratorSession is not null)
        {
            _copilotService.TerminateSessionProcess(_orchestratorSession.SessionId);
        }
    }
}