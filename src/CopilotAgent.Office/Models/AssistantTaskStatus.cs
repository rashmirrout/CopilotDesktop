namespace CopilotAgent.Office.Models;

/// <summary>
/// Lifecycle status of an individual assistant task.
/// </summary>
public enum AssistantTaskStatus
{
    /// <summary>Task is waiting in the queue for an available assistant.</summary>
    Queued,

    /// <summary>Task has been dispatched to an assistant and is actively running.</summary>
    Running,

    /// <summary>Task completed successfully.</summary>
    Completed,

    /// <summary>Task failed after exhausting retries.</summary>
    Failed,

    /// <summary>Task was cancelled before completion.</summary>
    Cancelled,

    /// <summary>Task timed out during execution.</summary>
    TimedOut
}