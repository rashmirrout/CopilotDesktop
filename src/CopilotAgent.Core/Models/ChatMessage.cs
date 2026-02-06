using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Represents a single message in a chat conversation
/// </summary>
public class ChatMessage
{
    /// <summary>Unique identifier for this message</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Role of the message sender</summary>
    [JsonPropertyName("role")]
    public MessageRole Role { get; set; }

    /// <summary>Message content</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>Timestamp when the message was created</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Tool call information (for Tool role messages)</summary>
    [JsonPropertyName("toolCall")]
    public ToolCall? ToolCall { get; set; }

    /// <summary>Tool result information (for ToolResult role messages)</summary>
    [JsonPropertyName("toolResult")]
    public ToolResult? ToolResult { get; set; }

    /// <summary>Metadata for the message (e.g., model used, tokens, etc.)</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>Whether this message is currently streaming (incomplete)</summary>
    [JsonPropertyName("isStreaming")]
    public bool IsStreaming { get; set; }

    /// <summary>Whether this message contains an error</summary>
    [JsonPropertyName("isError")]
    public bool IsError { get; set; }

    #region Agent Commentary / Reasoning Properties

    /// <summary>
    /// SDK reasoning ID for this message.
    /// Used to track and update streaming reasoning content.
    /// Only populated for Reasoning role messages.
    /// </summary>
    [JsonPropertyName("reasoningId")]
    public string? ReasoningId { get; set; }

    /// <summary>
    /// Turn ID this message belongs to.
    /// Used to group messages for collapsing after turn completion.
    /// </summary>
    [JsonPropertyName("turnId")]
    public string? TurnId { get; set; }

    /// <summary>
    /// Whether this is an agent work item (reasoning or tool event)
    /// that should be collapsed after the turn completes.
    /// </summary>
    [JsonPropertyName("isAgentWork")]
    public bool IsAgentWork { get; set; }

    /// <summary>
    /// For AgentWorkSummary messages: collection of collapsed agent work items.
    /// </summary>
    [JsonIgnore]
    public List<ChatMessage>? CollapsedMessages { get; set; }

    /// <summary>
    /// For AgentWorkSummary messages: summary text (e.g., "3 tools, 2 reasoning steps")
    /// </summary>
    [JsonPropertyName("summaryText")]
    public string? SummaryText { get; set; }

    /// <summary>
    /// Whether the collapsed summary is expanded in the UI.
    /// </summary>
    [JsonIgnore]
    public bool IsExpanded { get; set; }

    /// <summary>
    /// Number of tools executed (for summary display)
    /// </summary>
    [JsonPropertyName("toolCount")]
    public int ToolCount { get; set; }

    /// <summary>
    /// Number of reasoning steps (for summary display)
    /// </summary>
    [JsonPropertyName("reasoningCount")]
    public int ReasoningCount { get; set; }

    #endregion
}
