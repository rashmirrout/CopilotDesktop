namespace CopilotAgent.Office.Events;

/// <summary>
/// All event types emitted by the Office system.
/// </summary>
public enum OfficeEventType
{
    /// <summary>Manager phase changed.</summary>
    PhaseChanged,

    /// <summary>Assistant lifecycle event (started, completed, failed).</summary>
    AssistantEvent,

    /// <summary>Scheduling decision made.</summary>
    SchedulingEvent,

    /// <summary>Iteration completed with report.</summary>
    IterationCompleted,

    /// <summary>Rest countdown tick.</summary>
    RestCountdown,

    /// <summary>Chat message added.</summary>
    ChatMessage,

    /// <summary>Clarification requested or answered.</summary>
    Clarification,

    /// <summary>Live commentary emitted.</summary>
    Commentary,

    /// <summary>Error occurred.</summary>
    Error,

    /// <summary>Run started.</summary>
    RunStarted,

    /// <summary>Run stopped.</summary>
    RunStopped,

    /// <summary>Instruction injected.</summary>
    InstructionInjected,

    /// <summary>Activity status changed (for live status panel).</summary>
    ActivityStatus
}
