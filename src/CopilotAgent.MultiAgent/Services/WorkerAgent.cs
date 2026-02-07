using System.Diagnostics;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.MultiAgent.Events;
using CopilotAgent.MultiAgent.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// A single worker agent that executes one work chunk via a role-specialized Copilot session.
/// Each worker is short-lived: created per chunk, used once, then disposed.
/// The workspace path and dependency results are set via constructor before calling ExecuteAsync.
/// </summary>
public sealed class WorkerAgent : IWorkerAgent
{
    private readonly ICopilotService _copilotService;
    private readonly IAgentRoleProvider _roleProvider;
    private readonly IApprovalQueue _approvalQueue;
    private readonly ITaskLogStore _logStore;
    private readonly MultiAgentConfig _config;
    private readonly string _workspacePath;
    private readonly IReadOnlyDictionary<string, AgentResult> _dependencyResults;
    private readonly string _planId;
    private readonly ILogger<WorkerAgent> _logger;

    private Session? _session;
    private bool _disposed;

    public WorkerAgent(
        string workerId,
        WorkChunk chunk,
        string workspacePath,
        IReadOnlyDictionary<string, AgentResult> dependencyResults,
        string planId,
        ICopilotService copilotService,
        IAgentRoleProvider roleProvider,
        IApprovalQueue approvalQueue,
        ITaskLogStore logStore,
        MultiAgentConfig config,
        ILogger<WorkerAgent> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(dependencyResults);
        ArgumentNullException.ThrowIfNull(copilotService);
        ArgumentNullException.ThrowIfNull(roleProvider);
        ArgumentNullException.ThrowIfNull(approvalQueue);
        ArgumentNullException.ThrowIfNull(logStore);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        WorkerId = workerId;
        Chunk = chunk;
        Status = AgentStatus.Pending;
        ExecutionContext = new ChunkExecutionContext
        {
            ChunkId = chunk.ChunkId,
            AssignedRole = chunk.AssignedRole,
            WorkspacePath = workspacePath
        };

        _workspacePath = workspacePath;
        _dependencyResults = dependencyResults;
        _planId = planId;
        _copilotService = copilotService;
        _roleProvider = roleProvider;
        _approvalQueue = approvalQueue;
        _logStore = logStore;
        _config = config;
        _logger = logger;
    }

    public string WorkerId { get; }
    public WorkChunk Chunk { get; }
    public AgentStatus Status { get; private set; }
    public ChunkExecutionContext ExecutionContext { get; }

    public event EventHandler<WorkerProgressEvent>? ProgressUpdated;

    public async Task<AgentResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var stopwatch = Stopwatch.StartNew();
        ExecutionContext.StartedAtUtc = DateTime.UtcNow;
        Chunk.StartedAtUtc = DateTime.UtcNow;

        _logger.LogInformation(
            "Worker {WorkerId} ExecuteAsync invoked. Chunk={ChunkId}, Title='{Title}', Role={Role}, Workspace={Workspace}, Dependencies=[{Deps}]",
            WorkerId, Chunk.ChunkId, Chunk.Title, Chunk.AssignedRole, _workspacePath,
            string.Join(", ", Chunk.DependsOnChunkIds));

        try
        {
            UpdateStatus(AgentStatus.Running);

            await LogEntryAsync(
                OrchestrationLogLevel.Info,
                OrchestratorEventType.WorkerStarted,
                $"Worker {WorkerId} starting chunk '{Chunk.Title}' in {_workspacePath}");

            // 1. Create a Session model for the worker
            var modelId = _config.WorkerModelId ?? _config.OrchestratorModelId ?? string.Empty;
            _session = new Session
            {
                SessionId = Guid.NewGuid().ToString(),
                DisplayName = $"Worker-{Chunk.Title}",
                WorkingDirectory = _workspacePath,
                ModelId = modelId,
                CreatedAt = DateTime.UtcNow,
                LastActiveAt = DateTime.UtcNow
            };

            ExecutionContext.WorkerSessionId = _session.SessionId;
            Chunk.AssignedSessionId = _session.SessionId;
            Chunk.AssignedWorkspace = _workspacePath;

            _logger.LogInformation(
                "Worker {WorkerId} created session {SessionId} for role {Role}, chunk {ChunkId}, model={Model}",
                WorkerId, _session.SessionId, Chunk.AssignedRole, Chunk.ChunkId, modelId);

            // 2. Build the worker prompt (includes role system instructions + task + dependency context)
            var prompt = BuildWorkerPrompt();
            _logger.LogDebug(
                "Worker {WorkerId} built prompt ({PromptLen} chars) with {DepCount} dependency results injected",
                WorkerId, prompt.Length, _dependencyResults.Count);

            // 3. Send prompt and collect response
            var response = await SendPromptAsync(prompt, cancellationToken);

            stopwatch.Stop();
            ExecutionContext.CompletedAtUtc = DateTime.UtcNow;
            Chunk.CompletedAtUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "Worker {WorkerId} succeeded. Chunk={ChunkId}, Duration={Duration:F1}s, ResponseLen={RespLen}",
                WorkerId, Chunk.ChunkId, stopwatch.Elapsed.TotalSeconds, response.Length);

