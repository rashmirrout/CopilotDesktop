using System.Collections.Generic;

namespace CopilotAgent.Office.Models;

/// <summary>
/// Result produced by an assistant after executing a task.
/// </summary>
public sealed record AssistantResult
{
    /// <summary>ID of the task that produced this result.</summary>
    public required string TaskId { get; init; }

    /// <summary>Index of the assistant that executed the task.</summary>
    public int AssistantIndex { get; init; }

    /// <summary>Whether the task completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>The assistant's response content.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>Summary extracted from the response.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Tool executions captured during the assistant's work.</summary>
    public IReadOnlyList<ToolExecution> ToolExecutions { get; init; } = [];

    /// <summary>Error message if the task failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Wall-clock duration of the task execution.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Timestamp when the result was produced.</summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}