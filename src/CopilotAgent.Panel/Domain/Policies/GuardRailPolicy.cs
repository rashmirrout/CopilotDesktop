using CopilotAgent.Panel.Models;

namespace CopilotAgent.Panel.Domain.Policies;

/// <summary>
/// First-class domain entity that enforces safety and resource limits on a panel discussion.
/// Created once per session. Immutable after construction.
/// 
/// The Moderator agent checks this policy on every turn. If any limit is exceeded,
/// the Moderator forces convergence or terminates the discussion.
///
/// DEFAULT VALUES are chosen for a typical 30-minute analysis session with 3-5 panelists.
/// Override via PanelSettings for specific use cases.
/// </summary>
public sealed class GuardRailPolicy
{
    /// <summary>Maximum number of turns across all panelists before forced convergence.</summary>
    public int MaxTurnsPerDiscussion { get; init; } = 30;

    /// <summary>Maximum tokens a single panelist can produce in one turn.</summary>
    public int MaxTokensPerTurn { get; init; } = 4000;

    /// <summary>Maximum total tokens across all agents for the entire discussion.</summary>
    public int MaxTotalTokens { get; init; } = 100_000;

    /// <summary>Maximum tool calls a single panelist can make in one turn.</summary>
    public int MaxToolCallsPerTurn { get; init; } = 5;

    /// <summary>Maximum total tool calls across the entire discussion.</summary>
    public int MaxToolCallsPerDiscussion { get; init; } = 50;

    /// <summary>Maximum wall-clock time for the entire discussion.</summary>
    public TimeSpan MaxDiscussionDuration { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Maximum time for a single agent's turn before timeout.</summary>
    public TimeSpan MaxSingleTurnDuration { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>Regex patterns that will cause message blocking if matched.</summary>
    public IReadOnlyList<string> ProhibitedContentPatterns { get; init; } = [];

    /// <summary>Allowed domains for web crawling tools. Empty = all allowed.</summary>
    public IReadOnlyList<string> AllowedDomains { get; init; } = [];

    /// <summary>Whether panelists can access the local file system.</summary>
    public bool AllowFileSystemAccess { get; init; } = true;

    /// <summary>Allowed file paths for file system access. Empty = all allowed.</summary>
    public IReadOnlyList<string> AllowedFilePaths { get; init; } = [];

    /// <summary>Maximum critique-refine iterations per topic before force-accept.</summary>
    public int MaxCritiqueRounds { get; init; } = 2;

    /// <summary>
    /// Creates a policy from PanelSettings with validation.
    /// </summary>
    public static GuardRailPolicy FromSettings(PanelSettings settings)
    {
        return new GuardRailPolicy
        {
            MaxTurnsPerDiscussion = Math.Clamp(settings.MaxTurns, 5, 100),
            MaxTotalTokens = Math.Clamp(settings.MaxTotalTokens, 10_000, 500_000),
            MaxDiscussionDuration = TimeSpan.FromMinutes(
                Math.Clamp(settings.MaxDurationMinutes, 5, 120)),
            MaxToolCallsPerDiscussion = Math.Clamp(settings.MaxToolCalls, 10, 200),
            AllowFileSystemAccess = settings.AllowFileSystemAccess
        };
    }
}