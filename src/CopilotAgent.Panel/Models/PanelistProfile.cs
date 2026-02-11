// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace CopilotAgent.Panel.Models;

/// <summary>
/// Defines a panelist's expertise, persona, and behavioral characteristics.
/// Used by the factory to construct panelist agents with appropriate system prompts.
/// Profiles are serializable for persistence and user customization.
/// </summary>
public sealed class PanelistProfile
{
    /// <summary>Unique identifier for this profile.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name shown in the UI (e.g., "Security Expert").</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The area of expertise this panelist represents.</summary>
    [JsonPropertyName("expertise")]
    public string Expertise { get; init; } = string.Empty;

    /// <summary>
    /// Detailed persona description used in the system prompt.
    /// Defines the panelist's perspective, priorities, and communication style.
    /// </summary>
    [JsonPropertyName("persona")]
    public string Persona { get; init; } = string.Empty;

    /// <summary>
    /// Short icon/emoji identifier for the agent avatar in the UI.
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "🧑‍💼";

    /// <summary>
    /// Color hex code for the agent's messages in the discussion stream.
    /// </summary>
    [JsonPropertyName("color")]
    public string Color { get; init; } = "#6B7280";

    /// <summary>
    /// Priority weighting for turn ordering (higher = speaks earlier in round-robin).
    /// Range: 1-10, default 5.
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; init; } = 5;

    /// <summary>
    /// Whether this panelist is allowed to use tools during the discussion.
    /// </summary>
    [JsonPropertyName("toolsEnabled")]
    public bool ToolsEnabled { get; init; }

    /// <summary>
    /// Optional list of specific tool names this panelist is allowed to invoke.
    /// If null or empty and ToolsEnabled is true, all available tools are permitted.
    /// </summary>
    [JsonPropertyName("allowedTools")]
    public IReadOnlyList<string>? AllowedTools { get; init; }
}