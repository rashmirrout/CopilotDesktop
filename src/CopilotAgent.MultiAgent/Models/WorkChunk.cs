namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// An atomic unit of work derived from task decomposition.
/// Contains both the definition (immutable after planning) and runtime state.
/// </summary>
public sealed class WorkChunk
{
    /// <summary>Unique identifier for this chunk.</summary>
    public string ChunkId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Zero-based sequence index from the LLM plan output.</summary>
    public int SequenceIndex { get; set; }

    /// <summary>Human-readable title of the chunk.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Self-contained prompt sent to the worker agent.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>ChunkIds this chunk depends on (must complete first).</summary>
    public List<string> DependsOnChunkIds { get; set; } = new();

    /// <summary>Directory/file scope the worker should focus on.</summary>
    public string? WorkingScope { get; set; }

    /// <summary>Skills/tools the worker needs for this chunk.</summary>
    public List<string> RequiredSkills { get; set; } = new();

    /// <summary>Estimated complexity of this chunk.</summary>
    public ChunkComplexity Complexity { get; set; } = ChunkComplexity.Medium;

    /// <summary>Role assigned to the worker for this chunk.</summary>
    public AgentRole AssignedRole { get; set; } = AgentRole.Generic;

    // --- Runtime state (set during execution) ---

    /// <summary>Current execution status.</summary>
    public AgentStatus Status { get; set; } = AgentStatus.Pending;

    /// <summary>Number of retry attempts made.</summary>
    public int RetryCount { get; set; }

    /// <summary>Result from the worker agent after execution.</summary>
    public AgentResult? Result { get; set; }

    /// <summary>Workspace path assigned during execution.</summary>
    public string? AssignedWorkspace { get; set; }

    /// <summary>Copilot SDK session ID assigned during execution.</summary>
    public string? AssignedSessionId { get; set; }

    /// <summary>When execution started.</summary>
    public DateTime? StartedAtUtc { get; set; }

    /// <summary>When execution completed (success or failure).</summary>
    public DateTime? CompletedAtUtc { get; set; }
}

/// <summary>
/// Estimated complexity of a work chunk — used for scheduling heuristics.
/// </summary>
public enum ChunkComplexity
{
    Low,
    Medium,
    High
}