using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Configurable timeout settings for streaming responses and tool execution.
/// These settings control how long the application waits for different types
/// of activity before showing timeout warnings or stopping streaming.
/// 
/// Enterprise default values are based on production workload analysis:
/// - IdleTimeout: 30s - LLM responses typically arrive within 10-20s, 30s allows for cold starts
/// - ToolExecutionTimeout: 120s - Complex operations (git, network I/O) need time
/// - ApprovalWaitTimeout: 0 (infinite) - Never rush security decisions
/// </summary>
public class StreamingTimeoutSettings
{
    /// <summary>
    /// Timeout in seconds when waiting for events in idle state (no tools executing).
    /// This applies when waiting for the initial LLM response or between tool executions.
    /// 
    /// Default: 90 seconds (enterprise-grade for complex playbooks)
    /// Enterprise recommendation: 60-120s depending on model latency and workload
    /// Conservative: 120s (slow networks/models, complex operations)
    /// Aggressive: 30s (fast local models)
    /// </summary>
    [JsonPropertyName("idleTimeoutSeconds")]
    public int IdleTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Timeout in seconds when a tool is actively executing.
    /// Tools like file operations, git operations, or network requests can take time.
    /// 
    /// Default: 240 seconds (4 minutes) - enterprise-grade for complex tools
    /// Enterprise recommendation: 180-300s for complex operations
    /// Conservative: 300s (5 minutes for very large operations)
    /// Aggressive: 120s (simple read-only operations)
    /// </summary>
    [JsonPropertyName("toolExecutionTimeoutSeconds")]
    public int ToolExecutionTimeoutSeconds { get; set; } = 240;

    /// <summary>
    /// Timeout in seconds when waiting for user approval of a tool.
    /// Set to 0 or negative for no timeout (wait indefinitely for user decision).
    /// 
    /// Default: 0 (no timeout - wait for user)
    /// Enterprise recommendation: 0 (user decisions should never be rushed)
    /// 
    /// IMPORTANT: For security-critical applications, this should always be 0.
    /// Timeout on approval can lead to users making hasty decisions.
    /// </summary>
    [JsonPropertyName("approvalWaitTimeoutSeconds")]
    public int ApprovalWaitTimeoutSeconds { get; set; } = 0;

    /// <summary>
    /// Enable detailed progress tracking and reporting during tool execution.
    /// When enabled, shows messages like "Executing: read_file (2/5)" and elapsed time.
    /// 
    /// Default: true
    /// Enterprise recommendation: true for transparency and debugging
    /// </summary>
    [JsonPropertyName("enableProgressTracking")]
    public bool EnableProgressTracking { get; set; } = true;

    /// <summary>
    /// Show elapsed time in progress messages (e.g., "15s elapsed").
    /// 
    /// Default: true
    /// </summary>
    [JsonPropertyName("showElapsedTime")]
    public bool ShowElapsedTime { get; set; } = true;

    /// <summary>
    /// Warning threshold percentage (0.0-1.0) for showing "taking longer than expected" messages.
    /// For example, 0.75 means show warning at 75% of timeout duration.
    /// 
    /// Default: 0.75 (show warning at 75% of timeout duration)
    /// This gives users advance notice before timeout, allowing them to decide
    /// whether to abort or continue waiting.
    /// </summary>
    [JsonPropertyName("warningThresholdPercentage")]
    public double WarningThresholdPercentage { get; set; } = 0.75;

    /// <summary>
    /// Maximum number of consecutive warnings to show before automatically timing out.
    /// This prevents spamming the user with repeated warnings.
    /// Set to 0 to disable (always show warnings until timeout).
    /// 
    /// Default: 3
    /// </summary>
    [JsonPropertyName("maxConsecutiveWarnings")]
    public int MaxConsecutiveWarnings { get; set; } = 3;

    /// <summary>
    /// Minimum interval in seconds between progress updates.
    /// Prevents UI flickering from too-frequent updates.
    /// 
    /// Default: 1 second
    /// </summary>
    [JsonPropertyName("progressUpdateIntervalSeconds")]
    public double ProgressUpdateIntervalSeconds { get; set; } = 1.0;

    /// <summary>
    /// Whether to automatically extend timeout when events are received.
    /// When true, receiving any event resets the timeout timer.
    /// When false, timeout is absolute from start of operation.
    /// 
    /// Default: true (recommended for long-running playbooks)
    /// </summary>
    [JsonPropertyName("extendTimeoutOnActivity")]
    public bool ExtendTimeoutOnActivity { get; set; } = true;

