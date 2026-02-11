// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CopilotAgent.Panel.Domain.Entities;
using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.ValueObjects;
using CopilotAgent.Panel.Models;

namespace CopilotAgent.Panel.Domain.Interfaces;

/// <summary>
/// Core abstraction for all panel agents (Head, Moderator, Panelist).
/// Each agent processes input and produces output via the Copilot SDK.
/// Implements IAsyncDisposable for deterministic cleanup of agent resources.
/// </summary>
public interface IPanelAgent : IAsyncDisposable
{
    /// <summary>Unique identifier for this agent instance.</summary>
    Guid Id { get; }

    /// <summary>Display name of the agent (e.g., "Security Expert", "Moderator").</summary>
    string Name { get; }

    /// <summary>The role this agent plays in the panel discussion.</summary>
    PanelAgentRole Role { get; }

    /// <summary>Current operational status of the agent.</summary>
    PanelAgentStatus Status { get; }

    /// <summary>
    /// Process input and produce a response. This is the primary interaction point.
    /// </summary>
    /// <param name="input">The input containing conversation history, system prompt, and context.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>The agent's output including its message and any tool calls.</returns>
    Task<AgentOutput> ProcessAsync(AgentInput input, CancellationToken ct = default);

    /// <summary>Pause the agent's processing (blocks at next yield point).</summary>
    Task PauseAsync();

    /// <summary>Resume the agent's processing after a pause.</summary>
    Task ResumeAsync();
}

/// <summary>
/// Input provided to an agent for processing a single turn.
/// Immutable record ensuring thread-safe passing between orchestrator and agents.
/// </summary>
/// <param name="SessionId">The panel session this input belongs to.</param>
/// <param name="ConversationHistory">Ordered list of prior messages in the discussion.</param>
/// <param name="SystemPrompt">Role-specific system prompt for this agent.</param>
/// <param name="CurrentTurn">The current turn number in the discussion.</param>
/// <param name="ToolOutputs">Optional tool execution results from previous turns.</param>
public sealed record AgentInput(
    PanelSessionId SessionId,
    IReadOnlyList<PanelMessage> ConversationHistory,
    string SystemPrompt,
    TurnNumber CurrentTurn,
    IReadOnlyList<string>? ToolOutputs);

/// <summary>
/// Output produced by an agent after processing a turn.
/// Contains the response message, any tool calls made, and metadata.
/// </summary>
/// <param name="Message">The agent's response message.</param>
/// <param name="ToolCalls">Any tool invocations the agent made during processing.</param>
/// <param name="RequestsMoreTurns">Whether the agent believes more discussion is needed.</param>
/// <param name="InternalReasoning">Optional chain-of-thought reasoning (not shown to user by default).</param>
public sealed record AgentOutput(
    PanelMessage Message,
    IReadOnlyList<ToolCallRecord>? ToolCalls,
    bool RequestsMoreTurns,
    string? InternalReasoning);