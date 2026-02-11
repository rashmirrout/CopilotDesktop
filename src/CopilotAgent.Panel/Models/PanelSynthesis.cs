// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CopilotAgent.Panel.Models;

/// <summary>
/// The final synthesis produced by the Head Agent after the panel discussion converges.
/// Contains the consolidated answer, key arguments from each perspective, and actionable recommendations.
/// </summary>
public sealed class PanelSynthesis
{
    /// <summary>The consolidated, synthesized answer to the user's original question.</summary>
    public required string Answer { get; init; }

    /// <summary>Key arguments organized by panelist perspective.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> ArgumentsByPerspective { get; init; }

    /// <summary>Points where all panelists agreed.</summary>
    public required IReadOnlyList<string> ConsensusPoints { get; init; }

    /// <summary>Points of unresolved disagreement between panelists.</summary>
    public required IReadOnlyList<string> DissentingPoints { get; init; }

    /// <summary>Actionable recommendations distilled from the discussion.</summary>
    public required IReadOnlyList<string> Recommendations { get; init; }

    /// <summary>Confidence level of the synthesis (0-100).</summary>
    public int ConfidenceScore { get; init; }

    /// <summary>Areas that may benefit from further investigation.</summary>
    public IReadOnlyList<string>? AreasForFurtherResearch { get; init; }

    /// <summary>When this synthesis was generated.</summary>
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
}