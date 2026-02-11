// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reactive.Subjects;
using CopilotAgent.Core.Services;
using CopilotAgent.Panel.Domain.Events;
using CopilotAgent.Panel.Domain.Interfaces;
using CopilotAgent.Panel.Domain.Policies;
using CopilotAgent.Panel.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Panel.Agents;

/// <summary>
/// Factory for creating fully-initialized panel agent instances.
/// Encapsulates the complexity of agent construction:
///   1. Agent instantiation with correct dependencies
///   2. System prompt assembly from role definition + persona
///   3. Copilot SDK session initialization with appropriate model
///   4. Guard rail policy injection (Moderator only)
///
/// DESIGN DECISIONS:
///   - Head uses PrimaryModel (needs strongest reasoning for synthesis)
///   - Moderator uses PrimaryModel (needs reliable JSON output for decisions)
///   - Panelists use PanelistModels pool (cost optimization via model diversity)
///   - Each agent gets an isolated session (no cross-contamination)
///
/// THREAD SAFETY: Factory methods are stateless and can be called concurrently.
/// The created agents themselves serialize their own LLM calls.
/// </summary>
public sealed class PanelAgentFactory : IPanelAgentFactory
{
    private readonly ICopilotService _copilotService;
    private readonly ISubject<PanelEvent> _eventStream;
    private readonly ILoggerFactory _loggerFactory;

