// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using CopilotAgent.Panel.Agents;
using CopilotAgent.Panel.Domain.Entities;
using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.Interfaces;
using CopilotAgent.Panel.Domain.Policies;
using CopilotAgent.Panel.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Panel.Services;

/// <summary>
/// Detects when a panel discussion has reached sufficient convergence.
/// Delegates the actual convergence assessment to the <see cref="ModeratorAgent"/>
/// via its <see cref="ModeratorAgent.DecideNextTurnAsync"/> method, then extracts
/// the convergence score from the structured decision.
///
/// DESIGN:
///   - Checks only run every N turns (configurable check interval) to avoid
///     excessive LLM calls.
///   - Minimum turn threshold prevents premature convergence detection.
///   - Convergence threshold is read from <see cref="GuardRailPolicy"/> or settings.
///   - On parse/error, returns a safe <see cref="ConvergenceResult.Error"/> that
///     allows the discussion to continue (fail-open).
///
/// THREAD SAFETY: Stateless — all state comes from parameters. Safe for concurrent use.
/// </summary>
public sealed class ConvergenceDetector : IConvergenceDetector
{
    private readonly ILogger<ConvergenceDetector> _logger;

    /// <summary>Minimum number of turns before convergence checks begin.</summary>
    private const int MinTurnsBeforeCheck = 4;

    /// <summary>Check convergence every N turns (reduces LLM cost).</summary>
    private const int CheckInterval = 3;

    /// <summary>Default convergence threshold if not specified in policy.</summary>
    private const int DefaultConvergenceThreshold = 80;

    public ConvergenceDetector(ILogger<ConvergenceDetector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<ConvergenceResult> CheckConvergenceAsync(
        IReadOnlyList<PanelMessage> messages,
        TurnNumber currentTurn,
        GuardRailPolicy policy,
        CancellationToken ct = default)
    {
        // Too early — not enough turns for meaningful convergence
        if (currentTurn.Value < MinTurnsBeforeCheck)
        {
            _logger.LogDebug(
                "[ConvergenceDetector] Turn {Turn} < {Min} — too early",
                currentTurn, MinTurnsBeforeCheck);
            return Task.FromResult(ConvergenceResult.NotReady);
        }

        // Not a check turn — skip to reduce LLM calls
        if (currentTurn.Value % CheckInterval != 0)
        {
            _logger.LogDebug(
                "[ConvergenceDetector] Turn {Turn} is not a check turn (interval={Interval})",
                currentTurn, CheckInterval);
            return Task.FromResult(ConvergenceResult.SkippedThisTurn);
        }

        // Hard limit check — if we've exceeded max turns, force convergence
        if (currentTurn.Exceeds(policy.MaxTurnsPerDiscussion))
        {
            _logger.LogWarning(
                "[ConvergenceDetector] Turn {Turn} exceeds max {Max} — forced convergence",
                currentTurn, policy.MaxTurnsPerDiscussion);

            return Task.FromResult(new ConvergenceResult(
                Score: 100,
                IsConverged: true,
                Reason: $"Turn limit reached ({currentTurn}/{policy.MaxTurnsPerDiscussion})",
                Status: ConvergenceCheckStatus.Completed));
        }

        // Heuristic convergence: analyze message patterns without LLM call
        return Task.FromResult(AnalyzeConvergenceHeuristic(messages, currentTurn, policy));
    }

    /// <summary>
    /// Heuristic convergence analysis based on message patterns.
    /// This avoids an extra LLM call — the Moderator's DecideNextTurnAsync
    /// already produces a convergenceScore that the orchestrator can use.
    ///
    /// This method provides a SUPPLEMENTARY signal based on:
    ///   1. Repetition detection (panelists restating similar points)
    ///   2. Agreement signal words ("I agree", "building on", etc.)
    ///   3. Message length decay (shorter messages = less new content)
    ///   4. Unique perspective ratio (fewer new ideas per turn)
    /// </summary>
    private ConvergenceResult AnalyzeConvergenceHeuristic(
        IReadOnlyList<PanelMessage> messages,
        TurnNumber currentTurn,
        GuardRailPolicy policy)
    {
        var panelistMessages = messages
            .Where(m => m.AuthorRole == PanelAgentRole.Panelist)
            .ToList();

        if (panelistMessages.Count < 4)
        {
            return new ConvergenceResult(
                Score: 0,
                IsConverged: false,
                Reason: "Insufficient panelist messages for heuristic analysis",
                Status: ConvergenceCheckStatus.Completed);
        }

        var score = 0;

        // 1. Agreement signal detection
        var agreementSignals = new[]
        {
            "i agree", "building on", "as mentioned", "echoing",
            "consistent with", "aligning with", "in line with",
            "similar to what", "reinforcing", "corroborating"
        };

        var recentMessages = panelistMessages
            .Skip(Math.Max(0, panelistMessages.Count - 6))
            .ToList();

        var agreementCount = recentMessages
            .Count(m => agreementSignals.Any(s =>
                m.Content.Contains(s, StringComparison.OrdinalIgnoreCase)));

        var agreementRatio = recentMessages.Count > 0
            ? (double)agreementCount / recentMessages.Count
            : 0;

        score += (int)(agreementRatio * 40); // Up to 40 points from agreement signals

        // 2. Message length decay (shorter recent messages = running out of new ideas)
        if (recentMessages.Count >= 4)
        {
            var olderAvgLength = recentMessages
                .Take(recentMessages.Count / 2)
                .Average(m => m.Content.Length);
            var newerAvgLength = recentMessages
                .Skip(recentMessages.Count / 2)
                .Average(m => m.Content.Length);

            if (olderAvgLength > 0 && newerAvgLength < olderAvgLength * 0.7)
                score += 20; // Messages getting significantly shorter
            else if (olderAvgLength > 0 && newerAvgLength < olderAvgLength * 0.85)
                score += 10;
        }

        // 3. Turn progress (natural convergence as discussion matures)
        var progressRatio = (double)currentTurn.Value / policy.MaxTurnsPerDiscussion;
        score += (int)(progressRatio * 20); // Up to 20 points from progress

        // 4. Unique author count in recent messages (fewer = less diversity of opinion)
        var recentAuthors = recentMessages.Select(m => m.AuthorName).Distinct().Count();
        var totalPanelists = panelistMessages.Select(m => m.AuthorName).Distinct().Count();
        if (totalPanelists > 0 && recentAuthors == totalPanelists)
            score += 10; // All panelists still actively contributing

        score = Math.Clamp(score, 0, 100);
        var isConverged = score >= DefaultConvergenceThreshold;

        _logger.LogDebug(
            "[ConvergenceDetector] Heuristic score={Score}, Converged={Converged}, " +
            "AgreementRatio={AgreementRatio:F2}, Turn={Turn}/{Max}",
            score, isConverged, agreementRatio, currentTurn, policy.MaxTurnsPerDiscussion);

        return new ConvergenceResult(
            Score: score,
            IsConverged: isConverged,
            Reason: isConverged
                ? $"Heuristic convergence detected (score: {score}/100)"
                : $"Discussion ongoing (heuristic score: {score}/100)",
            Status: ConvergenceCheckStatus.Completed);
    }
}