// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reactive.Subjects;
using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.Events;
using CopilotAgent.Panel.Domain.ValueObjects;
using CopilotAgent.Panel.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Panel.Services;

/// <summary>
/// Provides cost estimation and runtime cost tracking for panel discussions.
///
/// TWO MODES:
///   1. <b>Pre-Discussion Estimate</b>: Calculate projected cost before user approval.
///      Shown during <see cref="PanelPhase.AwaitingApproval"/> so the user can make
///      an informed decision about proceeding.
///   2. <b>Runtime Tracking</b>: Accumulate actual token usage as the discussion
///      progresses, emitting <see cref="CostUpdateEvent"/>s through the event stream.
///
/// THREAD SAFETY: All mutable state is accessed via Interlocked operations.
/// Safe for concurrent use from the debate loop and UI reads.
/// </summary>
public sealed class CostEstimationService
{
    private readonly ISubject<PanelEvent> _eventStream;
    private readonly ILogger<CostEstimationService> _logger;

    // ── Runtime tracking (mutable, thread-safe via Interlocked) ──
    private long _totalInputTokens;
    private long _totalOutputTokens;
    private int _turnCount;
    private PanelSessionId _activeSessionId;

    /// <summary>Current accumulated cost estimate based on actual usage.</summary>
    public CostEstimate CurrentCost => new(
        InputTokens: Interlocked.Read(ref _totalInputTokens),
        OutputTokens: Interlocked.Read(ref _totalOutputTokens),
        TotalTokens: Interlocked.Read(ref _totalInputTokens) + Interlocked.Read(ref _totalOutputTokens),
        EstimatedCostUsd: CalculateUsdCost(
            Interlocked.Read(ref _totalInputTokens),
            Interlocked.Read(ref _totalOutputTokens)),
        TurnCount: Volatile.Read(ref _turnCount));

    public CostEstimationService(
        ISubject<PanelEvent> eventStream,
        ILogger<CostEstimationService> logger)
    {
        _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ═══════════════════════════════════════════════════════
    //  PRE-DISCUSSION ESTIMATION
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Calculate an upfront cost estimate before the panel starts.
    /// Shown to the user during <see cref="PanelPhase.AwaitingApproval"/> so they
    /// can decide whether to proceed.
    /// </summary>
    /// <param name="panelistCount">Number of panelists that will participate.</param>
    /// <param name="settings">Current panel settings (maxTurns, etc.).</param>
    /// <returns>A projected cost summary.</returns>
    public PreDiscussionEstimate EstimateBeforeStart(int panelistCount, PanelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Conservative estimates per turn per panelist
        const int avgInputTokensPerTurn = 2000;
        const int avgOutputTokensPerTurn = 800;
        const int moderatorTokensPerTurn = 500;
        const int headClarificationTokens = 3000;
        const int headSynthesisTokens = 5000;
        const int avgSecondsPerTurn = 15;

        var maxTurns = Math.Clamp(settings.MaxTurns, 5, 100);
        var clampedPanelists = Math.Clamp(panelistCount, 1, settings.MaxPanelists);

        // Panelist tokens
        var panelistInputTokens = (long)clampedPanelists * maxTurns * avgInputTokensPerTurn;
        var panelistOutputTokens = (long)clampedPanelists * maxTurns * avgOutputTokensPerTurn;

        // Moderator tokens (one evaluation per turn)
        var moderatorTokens = (long)maxTurns * moderatorTokensPerTurn;

        // Head tokens (clarification + synthesis)
        var headTokens = (long)headClarificationTokens + headSynthesisTokens;

        var totalInput = panelistInputTokens + moderatorTokens + headTokens;
        var totalOutput = panelistOutputTokens + (moderatorTokens / 2) + headTokens;
        var totalTokens = totalInput + totalOutput;

        var estimatedDurationSeconds = clampedPanelists * maxTurns * avgSecondsPerTurn;
        var estimatedCostUsd = CalculateUsdCost(totalInput, totalOutput);

        var estimate = new PreDiscussionEstimate(
            PanelistCount: clampedPanelists,
            EstimatedTurns: maxTurns,
            EstimatedTotalTokens: totalTokens,
            EstimatedInputTokens: totalInput,
            EstimatedOutputTokens: totalOutput,
            EstimatedCostUsd: estimatedCostUsd,
            EstimatedDuration: TimeSpan.FromSeconds(estimatedDurationSeconds),
            Summary: FormatEstimateSummary(clampedPanelists, maxTurns, totalTokens, estimatedDurationSeconds));

        _logger.LogInformation(
            "[CostEstimation] Pre-discussion estimate: {Panelists} panelists × {Turns} turns ≈ {Tokens}K tokens, ~{Duration} min, ${Cost:F4}",
            clampedPanelists, maxTurns, totalTokens / 1000, estimatedDurationSeconds / 60, estimatedCostUsd);

        return estimate;
    }

    // ═══════════════════════════════════════════════════════
    //  RUNTIME COST TRACKING
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Initialize cost tracking for a new session.
    /// Call once when the panel discussion starts.
    /// </summary>
    public void StartTracking(PanelSessionId sessionId)
    {
        _activeSessionId = sessionId;
        Interlocked.Exchange(ref _totalInputTokens, 0);
        Interlocked.Exchange(ref _totalOutputTokens, 0);
        Volatile.Write(ref _turnCount, 0);

        _logger.LogDebug("[CostEstimation] Started tracking for session {SessionId}", sessionId);
    }

    /// <summary>
    /// Record token usage from a single agent turn and emit a cost update event.
    /// </summary>
    /// <param name="inputTokens">Input tokens consumed in this turn.</param>
    /// <param name="outputTokens">Output tokens generated in this turn.</param>
    /// <param name="maxTotalTokens">Maximum total tokens from guard rail policy.</param>
    public void RecordTurn(long inputTokens, long outputTokens, int maxTotalTokens)
    {
        var newInputTotal = Interlocked.Add(ref _totalInputTokens, inputTokens);
        var newOutputTotal = Interlocked.Add(ref _totalOutputTokens, outputTokens);
        var turnNum = Interlocked.Increment(ref _turnCount);

        var totalConsumed = newInputTotal + newOutputTotal;
        var estimatedRemaining = Math.Max(0, maxTotalTokens - (int)Math.Min(totalConsumed, int.MaxValue));

        _eventStream.OnNext(new CostUpdateEvent(
            _activeSessionId,
            TokensConsumedThisTurn: (int)Math.Min(inputTokens + outputTokens, int.MaxValue),
            TotalTokensConsumed: (int)Math.Min(totalConsumed, int.MaxValue),
            EstimatedTokensRemaining: estimatedRemaining,
            DateTimeOffset.UtcNow));

        if (turnNum % 5 == 0 || totalConsumed > maxTotalTokens * 0.8)
        {
            _logger.LogInformation(
                "[CostEstimation] Session {Id} — Turn {Turn}: {Total}K tokens consumed ({Pct:P0} of budget)",
                _activeSessionId, turnNum, totalConsumed / 1000,
                maxTotalTokens > 0 ? (double)totalConsumed / maxTotalTokens : 0);
        }
    }

    /// <summary>
    /// Check whether the current session has exceeded its token budget.
    /// </summary>
    /// <param name="maxTotalTokens">Maximum total tokens from guard rail policy.</param>
    /// <returns>True if the accumulated tokens exceed the budget.</returns>
    public bool IsBudgetExceeded(int maxTotalTokens)
    {
        var total = Interlocked.Read(ref _totalInputTokens) + Interlocked.Read(ref _totalOutputTokens);
        return total > maxTotalTokens;
    }

    /// <summary>
    /// Reset tracking state. Called when a session ends or is reset.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _totalInputTokens, 0);
        Interlocked.Exchange(ref _totalOutputTokens, 0);
        Volatile.Write(ref _turnCount, 0);
        _activeSessionId = default;

        _logger.LogDebug("[CostEstimation] Tracking reset");
    }

