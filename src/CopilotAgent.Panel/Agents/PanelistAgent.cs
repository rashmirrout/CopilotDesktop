// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reactive.Subjects;
using CopilotAgent.Core.Services;
using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.Events;
using CopilotAgent.Panel.Domain.Interfaces;
using CopilotAgent.Panel.Domain.ValueObjects;
using CopilotAgent.Panel.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Panel.Agents;

/// <summary>
/// A Panelist agent represents a domain expert in the panel discussion.
/// Each panelist has a unique <see cref="PanelistProfile"/> defining their
/// expertise, persona, communication style, and tool access.
///
/// DESIGN:
///   - Each panelist gets its own isolated Copilot SDK session.
///   - The system prompt is built from the profile persona + discussion topic.
///   - Panelists respond to the discussion context with their expert perspective.
///   - Tool calls are gated by <see cref="PanelistProfile.ToolsEnabled"/>.
///
/// LIFECYCLE:
///   1. Created by <see cref="PanelAgentFactory"/> with a specific profile.
///   2. Initialized with a session whose system prompt encodes the persona.
///   3. <see cref="ProcessAsync"/> called each turn by the orchestrator.
///   4. Disposed when the discussion ends or the panelist is removed.
/// </summary>
public sealed class PanelistAgent : PanelAgentBase
{
    private readonly PanelistProfile _profile;
    private readonly ILogger<PanelistAgent> _logger;

    /// <summary>The expertise profile driving this panelist's behavior.</summary>
    public PanelistProfile Profile => _profile;

    public override string Name => _profile.DisplayName;
    public override PanelAgentRole Role => PanelAgentRole.Panelist;

    public PanelistAgent(
        PanelistProfile profile,
        ICopilotService copilotService,
        ISubject<PanelEvent> eventStream,
        ILogger<PanelistAgent> logger)
        : base(copilotService, eventStream, logger)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _logger = logger;
    }

    /// <inheritdoc/>
    public override async Task<AgentOutput> ProcessAsync(
        AgentInput input, CancellationToken ct = default)
    {
        EmitCommentary(
            $"Analyzing discussion from {_profile.Expertise} perspective...",
            input.SessionId, CommentaryMode.Detailed);

        var history = FormatConversationHistory(input.ConversationHistory, lastN: 15);

        var prompt = $$"""
            You are participating in a multi-agent panel discussion as a {{_profile.Expertise}} expert.

            YOUR ROLE: {{_profile.DisplayName}}
            YOUR EXPERTISE: {{_profile.Expertise}}

            DISCUSSION SO FAR:
            {{history}}

            INSTRUCTIONS:
            1. Contribute your expert perspective on the topic under discussion.
            2. Build on points made by other panelists where you agree.
            3. Respectfully challenge points you disagree with, providing evidence or reasoning.
            4. Introduce new angles or considerations unique to your expertise.
            5. Be specific and actionable — avoid generic advice.
            6. Keep your response focused and concise (aim for 300-500 words).

            If another panelist has asked you a direct question, prioritize answering it.
            If the Moderator has provided redirection, follow it.

            Respond as {{_profile.DisplayName}}.
            """;

        var response = await SendToLlmAsync(prompt, ct);

        var message = BuildMessage(
            input.SessionId,
            response,
            PanelMessageType.PanelistArgument);

        _logger.LogDebug(
            "[{AgentName}] Produced argument ({Length} chars)",
            Name, response.Length);

        EmitCommentary(
            $"Contributed {_profile.Expertise} analysis ({response.Length} chars).",
            input.SessionId, CommentaryMode.Detailed);

        return new AgentOutput(
            message,
            ToolCalls: null,
            RequestsMoreTurns: true,
            InternalReasoning: $"Perspective: {_profile.Expertise}");
    }

    /// <summary>
    /// Generate a response to a specific question or redirection from the Moderator.
    /// Used when the Moderator targets this specific panelist for the next turn.
    /// </summary>
    /// <param name="moderatorPrompt">The Moderator's question or redirection message.</param>
    /// <param name="conversationHistory">Recent discussion history for context.</param>
    /// <param name="sessionId">Panel session ID for event routing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The panelist's targeted response.</returns>
    public async Task<string> RespondToDirectionAsync(
        string moderatorPrompt,
        IReadOnlyList<Domain.Entities.PanelMessage> conversationHistory,
        PanelSessionId sessionId,
        CancellationToken ct)
    {
        EmitCommentary(
            $"Responding to Moderator direction...",
            sessionId, CommentaryMode.Brief);

        var history = FormatConversationHistory(conversationHistory, lastN: 10);

        var prompt = $$"""
            You are {{_profile.DisplayName}}, a {{_profile.Expertise}} expert in a panel discussion.

            The Moderator has specifically directed a question or topic to you:

            MODERATOR DIRECTION:
            {{moderatorPrompt}}

            RECENT DISCUSSION CONTEXT:
            {{history}}

            Respond directly to the Moderator's direction from your expert perspective.
            Be specific, evidence-based, and actionable.
            """;

        return await SendToLlmAsync(prompt, ct);
    }

    /// <summary>
    /// Generate a critique of another panelist's argument.
    /// Used during critique-refine rounds to improve discussion quality.
    /// </summary>
    /// <param name="targetArgument">The argument to critique.</param>
    /// <param name="targetAuthor">The author of the argument.</param>
    /// <param name="sessionId">Panel session ID for event routing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A structured critique from this panelist's perspective.</returns>
    public async Task<string> CritiqueAsync(
        string targetArgument,
        string targetAuthor,
        PanelSessionId sessionId,
        CancellationToken ct)
    {
        EmitCommentary(
            $"Critiquing {targetAuthor}'s argument from {_profile.Expertise} perspective...",
            sessionId, CommentaryMode.Detailed);

        var prompt = $$"""
            You are {{_profile.DisplayName}}, a {{_profile.Expertise}} expert.

            Critique the following argument from {{targetAuthor}}:

            ARGUMENT:
            {{targetArgument}}

            Provide a structured critique:
            1. **Strengths**: What aspects are well-reasoned or insightful?
            2. **Weaknesses**: What gaps, errors, or oversights exist?
            3. **Counter-arguments**: What alternative perspectives should be considered?
            4. **Suggestions**: How could the argument be improved?

            Be constructive and professional. Focus on the substance, not the person.
            Keep your critique concise (200-400 words).
            """;

        return await SendToLlmAsync(prompt, ct);
    }
}