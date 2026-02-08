namespace CopilotAgent.Office.Models;

/// <summary>
/// Actions taken by the scheduler when deciding how to handle tasks in an iteration.
/// </summary>
public enum SchedulingAction
{
    /// <summary>Task was dispatched to an available assistant.</summary>
    Dispatched,

    /// <summary>Task was queued because all assistants are busy.</summary>
    Queued,

    /// <summary>Task was skipped (e.g., duplicate or low priority).</summary>
    Skipped,

    /// <summary>Task was deferred to the next iteration.</summary>
    Deferred,

    /// <summary>Task was merged with another similar task.</summary>
    Merged
}