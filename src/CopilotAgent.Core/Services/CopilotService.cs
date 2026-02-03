using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CopilotAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Service for interacting with GitHub Copilot via copilot CLI
/// </summary>
public class CopilotService : ICopilotService
{
    private readonly ILogger<CopilotService> _logger;
    private readonly string _copilotPath;

    public CopilotService(ILogger<CopilotService> logger)
    {
        _logger = logger;
        _copilotPath = FindCopilotExecutable();
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
        _logger.LogInformation("Sending message to GitHub Copilot CLI for session {SessionId}: {Message}", 
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

            _logger.LogInformation("Copilot CLI response complete for session {SessionId}", session.SessionId);
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
        var ansiPattern = @"\x1B\[[0-9;]*[a-zA-Z]";
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
}
