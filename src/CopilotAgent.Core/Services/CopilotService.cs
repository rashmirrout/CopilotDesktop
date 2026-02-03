using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CopilotAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Service for interacting with GitHub Copilot via copilot CLI.
/// 
/// Supports two modes of operation controlled by USE_PERSISTENT_SESSION:
/// 
/// 1. LEGACY MODE (USE_PERSISTENT_SESSION = false):
///    - Spawns a new Copilot CLI process for each message
///    - No conversation context between messages
///    - Simple but no memory of previous exchanges
/// 
/// 2. PERSISTENT SESSION MODE (USE_PERSISTENT_SESSION = true):
///    - Maintains one Copilot CLI process per chat session
///    - Conversation context preserved within app lifetime
///    - Process communicates via stdin/stdout in interactive mode
///    
/// FUTURE ENHANCEMENTS:
/// 
/// Option B - Conversation Summary on Restart:
///   When app restarts and loads saved session, send a brief summary
///   of previous conversation to Copilot as context:
///   "Previous conversation context: You were helping me with [topic].
///    Last message was about [summary]."
///   
/// Option C - Full Context Replay:
///   On process start, use BuildContextFromSession() to replay full
///   conversation history. Risk: token limits for long conversations.
///   Could limit to last N messages or truncate older ones.
/// </summary>
public class CopilotService : ICopilotService, IDisposable
{
    private readonly ILogger<CopilotService> _logger;
    private readonly string _copilotPath;
    private bool _disposed;

    /// <summary>
    /// Feature flag to enable persistent session mode.
    /// Set to true for conversation context support.
    /// Set to false to revert to legacy per-message process mode.
    /// 
    /// NOTE: Currently disabled due to issues with reading Copilot CLI response.
    /// The interactive mode prompt detection needs refinement.
    /// </summary>
    private const bool USE_PERSISTENT_SESSION = false;

    /// <summary>
    /// Tracks active Copilot CLI processes per session.
    /// Key: SessionId, Value: Process info for that session.
    /// </summary>
    private readonly ConcurrentDictionary<string, CopilotSessionProcess> _sessionProcesses = new();

    /// <summary>
    /// Lock for process creation to prevent race conditions.
    /// </summary>
    private readonly SemaphoreSlim _processCreationLock = new(1, 1);

    /// <summary>
    /// Represents a persistent Copilot CLI process for a session.
    /// </summary>
    private class CopilotSessionProcess : IDisposable
    {
        public required Process Process { get; init; }
        public required StreamWriter StdinWriter { get; init; }
        public required StreamReader StdoutReader { get; init; }
        public required StreamReader StderrReader { get; init; }
        public CancellationTokenSource CancellationSource { get; } = new();
        public bool IsRunning => Process != null && !Process.HasExited;

        public void Dispose()
        {
            CancellationSource.Cancel();
            CancellationSource.Dispose();
            
            try
            {
                StdinWriter.Dispose();
                StdoutReader.Dispose();
                StderrReader.Dispose();
                
                if (!Process.HasExited)
                {
                    Process.Kill();
                }
                Process.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }

    public CopilotService(ILogger<CopilotService> logger)
    {
        _logger = logger;
        _copilotPath = FindCopilotExecutable();
        _logger.LogInformation("CopilotService initialized. PersistentSessionMode={Mode}", USE_PERSISTENT_SESSION);
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
        // GitHub Copilot CLI available models
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
        if (USE_PERSISTENT_SESSION)
        {
            // New persistent session mode - maintains conversation context
            await foreach (var msg in SendMessagePersistentAsync(session, userMessage, cancellationToken))
            {
                yield return msg;
            }
        }
        else
        {
            // Legacy mode - new process per message, no context
            await foreach (var msg in SendMessageLegacyAsync(session, userMessage, cancellationToken))
            {
                yield return msg;
            }
        }
    }

    /// <summary>
    /// Persistent session mode: Maintains one Copilot CLI process per session.
    /// Messages are sent via stdin and responses read from stdout.
    /// Conversation context is preserved within the app lifetime.
    /// 
    /// After app restart: Process is created fresh, context does not persist.
    /// (See class documentation for future enhancement options B and C)
    /// </summary>
    private async IAsyncEnumerable<ChatMessage> SendMessagePersistentAsync(
        Session session,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending message via persistent session for {SessionId}: {Message}",
            session.SessionId, userMessage.Length > 50 ? userMessage[..50] + "..." : userMessage);

        var message = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = string.Empty,
            IsStreaming = true,
            Timestamp = DateTime.UtcNow
        };

        CopilotSessionProcess? sessionProcess = null;

