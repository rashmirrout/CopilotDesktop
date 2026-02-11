// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CopilotAgent.Panel.Models;

/// <summary>
/// The discussion plan created by the Head Agent after clarifying the user's question.
/// Presented to the user for approval before the panel discussion begins.
/// Contains the refined topic, selected panelists, and estimated resource usage.
/// </summary>
public sealed class PanelDiscussionPlan
{
    /// <summary>The refined, clarified topic for the panel discussion.</summary>
    public required string RefinedTopic { get; init; }

    /// <summary>Key aspects or sub-questions the panel should address.</summary>
    public required IReadOnlyList<string> KeyAspects { get; init; }

    /// <summary>The panelist profiles selected for this discussion.</summary>
    public required IReadOnlyList<PanelistProfile> SelectedPanelists { get; init; }

    /// <summary>Estimated number of discussion rounds.</summary>
    public int EstimatedRounds { get; init; }

    /// <summary>Estimated total token usage for the discussion.</summary>
    public long EstimatedTokens { get; init; }

    /// <summary>Estimated cost in USD.</summary>
    public decimal EstimatedCostUsd { get; init; }

    /// <summary>Any special instructions or constraints for the discussion.</summary>
    public string? SpecialInstructions { get; init; }

    /// <summary>When this plan was created.</summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}