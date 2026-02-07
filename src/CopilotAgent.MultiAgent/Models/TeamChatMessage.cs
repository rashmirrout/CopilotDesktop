using CopilotAgent.Core.Models;

namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Chat message model for the Agent Team UI.
/// Completely independent from CopilotAgent.Core.Models.ChatMessage.
/// </summary>
public sealed class TeamChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public TeamMessageSource Source { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string SourceDisplayName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public TeamMessageType MessageType { get; set; }
    public OrchestrationPlan? PlanData { get; set; }
    public ToolApprovalRequest? ApprovalRequest { get; set; }
    public bool IsStreaming { get; set; }
    public string ColorKey { get; set; } = string.Empty;
    public string? ChunkId { get; set; }
    public bool IsCollapsible { get; set; }
    public bool IsExpanded { get; set; }

    /// <summary>Avatar emoji for the source.</summary>
    public string Avatar { get; set; } = "🤖";

    /// <summary>Role badge text (e.g., "MemoryDiag", "Synthesis") — V3.</summary>
    public string? RoleBadge { get; set; }

    /// <summary>Thread/chunk group for message threading.</summary>
    public string? ThreadId { get; set; }
}

public enum TeamMessageSource
{
    User,
    Orchestrator,
    Worker,
    System,
    Injection
}

public enum TeamMessageType
{
    Chat,
    ClarificationRequest,
    PlanReview,
    WorkerCommentary,
    WorkerToolEvent,
    WorkerReasoning,
    ToolApproval,
    UserInjection,
    InjectionResponse,
    PhaseTransition,
    Error,
    Report
}