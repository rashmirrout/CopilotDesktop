using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Application-wide settings
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Compile-time fallback model used as the default when no user preference is persisted.
    /// This is the SINGLE SOURCE OF TRUTH for the default model across the entire codebase.
    /// All property initializers and null-coalescing fallbacks must reference this constant.
    /// </summary>
    public const string FallbackModel = "claude-sonnet-4.5";

    /// <summary>Default model to use for new sessions</summary>
    [JsonPropertyName("defaultModel")]
    public string DefaultModel { get; set; } = FallbackModel;

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
        "claude-sonnet-4.5",
        "claude-haiku-4.5",
        "gpt-5.2-codex",
        "gpt-5.1",
        "gpt-4.1",
        "gpt-4o"
    };

    /// <summary>Recently opened session IDs</summary>
    [JsonPropertyName("recentSessions")]
    public List<string> RecentSessions { get; set; } = new();

    /// <summary>Maximum number of recent sessions to track</summary>
    [JsonPropertyName("maxRecentSessions")]
    public int MaxRecentSessions { get; set; } = 10;

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

    /// <summary>
    /// Browser automation settings for OAuth/SAML authentication flows.
    /// </summary>
    [JsonPropertyName("browserAutomation")]
    public BrowserAutomationSettings BrowserAutomation { get; set; } = new();

    /// <summary>
    /// Streaming timeout and progress tracking settings.
    /// Controls how long the application waits for different types of activity
    /// during streaming responses and tool execution.
    /// 
    /// Enterprise defaults:
    /// - IdleTimeout: 30s (waiting for LLM response)
    /// - ToolExecutionTimeout: 120s (complex operations)
    /// - ApprovalWaitTimeout: 0 (infinite - never rush user decisions)
    /// </summary>
    [JsonPropertyName("streamingTimeouts")]
    public StreamingTimeoutSettings StreamingTimeouts { get; set; } = new();

    /// <summary>
    /// Show agent reasoning/commentary during response generation.
    /// When enabled, displays the LLM's thinking process in real-time
    /// like VS Code Copilot does. Enabled by default for transparency.
    /// </summary>
    [JsonPropertyName("showAgentCommentary")]
    public bool ShowAgentCommentary { get; set; } = true;

    /// <summary>
    /// Automatically collapse agent commentary after turn completion.
    /// When enabled, reasoning messages are collapsed into a summary bar
    /// after the assistant response is complete.
    /// </summary>
    [JsonPropertyName("autoCollapseCommentary")]
    public bool AutoCollapseCommentary { get; set; } = true;

    /// <summary>
    /// Multi-agent orchestration settings.
    /// Controls parallel worker sessions, workspace strategy, retry policies,
    /// and model overrides for the Agent Team feature.
    /// </summary>
    [JsonPropertyName("multiAgent")]
    public MultiAgentSettings MultiAgent { get; set; } = new();

    /// <summary>
    /// Interval in seconds between session health checks for the orchestrator
    /// session liveness indicator. Clamped to [5, 60] at usage site.
    /// Default: 15 seconds.
    /// </summary>
    [JsonPropertyName("sessionHealthCheckIntervalSeconds")]
    public int SessionHealthCheckIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Agent Office settings.
    /// Controls default configuration for the Manager-Assistant iteration loop.
    /// </summary>
    [JsonPropertyName("office")]
    public OfficeSettings Office { get; set; } = new();

    /// <summary>
    /// Panel Discussion settings.
    /// Controls default configuration for multi-agent panel discussions
    /// including panelist limits, convergence thresholds, and resource budgets.
    /// </summary>
    [JsonPropertyName("panel")]
    public PanelSettings Panel { get; set; } = new();
}
