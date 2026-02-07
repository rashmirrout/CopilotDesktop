using System.Collections.Concurrent;
using CopilotAgent.Core.Services;
using CopilotAgent.MultiAgent.Events;
using CopilotAgent.MultiAgent.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Manages a pool of worker agents with concurrency control via SemaphoreSlim.
/// Dispatches work chunks to workers, handles retry logic with re-prompting,
/// and provides batch execution respecting MaxParallelSessions.
/// </summary>
public sealed class AgentPool : IAgentPool
{
    private readonly ICopilotService _copilotService;
    private readonly IAgentRoleProvider _roleProvider;
    private readonly IApprovalQueue _approvalQueue;
    private readonly ITaskLogStore _logStore;
    private readonly Func<WorkspaceStrategyType, IWorkspaceStrategy> _strategyFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AgentPool> _logger;

    private readonly ConcurrentDictionary<string, IWorkerAgent> _activeWorkers = new();
    private SemaphoreSlim? _concurrencyGate;

    /// <summary>
    /// Accumulated results across all dispatched chunks in the current orchestration.
    /// Used to provide dependency results to chunks that depend on previously completed chunks.
    /// </summary>
    private readonly ConcurrentDictionary<string, AgentResult> _accumulatedResults = new();

    /// <summary>Current plan ID for log correlation.</summary>
    private string _currentPlanId = string.Empty;

    public AgentPool(
        ICopilotService copilotService,
        IAgentRoleProvider roleProvider,
        IApprovalQueue approvalQueue,
        ITaskLogStore logStore,
        Func<WorkspaceStrategyType, IWorkspaceStrategy> strategyFactory,
        ILoggerFactory loggerFactory)
    {
        _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
        _roleProvider = roleProvider ?? throw new ArgumentNullException(nameof(roleProvider));
        _approvalQueue = approvalQueue ?? throw new ArgumentNullException(nameof(approvalQueue));
        _logStore = logStore ?? throw new ArgumentNullException(nameof(logStore));
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<AgentPool>();
    }

    public int ActiveWorkerCount => _activeWorkers.Count;

    public event EventHandler<WorkerProgressEvent>? WorkerProgress;

    public async Task<AgentResult> DispatchAsync(
        WorkChunk chunk,
        string orchestratorSessionId,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentException.ThrowIfNullOrWhiteSpace(orchestratorSessionId);
        ArgumentNullException.ThrowIfNull(config);

        var workspaceStrategy = _strategyFactory(config.WorkspaceStrategy);
        var retryPolicy = config.RetryPolicy;
        var attempt = 0;
        AgentResult? lastResult = null;
        var originalPrompt = chunk.Prompt;

        while (attempt <= retryPolicy.MaxRetriesPerChunk)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attempt > 0)
            {
                chunk.Status = AgentStatus.Retrying;
                chunk.RetryCount = attempt;

                _logger.LogInformation(
                    "Retrying chunk {ChunkId} (attempt {Attempt}/{MaxRetries}) after {Delay}s delay",
                    chunk.ChunkId, attempt, retryPolicy.MaxRetriesPerChunk,
                    retryPolicy.RetryDelay.TotalSeconds);

                await Task.Delay(retryPolicy.RetryDelay, cancellationToken);

                // Re-prompt with error context if configured
                if (retryPolicy.RepromptOnRetry && lastResult != null)
                {
                    chunk.Prompt = BuildRetryPrompt(originalPrompt, lastResult);
                }
            }

