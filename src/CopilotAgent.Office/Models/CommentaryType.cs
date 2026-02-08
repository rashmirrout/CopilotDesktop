namespace CopilotAgent.Office.Models;

/// <summary>
/// Categories of live commentary emitted by the Manager and Assistants.
/// </summary>
public enum CommentaryType
{
    /// <summary>Manager is thinking or reasoning about the next step.</summary>
    ManagerThinking,

    /// <summary>Manager is evaluating clarity of a user instruction.</summary>
    ManagerEvaluating,

    /// <summary>An assistant has started working on a task.</summary>
    AssistantStarted,

    /// <summary>An assistant is making progress on a task.</summary>
    AssistantProgress,

    /// <summary>An assistant has completed a task.</summary>
    AssistantCompleted,

    /// <summary>An assistant encountered an error.</summary>
    AssistantError,

    /// <summary>Scheduling decision commentary.</summary>
    SchedulingDecision,

    /// <summary>Aggregation/summary commentary.</summary>
    Aggregation,

    /// <summary>System-level informational commentary.</summary>
    System
}