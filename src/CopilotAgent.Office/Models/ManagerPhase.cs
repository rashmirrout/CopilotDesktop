namespace CopilotAgent.Office.Models;

/// <summary>
/// Represents the current phase of the Office Manager's state machine.
/// Transitions follow: Idle → Clarifying → Planning → AwaitingApproval →
/// FetchingEvents → Scheduling → Executing → Aggregating → Resting → (loop back to FetchingEvents).
/// </summary>
public enum ManagerPhase
{
    /// <summary>No active run. Waiting for user to start.</summary>
    Idle,

    /// <summary>Manager is asking the user clarifying questions before planning.</summary>
    Clarifying,

    /// <summary>Manager is generating an execution plan via LLM.</summary>
    Planning,

    /// <summary>Plan has been generated and is awaiting user approval.</summary>
    AwaitingApproval,

    /// <summary>Manager is fetching events/tasks from the LLM for this iteration.</summary>
    FetchingEvents,

    /// <summary>Manager is scheduling assistant tasks based on fetched events.</summary>
    Scheduling,

    /// <summary>Assistants are actively executing their assigned tasks.</summary>
    Executing,

    /// <summary>Manager is aggregating results from completed assistant tasks.</summary>
    Aggregating,

    /// <summary>Iteration complete; resting before the next iteration cycle.</summary>
    Resting,

    /// <summary>The run has been paused by the user.</summary>
    Paused,

    /// <summary>The run has been stopped (terminal state until reset).</summary>
    Stopped,

    /// <summary>An unrecoverable error has occurred.</summary>
    Error
}