namespace CopilotAgent.Office.Models;

/// <summary>
/// Mutable state carried by the Manager across iterations.
/// </summary>
public sealed class ManagerContext
{
    /// <summary>The active configuration for this run.</summary>
    public OfficeConfig? Config { get; set; }

    /// <summary>Current phase of the state machine.</summary>
    public ManagerPhase CurrentPhase { get; set; } = ManagerPhase.Idle;

    /// <summary>Current iteration number (1-based).</summary>
    public int CurrentIteration { get; set; }

    /// <summary>The approved plan text (Markdown).</summary>
    public string? ApprovedPlan { get; set; }

    /// <summary>Clarification exchanges that occurred before planning.</summary>
    public List<ClarificationExchange> Clarifications { get; } = [];

    /// <summary>Instructions injected by the user mid-run.</summary>
    public List<string> InjectedInstructions { get; } = [];

    /// <summary>Accumulated iteration reports.</summary>
    public List<IterationReport> IterationReports { get; } = [];

    /// <summary>Timestamp when the run started.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Timestamp when the run ended.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Total tasks completed across all iterations.</summary>
    public int TotalTasksCompleted { get; set; }

    /// <summary>Total tasks failed across all iterations.</summary>
    public int TotalTasksFailed { get; set; }

    /// <summary>Last error message if the run entered Error phase.</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// A single clarification question-answer exchange between Manager and user.
/// </summary>
public sealed record ClarificationExchange
{
    /// <summary>The question asked by the Manager.</summary>
    public required string Question { get; init; }

    /// <summary>The user's response, or null if still pending.</summary>
    public string? Answer { get; set; }

    /// <summary>Timestamp when the question was asked.</summary>
    public DateTimeOffset AskedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Timestamp when the answer was provided.</summary>
    public DateTimeOffset? AnsweredAt { get; set; }
}