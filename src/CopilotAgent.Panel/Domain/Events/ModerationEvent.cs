using CopilotAgent.Panel.Domain.ValueObjects;

namespace CopilotAgent.Panel.Domain.Events;

public sealed record ModerationEvent(
    PanelSessionId SessionId,
    string Action,
    string Reason,
    double? ConvergenceScore,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);