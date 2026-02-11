// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reactive.Subjects;
using System.Text.Json;
using CopilotAgent.Core.Services;
using CopilotAgent.Panel.Domain.Entities;
using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.Events;
using CopilotAgent.Panel.Domain.Interfaces;
using CopilotAgent.Panel.Domain.Policies;
using CopilotAgent.Panel.Domain.ValueObjects;
using CopilotAgent.Panel.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Panel.Agents;

/// <summary>
/// The Moderator agent manages discussion flow, enforces guard rails,
/// and detects convergence. Produces structured JSON decisions.
///
/// Responsibilities:
///   1. GUARD RAIL ENFORCEMENT — Validate every message against policy limits.
///   2. SPEAKER SELECTION — Decide which panelist speaks next.
///   3. CONVERGENCE EVALUATION — Identify when panelists have substantially agreed.
///   4. FLOW REDIRECTION — Nudge panelists back on topic if discussion drifts.
///   5. RESOURCE MONITORING — Track token consumption, tool calls, and time.
///
/// FAIL-OPEN DESIGN: If the LLM response cannot be parsed into a structured
/// <see cref="ModeratorDecision"/>, we fall back to
/// <see cref="ModeratorDecision.Fallback"/> — continue discussion with all panelists.
/// </summary>
public sealed class ModeratorAgent : PanelAgentBase
{
    private readonly GuardRailPolicy _policy;
    private readonly ILogger<ModeratorAgent> _logger;

    public override string Name => "Moderator";
    public override PanelAgentRole Role => PanelAgentRole.Moderator;

