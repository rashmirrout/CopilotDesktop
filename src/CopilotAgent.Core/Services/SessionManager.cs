using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CopilotAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Implementation of session manager
/// </summary>
public class SessionManager : ISessionManager
{
    private readonly IPersistenceService _persistence;
    private readonly ILogger<SessionManager> _logger;
    private readonly List<Session> _sessions = new();
    private Session? _activeSession;

    public SessionManager(IPersistenceService persistence, ILogger<SessionManager> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public Session? ActiveSession
    {
        get => _activeSession;
        set
        {
            if (_activeSession != value)
            {
                _activeSession = value;
                if (_activeSession != null)
                {
                    _activeSession.LastActiveAt = DateTime.UtcNow;
                }
                ActiveSessionChanged?.Invoke(this, _activeSession);
            }
        }
    }

    public IReadOnlyList<Session> Sessions => _sessions.AsReadOnly();

    public event EventHandler<Session>? SessionAdded;
    public event EventHandler<string>? SessionRemoved;
    public event EventHandler<Session?>? ActiveSessionChanged;

    public async Task<Session> CreateSessionAsync(string? workingDirectory = null, string? displayName = null)
    {
        _logger.LogInformation("Creating new session");

        var session = new Session
        {
            SessionId = Guid.NewGuid().ToString(),
            DisplayName = displayName ?? $"Session {DateTime.Now:yyyy-MM-dd HH:mm}",
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            IsActive = true
        };

        _sessions.Add(session);
        await _persistence.SaveSessionAsync(session);
        
        SessionAdded?.Invoke(this, session);
        ActiveSession = session;

        _logger.LogInformation("Created session {SessionId}: {DisplayName}", session.SessionId, session.DisplayName);
        return session;
    }

    public async Task<Session> CreateWorktreeSessionAsync(string issueUrl)
    {
        _logger.LogInformation("Creating worktree session from issue: {IssueUrl}", issueUrl);

        // Parse issue URL: https://github.com/owner/repo/issues/123
        var match = Regex.Match(issueUrl, @"github\.com/([^/]+)/([^/]+)/issues/(\d+)");
        if (!match.Success)
        {
            throw new ArgumentException("Invalid GitHub issue URL format", nameof(issueUrl));
        }

        var owner = match.Groups[1].Value;
        var repo = match.Groups[2].Value;
        var issueNumber = int.Parse(match.Groups[3].Value);

        // Fetch issue details using gh CLI
        var issueInfo = await FetchIssueDetailsAsync(owner, repo, issueNumber);

        // Create worktree directory
        var settings = await _persistence.LoadSettingsAsync();
        var worktreePath = Path.Combine(
            settings.WorktreeBasePath,
            $"{repo}-issue-{issueNumber}"
        );

        // Create worktree using git
        await CreateGitWorktreeAsync(worktreePath, $"issue-{issueNumber}");

        var session = new Session
        {
            SessionId = Guid.NewGuid().ToString(),
            DisplayName = $"ISSUE-{issueNumber}: {issueInfo.Title}",
            WorkingDirectory = worktreePath,
            GitWorktreeInfo = new GitWorktreeInfo
            {
                Owner = owner,
                Repo = repo,
                IssueNumber = issueNumber,
                IssueTitle = issueInfo.Title,
                IssueBody = issueInfo.Body,
                BranchName = $"issue-{issueNumber}",
                WorktreePath = worktreePath
            },
            SystemPrompt = $"You are helping work on GitHub issue #{issueNumber}: {issueInfo.Title}\n\n{issueInfo.Body}",
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            IsActive = true
        };

        _sessions.Add(session);
        await _persistence.SaveSessionAsync(session);
        
        SessionAdded?.Invoke(this, session);
        ActiveSession = session;

        _logger.LogInformation("Created worktree session {SessionId} for issue {IssueNumber}", session.SessionId, issueNumber);
        return session;
    }

    public async Task LoadSessionsAsync()
    {
        _logger.LogInformation("Loading saved sessions");
        
        var sessions = await _persistence.LoadAllSessionsAsync();
        _sessions.Clear();
        _sessions.AddRange(sessions);

        // Set the most recently active session as active
        var mostRecent = _sessions.OrderByDescending(s => s.LastActiveAt).FirstOrDefault();
        if (mostRecent != null)
        {
            ActiveSession = mostRecent;
        }

        _logger.LogInformation("Loaded {Count} sessions", _sessions.Count);
    }

    public Session? GetSession(string sessionId)
    {
        return _sessions.FirstOrDefault(s => s.SessionId == sessionId);
    }

    public async Task SaveSessionAsync(Session session)
    {
        await _persistence.SaveSessionAsync(session);
        _logger.LogDebug("Saved session {SessionId}", session.SessionId);
    }

    public async Task SaveActiveSessionAsync()
    {
        if (ActiveSession != null)
        {
            await SaveSessionAsync(ActiveSession);
        }
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        _logger.LogInformation("Deleting session {SessionId}", sessionId);
        
        var session = GetSession(sessionId);
        if (session != null)
        {
            _sessions.Remove(session);
            await _persistence.DeleteSessionAsync(sessionId);
            
            if (ActiveSession?.SessionId == sessionId)
            {
                ActiveSession = _sessions.FirstOrDefault();
            }
            
            SessionRemoved?.Invoke(this, sessionId);
        }
    }

    public void AddMessage(ChatMessage message)
    {
        if (ActiveSession != null)
        {
            ActiveSession.MessageHistory.Add(message);
            ActiveSession.LastActiveAt = DateTime.UtcNow;
        }
    }

    private async Task<(string Title, string Body)> FetchIssueDetailsAsync(string owner, string repo, int issueNumber)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = $"issue view {issueNumber} --repo {owner}/{repo} --json title,body",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start gh process");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"gh issue view failed with exit code {process.ExitCode}");
            }

            var json = JsonDocument.Parse(output);
            var title = json.RootElement.GetProperty("title").GetString() ?? $"Issue #{issueNumber}";
            var body = json.RootElement.GetProperty("body").GetString() ?? string.Empty;

            return (title, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch issue details for {Owner}/{Repo}#{IssueNumber}", owner, repo, issueNumber);
            return ($"Issue #{issueNumber}", "Failed to fetch issue details");
        }
    }

    private async Task CreateGitWorktreeAsync(string path, string branchName)
    {
        try
        {
            // Ensure parent directory exists
            var parentDir = Path.GetDirectoryName(path);
            if (parentDir != null && !Directory.Exists(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            // Create worktree
            var processInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"worktree add \"{path}\" -b {branchName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start git process");
            }

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogWarning("Git worktree creation returned exit code {ExitCode}: {Error}", process.ExitCode, error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create git worktree at {Path}", path);
            // Don't throw - allow session creation to continue even if worktree fails
        }
    }
}