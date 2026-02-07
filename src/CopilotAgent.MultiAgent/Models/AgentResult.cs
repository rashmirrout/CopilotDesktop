namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Result returned by a worker agent after executing a work chunk.
/// </summary>
public sealed class AgentResult
{
    /// <summary>The chunk ID this result corresponds to.</summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>Whether the worker completed the chunk successfully.</summary>
    public bool IsSuccess { get; set; }

    /// <summary>The worker's response text (analysis, code, etc.).</summary>
    public string Response { get; set; } = string.Empty;

    /// <summary>Error message if the worker failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Files modified during execution.</summary>
    public List<string> FilesModified { get; set; } = new();

    /// <summary>Tool calls executed during the chunk.</summary>
    public List<ToolCallRecord> ToolCallsExecuted { get; set; } = new();

    /// <summary>Wall-clock duration of the execution.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Approximate token count consumed.</summary>
    public int TokensUsed { get; set; }
}

/// <summary>
/// Record of a single tool call made during worker execution.
/// </summary>
public sealed class ToolCallRecord
{
    /// <summary>Name of the tool invoked.</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Arguments passed to the tool (JSON string).</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>Whether the user approved this tool call.</summary>
    public bool WasApproved { get; set; }

    /// <summary>Result returned by the tool (truncated if large).</summary>
    public string? Result { get; set; }
}