    /// <summary>
    /// Minimum inactivity period in seconds before timeout can trigger.
    /// If ANY event has been received within this window, timeout is suppressed.
    /// This is the critical safety net for long-running operations that emit events
    /// but may have gaps in activity.
    /// 
    /// Default: 30 seconds (enterprise-grade for complex playbooks with many tools)
    /// Enterprise recommendation: 20-45s depending on workload characteristics
    /// 
    /// This setting provides an absolute guarantee: if the system is receiving events,
    /// it will NOT timeout, regardless of other settings. This fixes the "173 events
    /// received but still timed out" bug.
    /// </summary>
    [JsonPropertyName("recentActivityThresholdSeconds")]
    public double RecentActivityThresholdSeconds { get; set; } = 30.0;

    /// <summary>
    /// Checks if the approval wait timeout is effectively infinite.
    /// </summary>
    [JsonIgnore]
    public bool IsApprovalWaitInfinite => ApprovalWaitTimeoutSeconds <= 0;

    /// <summary>
    /// Gets the idle timeout as a TimeSpan.
    /// </summary>
    [JsonIgnore]
    public TimeSpan IdleTimeout => TimeSpan.FromSeconds(IdleTimeoutSeconds);

    /// <summary>
    /// Gets the tool execution timeout as a TimeSpan.
    /// </summary>
    [JsonIgnore]
    public TimeSpan ToolExecutionTimeout => TimeSpan.FromSeconds(ToolExecutionTimeoutSeconds);

    /// <summary>
    /// Gets the approval wait timeout as a TimeSpan, or TimeSpan.MaxValue if infinite.
    /// </summary>
    [JsonIgnore]
    public TimeSpan ApprovalWaitTimeout => 
        IsApprovalWaitInfinite ? TimeSpan.MaxValue : TimeSpan.FromSeconds(ApprovalWaitTimeoutSeconds);

    /// <summary>
    /// Gets the progress update interval as a TimeSpan.
    /// </summary>
    [JsonIgnore]
    public TimeSpan ProgressUpdateInterval => TimeSpan.FromSeconds(ProgressUpdateIntervalSeconds);

    /// <summary>
    /// Gets the recent activity threshold as a TimeSpan.
    /// </summary>
    [JsonIgnore]
    public TimeSpan RecentActivityThreshold => TimeSpan.FromSeconds(RecentActivityThresholdSeconds);

    /// <summary>
    /// Creates a copy of the settings.
    /// </summary>
    public StreamingTimeoutSettings Clone() => new()
    {
        IdleTimeoutSeconds = IdleTimeoutSeconds,
        ToolExecutionTimeoutSeconds = ToolExecutionTimeoutSeconds,
        ApprovalWaitTimeoutSeconds = ApprovalWaitTimeoutSeconds,
        EnableProgressTracking = EnableProgressTracking,
        ShowElapsedTime = ShowElapsedTime,
        WarningThresholdPercentage = WarningThresholdPercentage,
        MaxConsecutiveWarnings = MaxConsecutiveWarnings,
        ProgressUpdateIntervalSeconds = ProgressUpdateIntervalSeconds,
        ExtendTimeoutOnActivity = ExtendTimeoutOnActivity,
        RecentActivityThresholdSeconds = RecentActivityThresholdSeconds
    };

    /// <summary>
    /// Validates the settings and returns any validation errors.
    /// </summary>
    public IEnumerable<string> Validate()
    {
        if (IdleTimeoutSeconds < 5)
            yield return "Idle timeout must be at least 5 seconds";

        if (ToolExecutionTimeoutSeconds < 10)
            yield return "Tool execution timeout must be at least 10 seconds";

        if (WarningThresholdPercentage < 0.1 || WarningThresholdPercentage > 1.0)
            yield return "Warning threshold percentage must be between 0.1 and 1.0";

        if (ProgressUpdateIntervalSeconds < 0.1)
            yield return "Progress update interval must be at least 0.1 seconds";

        if (RecentActivityThresholdSeconds < 1.0)
            yield return "Recent activity threshold must be at least 1 second";
    }

    /// <summary>
    /// Returns true if settings are valid.
    /// </summary>
    [JsonIgnore]
    public bool IsValid => !Validate().Any();
}