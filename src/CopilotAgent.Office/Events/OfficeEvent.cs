using CopilotAgent.Office.Models;

namespace CopilotAgent.Office.Events;

/// <summary>
/// Base class for all Office events. Carries common metadata.
/// </summary>
public abstract class OfficeEvent
{
    /// <summary>The type of this event.</summary>
    public abstract OfficeEventType EventType { get; }

    /// <summary>Timestamp when the event occurred.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Iteration number this event belongs to (0 for pre-iteration).</summary>
    public int IterationNumber { get; init; }

    /// <summary>Human-readable description of the event.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>Raised when the Manager transitions to a new phase.</summary>
public sealed class PhaseChangedEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.PhaseChanged;

    /// <summary>The previous phase.</summary>
    public ManagerPhase PreviousPhase { get; init; }

    /// <summary>The new phase.</summary>
    public ManagerPhase NewPhase { get; init; }
}

/// <summary>Raised for assistant lifecycle events (started, completed, failed).</summary>
public sealed class AssistantEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.AssistantEvent;

    /// <summary>The task associated with this event.</summary>
    public required AssistantTask Task { get; init; }

    /// <summary>The result, if the task completed.</summary>
    public AssistantResult? Result { get; init; }

    /// <summary>Index of the assistant.</summary>
    public int AssistantIndex { get; init; }

    /// <summary>The task status that triggered this event.</summary>
    public AssistantTaskStatus Status { get; init; }
}

/// <summary>Raised when a scheduling decision is made.</summary>
public sealed class SchedulingEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.SchedulingEvent;

    /// <summary>The scheduling decision details.</summary>
    public required SchedulingDecision Decision { get; init; }
}

/// <summary>Raised when an iteration cycle completes.</summary>
public sealed class IterationCompletedEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.IterationCompleted;

    /// <summary>The iteration report.</summary>
    public required IterationReport Report { get; init; }
}

/// <summary>Raised every second during rest countdown.</summary>
public sealed class RestCountdownEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.RestCountdown;

    /// <summary>Seconds remaining in the rest period.</summary>
    public int SecondsRemaining { get; init; }

    /// <summary>Total seconds in the rest period.</summary>
    public int TotalSeconds { get; init; }

    /// <summary>Progress percentage (0-100).</summary>
    public double ProgressPercent => TotalSeconds > 0
        ? Math.Round((1.0 - (double)SecondsRemaining / TotalSeconds) * 100, 1)
        : 100;
}

/// <summary>Raised when a chat message is added to the conversation.</summary>
public sealed class ChatMessageEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.ChatMessage;

    /// <summary>The chat message.</summary>
    public required OfficeChatMessage Message { get; init; }
}

/// <summary>Raised when a clarification is requested or answered.</summary>
public sealed class ClarificationEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.Clarification;

    /// <summary>The clarification exchange.</summary>
    public required ClarificationExchange Exchange { get; init; }

    /// <summary>Whether this is a new question (true) or an answer to a previous question (false).</summary>
    public bool IsQuestion { get; init; }
}

/// <summary>Raised when live commentary is emitted.</summary>
public sealed class CommentaryEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.Commentary;

    /// <summary>The commentary entry.</summary>
    public required LiveCommentary Commentary { get; init; }
}

/// <summary>Raised when an error occurs.</summary>
public sealed class ErrorEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.Error;

    /// <summary>The error message.</summary>
    public required string ErrorMessage { get; init; }

    /// <summary>The exception, if available.</summary>
    public Exception? Exception { get; init; }
}

/// <summary>Raised when the Office run starts.</summary>
public sealed class RunStartedEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.RunStarted;

    /// <summary>The configuration used for this run.</summary>
    public required OfficeConfig Config { get; init; }
}

/// <summary>Raised when the Office run stops.</summary>
public sealed class RunStoppedEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.RunStopped;

    /// <summary>Reason the run stopped.</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>Raised when a user instruction is injected mid-run.</summary>
public sealed class InstructionInjectedEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.InstructionInjected;

    /// <summary>The injected instruction text.</summary>
    public required string Instruction { get; init; }
}

/// <summary>Raised when the Manager requests clarification from the user.</summary>
public sealed class ClarificationRequestedEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.Clarification;

    /// <summary>The clarification question.</summary>
    public required string Question { get; init; }
}

/// <summary>
/// Classification of activity status updates for the live status panel.
/// Drives the status text shown above the input area.
/// </summary>
public enum ActivityStatusType
{
    /// <summary>No active work — panel should be hidden.</summary>
    Idle,

    /// <summary>Manager is thinking/planning (LLM call in progress).</summary>
    ManagerThinking,

    /// <summary>Manager is waiting for user clarification.</summary>
    ManagerClarifying,

    /// <summary>Manager has delegated tasks to assistants.</summary>
    Delegated,

    /// <summary>Assistants are actively working on tasks.</summary>
    AssistantsWorking,

    /// <summary>An assistant finished its task (update active list).</summary>
    AssistantFinished,

    /// <summary>Manager is aggregating/preparing the response.</summary>
    ManagerAggregating,

    /// <summary>Manager is waiting for plan approval.</summary>
    AwaitingApproval,

    /// <summary>Session resting between iterations.</summary>
    Resting
}

/// <summary>
/// Raised when the activity status changes. Consumed by the ViewModel to drive
/// the blinking status panel that shows real-time progress to the user.
/// </summary>
public sealed class ActivityStatusEvent : OfficeEvent
{
    public override OfficeEventType EventType => OfficeEventType.ActivityStatus;

    /// <summary>The type of activity.</summary>
    public required ActivityStatusType StatusType { get; init; }

    /// <summary>Human-readable status message (e.g. "Manager thinking...", "Assistants [1, 2] working...").</summary>
    public required string StatusMessage { get; init; }

    /// <summary>Indices of currently active assistants (empty when not in execution phase).</summary>
    public IReadOnlyList<int> ActiveAssistantIndices { get; init; } = [];

    /// <summary>Total number of assistants dispatched this iteration.</summary>
    public int TotalAssistantsDispatched { get; init; }
}
