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
/// The Head agent manages user interaction and final synthesis.
/// Uses a dedicated Copilot SDK session that persists across the entire discussion.
///
/// KEY DESIGN: The Head's session is LONG-LIVED (reused for clarification, synthesis,
/// and follow-up). Panelist sessions are ephemeral.
///
/// Responsibilities:
///   1. CLARIFICATION — Analyze user prompt, ask targeted questions.
///   2. TOPIC GENERATION — Build "Topic of Discussion" from clarification exchange.
///   3. SYNTHESIS — Aggregate all panelist findings into a final report.
///   4. FOLLOW-UP Q&amp;A — Answer user questions using a Knowledge Brief.
///   5. META-QUESTIONS — Respond to user queries about panel status during execution.
/// </summary>
public sealed class HeadAgent : PanelAgentBase
{
    private readonly ILogger<HeadAgent> _logger;

    /// <summary>
    /// Stored knowledge brief for post-discussion follow-up.
    /// Set after synthesis completes.
    /// </summary>
    internal KnowledgeBrief? KnowledgeBrief { get; set; }

    public override string Name => "Head";
    public override PanelAgentRole Role => PanelAgentRole.Head;

    public HeadAgent(
        ICopilotService copilotService,
        ISubject<PanelEvent> eventStream,
        ILogger<HeadAgent> logger)
        : base(copilotService, eventStream, logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public override async Task<AgentOutput> ProcessAsync(
        AgentInput input, CancellationToken ct = default)
    {
        // Generic processing — for clarification-phase turns routed via the orchestrator
        var history = FormatConversationHistory(input.ConversationHistory, lastN: 15);
        var prompt = $"""
            You are the Head of a multi-agent panel discussion.
            Continue the conversation with the user based on the history below.

            CONVERSATION HISTORY:
            {history}

            Respond helpfully and concisely.
            """;

        var response = await SendToLlmAsync(prompt, ct);
        var message = BuildMessage(
            input.SessionId, response, PanelMessageType.Clarification);

        return new AgentOutput(message, ToolCalls: null, RequestsMoreTurns: false, InternalReasoning: null);
    }

    /// <summary>
    /// Clarification: Analyze user prompt and generate targeted questions.
    /// Returns "CLEAR: No further clarification needed." if the prompt is self-sufficient.
    /// </summary>
    /// <param name="userPrompt">The user's original task/question.</param>
    /// <param name="sessionId">Panel session ID for event routing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Clarification questions or a CLEAR indicator.</returns>
    public async Task<string> ClarifyAsync(
        string userPrompt, PanelSessionId sessionId, CancellationToken ct)
    {
        EmitCommentary("Analyzing user request to identify ambiguities...", sessionId, CommentaryMode.Brief);

        var prompt = $"""
            You are the HEAD of a multi-agent panel discussion system.
            Analyze the following user request and generate 2-5 specific
            clarification questions to ensure the panel can produce
            the most useful analysis.

            Focus on:
            - Scope boundaries (what's in/out of scope)
            - Priority areas
            - Specific concerns or known issues
            - Expected output format
            - Any constraints (time, technology, budget)

            If the request is already crystal clear, respond with:
            "CLEAR: No further clarification needed."

            USER REQUEST:
            {userPrompt}
            """;

        var response = await SendToLlmAsync(prompt, ct);
        EmitCommentary("Clarification analysis complete.", sessionId, CommentaryMode.Brief);
        return response;
    }

    /// <summary>
    /// Process a user reply during the clarification phase.
    /// Continues the clarification dialogue or signals readiness.
    /// </summary>
    /// <param name="userMessage">The user's clarification reply.</param>
    /// <param name="sessionId">Panel session ID for event routing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Follow-up questions or a CLEAR indicator.</returns>
    public async Task<string> ProcessClarificationReplyAsync(
        string userMessage, PanelSessionId sessionId, CancellationToken ct)
    {
        EmitCommentary("Processing user clarification response...", sessionId);

        var prompt = $"""
            The user has provided additional clarification. Based on their response,
            determine if you now have enough information to create a discussion topic,
            or if you need to ask more questions.

            USER RESPONSE:
            {userMessage}

            If you have enough information, respond with:
            "CLEAR: No further clarification needed."

            Otherwise, ask additional targeted questions (max 3).
            """;

        var response = await SendToLlmAsync(prompt, ct);
        return response;
    }

    /// <summary>
    /// Build the Topic of Discussion from the clarification exchange.
    /// This becomes the prompt given to all panelists.
    /// </summary>
    /// <param name="originalPrompt">The user's original request.</param>
    /// <param name="clarificationExchange">The clarification Q&amp;A messages.</param>
    /// <param name="sessionId">Panel session ID for event routing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A comprehensive Topic of Discussion string.</returns>
    public async Task<string> BuildTopicOfDiscussionAsync(
        string originalPrompt,
        IReadOnlyList<Domain.Entities.PanelMessage> clarificationExchange,
        PanelSessionId sessionId,
        CancellationToken ct)
    {
        EmitCommentary("Synthesizing clarifications into discussion topic...", sessionId, CommentaryMode.Brief);

        var exchange = clarificationExchange.Count > 0
            ? string.Join("\n\n",
                clarificationExchange.Select(m => $"**{m.AuthorName}**: {m.Content}"))
            : "(No clarification needed — prompt was clear)";

        var prompt = $"""
            You are the HEAD of a multi-agent panel discussion.

            ORIGINAL USER REQUEST:
            {originalPrompt}

            CLARIFICATION EXCHANGE:
            {exchange}

            Compose a comprehensive "Topic of Discussion" for a panel of expert AI analysts.
            The topic should:
            1. State the exact analysis goal
            2. List specific areas to investigate
            3. Define success criteria
            4. Specify any constraints or boundaries
            5. Indicate what tools/data sources are available
            6. Define the expected output format

            Also recommend the number of panelists (2-6) and their specializations.

            Be thorough but concise. This prompt will guide the entire panel discussion.
            """;

        var response = await SendToLlmAsync(prompt, ct);
        EmitCommentary(
            $"Topic of Discussion ready ({response.Length} chars).",
            sessionId, CommentaryMode.Brief);
        return response;
    }

    /// <summary>
    /// Synthesize all panel findings into a final comprehensive report.
    /// </summary>
    /// <param name="panelMessages">All panelist argument messages from the discussion.</param>
    /// <param name="sessionId">Panel session ID for event routing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The structured synthesis report in Markdown.</returns>
    public async Task<string> SynthesizeAsync(
        IReadOnlyList<Domain.Entities.PanelMessage> panelMessages,
        PanelSessionId sessionId,
        CancellationToken ct)
    {
        EmitCommentary(
            $"Synthesizing {panelMessages.Count} messages into final report...",
            sessionId, CommentaryMode.Brief);

        var messages = string.Join("\n\n---\n\n",
            panelMessages.Select(m =>
                $"**{m.AuthorName}** ({m.AuthorRole}):\n{m.Content}"));

        var prompt = $"""
            The panel discussion has concluded. Below are all panelist contributions:

            {messages}

            Synthesize all findings into a comprehensive final report with:
            1. **Executive Summary** — Key findings in 3-5 bullet points
            2. **Detailed Analysis** — Organized by topic/area
            3. **Agreements** — Points all panelists agreed on
            4. **Disagreements** — Points of contention with different perspectives
            5. **Recommendations** — Concrete, actionable next steps
            6. **Risk Assessment** — Potential risks and mitigations
            7. **Follow-Up Items** — Questions or areas needing further investigation

            Use rich markdown formatting. Be comprehensive and elaborative.
            """;

        var synthesis = await SendToLlmAsync(prompt, ct);
        EmitCommentary("Synthesis complete.", sessionId, CommentaryMode.Brief);
        return synthesis;
    }

    /// <summary>
    /// Answer follow-up questions using the Knowledge Brief.
    /// Available after Completed phase without re-running the panel.
    /// </summary>
    /// <param name="userQuestion">The user's follow-up question.</param>
    /// <param name="sessionId">Panel session ID for event routing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The answer grounded in the Knowledge Brief, or a prompt to start a discussion.</returns>
    public async Task<string> AnswerFollowUpAsync(
        string userQuestion, PanelSessionId sessionId, CancellationToken ct)
    {
        if (KnowledgeBrief is null)
            return "No panel discussion has been completed yet. Please start a discussion first.";

        EmitCommentary(
            $"Answering follow-up: \"{Truncate(userQuestion, 50)}\"",
            sessionId, CommentaryMode.Brief);

        var prompt = $"""
            You are the Head of a completed panel discussion.
            Use the following Knowledge Brief to answer the user's question.

            ## Knowledge Brief
            {KnowledgeBrief.Summary}

            ## Key Arguments
            {string.Join("\n", KnowledgeBrief.KeyArguments.Select(a => $"- {a}"))}

            ## Consensus Points
            {string.Join("\n", KnowledgeBrief.ConsensusPoints.Select(p => $"- {p}"))}

            ## Dissenting Views
            {string.Join("\n", KnowledgeBrief.DissentingViews.Select(v => $"- {v}"))}

            ## User Question
            {userQuestion}

            Answer based on the discussion findings. If the question is outside
            the scope of the completed discussion, say so and suggest starting
            a new panel for that topic.
            """;

        return await SendToLlmAsync(prompt, ct);
    }

    /// <summary>
    /// Handle meta-questions about panel status during execution.
    /// Returns an instant response WITHOUT calling the LLM.
    /// </summary>
    /// <param name="question">The user's meta-question.</param>
    /// <param name="currentTurn">Current turn number in the discussion.</param>
    /// <param name="maxTurns">Maximum turns configured.</param>
    /// <param name="panelistCount">Number of active panelists.</param>
    /// <param name="currentPhase">Current phase of the discussion.</param>
    /// <returns>A formatted status string.</returns>
    public static string HandleMetaQuestion(
        string question,
        int currentTurn,
        int maxTurns,
        int panelistCount,
        PanelPhase currentPhase)
    {
        var remainingTurns = maxTurns - currentTurn;
        var estimatedSeconds = remainingTurns * 15 * Math.Max(1, panelistCount);
        var eta = TimeSpan.FromSeconds(estimatedSeconds);

        return $"""
            **Panel Status**
            - Phase: {currentPhase}
            - Progress: Turn {currentTurn}/{maxTurns}
            - Active Panelists: {panelistCount}
            - Estimated Time Remaining: ~{eta.TotalMinutes:F0} minutes

            The panel is actively discussing your request. You can:
            - **Pause** to temporarily halt discussion
            - **Stop** to end and get partial results
            - Wait for the panel to complete naturally
            """;
    }
}