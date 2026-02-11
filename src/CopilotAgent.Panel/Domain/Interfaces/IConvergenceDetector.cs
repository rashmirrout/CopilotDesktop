// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CopilotAgent.Panel.Domain.Entities;
using CopilotAgent.Panel.Domain.ValueObjects;
using CopilotAgent.Panel.Models;

namespace CopilotAgent.Panel.Domain.Interfaces;

/// <summary>
/// Detects when panel discussion has reached sufficient agreement or exhausted productive debate.
/// Uses the Moderator agent to evaluate convergence based on recent discussion history.
/// Convergence checks are performed at configurable intervals (e.g., every N turns).
/// </summary>
public interface IConvergenceDetector
{
    /// <summary>
    /// Evaluate whether the panel discussion has converged.
    /// </summary>
    /// <param name="messages">The complete conversation history to analyze.</param>
    /// <param name="currentTurn">The current turn number.</param>
    /// <param name="policy">Guard rail policy with convergence thresholds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ConvergenceResult"/> indicating whether convergence has been reached,
    /// the confidence score, and the reason for the determination.
    /// </returns>
    Task<ConvergenceResult> CheckConvergenceAsync(
        IReadOnlyList<PanelMessage> messages,
        TurnNumber currentTurn,
        Domain.Policies.GuardRailPolicy policy,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a convergence check performed by the detector.
/// </summary>
/// <param name="Score">Convergence score from 0-100 (100 = fully converged).</param>
/// <param name="IsConverged">Whether the score exceeds the convergence threshold.</param>
/// <param name="Reason">Human-readable explanation of the convergence determination.</param>
/// <param name="Status">The outcome status of the convergence check operation.</param>
public sealed record ConvergenceResult(
    int Score,
    bool IsConverged,
    string? Reason,
    ConvergenceCheckStatus Status)
{
    /// <summary>Convergence cannot be assessed yet (not enough turns).</summary>
    public static ConvergenceResult NotReady =>
        new(0, false, "Too early to check", ConvergenceCheckStatus.TooEarly);

    /// <summary>This turn is not a convergence check turn (skipped by interval).</summary>
    public static ConvergenceResult SkippedThisTurn =>
        new(0, false, "Not a check turn", ConvergenceCheckStatus.Skipped);

    /// <summary>The convergence response from the Moderator could not be parsed.</summary>
    public static ConvergenceResult ParseFailed =>
        new(0, false, "Failed to parse convergence response", ConvergenceCheckStatus.ParseError);

    /// <summary>An error occurred during convergence detection.</summary>
    public static ConvergenceResult Error(string reason) =>
        new(0, false, reason, ConvergenceCheckStatus.Error);
}

/// <summary>
/// Status of a convergence check operation.
/// </summary>
public enum ConvergenceCheckStatus
{
    /// <summary>Check completed successfully with a valid score.</summary>
    Completed,

    /// <summary>Too few turns have elapsed for a meaningful check.</summary>
    TooEarly,

    /// <summary>This turn was not a scheduled check turn.</summary>
    Skipped,

    /// <summary>The Moderator's response could not be parsed into a score.</summary>
    ParseError,

    /// <summary>An unexpected error occurred during the check.</summary>
    Error
}