        try
        {
            // Get or create the persistent process for this session
            sessionProcess = await GetOrCreateSessionProcessAsync(session, cancellationToken);

            if (sessionProcess == null || !sessionProcess.IsRunning)
            {
                message.Content = "Error: Failed to start or connect to Copilot CLI process";
                message.IsStreaming = false;
                message.IsError = true;
                yield return message;
                yield break;
            }

            // Send the message to Copilot via stdin
            await sessionProcess.StdinWriter.WriteLineAsync(userMessage);
            await sessionProcess.StdinWriter.FlushAsync();

            var outputBuilder = new StringBuilder();
            var buffer = new char[256];
            var lastYieldTime = DateTime.UtcNow;
            var promptDetected = false;

            // Read response until we detect the prompt (ready for next input)
            // Copilot CLI typically shows a prompt like "> " when ready
            while (!cancellationToken.IsCancellationRequested && !promptDetected)
            {
                // Check if process is still running
                if (!sessionProcess.IsRunning)
                {
                    _logger.LogWarning("Copilot process exited unexpectedly for session {SessionId}", session.SessionId);
                    break;
                }

                // Try to read available data
                if (sessionProcess.Process.StandardOutput.Peek() >= 0)
                {
                    var charsRead = await sessionProcess.StdoutReader.ReadAsync(buffer, 0, buffer.Length);
                    if (charsRead > 0)
                    {
                        var chunk = new string(buffer, 0, charsRead);
                        outputBuilder.Append(chunk);

                        // Check for end of response markers
                        // Copilot CLI typically ends responses and shows a prompt
                        var content = outputBuilder.ToString();
                        
                        // Detect if we've received a complete response
                        // Look for common prompt patterns that indicate Copilot is waiting for input
                        if (IsPromptDetected(content))
                        {
                            promptDetected = true;
                            // Remove the prompt from the output
                            content = RemovePromptFromOutput(content);
                            outputBuilder.Clear();
                            outputBuilder.Append(content);
                        }

                        // Clean and yield intermediate results
                        var cleanedContent = CleanCopilotOutput(outputBuilder.ToString());
                        message.Content = cleanedContent;
                        
                        // Yield periodically to update UI
                        if ((DateTime.UtcNow - lastYieldTime).TotalMilliseconds > 50)
                        {
                            yield return message;
                            lastYieldTime = DateTime.UtcNow;
                        }
                    }
                }
                else
                {
                    // No data available, wait a bit
                    await Task.Delay(10, cancellationToken);
                    
                    // If we have content and haven't received anything for a while, 
                    // consider the response complete
                    if (outputBuilder.Length > 0)
                    {
                        // Check for end-of-response by looking at patterns
                        var content = outputBuilder.ToString();
                        if (LooksLikeCompleteResponse(content))
                        {
                            promptDetected = true;
                        }
                    }
                }
            }

            // Final message
            message.Content = CleanCopilotOutput(outputBuilder.ToString());
            message.IsStreaming = false;
            yield return message;

            _logger.LogInformation("Persistent session response complete for {SessionId}", session.SessionId);
        }
        finally
        {
            // Don't dispose the process - keep it for next message
            // Process will be disposed when session is closed or app exits
        }
    }

    /// <summary>
    /// Legacy mode: Spawns a new process for each message.
    /// No conversation context is preserved between messages.
    /// </summary>
    private async IAsyncEnumerable<ChatMessage> SendMessageLegacyAsync(
        Session session,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending message via legacy mode for session {SessionId}: {Message}", 
            session.SessionId, userMessage.Length > 50 ? userMessage.Substring(0, 50) + "..." : userMessage);

