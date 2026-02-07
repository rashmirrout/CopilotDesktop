using CopilotAgent.MultiAgent.Events;
using CopilotAgent.MultiAgent.Models;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Represents a single worker agent that executes one work chunk
/// in its own isolated Copilot SDK session and workspace.
/// </summary>
public interface IWorkerAgent : IAsyncDisposable
{
    /// <summary>Unique identifier for this worker instance.</summary>
    string WorkerId { get; }

    /// <summary>The work chunk assigned to this worker.</summary>
    WorkChunk Chunk { get; }

    /// <summary>Current execution status of this worker.</summary>
    AgentStatus Status { get; }

    /// <summary>Runtime execution context with detailed state tracking (V3).</summary>
    ChunkExecutionContext ExecutionContext { get; }

    /// <summary>
    /// Execute the assigned work chunk and return the result.
    /// </summary>
    Task<AgentResult> ExecuteAsync(CancellationToken cancellationToken = default);

    /// <summary>Event raised when the worker reports progress updates.</summary>
    event EventHandler<WorkerProgressEvent>? ProgressUpdated;
}