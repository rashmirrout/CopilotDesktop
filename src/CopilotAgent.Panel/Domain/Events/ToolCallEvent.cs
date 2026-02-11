using CopilotAgent.Panel.Domain.ValueObjects;

namespace CopilotAgent.Panel.Domain.Events;

public sealed record ToolCallEvent(
    PanelSessionId SessionId,
    Guid AgentId,
    string AgentName,
    string ToolName,
    string Input,
    string? Output,
    bool Succeeded,
    TimeSpan Duration,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);