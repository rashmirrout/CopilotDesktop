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
