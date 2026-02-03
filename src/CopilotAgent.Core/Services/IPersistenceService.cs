using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Service for persisting and loading application data
/// </summary>
public interface IPersistenceService
{
    /// <summary>
    /// Saves application settings
    /// </summary>
    Task SaveSettingsAsync(AppSettings settings);

    /// <summary>
    /// Loads application settings
    /// </summary>
    Task<AppSettings> LoadSettingsAsync();

    /// <summary>
    /// Saves a session
    /// </summary>
    Task SaveSessionAsync(Session session);

    /// <summary>
    /// Loads a session by ID
    /// </summary>
    Task<Session?> LoadSessionAsync(string sessionId);

    /// <summary>
    /// Loads all sessions
    /// </summary>
    Task<List<Session>> LoadAllSessionsAsync();

    /// <summary>
    /// Deletes a session
    /// </summary>
    Task DeleteSessionAsync(string sessionId);

    /// <summary>
    /// Gets the data directory path
    /// </summary>
    string GetDataDirectory();
}