using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Application-wide settings
/// </summary>
public class AppSettings
{
    /// <summary>Default model to use for new sessions</summary>
    [JsonPropertyName("defaultModel")]
    public string DefaultModel { get; set; } = "gpt-4";

    /// <summary>Base path for worktree sessions</summary>
    [JsonPropertyName("worktreeBasePath")]
    public string WorktreeBasePath { get; set; } = 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot-sessions");

    /// <summary>Personal skills folder path</summary>
    [JsonPropertyName("skillsFolder")]
    public string SkillsFolder { get; set; } = 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CopilotAgent", "Skills");

    /// <summary>Command policy</summary>
    [JsonPropertyName("commandPolicy")]
    public CommandPolicy CommandPolicy { get; set; } = new();

    /// <summary>MCP servers configuration</summary>
    [JsonPropertyName("mcpServers")]
    public List<McpServerConfig> McpServers { get; set; } = new();

    /// <summary>Current theme name</summary>
    [JsonPropertyName("currentTheme")]
    public string CurrentTheme { get; set; } = "Dark";

    /// <summary>Token budget threshold for context compaction</summary>
    [JsonPropertyName("tokenBudgetThreshold")]
    public int TokenBudgetThreshold { get; set; } = 7000;

    /// <summary>Maximum token limit</summary>
    [JsonPropertyName("maxTokenLimit")]
    public int MaxTokenLimit { get; set; } = 8000;

    /// <summary>Enable automatic context compaction</summary>
    [JsonPropertyName("autoCompaction")]
    public bool AutoCompaction { get; set; } = true;

    /// <summary>Check for updates on startup</summary>
    [JsonPropertyName("checkUpdates")]
    public bool CheckUpdates { get; set; } = true;

    /// <summary>Enable telemetry</summary>
    [JsonPropertyName("enableTelemetry")]
    public bool EnableTelemetry { get; set; } = false;

    /// <summary>Log level (Debug, Information, Warning, Error)</summary>
    [JsonPropertyName("logLevel")]
    public string LogLevel { get; set; } = "Information";

    /// <summary>Path to gh CLI executable (auto-detected if empty)</summary>
    [JsonPropertyName("ghCliPath")]
    public string? GhCliPath { get; set; }

    /// <summary>Available Copilot models</summary>
    [JsonPropertyName("availableModels")]
    public List<string> AvailableModels { get; set; } = new()
    {
        "gpt-4",
        "gpt-4-turbo",
        "gpt-3.5-turbo"
    };

    /// <summary>Recently opened session IDs</summary>
    [JsonPropertyName("recentSessions")]
    public List<string> RecentSessions { get; set; } = new();

    /// <summary>Maximum number of recent sessions to track</summary>
    [JsonPropertyName("maxRecentSessions")]
    public int MaxRecentSessions { get; set; } = 10;

    /// <summary>
    /// Use SDK mode (recommended) for Copilot communication.
    /// Set to false for legacy CLI mode using process spawning.
    /// Default: true
    /// </summary>
    [JsonPropertyName("useSdkMode")]
    public bool UseSdkMode { get; set; } = true;

    /// <summary>
    /// How to display tool approval requests to the user.
    /// </summary>
    [JsonPropertyName("approvalUIMode")]
    public ApprovalUIMode ApprovalUIMode { get; set; } = ApprovalUIMode.Both;

    /// <summary>
    /// Auto-approve low-risk tool operations (read operations).
    /// </summary>
    [JsonPropertyName("autoApproveLowRisk")]
    public bool AutoApproveLowRisk { get; set; } = false;

    /// <summary>
    /// Default autonomous mode settings for new sessions.
    /// </summary>
    [JsonPropertyName("defaultAutonomousMode")]
    public AutonomousModeSettings DefaultAutonomousMode { get; set; } = new();
}
