// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.Panel.Domain.Entities;
using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.Interfaces;
using CopilotAgent.Panel.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Panel.Services;

/// <summary>
/// Generates and queries Knowledge Briefs — compressed (~2K token) summaries
/// of completed panel discussions that enable efficient follow-up Q&amp;A
/// without replaying the full conversation history.
///
/// DESIGN:
///   - Uses the <see cref="ICopilotService"/> to compress discussions via LLM.
///   - Produces structured JSON output for reliable parsing.
///   - Falls back to a simple text summary on parse failure.
///   - Each brief captures: key arguments, consensus, dissent, recommendations.
///
/// THREAD SAFETY: Stateless service — safe for concurrent use.
/// </summary>
public sealed class KnowledgeBriefService : IKnowledgeBriefService
{
    private readonly ICopilotService _copilotService;
    private readonly ILogger<KnowledgeBriefService> _logger;

    public KnowledgeBriefService(
        ICopilotService copilotService,
        ILogger<KnowledgeBriefService> logger)
    {
        _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<KnowledgeBrief> GenerateAsync(
        PanelSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        _logger.LogInformation(
            "[KnowledgeBrief] Generating brief for session {SessionId} ({MessageCount} messages)",
            session.Id, session.Messages.Count);

        var panelistMessages = session.Messages
            .Where(m => m.AuthorRole == PanelAgentRole.Panelist)
            .ToList();

        var synthesisMessage = session.Messages
            .LastOrDefault(m => m.Type == PanelMessageType.Synthesis);

        var messagesText = string.Join("\n\n---\n\n",
            panelistMessages.Select(m => $"**{m.AuthorName}**: {m.Content}"));

        var synthesisText = synthesisMessage?.Content ?? "(No synthesis available)";

        var prompt = $$"""
            You are compressing a multi-agent panel discussion into a Knowledge Brief.
            The brief must be concise (~2000 tokens) yet preserve all critical information.

            ORIGINAL TOPIC: {{session.RefinedTopicOfDiscussion ?? session.OriginalUserPrompt}}

            PANEL DISCUSSION ({{panelistMessages.Count}} messages):
            {{messagesText}}

            SYNTHESIS:
            {{synthesisText}}

            Respond with ONLY a JSON object (no markdown fences):
            {
              "summary": "<high-level summary in 2-3 sentences>",
              "keyArguments": ["<argument 1>", "<argument 2>", ...],
              "consensusPoints": ["<point 1>", "<point 2>", ...],
              "dissentingViews": ["<view 1>", "<view 2>", ...],
              "recommendations": ["<rec 1>", "<rec 2>", ...]
            }

            RULES:
            - Each array should have 3-7 items
            - Each item should be 1-2 sentences
            - Preserve nuance — capture agreements AND disagreements
            - Be specific, not generic
            """;

        try
        {
            // Create a temporary session for the brief generation
            var briefSession = new Session
            {
                SessionId = $"panel-brief-{session.Id}",
                DisplayName = "Knowledge Brief Generator",
                ModelId = "gpt-4o",
                SystemPrompt = "You are a precise summarization engine. Output only valid JSON.",
                IsActive = true,
                AutonomousMode = new AutonomousModeSettings
                {
                    AllowAllTools = false,
                    AllowAllPaths = false,
                    AllowAllUrls = false
                }
            };

            var response = await _copilotService.SendMessageAsync(briefSession, prompt, ct);
            var content = response.Content ?? string.Empty;

            // Terminate the temporary session
            try { _copilotService.TerminateSessionProcess(briefSession.SessionId); }
            catch { /* best effort */ }

            var brief = ParseBrief(session.Id, session.RefinedTopicOfDiscussion ?? session.OriginalUserPrompt, content);

            _logger.LogInformation(
                "[KnowledgeBrief] Generated brief: {KeyArgs} arguments, {Consensus} consensus, {Dissent} dissenting",
                brief.KeyArguments.Count, brief.ConsensusPoints.Count, brief.DissentingViews.Count);

            return brief;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[KnowledgeBrief] Failed to generate structured brief — using fallback");
            return BuildFallbackBrief(session);
        }
    }

    /// <inheritdoc/>
    public async Task<string> AnswerFollowUpAsync(
        KnowledgeBrief brief, string followUpQuestion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(brief);

        _logger.LogInformation(
            "[KnowledgeBrief] Answering follow-up for session {SessionId}: \"{Question}\"",
            brief.SessionId, followUpQuestion.Length > 60 ? followUpQuestion[..60] + "..." : followUpQuestion);

        var briefContext = $"""
            ## Knowledge Brief
            **Topic**: {brief.Topic}
            **Summary**: {brief.Summary}

            ### Key Arguments
            {string.Join("\n", brief.KeyArguments.Select(a => $"- {a}"))}

            ### Consensus Points
            {string.Join("\n", brief.ConsensusPoints.Select(p => $"- {p}"))}

            ### Dissenting Views
            {string.Join("\n", brief.DissentingViews.Select(v => $"- {v}"))}

            ### Recommendations
            {string.Join("\n", brief.Recommendations.Select(r => $"- {r}"))}
            """;

        var prompt = $"""
            You are answering a follow-up question about a completed panel discussion.
            Use ONLY the Knowledge Brief below as context. Do not hallucinate information
            that was not discussed.

            {briefContext}

            ## User's Follow-Up Question
            {followUpQuestion}

            Answer the question based on the discussion findings. If the question is
            outside the scope of the completed discussion, say so clearly and suggest
            starting a new panel for that topic.
            """;

        var session = new Session
        {
            SessionId = $"panel-followup-{brief.SessionId}",
            DisplayName = "Follow-Up Q&A",
            ModelId = "gpt-4o",
            SystemPrompt = "You are the Head of a completed panel discussion, answering follow-up questions.",
            IsActive = true,
            AutonomousMode = new AutonomousModeSettings
            {
                AllowAllTools = false,
                AllowAllPaths = false,
                AllowAllUrls = false
            }
        };

        try
        {
            var response = await _copilotService.SendMessageAsync(session, prompt, ct);
            return response.Content ?? "Unable to generate an answer.";
        }
        finally
        {
            try { _copilotService.TerminateSessionProcess(session.SessionId); }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Parse the LLM's JSON response into a <see cref="KnowledgeBrief"/>.
    /// Falls back to a simple brief on parse failure.
    /// </summary>
    private KnowledgeBrief ParseBrief(PanelSessionId sessionId, string topic, string response)
    {
        try
        {
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start)
                throw new JsonException("No JSON object found in response");

            var json = response[start..(end + 1)];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new KnowledgeBrief(
                SessionId: sessionId,
                Topic: topic,
                Summary: root.TryGetProperty("summary", out var s)
                    ? s.GetString() ?? "" : "",
                KeyArguments: ParseStringArray(root, "keyArguments"),
                ConsensusPoints: ParseStringArray(root, "consensusPoints"),
                DissentingViews: ParseStringArray(root, "dissentingViews"),
                Recommendations: ParseStringArray(root, "recommendations"),
                GeneratedAtUtc: DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[KnowledgeBrief] JSON parse failed — using raw text as summary");
            return new KnowledgeBrief(
                SessionId: sessionId,
                Topic: topic,
                Summary: response.Length > 2000 ? response[..2000] : response,
                KeyArguments: [],
                ConsensusPoints: [],
                DissentingViews: [],
                Recommendations: [],
                GeneratedAtUtc: DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Build a fallback brief when LLM generation fails entirely.
    /// </summary>
    private static KnowledgeBrief BuildFallbackBrief(PanelSession session)
    {
        var synthesis = session.Messages
            .LastOrDefault(m => m.Type == PanelMessageType.Synthesis);

        return new KnowledgeBrief(
            SessionId: session.Id,
            Topic: session.RefinedTopicOfDiscussion ?? session.OriginalUserPrompt,
            Summary: synthesis?.Content ?? "Discussion completed but brief generation failed.",
            KeyArguments: session.Messages
                .Where(m => m.AuthorRole == PanelAgentRole.Panelist)
                .Take(5)
                .Select(m => $"{m.AuthorName}: {(m.Content.Length > 100 ? m.Content[..100] + "..." : m.Content)}")
                .ToList(),
            ConsensusPoints: [],
            DissentingViews: [],
            Recommendations: [],
            GeneratedAtUtc: DateTime.UtcNow);
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        return arr.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }
}