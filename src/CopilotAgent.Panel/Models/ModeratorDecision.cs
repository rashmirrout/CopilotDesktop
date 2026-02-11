// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CopilotAgent.Panel.Models;

/// <summary>
/// Represents the Moderator's turn-level decision after evaluating the discussion state.
/// Contains speaker selection, convergence assessment, and optional redirection.
///
/// DESIGN: This is a TURN-LEVEL decision (who speaks next, should we stop?).
/// For MESSAGE-LEVEL moderation (approve/block), see <see cref="ModerationResult"/>.
///
/// FAIL-OPEN: If the LLM response cannot be parsed, use <see cref="Fallback"/>
/// which continues discussion with round-robin (no forced stop).
/// </summary>
public sealed class ModeratorDecision
{
    /// <summary>
    /// Name of the panelist who should speak next, or null for round-robin.
    /// Set by the Moderator when a specific panelist can add the most value.
    /// </summary>
    public string? NextSpeaker { get; init; }

    /// <summary>
    /// Convergence score from 0–100 (100 = fully converged).
    /// Values ≥ 80 typically indicate substantial agreement among panelists.
    /// </summary>
    public int ConvergenceScore { get; init; }

    /// <summary>
    /// Whether the discussion should end (convergence reached or resource limits hit).
    /// </summary>
    public bool StopDiscussion { get; init; }

    /// <summary>
    /// Human-readable explanation for the moderation decision.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Optional message to refocus the discussion if it has drifted off-topic.
    /// Null when the discussion is on-track.
    /// </summary>
    public string? RedirectMessage { get; init; }

    /// <summary>
    /// Whether the Moderator recommends parallel thinking for this turn.
    /// True when the Moderator determines that selected panelists can
    /// reason independently on orthogonal aspects.
    /// </summary>
    public bool AllowParallelThinking { get; init; }

    /// <summary>
    /// Names of panelists that should think in parallel (2-3 agents).
    /// Non-empty only when <see cref="AllowParallelThinking"/> is true.
    /// Agents think concurrently but their messages are recorded sequentially.
    /// </summary>
    public IReadOnlyList<string> ParallelGroup { get; init; } = [];

    /// <summary>
    /// Moderator's rationale for choosing parallel vs sequential execution.
    /// Null when parallel thinking is not recommended.
    /// </summary>
    public string? ParallelRationale { get; init; }

    /// <summary>
    /// Create a fail-open fallback decision: continue discussion, round-robin, zero convergence.
    /// Used when the LLM response cannot be parsed into a structured decision.
    /// </summary>
    /// <param name="reason">Explanation for why fallback was triggered.</param>
    /// <returns>A safe default decision that continues the discussion.</returns>
    public static ModeratorDecision Fallback(string reason) => new()
    {
        NextSpeaker = null,
        ConvergenceScore = 0,
        StopDiscussion = false,
        Reason = $"[Fallback] {reason}",
        RedirectMessage = null,
        AllowParallelThinking = false,
        ParallelGroup = [],
        ParallelRationale = null
    };

    /// <summary>
    /// Create a decision that forces convergence (end of discussion).
    /// </summary>
    /// <param name="reason">Explanation for forcing convergence.</param>
    /// <param name="convergenceScore">The convergence score at the time of decision.</param>
    /// <returns>A decision that stops the discussion.</returns>
    public static ModeratorDecision ForceConverge(string reason, int convergenceScore = 100) => new()
    {
        NextSpeaker = null,
        ConvergenceScore = Math.Clamp(convergenceScore, 0, 100),
        StopDiscussion = true,
        Reason = reason,
        RedirectMessage = null,
        AllowParallelThinking = false,
        ParallelGroup = [],
        ParallelRationale = null
    };
}

/// <summary>
/// Result of a message-level moderation evaluation.
/// Used by the Moderator to approve, block, or redirect individual panelist messages.
///
/// DESIGN: This is a MESSAGE-LEVEL decision (should this message enter the stream?).
/// For TURN-LEVEL decisions (who speaks next?), see <see cref="ModeratorDecision"/>.
/// </summary>
/// <param name="Action">The moderation action taken.</param>
/// <param name="Reason">Explanation for the moderation decision.</param>
public sealed record ModerationResult(ModerationAction Action, string? Reason)
{
    /// <summary>Message is approved to proceed.</summary>
    public static ModerationResult Approved() =>
        new(ModerationAction.Approved, null);

    /// <summary>Message is blocked from the discussion.</summary>
    public static ModerationResult Blocked(string reason) =>
        new(ModerationAction.Blocked, reason);

    /// <summary>Conversation should be redirected.</summary>
    public static ModerationResult Redirect(string reason) =>
        new(ModerationAction.Redirect, reason);

    /// <summary>Discussion should be forced toward convergence.</summary>
    public static ModerationResult ForceConverge(string reason) =>
        new(ModerationAction.ForceConverge, reason);

    /// <summary>Convergence has been detected by the moderator.</summary>
    public static ModerationResult ConvergenceDetected() =>
        new(ModerationAction.ConvergenceDetected, null);
}

/// <summary>
/// Actions the Moderator can take on a message or discussion state.
/// </summary>
public enum ModerationAction
{
    /// <summary>Message is approved and enters the discussion stream.</summary>
    Approved,

    /// <summary>Message is blocked (off-topic, repetitive, or harmful).</summary>
    Blocked,

    /// <summary>Conversation should be redirected to another agent or topic.</summary>
    Redirect,

    /// <summary>Discussion should be forced toward convergence/synthesis.</summary>
    ForceConverge,

    /// <summary>Convergence has been detected organically.</summary>
    ConvergenceDetected
}