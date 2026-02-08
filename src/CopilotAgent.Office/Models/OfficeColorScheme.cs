namespace CopilotAgent.Office.Models;

/// <summary>
/// Static color helper for Office UI elements.
/// Provides consistent color assignments for Manager, Assistants, and phases.
/// </summary>
public static class OfficeColorScheme
{
    /// <summary>Manager accent color (blue).</summary>
    public const string ManagerColor = "#4A90D9";

    /// <summary>User message accent color (green).</summary>
    public const string UserColor = "#4CAF50";

    /// <summary>System message accent color (grey).</summary>
    public const string SystemColor = "#888888";

    /// <summary>Error accent color (red).</summary>
    public const string ErrorColor = "#E53935";

    /// <summary>Rest/idle accent color (amber).</summary>
    public const string RestColor = "#FFA726";

    private static readonly string[] AssistantColors =
    [
        "#AB47BC", // Purple
        "#26A69A", // Teal
        "#EF5350", // Red
        "#42A5F5", // Light Blue
        "#66BB6A", // Green
        "#FFA726", // Orange
        "#8D6E63", // Brown
        "#78909C", // Blue Grey
    ];

    /// <summary>
    /// Gets the accent color for an assistant by index.
    /// Colors cycle if more assistants than colors.
    /// </summary>
    public static string GetAssistantColor(int assistantIndex) =>
        AssistantColors[assistantIndex % AssistantColors.Length];

    /// <summary>
    /// Gets a display-friendly color for a Manager phase.
    /// </summary>
    public static string GetPhaseColor(ManagerPhase phase) => phase switch
    {
        ManagerPhase.Idle => "#888888",
        ManagerPhase.Clarifying => "#FFB74D",
        ManagerPhase.Planning => "#4FC3F7",
        ManagerPhase.AwaitingApproval => "#FFD54F",
        ManagerPhase.FetchingEvents => "#4DB6AC",
        ManagerPhase.Scheduling => "#7986CB",
        ManagerPhase.Executing => "#81C784",
        ManagerPhase.Aggregating => "#BA68C8",
        ManagerPhase.Resting => "#FFA726",
        ManagerPhase.Paused => "#A1887F",
        ManagerPhase.Stopped => "#E57373",
        ManagerPhase.Error => "#E53935",
        _ => "#888888"
    };

    /// <summary>
    /// Gets a display-friendly emoji for a Manager phase.
    /// </summary>
    public static string GetPhaseEmoji(ManagerPhase phase) => phase switch
    {
        ManagerPhase.Idle => "⏸️",
        ManagerPhase.Clarifying => "❓",
        ManagerPhase.Planning => "📋",
        ManagerPhase.AwaitingApproval => "✋",
        ManagerPhase.FetchingEvents => "🔍",
        ManagerPhase.Scheduling => "📅",
        ManagerPhase.Executing => "⚡",
        ManagerPhase.Aggregating => "📊",
        ManagerPhase.Resting => "😴",
        ManagerPhase.Paused => "⏯️",
        ManagerPhase.Stopped => "⏹️",
        ManagerPhase.Error => "❌",
        _ => "❔"
    };
}