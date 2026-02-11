namespace CopilotAgent.Panel.Domain.ValueObjects;

/// <summary>
/// Token budget for cost management. Tracks per-turn and total limits.
/// </summary>
public readonly record struct TokenBudget(int MaxTokensPerTurn, int MaxTotalTokens)
{
    public bool IsExceeded(int currentTurnTokens, int totalTokens) =>
        currentTurnTokens > MaxTokensPerTurn || totalTokens > MaxTotalTokens;
}