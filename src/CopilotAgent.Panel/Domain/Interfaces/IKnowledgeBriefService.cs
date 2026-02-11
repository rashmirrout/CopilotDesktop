// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CopilotAgent.Panel.Domain.Entities;
using CopilotAgent.Panel.Domain.ValueObjects;
using CopilotAgent.Panel.Models;

namespace CopilotAgent.Panel.Domain.Interfaces;

/// <summary>
/// Service for generating and querying Knowledge Briefs.
/// A Knowledge Brief is a compressed (~2K token) summary of a completed panel discussion
/// that enables efficient follow-up Q&amp;A without replaying the full conversation history.
/// </summary>
public interface IKnowledgeBriefService
{
    /// <summary>
    /// Generate a Knowledge Brief from a completed panel session.
    /// Compresses the full discussion into a structured summary preserving
    /// key arguments, consensus points, dissenting views, and recommendations.
    /// </summary>
    /// <param name="session">The completed panel session to summarize.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A compressed Knowledge Brief suitable for follow-up queries.</returns>
    Task<KnowledgeBrief> GenerateAsync(PanelSession session, CancellationToken ct = default);

    /// <summary>
    /// Answer a follow-up question using the Knowledge Brief as context.
    /// Uses the Head Agent with the brief as compressed context to provide
    /// answers grounded in the panel discussion without full history replay.
    /// </summary>
    /// <param name="brief">The Knowledge Brief from a completed discussion.</param>
    /// <param name="followUpQuestion">The user's follow-up question.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Head Agent's answer grounded in the Knowledge Brief.</returns>
    Task<string> AnswerFollowUpAsync(KnowledgeBrief brief, string followUpQuestion, CancellationToken ct = default);
}

/// <summary>
/// A compressed summary of a completed panel discussion.
/// Designed to fit within ~2K tokens for efficient follow-up Q&amp;A context.
/// </summary>
/// <param name="SessionId">The panel session this brief was generated from.</param>
/// <param name="Topic">The original discussion topic/question.</param>
/// <param name="Summary">High-level summary of the discussion outcome.</param>
/// <param name="KeyArguments">Major arguments made by panelists, grouped by perspective.</param>
/// <param name="ConsensusPoints">Points where panelists reached agreement.</param>
/// <param name="DissentingViews">Points of disagreement that were not resolved.</param>
/// <param name="Recommendations">Actionable recommendations from the panel.</param>
/// <param name="GeneratedAtUtc">When this brief was generated.</param>
public sealed record KnowledgeBrief(
    PanelSessionId SessionId,
    string Topic,
    string Summary,
    IReadOnlyList<string> KeyArguments,
    IReadOnlyList<string> ConsensusPoints,
    IReadOnlyList<string> DissentingViews,
    IReadOnlyList<string> Recommendations,
    DateTime GeneratedAtUtc);