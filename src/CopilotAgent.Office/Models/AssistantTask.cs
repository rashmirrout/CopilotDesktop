namespace CopilotAgent.Office.Models;

/// <summary>
/// Represents a single task to be executed by an assistant.
/// </summary>
public sealed class AssistantTask
{
    /// <summary>Unique identifier for this task.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>The iteration number this task belongs to.</summary>
    public int IterationNumber { get; init; }

    /// <summary>Human-readable title for the task.</summary>
    public required string Title { get; init; }

    /// <summary>The full prompt to send to the assistant.</summary>
    public required string Prompt { get; init; }

    /// <summary>Priority (lower = higher priority). Used for scheduling order.</summary>
    public int Priority { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public AssistantTaskStatus Status { get; set; } = AssistantTaskStatus.Queued;

    /// <summary>Index of the assistant that executed this task (set at dispatch).</summary>
    public int? AssistantIndex { get; set; }

    /// <summary>Number of retry attempts consumed.</summary>
    public int RetryCount { get; set; }

    /// <summary>Timestamp when the task was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Timestamp when execution started.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Timestamp when execution completed (success or failure).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Error message if the task failed.</summary>
    public string? ErrorMessage { get; set; }
}