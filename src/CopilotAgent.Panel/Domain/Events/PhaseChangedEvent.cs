using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.ValueObjects;

namespace CopilotAgent.Panel.Domain.Events;

public sealed record PhaseChangedEvent(
    PanelSessionId SessionId,
    PanelPhase OldPhase,
    PanelPhase NewPhase,
    string? CorrelationId,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);