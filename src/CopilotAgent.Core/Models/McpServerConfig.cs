using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Configuration for an MCP (Model Context Protocol) server
/// </summary>
public class McpServerConfig
{
    /// <summary>Unique name for this MCP server</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Description of what this MCP server provides</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Transport mechanism</summary>
    [JsonPropertyName("transport")]
    public McpTransport Transport { get; set; } = McpTransport.Stdio;

    /// <summary>Command to execute (for stdio transport)</summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    /// <summary>Arguments for the command (for stdio transport)</summary>
    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    /// <summary>Environment variables for the command</summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>URL for HTTP transport</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>HTTP headers (for http transport)</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>Whether this server is enabled globally</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Timeout in seconds</summary>
    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Tags for categorization</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}

/// <summary>
/// MCP transport mechanism
/// </summary>
public enum McpTransport
{
    /// <summary>Standard input/output communication</summary>
    Stdio,
    
    /// <summary>HTTP-based communication</summary>
    Http
}