using CopilotAgent.MultiAgent.Models;

namespace CopilotAgent.MultiAgent.Events;

/// <summary>
/// Base event for all orchestration events. Consumed by the UI via event handlers.
/// </summary>
public class OrchestratorEvent
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public OrchestratorEventType EventType { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// All event types emitted during orchestration lifecycle.
/// </summary>
public enum OrchestratorEventType
{
    PlanCreated,
    StageStarted,
    StageCompleted,
    WorkerStarted,
    WorkerProgress,
    WorkerCompleted,
    WorkerFailed,
    WorkerRetrying,
    AggregationStarted,
    AggregationCompleted,
    TaskCompleted,
    TaskFailed,
    TaskAborted,
    FollowUpSent,
    FollowUpReceived,

    // V3: Commentary events for unified chat stream
    OrchestratorCommentary,
    WorkerCommentary,
    WorkerToolInvocation,
    WorkerToolResult,
    WorkerReasoning,
    InjectionReceived,
    InjectionProcessed,
    PhaseChanged,
    ApprovalRequested,
    ApprovalResolved
}

/// <summary>
/// Progress event emitted by individual workers during chunk execution.
/// </summary>
public sealed class WorkerProgressEvent : OrchestratorEvent
{
    public string ChunkId { get; set; } = string.Empty;
    public string ChunkTitle { get; set; } = string.Empty;
    public AgentStatus WorkerStatus { get; set; }
    public string? CurrentActivity { get; set; }
    public int RetryAttempt { get; set; }
    public double? ProgressPercent { get; set; }

    /// <summary>Worker index for color mapping in the UI.</summary>
    public int WorkerIndex { get; set; }

    /// <summary>Role assigned to this worker.</summary>
    public AgentRole WorkerRole { get; set; }
}

/// <summary>
/// Event emitted when the entire orchestration completes or is aborted.
/// </summary>
public sealed class OrchestrationCompletedEvent : OrchestratorEvent
{
    public ConsolidatedReport? Report { get; set; }
    public bool WasAborted { get; set; }
}