    public PanelAgentFactory(
        ICopilotService copilotService,
        ISubject<PanelEvent> eventStream,
        ILoggerFactory loggerFactory)
    {
        _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
        _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc/>
    public async Task<IPanelAgent> CreateHeadAgentAsync(
        PanelSettings settings, CancellationToken ct = default)
    {
        var logger = _loggerFactory.CreateLogger<HeadAgent>();
        var agent = new HeadAgent(_copilotService, _eventStream, logger);

        var systemPrompt = BuildHeadSystemPrompt();
        var modelId = ResolveModel(settings.PrimaryModel, "Head");

        await agent.InitializeSessionAsync(
            modelId,
            systemPrompt,
            NormalizeWorkingDirectory(settings.WorkingDirectory));

        logger.LogInformation(
            "[PanelAgentFactory] Created Head agent with model '{Model}'", modelId);

        return agent;
    }

    /// <inheritdoc/>
    public async Task<IPanelAgent> CreateModeratorAgentAsync(
        PanelSettings settings, CancellationToken ct = default)
    {
        var logger = _loggerFactory.CreateLogger<ModeratorAgent>();
        var policy = GuardRailPolicy.FromSettings(settings);
        var agent = new ModeratorAgent(_copilotService, _eventStream, policy, logger);

        var systemPrompt = BuildModeratorSystemPrompt(settings);
        var modelId = ResolveModel(settings.PrimaryModel, "Moderator");

        await agent.InitializeSessionAsync(
            modelId,
            systemPrompt,
            NormalizeWorkingDirectory(settings.WorkingDirectory));

        logger.LogInformation(
            "[PanelAgentFactory] Created Moderator agent with model '{Model}', policy: MaxTurns={MaxTurns}, MaxTokens={MaxTokens}",
            modelId, policy.MaxTurnsPerDiscussion, policy.MaxTotalTokens);

        return agent;
    }

    /// <inheritdoc/>
    public async Task<IPanelAgent> CreatePanelistAgentAsync(
        PanelistProfile profile, PanelSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var logger = _loggerFactory.CreateLogger<PanelistAgent>();
        var agent = new PanelistAgent(profile, _copilotService, _eventStream, logger);

        var systemPrompt = BuildPanelistSystemPrompt(profile);
        var modelId = ResolvePanelistModel(settings, profile);

        await agent.InitializeSessionAsync(
            modelId,
            systemPrompt,
            NormalizeWorkingDirectory(settings.WorkingDirectory));

        logger.LogInformation(
            "[PanelAgentFactory] Created Panelist '{Name}' ({Expertise}) with model '{Model}'",
            profile.DisplayName, profile.Expertise, modelId);

        return agent;
    }

    #region System Prompt Builders

    private static string BuildHeadSystemPrompt() =>
        """
        You are the HEAD of a multi-agent panel discussion system.

        YOUR RESPONSIBILITIES:
        1. CLARIFICATION — When a user submits a question, analyze it and ask
           targeted clarification questions to ensure the panel can produce useful results.
        2. TOPIC GENERATION — Once clarified, compose a comprehensive "Topic of Discussion"
           that guides all panelists.
        3. SYNTHESIS — After the panel discussion concludes, aggregate all panelist findings
           into a single comprehensive, well-structured final report.
        4. FOLLOW-UP — Answer user follow-up questions using the discussion's Knowledge Brief.

        COMMUNICATION STYLE:
        - Be professional, clear, and thorough.
        - When asking clarification questions, be specific about what information you need and why.
        - When synthesizing, preserve nuance — capture agreements AND disagreements.
        - Use rich Markdown formatting in reports.

        You speak directly to the user. The panelists and moderator are your colleagues.
        """;

    private static string BuildModeratorSystemPrompt(PanelSettings settings) =>
        $"""
        You are the MODERATOR of a multi-agent panel discussion.

        YOUR RESPONSIBILITIES:
        1. FLOW CONTROL — Decide which panelist speaks next for maximum value.
        2. CONVERGENCE DETECTION — Monitor when panelists have substantially agreed.
        3. GUARD RAIL ENFORCEMENT — Ensure discussions stay within resource limits.
        4. REDIRECTION — Nudge panelists back on-topic if discussion drifts.

        RESOURCE LIMITS:
        - Max turns: {settings.MaxTurns}
        - Max total tokens: {settings.MaxTotalTokens}
        - Max tool calls: {settings.MaxToolCalls}
        - Max duration: {settings.MaxDurationMinutes} minutes

        DECISION FORMAT:
        When asked to evaluate the discussion, respond with a JSON object containing:
        - nextSpeaker: name of the panelist who should speak next (or null for round-robin)
        - convergenceScore: 0-100 (how much agreement exists)
        - stopDiscussion: true/false (whether to end the discussion)
        - reason: brief explanation
        - redirectMessage: optional message to refocus discussion (or null)

        Always respond with valid JSON when making moderation decisions.
        """;

    private static string BuildPanelistSystemPrompt(PanelistProfile profile) =>
        $$"""
        You are {{profile.DisplayName}}, an expert in {{profile.Expertise}}.

        {{profile.Persona}}

        YOUR ROLE IN THE PANEL:
        - Contribute your unique {{profile.Expertise}} perspective to the discussion.
        - Build on other panelists' points where you agree.
        - Respectfully challenge points you disagree with, providing evidence.
        - Introduce new angles unique to your domain expertise.
        - Be specific and actionable — avoid generic advice.

        COMMUNICATION GUIDELINES:
        - Keep responses focused and concise (300-500 words per turn).
        - Reference specific findings, data, or examples when possible.
        - Acknowledge other panelists by name when building on or challenging their points.
        - If asked a direct question, prioritize answering it.
        - Follow Moderator redirections promptly.

        You are one of several expert panelists. Your goal is productive, evidence-based discussion.
        """;

    #endregion

    #region Model Resolution

    /// <summary>
    /// Resolve the model ID, falling back to a sensible default if empty.
    /// </summary>
    private static string ResolveModel(string configuredModel, string agentRole)
    {
        if (!string.IsNullOrWhiteSpace(configuredModel))
            return configuredModel;

        // Sensible default — the user must configure at least PrimaryModel
        return agentRole switch
        {
            "Head" or "Moderator" => "gpt-4o",
            _ => "gpt-4o-mini"
        };
    }

    /// <summary>
    /// Resolve the model for a panelist. Uses the PanelistModels pool with
    /// round-robin selection for model diversity, falling back to PrimaryModel.
    /// </summary>
    private static string ResolvePanelistModel(PanelSettings settings, PanelistProfile profile)
    {
        // If panelist models are configured, distribute across them
        if (settings.PanelistModels is { Count: > 0 } models)
        {
            // Stable selection based on profile ID hash for deterministic assignment
            var index = Math.Abs(profile.Id.GetHashCode()) % models.Count;
            return models[index];
        }

        // Fall back to primary model
        return ResolveModel(settings.PrimaryModel, "Panelist");
    }

    #endregion

    /// <summary>
    /// Normalize working directory: return null if empty/whitespace.
    /// </summary>
    private static string? NormalizeWorkingDirectory(string? workingDirectory) =>
        string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory;
}