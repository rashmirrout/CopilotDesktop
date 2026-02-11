// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CopilotAgent.Panel.Models;

namespace CopilotAgent.Panel.Domain.Interfaces;

/// <summary>
/// Factory for creating panel agent instances.
/// Encapsulates the complexity of agent construction including
/// Copilot SDK session creation, system prompt assembly, and tool configuration.
/// </summary>
public interface IPanelAgentFactory
{
    /// <summary>
    /// Create the Head Agent that interfaces between the user and the panel.
    /// The Head Agent clarifies the user's question, approves plans, and synthesizes final answers.
    /// </summary>
    /// <param name="settings">Panel settings for model and configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A fully configured Head Agent instance.</returns>
    Task<IPanelAgent> CreateHeadAgentAsync(PanelSettings settings, CancellationToken ct = default);

    /// <summary>
    /// Create the Moderator Agent that manages discussion flow and convergence.
    /// The Moderator enforces turn-taking, detects convergence, and ensures productive debate.
    /// </summary>
    /// <param name="settings">Panel settings for model and configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A fully configured Moderator Agent instance.</returns>
    Task<IPanelAgent> CreateModeratorAgentAsync(PanelSettings settings, CancellationToken ct = default);

    /// <summary>
    /// Create a Panelist Agent with a specific expertise profile.
    /// Each panelist brings a unique perspective to the discussion.
    /// </summary>
    /// <param name="profile">The panelist's expertise profile and persona definition.</param>
    /// <param name="settings">Panel settings for model and configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A fully configured Panelist Agent instance.</returns>
    Task<IPanelAgent> CreatePanelistAgentAsync(PanelistProfile profile, PanelSettings settings, CancellationToken ct = default);
}