    // ── Helpers ──────────────────────────────────────────

    /// <summary>
    /// Calculate estimated USD cost based on approximate GPT-4o pricing.
    /// These rates are conservative estimates; actual pricing depends on the provider.
    /// </summary>
    private static decimal CalculateUsdCost(long inputTokens, long outputTokens)
    {
        // Approximate GPT-4o pricing (per token)
        const decimal inputCostPerToken = 0.000003m;   // $3.00 / 1M tokens
        const decimal outputCostPerToken = 0.000015m;   // $15.00 / 1M tokens

        return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
    }

    private static string FormatEstimateSummary(
        int panelistCount, int maxTurns, long totalTokens, int durationSeconds)
    {
        var durationMinutes = durationSeconds / 60;
        return $"~{panelistCount} panelists × {maxTurns} turns ≈ " +
               $"{totalTokens / 1000}K tokens, ~{durationMinutes} minutes";
    }
}

/// <summary>
/// Pre-discussion cost estimate shown to the user during AwaitingApproval phase.
/// Immutable record — safe to display in UI without threading concerns.
/// </summary>
/// <param name="PanelistCount">Number of panelists in the estimate.</param>
/// <param name="EstimatedTurns">Maximum turns configured.</param>
/// <param name="EstimatedTotalTokens">Projected total tokens (input + output).</param>
/// <param name="EstimatedInputTokens">Projected input tokens.</param>
/// <param name="EstimatedOutputTokens">Projected output tokens.</param>
/// <param name="EstimatedCostUsd">Projected cost in USD.</param>
/// <param name="EstimatedDuration">Projected wall-clock duration.</param>
/// <param name="Summary">Human-readable summary string for UI display.</param>
public sealed record PreDiscussionEstimate(
    int PanelistCount,
    int EstimatedTurns,
    long EstimatedTotalTokens,
    long EstimatedInputTokens,
    long EstimatedOutputTokens,
    decimal EstimatedCostUsd,
    TimeSpan EstimatedDuration,
    string Summary);