        var message = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = string.Empty,
            IsStreaming = true,
            Timestamp = DateTime.UtcNow
        };

        // Use streaming mode for real-time output
        var processInfo = new ProcessStartInfo
        {
            FileName = _copilotPath,
            Arguments = $"-p \"{EscapeArgument(userMessage)}\" -s --stream on",
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
            int charsRead;

            // Read output as it streams
            while (!process.StandardOutput.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                charsRead = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length);
                if (charsRead > 0)
                {
                    var chunk = new string(buffer, 0, charsRead);
                    outputBuilder.Append(chunk);
                    
                    // Clean up the output - remove control characters and spinners
                    var cleanedContent = CleanCopilotOutput(outputBuilder.ToString());
                    message.Content = cleanedContent;
                    yield return message;
                }
            }

            // Wait for process to complete
            await process.WaitForExitAsync(cancellationToken);

            // If there was an error, include stderr
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

            _logger.LogInformation("Legacy mode response complete for session {SessionId}", session.SessionId);
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// Gets existing Copilot process for session or creates a new one.
    /// Uses lazy initialization - process is created on first message.
    /// </summary>
    private async Task<CopilotSessionProcess?> GetOrCreateSessionProcessAsync(
        Session session,
        CancellationToken cancellationToken)
    {
        // Try to get existing process
        if (_sessionProcesses.TryGetValue(session.SessionId, out var existingProcess))
        {
            if (existingProcess.IsRunning)
            {
                _logger.LogDebug("Reusing existing Copilot process for session {SessionId}", session.SessionId);
                return existingProcess;
            }
            else
            {
                // Process died, remove it and create new one
                _logger.LogWarning("Copilot process died for session {SessionId}, restarting", session.SessionId);
                _sessionProcesses.TryRemove(session.SessionId, out _);
                existingProcess.Dispose();
            }
        }

        // Create new process (with lock to prevent race conditions)
        await _processCreationLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_sessionProcesses.TryGetValue(session.SessionId, out existingProcess) && existingProcess.IsRunning)
            {
                return existingProcess;
            }

            _logger.LogInformation("Starting new Copilot process for session {SessionId}", session.SessionId);

            var processInfo = new ProcessStartInfo
            {
                FileName = _copilotPath,
                // Start in interactive session mode - no -p flag, just -s for session
                Arguments = "-s --stream on",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = session.WorkingDirectory ?? Environment.CurrentDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardInputEncoding = Encoding.UTF8
            };

            var process = Process.Start(processInfo);
            if (process == null)
            {
                _logger.LogError("Failed to start Copilot CLI process for session {SessionId}", session.SessionId);
                return null;
            }

            var sessionProcess = new CopilotSessionProcess
            {
                Process = process,
                StdinWriter = process.StandardInput,
                StdoutReader = process.StandardOutput,
                StderrReader = process.StandardError
            };

            // Wait a bit for process to initialize
            await Task.Delay(100, cancellationToken);

            // Read any initial output/prompt
            await ConsumeInitialOutputAsync(sessionProcess, cancellationToken);

            _sessionProcesses[session.SessionId] = sessionProcess;
            _logger.LogInformation("Copilot process started for session {SessionId}, PID={Pid}", 
                session.SessionId, process.Id);

            return sessionProcess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Copilot process for session {SessionId}", session.SessionId);
            return null;
        }
        finally
        {
            _processCreationLock.Release();
        }
    }

    /// <summary>
    /// Consumes initial output from Copilot CLI when process starts.
    /// This clears any welcome message or initial prompt.
    /// </summary>
    private async Task ConsumeInitialOutputAsync(CopilotSessionProcess sessionProcess, CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new char[1024];
            var timeout = DateTime.UtcNow.AddSeconds(2);

            while (DateTime.UtcNow < timeout && !cancellationToken.IsCancellationRequested)
            {
                if (sessionProcess.Process.StandardOutput.Peek() >= 0)
                {
                    var charsRead = await sessionProcess.StdoutReader.ReadAsync(buffer, 0, buffer.Length);
                    if (charsRead > 0)
                    {
                        var initialOutput = new string(buffer, 0, charsRead);
                        _logger.LogDebug("Consumed initial Copilot output: {Output}", 
                            initialOutput.Length > 100 ? initialOutput[..100] + "..." : initialOutput);
                        
                        // Continue reading until no more initial data
                        timeout = DateTime.UtcNow.AddMilliseconds(200);
                    }
                }
                else
                {
                    await Task.Delay(50, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error consuming initial Copilot output");
        }
    }

    /// <summary>
    /// Detects if the output contains a prompt indicating Copilot is ready for next input.
    /// </summary>
    private static bool IsPromptDetected(string content)
    {
        if (string.IsNullOrEmpty(content))
            return false;

        // Common prompt patterns for Copilot CLI
        // Typically ends with "> " or ">> " or similar
        var trimmed = content.TrimEnd();
        return trimmed.EndsWith(">") ||
               trimmed.EndsWith(">>") ||
               trimmed.EndsWith("> ") ||
               trimmed.EndsWith(">> ") ||
               trimmed.EndsWith("copilot>") ||
               trimmed.EndsWith("copilot> ");
    }

    /// <summary>
    /// Removes the prompt text from the end of output.
    /// </summary>
    private static string RemovePromptFromOutput(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // Remove common prompt patterns from the end
        var lines = content.Split('\n');
        var resultLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmedLine = line.TrimEnd();
            
            // Skip lines that are just prompts
            if (i == lines.Length - 1 &&
                (trimmedLine == ">" || trimmedLine == ">>" || 
                 trimmedLine == "copilot>" || string.IsNullOrWhiteSpace(trimmedLine)))
            {
                continue;
            }

            resultLines.Add(line);
        }

        return string.Join('\n', resultLines);
    }

    /// <summary>
    /// Heuristic to detect if response looks complete.
    /// Used when no explicit prompt is detected.
    /// </summary>
    private static bool LooksLikeCompleteResponse(string content)
    {
        if (string.IsNullOrEmpty(content))
            return false;

        // Response is likely complete if:
        // - Ends with punctuation followed by newlines
        // - Contains reasonable amount of text
        // - Hasn't received new data for a while (handled by caller)
        
        var trimmed = content.TrimEnd();
        if (trimmed.Length < 10)
            return false;

        // Check for sentence-ending punctuation
        return trimmed.EndsWith(".") ||
               trimmed.EndsWith("!") ||
               trimmed.EndsWith("?") ||
               trimmed.EndsWith("```") ||
               trimmed.EndsWith("```\n");
    }

    /// <summary>
    /// Terminates the Copilot process for a specific session.
    /// Call this when switching sessions or closing the app.
    /// </summary>
    public void TerminateSessionProcess(string sessionId)
    {
        if (_sessionProcesses.TryRemove(sessionId, out var sessionProcess))
        {
            _logger.LogInformation("Terminating Copilot process for session {SessionId}", sessionId);
            sessionProcess.Dispose();
        }
    }

    /// <summary>
    /// Terminates all active Copilot processes.
    /// Call this when closing the application.
    /// </summary>
    public void TerminateAllProcesses()
    {
        _logger.LogInformation("Terminating all Copilot processes ({Count} active)", _sessionProcesses.Count);
        
        foreach (var kvp in _sessionProcesses)
        {
            try
            {
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing Copilot process for session {SessionId}", kvp.Key);
            }
        }
        
        _sessionProcesses.Clear();
    }

    public async Task<ChatMessage> SendMessageAsync(
        Session session,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();
        
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

    private string BuildContextFromSession(Session session)
    {
        var context = new StringBuilder();
        
        // Add system prompt if available
        if (!string.IsNullOrEmpty(session.SystemPrompt))
        {
            context.AppendLine(session.SystemPrompt);
            context.AppendLine();
        }

        // Add recent messages (limit to last 10 to avoid token limits)
        var recentMessages = session.MessageHistory
            .Where(m => m.Role != MessageRole.System)
            .TakeLast(10);

        foreach (var msg in recentMessages)
        {
            var role = msg.Role switch
            {
                MessageRole.User => "User",
                MessageRole.Assistant => "Assistant",
                _ => msg.Role.ToString()
            };
            context.AppendLine($"{role}: {msg.Content}");
        }

        return context.ToString();
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
        // Try to find copilot in PATH
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (pathVariable != null)
        {
            var paths = pathVariable.Split(Path.PathSeparator);
            foreach (var path in paths)
            {
                // Check for copilot.cmd (Windows npm global install)
                var copilotPath = Path.Combine(path, "copilot.cmd");
                if (File.Exists(copilotPath))
                {
                    _logger.LogInformation("Found copilot at: {Path}", copilotPath);
                    return copilotPath;
                }
                
                // Check for copilot.exe
                copilotPath = Path.Combine(path, "copilot.exe");
                if (File.Exists(copilotPath))
                {
                    _logger.LogInformation("Found copilot at: {Path}", copilotPath);
                    return copilotPath;
                }

                // Check for copilot (no extension - Linux/Mac or PATH lookup)
                copilotPath = Path.Combine(path, "copilot");
                if (File.Exists(copilotPath))
                {
                    _logger.LogInformation("Found copilot at: {Path}", copilotPath);
                    return copilotPath;
                }
            }
        }

        // Default to just "copilot" and hope it's in PATH
        _logger.LogInformation("Using 'copilot' from PATH");
        return "copilot";
    }

    private static string EscapeArgument(string argument)
    {
        // Escape double quotes and backslashes for command line arguments
        return argument
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", " ")
            .Replace("\r", "");
    }

    private static string CleanCopilotOutput(string output)
    {
        if (string.IsNullOrEmpty(output))
            return string.Empty;

        // Remove ANSI escape codes
        var ansiPattern = @"\[[0-9;]*[a-zA-Z]";
        output = System.Text.RegularExpressions.Regex.Replace(output, ansiPattern, "");

        // Remove spinner characters
        var spinnerChars = new[] { '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏', '⠋', '⣾', '⣽', '⣻', '⢿', '⡿', '⣟', '⣯', '⣷' };
        foreach (var c in spinnerChars)
        {
            output = output.Replace(c.ToString(), "");
        }

        // Remove bullet point characters that might appear at the start
        output = output.TrimStart('●', ' ', '\t');

        // Remove carriage returns and normalize line endings
        output = output.Replace("\r\n", "\n").Replace("\r", "\n");

        // Remove multiple consecutive newlines
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
        _processCreationLock.Dispose();
    }
}