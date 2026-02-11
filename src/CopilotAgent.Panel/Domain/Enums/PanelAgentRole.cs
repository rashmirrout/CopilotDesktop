namespace CopilotAgent.Panel.Domain.Enums;

/// <summary>
/// Roles within a panel discussion.
/// Unlike AgentRole in MultiAgent (which represents domain expertise),
/// these represent structural positions in the panel hierarchy.
/// </summary>
public enum PanelAgentRole
{
    /// <summary>Manages user interaction, clarification, and synthesis.</summary>
    Head,

    /// <summary>Enforces guard rails, detects convergence, controls flow.</summary>
    Moderator,

    /// <summary>Provides expert analysis on the discussion topic.</summary>
    Panelist,

    /// <summary>The human user interacting with the Head.</summary>
    User
}