            // Prepare workspace
            string workspacePath;
            try
            {
                workspacePath = await workspaceStrategy.PrepareWorkspaceAsync(
                    chunk, config.WorkingDirectory, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to prepare workspace for chunk {ChunkId}", chunk.ChunkId);
                chunk.Status = AgentStatus.Failed;
                return new AgentResult
                {
                    ChunkId = chunk.ChunkId,
                    IsSuccess = false,
                    ErrorMessage = $"Workspace preparation failed: {ex.Message}"
                };
            }

            // Build dependency results for this chunk
            var depResults = BuildDependencyResults(chunk);

            var workerId = $"worker-{chunk.ChunkId[..Math.Min(8, chunk.ChunkId.Length)]}-a{attempt}";
            var worker = CreateWorker(workerId, chunk, workspacePath, depResults, config);

            try
            {
                _activeWorkers.TryAdd(workerId, worker);
                worker.ProgressUpdated += OnWorkerProgressUpdated;

                lastResult = await worker.ExecuteAsync(cancellationToken);

                if (lastResult.IsSuccess)
                {
                    // Store result for future dependency lookups
                    _accumulatedResults.TryAdd(chunk.ChunkId, lastResult);

                    // Merge results back
                    try
                    {
                        await workspaceStrategy.MergeResultsAsync(
                            workspacePath, config.WorkingDirectory, chunk, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to merge results for chunk {ChunkId}", chunk.ChunkId);
                    }

                    // Restore original prompt in case it was modified for retry
                    chunk.Prompt = originalPrompt;
                    return lastResult;
                }

                _logger.LogWarning(
                    "Worker {WorkerId} failed chunk {ChunkId}: {Error}",
                    workerId, chunk.ChunkId, lastResult.ErrorMessage);
            }
            finally
            {
                worker.ProgressUpdated -= OnWorkerProgressUpdated;
                _activeWorkers.TryRemove(workerId, out _);
                await worker.DisposeAsync();

                // Cleanup workspace
                try
                {
                    await workspaceStrategy.CleanupWorkspaceAsync(
                        workspacePath, chunk, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to cleanup workspace for chunk {ChunkId}", chunk.ChunkId);
                }
            }

            attempt++;
        }

        // All retries exhausted — restore original prompt
        chunk.Prompt = originalPrompt;

        _logger.LogError(
            "Chunk {ChunkId} failed after {Attempts} attempts",
            chunk.ChunkId, attempt);

        chunk.Status = AgentStatus.Failed;
        return lastResult ?? new AgentResult
        {
            ChunkId = chunk.ChunkId,
            IsSuccess = false,
            ErrorMessage = $"Failed after {attempt} attempts"
        };
    }

    public async Task<List<AgentResult>> DispatchBatchAsync(
        List<WorkChunk> chunks,
        string orchestratorSessionId,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentException.ThrowIfNullOrWhiteSpace(orchestratorSessionId);
        ArgumentNullException.ThrowIfNull(config);

        if (chunks.Count == 0)
        {
            return new List<AgentResult>();
        }

        // Initialize or resize concurrency gate
        EnsureConcurrencyGate(config.MaxParallelSessions);

        _logger.LogInformation(
            "Dispatching batch of {Count} chunks with max parallelism {MaxParallel}",
            chunks.Count, config.MaxParallelSessions);

        var results = new ConcurrentBag<AgentResult>();
        var failureCount = 0;

        var tasks = chunks.Select(chunk => Task.Run(async () =>
        {
            // Acquire concurrency slot
            await _concurrencyGate!.WaitAsync(cancellationToken);
            try
            {
                // Check abort threshold
                if (Volatile.Read(ref failureCount) >= config.RetryPolicy.AbortFailureThreshold)
                {
                    _logger.LogWarning(
                        "Skipping chunk {ChunkId} — abort threshold ({Threshold}) reached",
                        chunk.ChunkId, config.RetryPolicy.AbortFailureThreshold);

                    chunk.Status = AgentStatus.Skipped;
                    results.Add(new AgentResult
                    {
                        ChunkId = chunk.ChunkId,
                        IsSuccess = false,
                        ErrorMessage = "Skipped: abort failure threshold reached"
                    });
                    return;
                }

                var result = await DispatchAsync(chunk, orchestratorSessionId, config, cancellationToken);
                results.Add(result);

                if (!result.IsSuccess)
                {
                    Interlocked.Increment(ref failureCount);
                }
            }
            finally
            {
                _concurrencyGate!.Release();
            }
        }, cancellationToken));

        await Task.WhenAll(tasks);

        var resultList = results.ToList();
        _logger.LogInformation(
            "Batch complete: {Success}/{Total} chunks succeeded",
            resultList.Count(r => r.IsSuccess), chunks.Count);

        return resultList;
    }

    /// <summary>
    /// Set the current plan ID for log correlation.
    /// Called by OrchestratorService before dispatching.
    /// </summary>
    public void SetPlanContext(string planId)
    {
        _currentPlanId = planId ?? string.Empty;
        _accumulatedResults.Clear();
    }

    /// <summary>
    /// Register a result from a prior stage so future chunks can access it as a dependency.
    /// </summary>
    public void RegisterResult(string chunkId, AgentResult result)
    {
        _accumulatedResults.TryAdd(chunkId, result);
    }

    private WorkerAgent CreateWorker(
        string workerId,
        WorkChunk chunk,
        string workspacePath,
        IReadOnlyDictionary<string, AgentResult> dependencyResults,
        MultiAgentConfig config)
    {
        return new WorkerAgent(
            workerId,
            chunk,
            workspacePath,
            dependencyResults,
            _currentPlanId,
            _copilotService,
            _roleProvider,
            _approvalQueue,
            _logStore,
            config,
            _loggerFactory.CreateLogger<WorkerAgent>());
    }

    private static string BuildRetryPrompt(string originalPrompt, AgentResult previousResult)
    {
        var truncatedResponse = string.IsNullOrWhiteSpace(previousResult.Response)
            ? "(none)"
            : previousResult.Response[..Math.Min(2000, previousResult.Response.Length)];

        return $"""
            ## Retry Context
            Your previous attempt at this task failed with the following error:
            ```
            {previousResult.ErrorMessage}
            ```

            Previous partial response (if any):
            {truncatedResponse}

            Please try again, addressing the error above.

            ---

            {originalPrompt}
            """;
    }

    private IReadOnlyDictionary<string, AgentResult> BuildDependencyResults(WorkChunk chunk)
    {
        var deps = new Dictionary<string, AgentResult>();

        foreach (var depId in chunk.DependsOnChunkIds)
        {
            if (_accumulatedResults.TryGetValue(depId, out var result))
            {
                deps[depId] = result;
            }
        }

        return deps;
    }

    private void EnsureConcurrencyGate(int maxParallel)
    {
        if (_concurrencyGate == null)
        {
            _concurrencyGate = new SemaphoreSlim(maxParallel, maxParallel);
        }
    }

    private void OnWorkerProgressUpdated(object? sender, WorkerProgressEvent e)
    {
        WorkerProgress?.Invoke(this, e);
    }
}