using CopilotAgent.MultiAgent.Events;
using CopilotAgent.MultiAgent.Models;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Manages a concurrency-limited pool of worker agents.
/// Dispatches work chunks to workers and collects results.
/// </summary>
public interface IAgentPool
{
    /// <summary>
    /// Dispatch a single work chunk to a worker agent.
    /// </summary>
    Task<AgentResult> DispatchAsync(
        WorkChunk chunk,
        string orchestratorSessionId,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatch a batch of work chunks in parallel, respecting MaxParallelSessions.
    /// </summary>
    Task<List<AgentResult>> DispatchBatchAsync(
        List<WorkChunk> chunks,
        string orchestratorSessionId,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>Number of workers currently executing.</summary>
    int ActiveWorkerCount { get; }

    /// <summary>Event raised when a worker reports progress.</summary>
    event EventHandler<WorkerProgressEvent>? WorkerProgress;
}