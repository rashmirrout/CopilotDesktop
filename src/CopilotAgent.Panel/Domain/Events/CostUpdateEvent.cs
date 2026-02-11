using CopilotAgent.Panel.Domain.ValueObjects;

namespace CopilotAgent.Panel.Domain.Events;

public sealed record CostUpdateEvent(
    PanelSessionId SessionId,
    int TokensConsumedThisTurn,
    int TotalTokensConsumed,
    int EstimatedTokensRemaining,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);