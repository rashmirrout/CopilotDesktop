using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Service for managing MCP (Model Context Protocol) servers
/// </summary>
public interface IMcpService
{
    /// <summary>Event raised when server status changes</summary>
    event EventHandler<McpServerStatusChangedEventArgs>? ServerStatusChanged;

    /// <summary>Get all configured MCP servers</summary>
    IReadOnlyList<McpServerConfig> GetServers();

    /// <summary>Get a server by name</summary>
    McpServerConfig? GetServer(string name);

    /// <summary>Add a new MCP server configuration</summary>
    Task AddServerAsync(McpServerConfig config);

    /// <summary>Update an existing MCP server configuration</summary>
    Task UpdateServerAsync(McpServerConfig config);

    /// <summary>Remove an MCP server configuration</summary>
    Task RemoveServerAsync(string name);

    /// <summary>Start an MCP server process</summary>
    Task<bool> StartServerAsync(string name);

    /// <summary>Stop an MCP server process</summary>
    Task StopServerAsync(string name);

    /// <summary>Check if a server is running</summary>
    bool IsServerRunning(string name);

    /// <summary>Get the status of a server</summary>
    McpServerStatus GetServerStatus(string name);

    /// <summary>Get available tools from a running server</summary>
    Task<IReadOnlyList<McpToolInfo>> GetToolsAsync(string serverName);

    /// <summary>Call a tool on an MCP server</summary>
    Task<McpToolCallResult> CallToolAsync(string serverName, string toolName, Dictionary<string, object>? arguments);

    /// <summary>Load server configurations from persistence</summary>
    Task LoadServersAsync();

    /// <summary>Load server configurations from Copilot MCP config file (~/.copilot/mcp-config.json)</summary>
    Task LoadServersFromCopilotConfigAsync();

    /// <summary>Save server configurations to persistence</summary>
    Task SaveServersAsync();
}

/// <summary>
/// Event args for server status changes
/// </summary>
public class McpServerStatusChangedEventArgs : EventArgs
{
    public string ServerName { get; }
    public McpServerStatus OldStatus { get; }
    public McpServerStatus NewStatus { get; }
    public string? Message { get; }

    public McpServerStatusChangedEventArgs(string serverName, McpServerStatus oldStatus, McpServerStatus newStatus, string? message = null)
    {
        ServerName = serverName;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        Message = message;
    }
}

/// <summary>
/// MCP server runtime status
/// </summary>
public enum McpServerStatus
{
    Stopped,
    Starting,
    Running,
    Error,
    Stopping
}

/// <summary>
/// Information about an MCP tool
/// </summary>
public class McpToolInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Dictionary<string, McpToolParameter>? Parameters { get; set; }
}

/// <summary>
/// MCP tool parameter definition
/// </summary>
public class McpToolParameter
{
    public string Type { get; set; } = "string";
    public string? Description { get; set; }
    public bool Required { get; set; }
    public object? Default { get; set; }
}

/// <summary>
/// Result of an MCP tool call
/// </summary>
public class McpToolCallResult
{
    public bool Success { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
    public TimeSpan Duration { get; set; }
}