    public ModeratorAgent(
        ICopilotService copilotService,
        ISubject<PanelEvent> eventStream,
        GuardRailPolicy policy,
        ILogger<ModeratorAgent> logger)
        : base(copilotService, eventStream, logger)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _logger = logger;
    }

    /// <inheritdoc/>
    public override async Task<AgentOutput> ProcessAsync(
        AgentInput input, CancellationToken ct = default)
    {
        // Generic moderation processing — evaluate the latest messages
        var decision = await DecideNextTurnAsync(
            input.ConversationHistory, input.CurrentTurn, input.SessionId, ct);

        var content = decision.Reason ?? "Continue discussion.";
        if (decision.RedirectMessage is not null)
            content = $"{content}\n\n**Redirect**: {decision.RedirectMessage}";

        var message = BuildMessage(
            input.SessionId, content, PanelMessageType.ModerationNote);

        return new AgentOutput(
            message,
            ToolCalls: null,
            RequestsMoreTurns: !decision.StopDiscussion,
            InternalReasoning: $"Convergence: {decision.ConvergenceScore}%");
    }

    /// <summary>
    /// Evaluate the discussion and decide the next turn's configuration.
    /// Produces a structured <see cref="ModeratorDecision"/> via LLM JSON output.
    /// </summary>
    /// <param name="messages">Current discussion messages.</param>
    /// <param name="currentTurn">Current turn number.</param>
    /// <param name="sessionId">Panel session ID for event routing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A structured moderation decision.</returns>
    public async Task<ModeratorDecision> DecideNextTurnAsync(
        IReadOnlyList<PanelMessage> messages,
        TurnNumber currentTurn,
        PanelSessionId sessionId,
        CancellationToken ct)
    {
        EmitCommentary(
            $"Evaluating discussion state (turn {currentTurn})...",
            sessionId);

        var recentHistory = FormatConversationHistory(messages, lastN: 10);
        var panelistNames = string.Join(", ",
            messages.Where(m => m.AuthorRole == PanelAgentRole.Panelist)
                .Select(m => m.AuthorName)
                .Distinct());

        var prompt = $$"""
            You are the MODERATOR of a multi-agent panel discussion.
            Evaluate the current state and decide what should happen next.

            GUARD RAILS:
            - Max turns: {{_policy.MaxTurnsPerDiscussion}} (current: {{currentTurn}})
            - Max total tokens: {{_policy.MaxTotalTokens}}
            - Max tool calls per discussion: {{_policy.MaxToolCallsPerDiscussion}}

            ACTIVE PANELISTS: {{panelistNames}}

            RECENT DISCUSSION:
            {{recentHistory}}

            Respond with ONLY a JSON object (no markdown fences):
            {
              "nextSpeaker": "<panelist name or null for round-robin>",
              "convergenceScore": <0-100>,
              "stopDiscussion": <true/false>,
              "reason": "<brief explanation>",
              "redirectMessage": "<optional message to refocus discussion or null>"
            }

            DECISION CRITERIA:
            - convergenceScore >= 80: panelists are substantially in agreement
            - stopDiscussion = true: discussion should end (convergence or resource limits)
            - nextSpeaker: target the panelist who can add most value next (or null for all)
            - redirectMessage: if discussion has drifted off-topic, provide a refocus prompt
            """;

        try
        {
            var response = await SendToLlmAsync(prompt, ct);
            var decision = ParseDecision(response);

            _logger.LogInformation(
                "[Moderator] Turn {Turn}: Convergence={Score}, NextSpeaker={Speaker}, Stop={Stop}",
                currentTurn, decision.ConvergenceScore, decision.NextSpeaker ?? "all", decision.StopDiscussion);

            return decision;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[Moderator] Failed to produce decision — using fallback");
            return ModeratorDecision.Fallback($"LLM error: {ex.Message}");
        }
    }

    /// <summary>
    /// Validate a panelist message against guard rail policy.
    /// Checks for prohibited content, resource limits, and discussion drift.
    /// </summary>
    /// <param name="message">The panelist message to validate.</param>
    /// <param name="currentTurn">Current turn number.</param>
    /// <param name="sessionId">Panel session ID for event routing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A moderation result indicating whether the message is approved, blocked, or requires action.</returns>
    public Task<ModerationResult> ValidateMessageAsync(
        PanelMessage message,
        TurnNumber currentTurn,
        PanelSessionId sessionId,
        CancellationToken ct)
    {
        // Check turn limits
        if (currentTurn.Exceeds(_policy.MaxTurnsPerDiscussion))
        {
            _logger.LogWarning("[Moderator] Turn limit exceeded — forcing convergence");
            EmitCommentary("Turn limit reached. Forcing convergence.", sessionId, CommentaryMode.Brief);
            return Task.FromResult(ModerationResult.ForceConverge("Turn limit exceeded"));
        }

        // Check prohibited content patterns
        foreach (var pattern in _policy.ProhibitedContentPatterns)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(
                message.Content, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                _logger.LogWarning(
                    "[Moderator] Message blocked: matched prohibited pattern '{Pattern}'", pattern);
                return Task.FromResult(ModerationResult.Blocked($"Content matched prohibited pattern: {pattern}"));
            }
        }

        // Check message length (rough token proxy: 4 chars ≈ 1 token)
        var estimatedTokens = message.Content.Length / 4;
        if (estimatedTokens > _policy.MaxTokensPerTurn)
        {
            _logger.LogWarning(
                "[Moderator] Message too long (~{Tokens} tokens, max {Max})",
                estimatedTokens, _policy.MaxTokensPerTurn);
            return Task.FromResult(ModerationResult.Blocked(
                $"Message too long (~{estimatedTokens} tokens, max {_policy.MaxTokensPerTurn})"));
        }

        return Task.FromResult(ModerationResult.Approved());
    }

    /// <summary>
    /// Parse the LLM's JSON response into a structured <see cref="ModeratorDecision"/>.
    /// Falls back to <see cref="ModeratorDecision.Fallback"/> on parse failure.
    /// </summary>
    private static ModeratorDecision ParseDecision(string response)
    {
        try
        {
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start)
                return ModeratorDecision.Fallback("No JSON found in response");

            var json = response[start..(end + 1)];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new ModeratorDecision
            {
                NextSpeaker = root.TryGetProperty("nextSpeaker", out var ns) && ns.ValueKind != JsonValueKind.Null
                    ? ns.GetString()
                    : null,
                ConvergenceScore = root.TryGetProperty("convergenceScore", out var cs)
                    ? Math.Clamp(cs.GetInt32(), 0, 100)
                    : 0,
                StopDiscussion = root.TryGetProperty("stopDiscussion", out var sd) && sd.GetBoolean(),
                Reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null,
                RedirectMessage = root.TryGetProperty("redirectMessage", out var rm) && rm.ValueKind != JsonValueKind.Null
                    ? rm.GetString()
                    : null
            };
        }
        catch (Exception ex)
        {
            return ModeratorDecision.Fallback($"Parse error: {ex.Message}");
        }
    }
}