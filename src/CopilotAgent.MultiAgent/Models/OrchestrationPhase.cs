namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// State machine phases for the orchestrator.
/// </summary>
public enum OrchestrationPhase
{
    /// <summary>No active task — waiting for user input.</summary>
    Idle,

    /// <summary>Orchestrator is asking clarifying questions.</summary>
    Clarifying,

    /// <summary>Orchestrator is generating an execution plan.</summary>
    Planning,

    /// <summary>Plan is ready and awaiting user approval.</summary>
    AwaitingApproval,

    /// <summary>Workers are executing chunks.</summary>
    Executing,

    /// <summary>Workers are done; results are being aggregated.</summary>
    Aggregating,

    /// <summary>Task is complete; ready for follow-up questions.</summary>
    Completed,

    /// <summary>Task was cancelled by the user.</summary>
    Cancelled
}

/// <summary>
/// User's decision on a plan review.
/// </summary>
public enum PlanApprovalDecision
{
    /// <summary>Approve the plan and begin execution.</summary>
    Approve,

    /// <summary>Request changes to the plan.</summary>
    RequestChanges,

    /// <summary>Reject the plan entirely.</summary>
    Reject
}