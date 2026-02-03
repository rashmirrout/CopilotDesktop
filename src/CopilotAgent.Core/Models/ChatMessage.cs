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
}