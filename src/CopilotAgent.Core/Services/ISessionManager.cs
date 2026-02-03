using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Service for managing Copilot agent sessions
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Gets or sets the currently active session
    /// </summary>
    Session? ActiveSession { get; set; }

    /// <summary>
    /// Gets all active sessions
    /// </summary>
    IReadOnlyList<Session> Sessions { get; }

    /// <summary>
    /// Creates a new session
    /// </summary>
    Task<Session> CreateSessionAsync(string? workingDirectory = null, string? displayName = null);

    /// <summary>
    /// Creates a session from a GitHub issue URL with worktree
    /// </summary>
    Task<Session> CreateWorktreeSessionAsync(string issueUrl);

    /// <summary>
    /// Loads all saved sessions
    /// </summary>
    Task LoadSessionsAsync();

    /// <summary>
    /// Gets a session by ID
    /// </summary>
    Session? GetSession(string sessionId);

    /// <summary>
    /// Saves a session
    /// </summary>
    Task SaveSessionAsync(Session session);

    /// <summary>
    /// Saves the active session
    /// </summary>
    Task SaveActiveSessionAsync();

    /// <summary>
    /// Deletes a session
    /// </summary>
    Task DeleteSessionAsync(string sessionId);

    /// <summary>
    /// Adds a message to the active session
    /// </summary>
    void AddMessage(ChatMessage message);

    /// <summary>
    /// Event raised when a session is added
    /// </summary>
    event EventHandler<Session>? SessionAdded;

    /// <summary>
    /// Event raised when a session is removed
    /// </summary>
    event EventHandler<string>? SessionRemoved;

    /// <summary>
    /// Event raised when the active session changes
    /// </summary>
    event EventHandler<Session?>? ActiveSessionChanged;
}