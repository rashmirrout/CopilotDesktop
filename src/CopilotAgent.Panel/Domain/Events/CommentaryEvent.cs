using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.ValueObjects;

namespace CopilotAgent.Panel.Domain.Events;

public sealed record CommentaryEvent(
    PanelSessionId SessionId,
    Guid AgentId,
    string AgentName,
    PanelAgentRole Role,
    string Commentary,
    CommentaryMode MinimumLevel,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);