            var result = new AgentResult
            {
                ChunkId = Chunk.ChunkId,
                IsSuccess = true,
                Response = response,
                Duration = stopwatch.Elapsed
            };

            ExecutionContext.Result = result;
            Chunk.Result = result;
            UpdateStatus(AgentStatus.Succeeded);

            await LogEntryAsync(
                OrchestrationLogLevel.Info,
                OrchestratorEventType.WorkerCompleted,
                $"Worker {WorkerId} completed chunk '{Chunk.Title}' in {stopwatch.Elapsed.TotalSeconds:F1}s");

            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            ExecutionContext.CompletedAtUtc = DateTime.UtcNow;
            Chunk.CompletedAtUtc = DateTime.UtcNow;
            UpdateStatus(AgentStatus.Aborted);

            _logger.LogWarning(
                "Worker {WorkerId} cancelled. Chunk={ChunkId}, ElapsedBeforeCancel={Elapsed:F1}s",
                WorkerId, Chunk.ChunkId, stopwatch.Elapsed.TotalSeconds);

            await LogEntryAsync(
                OrchestrationLogLevel.Warning,
                OrchestratorEventType.WorkerFailed,
                $"Worker {WorkerId} cancelled for chunk '{Chunk.Title}'");

            var result = new AgentResult
            {
                ChunkId = Chunk.ChunkId,
                IsSuccess = false,
                ErrorMessage = "Operation was cancelled",
                Duration = stopwatch.Elapsed
            };
            ExecutionContext.Result = result;
            ExecutionContext.ErrorDetails = "Cancelled";
            Chunk.Result = result;
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            ExecutionContext.CompletedAtUtc = DateTime.UtcNow;
            ExecutionContext.ErrorDetails = ex.Message;
            Chunk.CompletedAtUtc = DateTime.UtcNow;
            UpdateStatus(AgentStatus.Failed);

            _logger.LogError(ex,
                "Worker {WorkerId} failed for chunk {ChunkId}: {Error}",
                WorkerId, Chunk.ChunkId, ex.Message);

            await LogEntryAsync(
                OrchestrationLogLevel.Error,
                OrchestratorEventType.WorkerFailed,
                $"Worker {WorkerId} failed: {ex.Message}");

