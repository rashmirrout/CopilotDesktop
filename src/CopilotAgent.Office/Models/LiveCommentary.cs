namespace CopilotAgent.Office.Models;

/// <summary>
/// A single live commentary entry displayed in the side panel.
/// </summary>
public sealed record LiveCommentary
{
    /// <summary>Type of commentary.</summary>
    public CommentaryType Type { get; init; }

    /// <summary>Emoji prefix for display.</summary>
    public string Emoji { get; init; } = "💭";

    /// <summary>Agent name or source identifier.</summary>
    public string AgentName { get; init; } = string.Empty;

    /// <summary>The commentary text.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Timestamp when the commentary was emitted.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Iteration number this commentary belongs to.</summary>
    public int IterationNumber { get; init; }
}