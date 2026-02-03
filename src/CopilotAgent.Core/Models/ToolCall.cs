using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Represents a tool invocation by the AI agent
/// </summary>
public class ToolCall
{
    /// <summary>Unique identifier for this tool call</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Name of the tool being invoked</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Arguments passed to the tool (as JSON object)</summary>
    [JsonPropertyName("arguments")]
    public Dictionary<string, object>? Arguments { get; set; }

    /// <summary>When the tool was invoked</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Status of the tool execution</summary>
    [JsonPropertyName("status")]
    public ToolCallStatus Status { get; set; } = ToolCallStatus.Pending;

    /// <summary>Duration of tool execution in milliseconds</summary>
    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; set; }
}

/// <summary>
/// Status of a tool call execution
/// </summary>
public enum ToolCallStatus
{
    /// <summary>Tool call is pending execution</summary>
    Pending,
    
    /// <summary>Tool is currently executing</summary>
    Running,
    
    /// <summary>Tool completed successfully</summary>
    Completed,
    
    /// <summary>Tool execution failed</summary>
    Failed,
    
    /// <summary>Tool execution was cancelled</summary>
    Cancelled,
    
    /// <summary>Tool execution requires user approval</summary>
    RequiresApproval
}