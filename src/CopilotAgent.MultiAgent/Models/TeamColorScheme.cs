namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Color scheme for the Agent Team chat interface.
/// Each source type and worker index has a distinct color for visual identification.
/// </summary>
public static class TeamColorScheme
{
    public static readonly Dictionary<string, string> SourceColors = new()
    {
        ["user"] = "#FFFFFF",          // White
        ["orchestrator"] = "#7B68EE",  // Medium Slate Blue
        ["system"] = "#808080",        // Gray
        ["injection"] = "#FFD700",     // Gold
        ["worker-0"] = "#4FC3F7",      // Light Blue
        ["worker-1"] = "#81C784",      // Light Green
        ["worker-2"] = "#FFB74D",      // Orange
        ["worker-3"] = "#F06292",      // Pink
        ["worker-4"] = "#BA68C8",      // Purple
        ["worker-5"] = "#4DD0E1",      // Cyan
        ["worker-6"] = "#AED581",      // Light Green 2
        ["worker-7"] = "#FF8A65",      // Deep Orange
    };

    /// <summary>
    /// Gets the color for a worker by its index, cycling through 8 colors.
    /// </summary>
    public static string GetWorkerColor(int workerIndex)
    {
        var key = $"worker-{workerIndex % 8}";
        return SourceColors.GetValueOrDefault(key, "#FFFFFF");
    }
}