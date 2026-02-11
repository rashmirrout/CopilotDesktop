namespace CopilotAgent.Panel.Domain.Enums;

/// <summary>
/// Controls the verbosity of agent commentary shown in the UI.
/// </summary>
public enum CommentaryMode
{
    /// <summary>Show all reasoning traces, tool calls, and internal decisions.</summary>
    Detailed,

    /// <summary>Show key decisions and tool results only.</summary>
    Brief,

    /// <summary>Show results only, no commentary.</summary>
    Off
}