            var result = new AgentResult
            {
                ChunkId = Chunk.ChunkId,
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed
            };
            ExecutionContext.Result = result;
            Chunk.Result = result;
            return result;
        }
    }

    private string BuildWorkerPrompt()
    {
        var parts = new List<string>();

        // Inject role-specific system instructions
        var roleConfig = _roleProvider.GetEffectiveRoleConfig(Chunk.AssignedRole, _config);
        if (!string.IsNullOrWhiteSpace(roleConfig.SystemInstructions))
        {
            parts.Add(roleConfig.SystemInstructions);
            parts.Add(string.Empty);
            parts.Add("---");
            parts.Add(string.Empty);
        }

        // Core task prompt
        parts.Add($"## Task: {Chunk.Title}");
        parts.Add(string.Empty);
        parts.Add(Chunk.Prompt);

        // Working scope context
        if (!string.IsNullOrWhiteSpace(Chunk.WorkingScope))
        {
            parts.Add(string.Empty);
            parts.Add("## Working Scope");
            parts.Add($"Focus your work on: `{Chunk.WorkingScope}`");
        }

        // Dependency context injection
        if (_dependencyResults.Count > 0)
        {
            parts.Add(string.Empty);
            parts.Add("## Context from Previous Steps");

            foreach (var depChunkId in Chunk.DependsOnChunkIds)
            {
                if (_dependencyResults.TryGetValue(depChunkId, out var depResult) && depResult.IsSuccess)
                {
                    parts.Add(string.Empty);
                    parts.Add($"### Result from dependency `{depChunkId}`:");
                    // Truncate very long dependency results to avoid token overflow
                    var depResponse = depResult.Response ?? string.Empty;
                    if (depResponse.Length > 4000)
                    {
                        depResponse = depResponse[..4000] + "\n\n[...truncated...]";
                    }
                    parts.Add(depResponse);
                }
            }
        }

        // Required skills hint
        if (Chunk.RequiredSkills.Count > 0)
        {
            parts.Add(string.Empty);
            parts.Add($"## Required Skills: {string.Join(", ", Chunk.RequiredSkills)}");
        }

        parts.Add(string.Empty);
        parts.Add("## Instructions");
        parts.Add("- Complete the task thoroughly and provide a detailed response.");
        parts.Add("- If you need to modify files, do so directly.");
        parts.Add("- Report what you did, any issues encountered, and the final state.");

        return string.Join("\n", parts);
    }

    private async Task<string> SendPromptAsync(string prompt, CancellationToken cancellationToken)
    {
        if (_session == null)
        {
            throw new InvalidOperationException("No session established for worker agent.");
        }

        _logger.LogDebug(
            "Worker {WorkerId} sending prompt ({Length} chars) to session {SessionId}",
            WorkerId, prompt.Length, _session.SessionId);

        RaiseProgress("Executing", $"Sending prompt for: {Chunk.Title}");

        // Use the Copilot service to send the prompt and collect the full response
        var chatMessage = await _copilotService.SendMessageAsync(
            _session, prompt, cancellationToken);

        var response = chatMessage.Content ?? string.Empty;

        _logger.LogDebug(
            "Worker {WorkerId} received response ({Length} chars) from session {SessionId}",
            WorkerId, response.Length, _session.SessionId);

        return response;
    }

    private void UpdateStatus(AgentStatus newStatus)
    {
        var oldStatus = Status;
        Status = newStatus;
        Chunk.Status = newStatus;
        ExecutionContext.Status = newStatus;

        _logger.LogDebug(
            "Worker {WorkerId} status: {OldStatus} → {NewStatus}",
            WorkerId, oldStatus, newStatus);

        RaiseProgress($"Status: {newStatus}", null);
    }

    private void RaiseProgress(string activity, string? message)
    {
        ProgressUpdated?.Invoke(this, new WorkerProgressEvent
        {
            ChunkId = Chunk.ChunkId,
            ChunkTitle = Chunk.Title,
            WorkerIndex = Chunk.SequenceIndex,
            WorkerRole = Chunk.AssignedRole,
            WorkerStatus = Status,
            CurrentActivity = activity,
            RetryAttempt = Chunk.RetryCount,
            Message = message ?? activity,
            TimestampUtc = DateTime.UtcNow
        });
    }

    private async Task LogEntryAsync(
        OrchestrationLogLevel level,
        OrchestratorEventType eventType,
        string message)
    {
        try
        {
            var entry = new LogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Level = level,
                EventType = eventType,
                ChunkId = Chunk.ChunkId,
                PlanId = _planId,
                Role = Chunk.AssignedRole,
                Source = $"Worker:{WorkerId}",
                Message = message
            };

            await _logStore.SaveLogEntryAsync(_planId, Chunk.ChunkId, entry);
        }
        catch (Exception ex)
        {
            // Logging failures should never crash the worker
            _logger.LogWarning(ex, "Failed to persist log entry for worker {WorkerId}", WorkerId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Terminate the Copilot process for this session if one was created
        if (_session != null)
        {
            try
            {
                _copilotService.TerminateSessionProcess(_session.SessionId);
                _logger.LogDebug("Worker {WorkerId} disposed session {SessionId}", WorkerId, _session.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to terminate session {SessionId} for worker {WorkerId}",
                    _session.SessionId, WorkerId);
            }
        }

        await ValueTask.CompletedTask;
    }
}