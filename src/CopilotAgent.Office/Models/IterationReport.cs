namespace CopilotAgent.Office.Models;

/// <summary>
/// Summary report for a single iteration cycle.
/// </summary>
public sealed record IterationReport
{
    /// <summary>Iteration number (1-based).</summary>
    public int IterationNumber { get; init; }

    /// <summary>Timestamp when the iteration started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Timestamp when the iteration completed.</summary>
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>Total tasks dispatched in this iteration.</summary>
    public int TasksDispatched { get; init; }

    /// <summary>Tasks that completed successfully.</summary>
    public int TasksSucceeded { get; init; }

    /// <summary>Tasks that failed.</summary>
    public int TasksFailed { get; init; }

    /// <summary>Tasks that were cancelled.</summary>
    public int TasksCancelled { get; init; }

    /// <summary>Scheduling decisions made during this iteration.</summary>
    public IReadOnlyList<SchedulingDecision> SchedulingDecisions { get; init; } = [];

    /// <summary>Results from all assistant tasks.</summary>
    public IReadOnlyList<AssistantResult> Results { get; init; } = [];

    /// <summary>Manager's aggregated summary of the iteration (Markdown).</summary>
    public string AggregatedSummary { get; init; } = string.Empty;

    /// <summary>Instructions that were injected during this iteration.</summary>
    public IReadOnlyList<string> InjectedInstructions { get; init; } = [];
}

/// <summary>
/// A single scheduling decision made for a task.
/// </summary>
public sealed record SchedulingDecision
{
    /// <summary>ID of the task this decision applies to.</summary>
    public required string TaskId { get; init; }

    /// <summary>Title of the task.</summary>
    public required string TaskTitle { get; init; }

    /// <summary>The scheduling action taken.</summary>
    public SchedulingAction Action { get; init; }

    /// <summary>Reason for the scheduling decision.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Timestamp of the decision.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}