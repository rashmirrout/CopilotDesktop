using System.Text.Json;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Persistence;

/// <summary>
/// JSON-based implementation of persistence service
/// </summary>
public class JsonPersistenceService : IPersistenceService
{
    private readonly ILogger<JsonPersistenceService> _logger;
    private readonly string _dataDirectory;
    private readonly string _sessionsDirectory;
    private readonly string _settingsFile;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonPersistenceService(ILogger<JsonPersistenceService> logger)
    {
        _logger = logger;
        
        // Set up data directories
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _dataDirectory = Path.Combine(appData, "CopilotAgent");
        _sessionsDirectory = Path.Combine(_dataDirectory, "Sessions");
        _settingsFile = Path.Combine(_dataDirectory, "settings.json");
        
        // Ensure directories exist
        Directory.CreateDirectory(_dataDirectory);
        Directory.CreateDirectory(_sessionsDirectory);
    }

    public string GetDataDirectory() => _dataDirectory;

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(_settingsFile, json);
            _logger.LogInformation("Settings saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            throw;
        }
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (!File.Exists(_settingsFile))
            {
                _logger.LogInformation("Settings file not found, creating default settings");
                var defaultSettings = new AppSettings();
                await SaveSettingsAsync(defaultSettings);
                return defaultSettings;
            }

            var json = await File.ReadAllTextAsync(_settingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings, using defaults");
            return new AppSettings();
        }
    }

    public async Task SaveSessionAsync(Session session)
    {
        try
        {
            var sessionFile = Path.Combine(_sessionsDirectory, $"{session.SessionId}.json");
            var json = JsonSerializer.Serialize(session, JsonOptions);
            await File.WriteAllTextAsync(sessionFile, json);
            _logger.LogInformation("Session {SessionId} saved successfully", session.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save session {SessionId}", session.SessionId);
            throw;
        }
    }

    public async Task<Session?> LoadSessionAsync(string sessionId)
    {
        try
        {
            var sessionFile = Path.Combine(_sessionsDirectory, $"{sessionId}.json");
            
            if (!File.Exists(sessionFile))
            {
                _logger.LogWarning("Session file not found: {SessionId}", sessionId);
                return null;
            }

            var json = await File.ReadAllTextAsync(sessionFile);
            var session = JsonSerializer.Deserialize<Session>(json, JsonOptions);
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task<List<Session>> LoadAllSessionsAsync()
    {
        var sessions = new List<Session>();
        
        try
        {
            var sessionFiles = Directory.GetFiles(_sessionsDirectory, "*.json");
            
            foreach (var file in sessionFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var session = JsonSerializer.Deserialize<Session>(json, JsonOptions);
                    if (session != null)
                    {
                        sessions.Add(session);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load session file: {File}", file);
                }
            }
            
            _logger.LogInformation("Loaded {Count} sessions", sessions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sessions");
        }
        
        return sessions;
    }

    public Task DeleteSessionAsync(string sessionId)
    {
        try
        {
            var sessionFile = Path.Combine(_sessionsDirectory, $"{sessionId}.json");
            
            if (File.Exists(sessionFile))
            {
                File.Delete(sessionFile);
                _logger.LogInformation("Session {SessionId} deleted", sessionId);
            }
            
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete session {SessionId}", sessionId);
            throw;
        }
    }
}