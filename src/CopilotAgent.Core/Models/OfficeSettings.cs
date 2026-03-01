using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Settings for the Agent Office feature.
/// </summary>
public class OfficeSettings
{
    /// <summary>Default check interval in minutes between iteration cycles.</summary>
    [JsonPropertyName("defaultCheckIntervalMinutes")]
    public int DefaultCheckIntervalMinutes { get; set; } = 5;

    /// <summary>Default maximum number of concurrent assistant sessions.</summary>
    [JsonPropertyName("defaultMaxAssistants")]
    public int DefaultMaxAssistants { get; set; } = 3;

    /// <summary>Default maximum queue depth before tasks are rejected.</summary>
    [JsonPropertyName("defaultMaxQueueDepth")]
    public int DefaultMaxQueueDepth { get; set; } = 20;

    /// <summary>Default model for the Manager LLM session.</summary>
    [JsonPropertyName("defaultManagerModel")]
    public string DefaultManagerModel { get; set; } = AppSettings.FallbackModel;

    /// <summary>Default model for Assistant LLM sessions.</summary>
    [JsonPropertyName("defaultAssistantModel")]
    public string DefaultAssistantModel { get; set; } = AppSettings.FallbackModel;

    /// <summary>Default timeout in seconds for individual assistant task execution.</summary>
    [JsonPropertyName("defaultAssistantTimeoutSeconds")]
    public int DefaultAssistantTimeoutSeconds { get; set; } = 600;

    /// <summary>Default timeout in seconds for Manager LLM calls.</summary>
    [JsonPropertyName("defaultManagerLlmTimeoutSeconds")]
    public int DefaultManagerLlmTimeoutSeconds { get; set; } = 60;

    /// <summary>Default maximum retries for a failed assistant task.</summary>
    [JsonPropertyName("defaultMaxRetries")]
    public int DefaultMaxRetries { get; set; } = 2;

    /// <summary>Whether to require user approval of the plan before execution by default.</summary>
    [JsonPropertyName("defaultRequirePlanApproval")]
    public bool DefaultRequirePlanApproval { get; set; } = true;

    /// <summary>Default workspace path for assistant work. Empty means use active session's working directory.</summary>
    [JsonPropertyName("defaultWorkspacePath")]
    public string DefaultWorkspacePath { get; set; } = string.Empty;

    /// <summary>Maximum number of live commentary entries to keep in memory.</summary>
    [JsonPropertyName("maxCommentaryEntries")]
    public int MaxCommentaryEntries { get; set; } = 200;

    /// <summary>
    /// Default commentary streaming mode: "StreamingTokens" for word-by-word,
    /// "CompleteThought" for buffered complete responses.
    /// Stored as string to avoid Core→Office project dependency on the enum.
    /// </summary>
    [JsonPropertyName("defaultCommentaryStreamingMode")]
    public string DefaultCommentaryStreamingMode { get; set; } = "CompleteThought";
}
