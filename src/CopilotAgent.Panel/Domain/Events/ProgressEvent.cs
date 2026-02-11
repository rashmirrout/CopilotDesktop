using CopilotAgent.Panel.Domain.ValueObjects;

namespace CopilotAgent.Panel.Domain.Events;

public sealed record ProgressEvent(
    PanelSessionId SessionId,
    int CompletedTurns,
    int EstimatedTotalTurns,
    int ActivePanelists,
    int DonePanelists,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);