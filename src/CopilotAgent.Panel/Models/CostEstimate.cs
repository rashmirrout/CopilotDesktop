// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CopilotAgent.Panel.Models;

/// <summary>
/// Tracks token usage and estimated cost for a panel discussion session.
/// Updated incrementally as each agent turn completes.
/// Thread-safe via immutable record semantics — create new instances for updates.
/// </summary>
/// <param name="InputTokens">Total input tokens consumed across all agent calls.</param>
/// <param name="OutputTokens">Total output tokens generated across all agent calls.</param>
/// <param name="TotalTokens">Sum of input and output tokens.</param>
/// <param name="EstimatedCostUsd">Estimated cost in USD based on model pricing.</param>
/// <param name="TurnCount">Number of completed agent turns contributing to this estimate.</param>
public sealed record CostEstimate(
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    decimal EstimatedCostUsd,
    int TurnCount)
{
    /// <summary>A zero-cost estimate for session initialization.</summary>
    public static CostEstimate Zero => new(0, 0, 0, 0m, 0);

    /// <summary>
    /// Create a new estimate by adding token usage from a single agent turn.
    /// </summary>
    /// <param name="inputTokens">Input tokens consumed in this turn.</param>
    /// <param name="outputTokens">Output tokens generated in this turn.</param>
    /// <param name="costPerInputToken">Cost per input token in USD.</param>
    /// <param name="costPerOutputToken">Cost per output token in USD.</param>
    /// <returns>A new CostEstimate with accumulated totals.</returns>
    public CostEstimate AddTurn(
        long inputTokens,
        long outputTokens,
        decimal costPerInputToken = 0.000003m,
        decimal costPerOutputToken = 0.000015m)
    {
        var newInputTokens = InputTokens + inputTokens;
        var newOutputTokens = OutputTokens + outputTokens;
        var turnCost = (inputTokens * costPerInputToken) + (outputTokens * costPerOutputToken);

        return new CostEstimate(
            newInputTokens,
            newOutputTokens,
            newInputTokens + newOutputTokens,
            EstimatedCostUsd + turnCost,
            TurnCount + 1);
    }

    /// <summary>
    /// Check whether the estimated cost exceeds a budget limit.
    /// </summary>
    /// <param name="budgetUsd">Maximum allowed cost in USD.</param>
    /// <returns>True if the estimate exceeds the budget.</returns>
    public bool ExceedsBudget(decimal budgetUsd) => EstimatedCostUsd > budgetUsd;
}