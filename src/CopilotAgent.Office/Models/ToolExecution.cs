namespace CopilotAgent.Office.Models;

/// <summary>
/// Captures a single tool call and its result during assistant task execution.
/// Populated by <see cref="Services.IAgentEventCollector"/> from SDK session events.
/// </summary>
public sealed record ToolExecution
{
    /// <summary>Name of the tool that was invoked (e.g., "read_file", "execute_command").</summary>
    public required string ToolName { get; init; }

    /// <summary>When the tool invocation started.</summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the tool invocation completed (null if still running or not captured).</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Whether the tool execution succeeded.</summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// Brief description or arguments of the tool call.
    /// Kept short for display purposes; full payloads are in logs.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Wall-clock duration of the tool execution.</summary>
    public TimeSpan Duration => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt
        : TimeSpan.Zero;
}