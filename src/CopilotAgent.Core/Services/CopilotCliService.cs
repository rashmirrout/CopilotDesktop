using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using CopilotAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Legacy CLI-based implementation for interacting with GitHub Copilot via copilot CLI.
/// 
/// This is the fallback implementation when SDK mode is disabled.
/// Uses Process.Start() to spawn Copilot CLI processes.
/// 
/// Supports two modes of operation controlled by USE_SESSION_CONTINUATION:
/// 
/// 1. LEGACY MODE (USE_SESSION_CONTINUATION = false):
///    - Spawns a new Copilot CLI process for each message with -p flag
///    - No conversation context between messages
///    - Simple but no memory of previous exchanges
/// 
/// 2. SESSION CONTINUATION MODE (USE_SESSION_CONTINUATION = true):
///    - Uses Copilot CLI's built-in session management
///    - First message: Creates a new Copilot session
///    - Subsequent messages: Uses --continue to resume the most recent session
///    - Conversation context preserved within Copilot's session storage
/// </summary>
public class CopilotCliService : ICopilotService, IDisposable
{
    private readonly ILogger<CopilotCliService> _logger;
    private readonly string _copilotPath;
    private bool _disposed;

    /// <summary>
    /// Feature flag to enable session continuation mode using --continue flag.
    /// Set to true for conversation context support.
    /// Set to false to revert to legacy per-message process mode.
    /// </summary>
    private const bool USE_SESSION_CONTINUATION = true;

    /// <summary>
    /// Tracks whether a session has had its first message sent (in current app run).
    /// Key: SessionId, Value: true if first message was sent
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _sessionHasStarted = new();

    /// <summary>
    /// Path to Copilot CLI's session storage
    /// </summary>
    private readonly string _copilotSessionStatePath;

