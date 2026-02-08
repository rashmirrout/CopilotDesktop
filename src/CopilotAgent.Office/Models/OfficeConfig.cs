using System.Text.Json.Serialization;

namespace CopilotAgent.Office.Models;

/// <summary>
/// Configuration for an Office run. Immutable after creation.
/// </summary>
public sealed record OfficeConfig
{
    /// <summary>The user's high-level objective for the Office run.</summary>
    [JsonPropertyName("objective")]
    public required string Objective { get; init; }

    /// <summary>Repository or workspace path the assistants will work in.</summary>
    [JsonPropertyName("workspacePath")]
    public string? WorkspacePath { get; init; }

    /// <summary>Interval in minutes between iteration cycles.</summary>
    [JsonPropertyName("checkIntervalMinutes")]
    public int CheckIntervalMinutes { get; init; } = 5;

    /// <summary>Maximum number of concurrent assistant sessions.</summary>
    [JsonPropertyName("maxAssistants")]
    public int MaxAssistants { get; init; } = 3;

    /// <summary>Maximum queue depth before tasks are rejected.</summary>
    [JsonPropertyName("maxQueueDepth")]
    public int MaxQueueDepth { get; init; } = 20;

    /// <summary>Model to use for the Manager LLM session.</summary>
    [JsonPropertyName("managerModel")]
    public string ManagerModel { get; init; } = "gpt-4";

    /// <summary>Model to use for Assistant LLM sessions.</summary>
    [JsonPropertyName("assistantModel")]
    public string AssistantModel { get; init; } = "gpt-4";

    /// <summary>Timeout in seconds for individual assistant task execution.</summary>
    [JsonPropertyName("assistantTimeoutSeconds")]
    public int AssistantTimeoutSeconds { get; init; } = 120;

    /// <summary>Timeout in seconds for Manager LLM calls.</summary>
    [JsonPropertyName("managerLlmTimeoutSeconds")]
    public int ManagerLlmTimeoutSeconds { get; init; } = 60;

    /// <summary>Maximum retries for a failed assistant task.</summary>
    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; init; } = 2;

    /// <summary>Whether to require user approval of the plan before execution.</summary>
    [JsonPropertyName("requirePlanApproval")]
    public bool RequirePlanApproval { get; init; } = true;

    /// <summary>Optional system prompt override for the Manager.</summary>
    [JsonPropertyName("managerSystemPrompt")]
    public string? ManagerSystemPrompt { get; init; }

    /// <summary>
    /// Controls how the Manager's LLM reasoning is streamed as live commentary.
    /// Default is CompleteThought (buffer full response, then emit).
    /// StreamingTokens emits each token chunk in real-time (word-by-word).
    /// </summary>
    [JsonPropertyName("commentaryStreamingMode")]
    public CommentaryStreamingMode CommentaryStreamingMode { get; init; } = CommentaryStreamingMode.CompleteThought;

    /// <summary>
    /// Enabled MCP servers for assistant sessions.
    /// null = use all from config, empty = none, non-empty = only these.
    /// Propagated from the active session's EnabledMcpServers.
    /// </summary>
    [JsonPropertyName("enabledMcpServers")]
    public List<string>? EnabledMcpServers { get; init; }

    /// <summary>
    /// Disabled skills for assistant sessions.
    /// null = default, empty = all enabled, non-empty = these are disabled.
    /// Propagated from the active session's DisabledSkills.
    /// </summary>
    [JsonPropertyName("disabledSkills")]
    public List<string>? DisabledSkills { get; init; }

    /// <summary>
    /// Skill directories to search.
    /// Propagated from the active session's SkillDirectories.
    /// </summary>
    [JsonPropertyName("skillDirectories")]
    public List<string>? SkillDirectories { get; init; }
}
