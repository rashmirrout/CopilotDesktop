using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Serializable multi-agent orchestration settings stored in AppSettings.
/// This is a lightweight DTO in Core that avoids a dependency on the MultiAgent project.
/// The MultiAgent project maps this to its richer <c>MultiAgentConfig</c> at runtime.
/// </summary>
public class MultiAgentSettings
{
    /// <summary>Maximum number of parallel worker sessions.</summary>
    [JsonPropertyName("maxParallelSessions")]
    public int MaxParallelSessions { get; set; } = 5;

    /// <summary>Workspace isolation strategy: "GitWorktree", "FileLocking", or "InMemory".</summary>
    [JsonPropertyName("workspaceStrategy")]
    public string WorkspaceStrategy { get; set; } = "GitWorktree";

    /// <summary>Maximum retries per work chunk before marking as failed.</summary>
    [JsonPropertyName("maxRetriesPerChunk")]
    public int MaxRetriesPerChunk { get; set; } = 2;

    /// <summary>Total failure count that triggers orchestration abort.</summary>
    [JsonPropertyName("abortFailureThreshold")]
    public int AbortFailureThreshold { get; set; } = 3;

    /// <summary>Whether to re-prompt with error context on retry.</summary>
    [JsonPropertyName("repromptOnRetry")]
    public bool RepromptOnRetry { get; set; } = true;

    /// <summary>Delay in seconds between retries.</summary>
    [JsonPropertyName("retryDelaySeconds")]
    public int RetryDelaySeconds { get; set; } = 5;

    /// <summary>Optional model override for the orchestrator session.</summary>
    [JsonPropertyName("orchestratorModelId")]
    public string? OrchestratorModelId { get; set; }

    /// <summary>Optional model override for worker sessions.</summary>
    [JsonPropertyName("workerModelId")]
    public string? WorkerModelId { get; set; }

    /// <summary>MCP servers enabled for multi-agent workers.</summary>
    [JsonPropertyName("enabledMcpServers")]
    public List<string> EnabledMcpServers { get; set; } = new();

    /// <summary>Skills disabled for multi-agent workers.</summary>
    [JsonPropertyName("disabledSkills")]
    public List<string> DisabledSkills { get; set; } = new();

    /// <summary>Auto-approve read-only tool operations in workers.</summary>
    [JsonPropertyName("autoApproveReadOnlyTools")]
    public bool AutoApproveReadOnlyTools { get; set; } = true;

    /// <summary>Worker session timeout in minutes.</summary>
    [JsonPropertyName("workerTimeoutMinutes")]
    public int WorkerTimeoutMinutes { get; set; } = 10;

    /// <summary>Maintain orchestrator context across follow-up tasks.</summary>
    [JsonPropertyName("maintainFollowUpContext")]
    public bool MaintainFollowUpContext { get; set; } = true;
}