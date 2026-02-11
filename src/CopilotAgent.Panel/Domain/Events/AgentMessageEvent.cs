using CopilotAgent.Panel.Domain.Entities;
using CopilotAgent.Panel.Domain.ValueObjects;

namespace CopilotAgent.Panel.Domain.Events;

public sealed record AgentMessageEvent(
    PanelSessionId SessionId,
    PanelMessage Message,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);