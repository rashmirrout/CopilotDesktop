namespace CopilotAgent.Panel.Domain.Enums;

/// <summary>
/// The 11-state lifecycle of a panel discussion session.
/// Every user action maps to a state-machine trigger; no "magic" booleans.
/// </summary>
public enum PanelPhase
{
    /// <summary>No active discussion. Waiting for user input.</summary>
    Idle,

    /// <summary>Head is asking clarification questions to refine the task.</summary>
    Clarifying,

    /// <summary>Head has proposed the Topic of Discussion. Awaiting user approval.</summary>
    AwaitingApproval,

    /// <summary>Spinning up panelists, validating models, initializing tools.</summary>
    Preparing,

    /// <summary>Panel discussion is actively running. Panelists are debating.</summary>
    Running,

    /// <summary>User has paused the discussion. Can be resumed.</summary>
    Paused,

    /// <summary>Moderator detected convergence. Final positions being collected.</summary>
    Converging,

    /// <summary>Head is aggregating all findings into a final report.</summary>
    Synthesizing,

    /// <summary>Discussion complete. Head available for follow-up questions.</summary>
    Completed,

    /// <summary>User stopped the discussion. Agents disposed.</summary>
    Stopped,

    /// <summary>Fatal error occurred. Recovery possible via Reset.</summary>
    Failed
}