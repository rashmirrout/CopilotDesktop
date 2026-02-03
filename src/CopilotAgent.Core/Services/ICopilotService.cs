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
}
