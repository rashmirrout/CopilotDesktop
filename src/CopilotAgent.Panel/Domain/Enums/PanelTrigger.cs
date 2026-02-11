namespace CopilotAgent.Panel.Domain.Enums;

/// <summary>
/// Triggers that cause state transitions in the panel state machine.
/// Each trigger maps to one or more permitted transitions in <see cref="Panel.StateMachine.PanelStateMachine"/>.
/// </summary>
public enum PanelTrigger
{
    // ── User-initiated triggers ──────────────────────────────────────

    /// <summary>User submits a topic prompt. Idle → Clarifying.</summary>
    UserSubmitted,

    /// <summary>User approves the discussion plan. AwaitingApproval → Preparing.</summary>
    UserApproved,

    /// <summary>User rejects the plan, requesting changes. AwaitingApproval → Clarifying.</summary>
    UserRejected,

    /// <summary>User pauses a running discussion. Running → Paused.</summary>
    UserPaused,

    /// <summary>User resumes a paused discussion. Paused → Running.</summary>
    UserResumed,

    /// <summary>User stops the discussion. Running/Paused → Stopped.</summary>
    UserStopped,

    /// <summary>User cancels before the discussion starts. Clarifying/AwaitingApproval → Idle.</summary>
    UserCancelled,

    // ── System / orchestrator triggers ───────────────────────────────

    /// <summary>Head agent finishes clarification and produces a plan. Clarifying → AwaitingApproval.</summary>
    ClarificationsComplete,

    /// <summary>All panelist agents are initialized and ready. Preparing → Running.</summary>
    PanelistsReady,

    /// <summary>A single turn completes (informational; may not change phase).</summary>
    TurnCompleted,

    /// <summary>Convergence detector signals agreement threshold reached. Running → Converging.</summary>
    ConvergenceDetected,

    /// <summary>Moderator confirms convergence and starts synthesis. Converging → Synthesizing.</summary>
    StartSynthesis,

    /// <summary>Convergence was premature; resume debate. Converging → Running.</summary>
    ResumeDebate,

    /// <summary>Synthesis is complete. Synthesizing → Completed.</summary>
    SynthesisComplete,

    /// <summary>Guard-rail timeout fires.</summary>
    Timeout,

    /// <summary>Unrecoverable error in any active phase. Multiple → Failed.</summary>
    Error,

    /// <summary>Reset from any terminal state back to Idle.</summary>
    Reset
}