    public CopilotCliService(ILogger<CopilotCliService> logger)
    {
        _logger = logger;
        _copilotPath = FindCopilotExecutable();
        _copilotSessionStatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot",
            "session-state"
        );
        _logger.LogInformation("CopilotCliService initialized. SessionContinuationMode={Mode}", USE_SESSION_CONTINUATION);
    }

    public async Task<bool> IsCopilotAvailableAsync()
    {
        try
        {
            var result = await ExecuteCopilotCommandAsync("--version");
            return result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check Copilot availability");
            return false;
        }
    }

    public async Task<List<string>> GetAvailableModelsAsync()
    {
        return await Task.FromResult(new List<string>
        {
            "claude-sonnet-4.5",
            "claude-haiku-4.5",
            "claude-opus-4.5",
            "claude-sonnet-4",
            "gemini-3-pro-preview",
            "gpt-5.2-codex",
            "gpt-5.2",
            "gpt-5.1-codex-max",
            "gpt-5.1-codex",
            "gpt-5.1",
            "gpt-5",
            "gpt-5.1-codex-mini",
            "gpt-5-mini",
            "gpt-4.1"
        });
    }

    public async IAsyncEnumerable<ChatMessage> SendMessageStreamingAsync(
        Session session,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending message for session {SessionId}: {Message}", 
            session.SessionId, userMessage.Length > 50 ? userMessage[..50] + "..." : userMessage);

        var message = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = string.Empty,
            IsStreaming = true,
            Timestamp = DateTime.UtcNow
        };

        // Build command arguments
        string arguments;
        bool shouldCaptureSessionId = false;
        
        // Get autonomous mode arguments from session settings
        var autonomousArgs = session.AutonomousMode.GetCliArguments();
        var autonomousArgsWithSpace = string.IsNullOrEmpty(autonomousArgs) ? "" : $"{autonomousArgs} ";
        
        if (USE_SESSION_CONTINUATION)
        {
            // Check if we have a stored Copilot session ID (from previous app run)
            if (!string.IsNullOrEmpty(session.CopilotSessionId))
            {
                // We have a stored Copilot session ID - use --resume to reconnect
                arguments = $"--resume {session.CopilotSessionId} {autonomousArgsWithSpace}-p \"{EscapeArgument(userMessage)}\" -s --stream on";
                _sessionHasStarted[session.SessionId] = true;
                _logger.LogDebug("Resuming Copilot session {CopilotSessionId} for {SessionId} with autonomous args: {Args}", 
                    session.CopilotSessionId, session.SessionId, autonomousArgs);
            }
            else
            {
                // Check if this session has already sent a message (in this app run)
                var hasStarted = _sessionHasStarted.GetOrAdd(session.SessionId, false);
                
                if (hasStarted)
                {
                    // Subsequent message - use --continue to resume the most recent session
                    arguments = $"--continue {autonomousArgsWithSpace}-p \"{EscapeArgument(userMessage)}\" -s --stream on";
                    _logger.LogDebug("Using --continue for session {SessionId} with autonomous args: {Args}", 
                        session.SessionId, autonomousArgs);
                }
                else
                {
                    // First message - start new session, capture the session ID after
                    arguments = $"{autonomousArgsWithSpace}-p \"{EscapeArgument(userMessage)}\" -s --stream on";
                    _sessionHasStarted[session.SessionId] = true;
                    shouldCaptureSessionId = true;
                    _logger.LogDebug("Starting new Copilot session for {SessionId} with autonomous args: {Args}", 
                        session.SessionId, autonomousArgs);
                }
            }
        }
        else
        {
            // Legacy mode - no session continuation
            arguments = $"{autonomousArgsWithSpace}-p \"{EscapeArgument(userMessage)}\" -s --stream on";
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = _copilotPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = session.WorkingDirectory ?? Environment.CurrentDirectory
        };

        Process? process = null;
        try
        {
            process = Process.Start(processInfo);
            if (process == null)
            {
                message.Content = "Error: Failed to start Copilot CLI process";
                message.IsStreaming = false;
                message.IsError = true;
                yield return message;
                yield break;
            }

            var outputBuilder = new StringBuilder();
            var buffer = new char[256];

            // Read output as it streams
            while (!process.StandardOutput.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var charsRead = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length);
                if (charsRead > 0)
                {
                    var chunk = new string(buffer, 0, charsRead);
                    outputBuilder.Append(chunk);
                    
                    var cleanedContent = CleanCopilotOutput(outputBuilder.ToString());
                    message.Content = cleanedContent;
                    yield return message;
                }
            }

            await process.WaitForExitAsync(cancellationToken);

            // Check for errors
            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    _logger.LogWarning("Copilot CLI stderr: {Stderr}", stderr);
                    if (string.IsNullOrWhiteSpace(message.Content))
                    {
                        message.Content = $"Error: {stderr}";
                        message.IsError = true;
                    }
                }
            }

            // Final message
            message.Content = CleanCopilotOutput(outputBuilder.ToString());
            message.IsStreaming = false;
            yield return message;

            _logger.LogInformation("Response complete for session {SessionId}", session.SessionId);
            
            // Capture the Copilot session ID if this was the first message
            if (shouldCaptureSessionId && string.IsNullOrEmpty(session.CopilotSessionId))
            {
                var copilotSessionId = await TryCaptureCopilotSessionIdAsync(
                    session.WorkingDirectory ?? Environment.CurrentDirectory);
                if (!string.IsNullOrEmpty(copilotSessionId))
                {
                    session.CopilotSessionId = copilotSessionId;
                    _logger.LogInformation("Captured Copilot session ID {CopilotSessionId} for session {SessionId}", 
                        copilotSessionId, session.SessionId);
                }
            }
        }
        finally
        {
            process?.Dispose();
        }
    }

    public async Task<ChatMessage> SendMessageAsync(
        Session session,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        await foreach (var msg in SendMessageStreamingAsync(session, userMessage, cancellationToken))
        {
            if (!msg.IsStreaming)
            {
                return msg;
            }
        }

        return new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = "No response received",
            IsError = true
        };
    }

    public async Task<ToolResult> ExecuteCommandAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing command: {Command} in {WorkingDirectory}", command, workingDirectory);

        var startTime = DateTime.UtcNow;
        var processInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null)
            {
                return new ToolResult
                {
                    Success = false,
                    Error = "Failed to start process"
                };
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            return new ToolResult
            {
                Success = process.ExitCode == 0,
                Stdout = stdout,
                Stderr = stderr,
                ExitCode = process.ExitCode,
                DurationMs = (long)duration
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command: {Command}", command);
            return new ToolResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Terminates the Copilot session tracking for a specific session.
    /// The actual Copilot CLI session is managed by Copilot itself.
    /// </summary>
    public void TerminateSessionProcess(string sessionId)
    {
        _sessionHasStarted.TryRemove(sessionId, out _);
        _logger.LogDebug("Cleared session tracking for {SessionId}", sessionId);
    }

    /// <summary>
    /// Clears all session tracking.
    /// </summary>
    public void TerminateAllProcesses()
    {
        _sessionHasStarted.Clear();
        _logger.LogDebug("Cleared all session tracking");
    }

    public Task AbortAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        // CLI mode doesn't support graceful abort - just terminate tracking
        TerminateSessionProcess(sessionId);
        return Task.CompletedTask;
    }

    private async Task<(int ExitCode, string Output)> ExecuteCopilotCommandAsync(string arguments)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = _copilotPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            return (-1, string.Empty);
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, output);
    }

    private string FindCopilotExecutable()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (pathVariable != null)
        {
            var paths = pathVariable.Split(Path.PathSeparator);
            foreach (var path in paths)
            {
                var copilotPath = Path.Combine(path, "copilot.cmd");
                if (File.Exists(copilotPath))
                {
                    _logger.LogInformation("Found copilot at: {Path}", copilotPath);
                    return copilotPath;
                }
                
                copilotPath = Path.Combine(path, "copilot.exe");
                if (File.Exists(copilotPath))
                {
                    _logger.LogInformation("Found copilot at: {Path}", copilotPath);
                    return copilotPath;
                }

                copilotPath = Path.Combine(path, "copilot");
                if (File.Exists(copilotPath))
                {
                    _logger.LogInformation("Found copilot at: {Path}", copilotPath);
                    return copilotPath;
                }
            }
        }

        _logger.LogInformation("Using 'copilot' from PATH");
        return "copilot";
    }

    private static string EscapeArgument(string argument)
    {
        return argument
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", " ")
            .Replace("\r", "");
    }

    /// <summary>
    /// Attempts to find the Copilot CLI session ID that was just created.
    /// Scans the session-state directory for the most recently created session
    /// matching the working directory.
    /// </summary>
    private async Task<string?> TryCaptureCopilotSessionIdAsync(string workingDirectory)
    {
        try
        {
            if (!Directory.Exists(_copilotSessionStatePath))
            {
                _logger.LogWarning("Copilot session-state directory not found: {Path}", _copilotSessionStatePath);
                return null;
            }

            // Get all session directories
            var sessionDirs = Directory.GetDirectories(_copilotSessionStatePath)
                .Where(d => Guid.TryParse(Path.GetFileName(d), out _)) // Only GUID-named folders
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.LastWriteTime) // Most recent first
                .Take(10) // Only check recent sessions
                .ToList();

            foreach (var sessionDir in sessionDirs)
            {
                var workspaceYaml = Path.Combine(sessionDir.FullName, "workspace.yaml");
                if (!File.Exists(workspaceYaml))
                    continue;

                try
                {
                    var yamlContent = await File.ReadAllTextAsync(workspaceYaml);
                    
                    // Simple YAML parsing for workspace.yaml (format: "key: value")
                    var cwd = ParseYamlValue(yamlContent, "cwd");
                    var createdAtStr = ParseYamlValue(yamlContent, "created_at");

                    if (!string.IsNullOrEmpty(cwd))
                    {
                        // Normalize paths for comparison
                        var normalizedCwd = Path.GetFullPath(cwd).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        var normalizedWorkingDir = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                        if (string.Equals(normalizedCwd, normalizedWorkingDir, StringComparison.OrdinalIgnoreCase))
                        {
                            // Check if this session was created recently (within last 60 seconds)
                            if (!string.IsNullOrEmpty(createdAtStr) && DateTime.TryParse(createdAtStr, out var createdAt))
                            {
                                var age = DateTime.UtcNow - createdAt;
                                if (age.TotalSeconds < 60)
                                {
                                    return sessionDir.Name; // The GUID
                                }
                            }
                            
                            // Fallback: if directory was modified recently
                            if ((DateTime.Now - sessionDir.LastWriteTime).TotalSeconds < 60)
                            {
                                return sessionDir.Name;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse workspace.yaml in {SessionDir}", sessionDir.FullName);
                }
            }

            _logger.LogDebug("Could not find matching Copilot session for cwd: {WorkingDirectory}", workingDirectory);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture Copilot session ID");
            return null;
        }
    }

    /// <summary>
    /// Simple YAML value parser for key: value format.
    /// </summary>
    private static string? ParseYamlValue(string yamlContent, string key)
    {
        var pattern = $@"^{Regex.Escape(key)}:\s*(.+?)\s*$";
        var match = Regex.Match(yamlContent, pattern, RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string CleanCopilotOutput(string output)
    {
        if (string.IsNullOrEmpty(output))
            return string.Empty;

        // Remove ANSI escape codes
        var ansiPattern = @"\[[0-9;]*[a-zA-Z]";
        output = Regex.Replace(output, ansiPattern, "");

        // Remove spinner characters
        var spinnerChars = new[] { '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏', '⠋', '⣾', '⣽', '⣻', '⢿', '⡿', '⣟', '⣯', '⣷' };
        foreach (var c in spinnerChars)
        {
            output = output.Replace(c.ToString(), "");
        }

        output = output.TrimStart('●', ' ', '\t');
        output = output.Replace("\r\n", "\n").Replace("\r", "\n");

        while (output.Contains("\n\n\n"))
        {
            output = output.Replace("\n\n\n", "\n\n");
        }

        return output.Trim();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        TerminateAllProcesses();
    }
}