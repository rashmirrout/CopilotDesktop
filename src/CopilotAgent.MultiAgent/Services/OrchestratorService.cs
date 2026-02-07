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
    public event EventHandler<OrchestratorEvent>? EventReceived;

    // ── IOrchestratorService methods ────────────────────────────

    /// <inheritdoc />
    public async Task<OrchestratorResponse> SubmitTaskAsync(
        string taskPrompt, MultiAgentConfig config, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        if (_isRunning)
            throw new InvalidOperationException("An orchestration task is already running. Cancel it first.");

        _activeConfig = config ?? throw new ArgumentNullException(nameof(config));
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isRunning = true;
        _currentPlan = null;

        try
        {
            await EnsureOrchestratorSessionAsync(config, _cts.Token).ConfigureAwait(false);

            await LogAsync(null, null, OrchestrationLogLevel.Info, "Orchestrator",
                $"Task submitted: {Truncate(taskPrompt, 200)}",
                OrchestratorEventType.OrchestratorCommentary).ConfigureAwait(false);

            // Ask the orchestrator LLM to evaluate whether clarification is needed
            var evaluationPrompt = BuildEvaluationPrompt(taskPrompt);
            var llmResponse = await SendToOrchestratorLlmAsync(evaluationPrompt, _cts.Token)
                .ConfigureAwait(false);

            // Parse LLM response to determine next phase
            var parsed = ParseEvaluationResponse(llmResponse);

            if (parsed.NeedsClarification && parsed.Questions is { Count: > 0 })
            {
                TransitionTo(OrchestrationPhase.Clarifying);
                return new OrchestratorResponse
                {
                    Phase = OrchestrationPhase.Clarifying,
                    Message = "I need some clarifications before proceeding.",
                    ClarifyingQuestions = parsed.Questions,
                    RequiresUserInput = true
                };
            }

            // Proceed directly to planning
            return await PlanTaskAsync(taskPrompt, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TransitionTo(OrchestrationPhase.Cancelled);
            _isRunning = false;
            return CancelledResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during task submission");
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
            throw new InvalidOperationException($"Cannot respond to clarification in phase {_currentPhase}.");

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _cts?.Token ?? CancellationToken.None);

        try
        {
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

            // Ready to plan — use the original task from conversation context
            var taskDescription = _context.ConversationHistory
                .FirstOrDefault(m => m.Role == MessageRole.User)?.Content ?? userResponse;

            return await PlanTaskAsync(taskDescription, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TransitionTo(OrchestrationPhase.Cancelled);
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

        // Terminate orchestrator session process
        if (_orchestratorSession is not null)
        {
            _copilotService.TerminateSessionProcess(_orchestratorSession.SessionId);
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

    private void TransitionTo(OrchestrationPhase newPhase)
    {
        var oldPhase = _currentPhase;
        _currentPhase = newPhase;

        _logger.LogInformation("Phase transition: {OldPhase} → {NewPhase}", oldPhase, newPhase);
        EmitEvent(OrchestratorEventType.PhaseChanged, $"{oldPhase} → {newPhase}");
    }

    // ── Private: Planning ───────────────────────────────────────

    private async Task<OrchestratorResponse> PlanTaskAsync(string taskDescription, CancellationToken ct)
    {
        TransitionTo(OrchestrationPhase.Planning);

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

        TransitionTo(OrchestrationPhase.AwaitingApproval);

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
        TransitionTo(OrchestrationPhase.Executing);
        var plan = _currentPlan!;
        var config = _activeConfig!;
        var sw = Stopwatch.StartNew();
        var allResults = new List<AgentResult>();

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
                var stageResults = await _agentPool
                    .DispatchBatchAsync(stage.Chunks, _context.OrchestratorSessionId, config, ct)
                    .ConfigureAwait(false);

                allResults.AddRange(stageResults);

                var succeeded = stageResults.Count(r => r.IsSuccess);
                var failed = stageResults.Count(r => !r.IsSuccess);

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

        // ── Aggregation phase ──
        return await AggregateResultsAsync(plan, allResults, sw.Elapsed, ct).ConfigureAwait(false);
    }

    private async Task<OrchestratorResponse> AggregateResultsAsync(
        OrchestrationPlan plan, List<AgentResult> results, TimeSpan duration, CancellationToken ct)
    {
        TransitionTo(OrchestrationPhase.Aggregating);
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

    // ── Private: LLM response parsing ───────────────────────────

    private static string BuildEvaluationPrompt(string taskPrompt)
    {
        return
            """
            You are a task orchestrator. Evaluate the following user request and determine if you need clarification before creating an execution plan.

            RESPOND WITH EXACTLY ONE OF THESE JSON FORMATS:

            If clarification is needed:
            {"action": "clarify", "questions": ["question1", "question2"]}

            If ready to proceed:
            {"action": "proceed"}

            If this is a simple conversational follow-up (not a new task):
            {"action": "respond", "message": "your response"}

            USER REQUEST:
            """ + "\n" + taskPrompt;
    }

    private static string BuildOrchestratorSystemPrompt()
    {
        return
            """
            You are a multi-agent orchestrator. Your role is to:
            1. Evaluate user tasks and determine if clarification is needed
            2. Break complex tasks into parallel work chunks
            3. Coordinate worker agents and synthesize their results
            4. Maintain context across follow-up interactions

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
            _logger.LogDebug(ex, "Could not parse LLM evaluation response as JSON, treating as proceed.");
        }

        // Default: proceed to planning (LLM didn't return structured JSON)
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