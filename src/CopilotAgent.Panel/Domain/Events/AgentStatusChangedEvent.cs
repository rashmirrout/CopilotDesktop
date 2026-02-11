using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.ValueObjects;

namespace CopilotAgent.Panel.Domain.Events;

public sealed record AgentStatusChangedEvent(
    PanelSessionId SessionId,
    Guid AgentId,
    string AgentName,
    PanelAgentRole Role,
    PanelAgentStatus NewStatus,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);