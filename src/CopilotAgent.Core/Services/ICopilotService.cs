using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Service for interacting with GitHub Copilot.
/// Supports both legacy per-message processes and persistent session mode.
/// </summary>
public interface ICopilotService : IDisposable
{
    /// <summary>
    /// Checks if Copilot CLI is available
    /// </summary>
    Task<bool> IsCopilotAvailableAsync();

    /// <summary>
    /// Gets available Copilot models
    /// </summary>
    Task<List<string>> GetAvailableModelsAsync();

    /// <summary>
    /// Sends a message and returns streaming response
    /// </summary>
    IAsyncEnumerable<ChatMessage> SendMessageStreamingAsync(
        Session session,
        string userMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message and waits for complete response
    /// </summary>
    Task<ChatMessage> SendMessageAsync(
        Session session,
        string userMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a command via Copilot
    /// </summary>
    Task<ToolResult> ExecuteCommandAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates the Copilot process for a specific session.
    /// Used when switching sessions or when session is deleted.
    /// </summary>
    void TerminateSessionProcess(string sessionId);

    /// <summary>
    /// Terminates all active Copilot processes.
    /// Call this when closing the application.
    /// </summary>
    void TerminateAllProcesses();

    /// <summary>
    /// Aborts the current operation in a session.
    /// For SDK mode: calls session.AbortAsync().
    /// For CLI mode: terminates the process.
    /// </summary>
    /// <param name="sessionId">The session ID to abort.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AbortAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recreates the SDK session with new configuration options.
    /// This is used when the user changes model or working directory,
    /// which requires disposing the old session and creating a new one.
    /// Message history in the app Session is preserved.
    /// </summary>
    /// <param name="session">The session to recreate.</param>
    /// <param name="options">Options specifying what to change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecreateSessionAsync(Session session, SessionRecreateOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about MCP servers active in the live SDK session.
    /// This queries the actual running session to get current server state and available tools.
    /// </summary>
    /// <param name="sessionId">The session ID to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of live MCP server information with tools.</returns>
    Task<List<LiveMcpServerInfo>> GetLiveMcpServersAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a session has an active SDK session (is connected).
    /// </summary>
    /// <param name="sessionId">The session ID to check.</param>
    /// <returns>True if the session has an active SDK connection.</returns>
    bool HasActiveSession(string sessionId);

    /// <summary>
    /// Forcefully reconnects to the Copilot CLI by disposing the current client
    /// and all SDK sessions, then re-establishing the connection on the next call.
    /// Use this after a <c>ConnectionLostException</c> or when the SDK pipe is broken
    /// and the service needs to recover to a clean state (equivalent to app startup).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReconnectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for recreating a session with new configuration.
/// </summary>
public class SessionRecreateOptions
{
    /// <summary>
    /// New model to use. If null, keeps the existing model.
    /// </summary>
    public string? NewModel { get; set; }

    /// <summary>
    /// New working directory. If null, keeps the existing directory.
    /// </summary>
    public string? NewWorkingDirectory { get; set; }
}

/// <summary>
/// Information about MCP servers active in the live SDK session.
/// </summary>
public class LiveMcpServerInfo
{
    /// <summary>Server name/identifier</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Whether the server is currently active/connected</summary>
    public bool IsActive { get; set; }
    
    /// <summary>Server status (running, stopped, error, etc.)</summary>
    public string Status { get; set; } = "unknown";
    
    /// <summary>Tools available from this server</summary>
    public List<LiveMcpToolInfo> Tools { get; set; } = new();
    
    /// <summary>Server transport type (stdio/http)</summary>
    public string Transport { get; set; } = "stdio";
    
    /// <summary>Command or URL used to connect</summary>
    public string ConnectionInfo { get; set; } = string.Empty;
    
    /// <summary>Error message if server failed to start</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Information about a tool from a live MCP server.
/// </summary>
public class LiveMcpToolInfo
{
    /// <summary>Tool name</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Tool description</summary>
    public string? Description { get; set; }
    
    /// <summary>Tool parameters</summary>
    public Dictionary<string, LiveMcpToolParameter>? Parameters { get; set; }
}

/// <summary>
/// Parameter information for a live MCP tool.
/// </summary>
public class LiveMcpToolParameter
{
    /// <summary>Parameter type</summary>
    public string Type { get; set; } = "string";
    
    /// <summary>Parameter description</summary>
    public string? Description { get; set; }
    
    /// <summary>Whether the parameter is required</summary>
    public bool Required { get; set; }
}
