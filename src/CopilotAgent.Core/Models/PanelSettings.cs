using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Panel Discussion settings stored in AppSettings.
/// Follows the same snapshot-based dirty tracking pattern as
/// MultiAgentSettings and OfficeSettings.
/// </summary>
public class PanelSettings
{
    /// <summary>Model used for Head and Moderator agents.</summary>
    [JsonPropertyName("primaryModel")]
    public string PrimaryModel { get; set; } = string.Empty;

    /// <summary>Pool of models for panelist agents (random selection).</summary>
    [JsonPropertyName("panelistModels")]
    public List<string> PanelistModels { get; set; } = [];

    /// <summary>Maximum number of panelists per discussion.</summary>
    [JsonPropertyName("maxPanelists")]
    public int MaxPanelists { get; set; } = 5;

    /// <summary>Maximum turns before forced convergence.</summary>
    [JsonPropertyName("maxTurns")]
    public int MaxTurns { get; set; } = 30;

    /// <summary>Maximum discussion duration in minutes.</summary>
    [JsonPropertyName("maxDurationMinutes")]
    public int MaxDurationMinutes { get; set; } = 30;

    /// <summary>Maximum total tokens across all agents.</summary>
    [JsonPropertyName("maxTotalTokens")]
    public int MaxTotalTokens { get; set; } = 100_000;

    /// <summary>Maximum tool calls across the discussion.</summary>
    [JsonPropertyName("maxToolCalls")]
    public int MaxToolCalls { get; set; } = 50;

    /// <summary>Whether panelists can access the file system.</summary>
    [JsonPropertyName("allowFileSystemAccess")]
    public bool AllowFileSystemAccess { get; set; } = true;

    /// <summary>Commentary verbosity: Detailed, Brief, or Off.</summary>
    [JsonPropertyName("commentaryMode")]
    public string CommentaryMode { get; set; } = "Brief";

    /// <summary>Working directory for file system tools.</summary>
    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Enabled MCP servers for panelist tools.</summary>
    [JsonPropertyName("enabledMcpServers")]
    public List<string> EnabledMcpServers { get; set; } = [];

    /// <summary>Convergence score threshold (0-100) to trigger synthesis.</summary>
    [JsonPropertyName("convergenceThreshold")]
    public int ConvergenceThreshold { get; set; } = 80;
}