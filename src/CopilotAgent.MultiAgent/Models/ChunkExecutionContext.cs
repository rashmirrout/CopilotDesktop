namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Runtime execution context for a single chunk. Separates runtime state from
/// the WorkChunk definition, enabling clean observability and replay.
/// </summary>
public sealed class ChunkExecutionContext
{
    /// <summary>The chunk being executed.</summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>Role assigned to this chunk's worker.</summary>
    public AgentRole AssignedRole { get; set; } = AgentRole.Generic;

    /// <summary>Current execution status.</summary>
    public AgentStatus Status { get; set; } = AgentStatus.Pending;

    /// <summary>The worker session ID (Copilot SDK session).</summary>
    public string? WorkerSessionId { get; set; }

    /// <summary>Streaming log entries captured during execution.</summary>
    public List<string> StreamingLogs { get; set; } = new();

    /// <summary>The final result after execution completes.</summary>
    public AgentResult? Result { get; set; }

    /// <summary>When execution started.</summary>
    public DateTime? StartedAtUtc { get; set; }

    /// <summary>When execution completed (success or failure).</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Number of retry attempts made.</summary>
    public int RetryCount { get; set; }

    /// <summary>The workspace path assigned to this chunk.</summary>
    public string? WorkspacePath { get; set; }

    /// <summary>Tool calls made during this execution.</summary>
    public List<ToolCallRecord> ToolCalls { get; set; } = new();

    /// <summary>Tokens consumed during execution.</summary>
    public int TokensUsed { get; set; }

    /// <summary>Error details if failed.</summary>
    public string? ErrorDetails { get; set; }
}