// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reactive.Subjects;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.Panel.Domain.Entities;
using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.Events;
using CopilotAgent.Panel.Domain.Interfaces;
using CopilotAgent.Panel.Domain.ValueObjects;
using CopilotAgent.Panel.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Panel.Agents;

/// <summary>
/// Abstract base class for all panel agents (Head, Moderator, Panelist).
/// Manages the Copilot SDK <see cref="Session"/> lifecycle, provides LLM interaction
/// helpers, and emits structured commentary events.
///
/// LIFECYCLE:
///   1. Constructed by <see cref="PanelAgentFactory"/>
///   2. <see cref="InitializeSessionAsync"/> creates a dedicated Copilot session
///   3. <see cref="ProcessAsync"/> called for each turn by the orchestrator
///   4. <see cref="PauseAsync"/>/<see cref="ResumeAsync"/> for user-controlled pausing
///   5. <see cref="DisposeAsync"/> terminates the Copilot session
///
/// THREAD SAFETY: Each agent owns its own <see cref="Session"/> and
/// <see cref="ICopilotService"/> calls are serialized per-agent.
/// </summary>
public abstract class PanelAgentBase : IPanelAgent
{
    private readonly ICopilotService _copilotService;
    private readonly ISubject<PanelEvent> _eventStream;
    private readonly ILogger _logger;
    private Session? _session;
    private bool _disposed;

    /// <summary>Unique identifier for this agent instance.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Human-readable display name shown in the UI.</summary>
    public abstract string Name { get; }

    /// <summary>The structural role this agent plays in the panel hierarchy.</summary>
    public abstract PanelAgentRole Role { get; }

    /// <summary>Current operational status of the agent.</summary>
    public PanelAgentStatus Status { get; private set; } = PanelAgentStatus.Created;

    /// <summary>
    /// The Copilot SDK session ID (string). Available after <see cref="InitializeSessionAsync"/>.
    /// Used by services that need to reference this agent's session (e.g., ConvergenceDetector).
    /// </summary>
    public string? SessionId => _session?.SessionId;

    protected PanelAgentBase(
        ICopilotService copilotService,
        ISubject<PanelEvent> eventStream,
        ILogger logger)
    {
        _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
        _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initialize this agent's dedicated Copilot SDK session.
    /// Each agent gets its own isolated session for clean conversation separation.
    /// </summary>
    /// <param name="modelId">The model identifier to use (e.g., "gpt-4o").</param>
    /// <param name="systemPrompt">Role-specific system prompt for this agent.</param>
    /// <param name="workingDirectory">Optional working directory for tool access.</param>
    public Task InitializeSessionAsync(
        string modelId,
        string systemPrompt,
        string? workingDirectory = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _session = new Session
        {
            SessionId = $"panel-{Role.ToString().ToLowerInvariant()}-{Id:N}",
            DisplayName = $"Panel: {Name}",
            ModelId = modelId,
            SystemPrompt = systemPrompt,
            WorkingDirectory = workingDirectory,
            IsActive = true,
            AutonomousMode = new AutonomousModeSettings
            {
                AllowAllTools = true,
                AllowAllPaths = true,
                AllowAllUrls = true
            }
        };

        Status = PanelAgentStatus.Active;
        _logger.LogDebug(
            "[{AgentName}] Session initialized: {SessionId}, Model: {Model}",
            Name, _session.SessionId, modelId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public abstract Task<AgentOutput> ProcessAsync(AgentInput input, CancellationToken ct = default);

    /// <inheritdoc/>
    public Task PauseAsync()
    {
        if (Status is PanelAgentStatus.Active or PanelAgentStatus.Idle or PanelAgentStatus.Thinking)
        {
            Status = PanelAgentStatus.Paused;
            _logger.LogDebug("[{AgentName}] Paused", Name);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ResumeAsync()
    {
        if (Status is PanelAgentStatus.Paused)
        {
            Status = PanelAgentStatus.Active;
            _logger.LogDebug("[{AgentName}] Resumed", Name);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Send a message to the LLM and return the complete response text.
    /// Automatically handles status transitions (Thinking → Idle).
    /// </summary>
    /// <param name="message">The user/system message to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The LLM's response content.</returns>
    protected async Task<string> SendToLlmAsync(string message, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_session is null)
            throw new InvalidOperationException(
                $"Agent '{Name}' has not been initialized. Call InitializeSessionAsync first.");

        var previousStatus = Status;
        Status = PanelAgentStatus.Thinking;

        try
        {
            _logger.LogDebug(
                "[{AgentName}] Sending message ({Length} chars) to LLM",
                Name, message.Length);

            var response = await _copilotService.SendMessageAsync(_session, message, ct);
            var content = response.Content ?? string.Empty;

            _logger.LogDebug(
                "[{AgentName}] Received response ({Length} chars)",
                Name, content.Length);

            return content;
        }
        finally
        {
            Status = previousStatus == PanelAgentStatus.Thinking
                ? PanelAgentStatus.Idle
                : previousStatus;
        }
    }

    /// <summary>
    /// Emit a commentary event for the UI's commentary panel.
    /// Respects the minimum commentary level setting.
    /// </summary>
    /// <param name="commentary">The commentary text to emit.</param>
    /// <param name="sessionId">The panel session ID for event routing.</param>
    /// <param name="minimumLevel">
    /// Minimum <see cref="CommentaryMode"/> required for this commentary to be shown.
    /// Defaults to <see cref="CommentaryMode.Detailed"/>.
    /// </param>
    protected void EmitCommentary(
        string commentary,
        PanelSessionId sessionId,
        CommentaryMode minimumLevel = CommentaryMode.Detailed)
    {
        _eventStream.OnNext(new CommentaryEvent(
            sessionId,
            Id,
            Name,
            Role,
            commentary,
            minimumLevel,
            DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Emit an agent status changed event for the UI's agent inspector.
    /// </summary>
    protected void EmitStatusChanged(PanelSessionId sessionId, PanelAgentStatus newStatus)
    {
        _eventStream.OnNext(new AgentStatusChangedEvent(
            sessionId,
            Id,
            Name,
            Role,
            newStatus,
            DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Build a <see cref="PanelMessage"/> from this agent's response.
    /// </summary>
    protected PanelMessage BuildMessage(
        PanelSessionId sessionId,
        string content,
        PanelMessageType type,
        Guid? inReplyTo = null,
        IReadOnlyList<ToolCallRecord>? toolCalls = null)
    {
        return PanelMessage.Create(
            sessionId,
            Id,
            Name,
            Role,
            content,
            type,
            inReplyTo,
            toolCalls);
    }

    /// <summary>
    /// Format conversation history into a string suitable for LLM context.
    /// </summary>
    protected static string FormatConversationHistory(
        IReadOnlyList<PanelMessage> messages,
        int lastN = 20)
    {
        var relevant = messages
            .Skip(Math.Max(0, messages.Count - lastN))
            .Select(m => $"[{m.AuthorName} ({m.AuthorRole})]: {m.Content}");

        return string.Join("\n\n---\n\n", relevant);
    }

    /// <summary>
    /// Truncate a string to a maximum length with ellipsis.
    /// </summary>
    protected static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        Status = PanelAgentStatus.Disposed;

        if (_session is not null)
        {
            try
            {
                _copilotService.TerminateSessionProcess(_session.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{AgentName}] Error terminating session", Name);
            }
            _session = null;
        }

        _logger.LogDebug("[{AgentName}] Disposed", Name);
        return ValueTask.CompletedTask;
    }
}