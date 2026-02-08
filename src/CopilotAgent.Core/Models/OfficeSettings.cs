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
    public string DefaultManagerModel { get; set; } = "gpt-4";

    /// <summary>Default model for Assistant LLM sessions.</summary>
    [JsonPropertyName("defaultAssistantModel")]
    public string DefaultAssistantModel { get; set; } = "gpt-4";

    /// <summary>Default timeout in seconds for individual assistant task execution.</summary>
    [JsonPropertyName("defaultAssistantTimeoutSeconds")]
    public int DefaultAssistantTimeoutSeconds { get; set; } = 120;

    /// <summary>Default timeout in seconds for Manager LLM calls.</summary>
    [JsonPropertyName("defaultManagerLlmTimeoutSeconds")]
    public int DefaultManagerLlmTimeoutSeconds { get; set; } = 60;

    /// <summary>Default maximum retries for a failed assistant task.</summary>
    [JsonPropertyName("defaultMaxRetries")]
    public int DefaultMaxRetries { get; set; } = 2;

    /// <summary>Whether to require user approval of the plan before execution by default.</summary>
    [JsonPropertyName("defaultRequirePlanApproval")]
    public bool DefaultRequirePlanApproval { get; set; } = true;

    /// <summary>Maximum number of live commentary entries to keep in memory.</summary>
    [JsonPropertyName("maxCommentaryEntries")]
    public int MaxCommentaryEntries { get; set; } = 200;
}