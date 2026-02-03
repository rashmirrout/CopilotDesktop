using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Represents the result of a tool execution
/// </summary>
public class ToolResult
{
    /// <summary>ID of the tool call this result belongs to</summary>
    [JsonPropertyName("toolCallId")]
    public string ToolCallId { get; set; } = string.Empty;

    /// <summary>Whether the tool execution was successful</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>Result data from the tool (as JSON)</summary>
    [JsonPropertyName("result")]
    public object? Result { get; set; }

    /// <summary>Error message if the tool failed</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Standard output from the tool (if applicable)</summary>
    [JsonPropertyName("stdout")]
    public string? Stdout { get; set; }

    /// <summary>Standard error from the tool (if applicable)</summary>
    [JsonPropertyName("stderr")]
    public string? Stderr { get; set; }

    /// <summary>Exit code (for command executions)</summary>
    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; set; }

    /// <summary>When the result was produced</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Duration of execution in milliseconds</summary>
    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; set; }
}