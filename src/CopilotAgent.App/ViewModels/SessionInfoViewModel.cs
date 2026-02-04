using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for the Session Info tab.
/// Provides session metadata display and Copilot CLI command execution.
/// All async operations run on background threads to prevent UI freezing.
/// </summary>
public partial class SessionInfoViewModel : ViewModelBase
{
    private readonly ICopilotService _copilotService;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SessionInfoViewModel> _logger;
    private Session? _session;
    private bool _hasLoadedOnce;

    [ObservableProperty]
    private string _sessionId = string.Empty;

    [ObservableProperty]
    private string _sessionName = string.Empty;

    [ObservableProperty]
    private string _copilotSessionId = "Not captured";

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    [ObservableProperty]
    private string _newDirectoryPath = string.Empty;

    [ObservableProperty]
    private int _messageCount;

    [ObservableProperty]
    private string _createdAt = string.Empty;

    [ObservableProperty]
    private string _lastActiveAt = string.Empty;

    [ObservableProperty]
    private string _currentModel = "Loading...";

    [ObservableProperty]
    private string? _selectedModel;

    [ObservableProperty]
    private ObservableCollection<string> _availableModels = new()
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
    };

    [ObservableProperty]
    private string _commandOutput = string.Empty;

    [ObservableProperty]
    private bool _isCommandRunning;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Autonomous Mode Properties
    [ObservableProperty]
    private bool _allowAll;

    [ObservableProperty]
    private bool _allowAllTools;

    [ObservableProperty]
    private bool _allowAllPaths;

    [ObservableProperty]
    private bool _allowAllUrls;

    [ObservableProperty]
    private string _autonomousModeStatus = "⚠️ Manual Approval Required";

    public SessionInfoViewModel(
        ICopilotService copilotService,
        ISessionManager sessionManager,
        ILogger<SessionInfoViewModel> logger)
    {
        _copilotService = copilotService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Sets the session to display information for.
    /// </summary>
    public void SetSession(Session session)
    {
        _session = session;
        _hasLoadedOnce = false;
        RefreshLocalSessionInfo();
    }

    /// <summary>
    /// Called when the view is loaded/shown. Auto-refreshes data.
    /// </summary>
    public async Task RefreshOnLoadAsync()
    {
        if (_session == null) return;
        
        RefreshLocalSessionInfo();
        
        // Only auto-fetch model on first load
        if (!_hasLoadedOnce)
        {
            _hasLoadedOnce = true;
            await RefreshModelAsync();
        }
    }

    /// <summary>
    /// Refreshes local session info (no Copilot CLI call).
    /// </summary>
    private void RefreshLocalSessionInfo()
    {
        if (_session == null) return;

        SessionId = _session.SessionId;
        SessionName = _session.DisplayName;
        CopilotSessionId = _session.CopilotSessionId ?? "Not captured yet";
        WorkingDirectory = _session.WorkingDirectory ?? Environment.CurrentDirectory;
        NewDirectoryPath = WorkingDirectory;
        MessageCount = _session.MessageHistory.Count;
        CreatedAt = _session.CreatedAt.ToLocalTime().ToString("MMM dd, yyyy HH:mm");
        LastActiveAt = _session.LastActiveAt.ToLocalTime().ToString("MMM dd, yyyy HH:mm");

        // Refresh autonomous mode settings
        AllowAll = _session.AutonomousMode.AllowAll;
        AllowAllTools = _session.AutonomousMode.AllowAllTools;
        AllowAllPaths = _session.AutonomousMode.AllowAllPaths;
        AllowAllUrls = _session.AutonomousMode.AllowAllUrls;
        AutonomousModeStatus = _session.AutonomousMode.GetDisplayString();

        _logger.LogDebug("Refreshed local session info for {SessionId}", _session.SessionId);
    }

    /// <summary>
    /// Refreshes all information including Copilot session data.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        RefreshLocalSessionInfo();
        await RefreshModelAsync();
    }

    /// <summary>
    /// Refreshes the current model from Copilot.
    /// </summary>
    [RelayCommand]
    private async Task RefreshModelAsync()
    {
        if (_session == null) return;

        await RunCommandAsync("/model", "Fetching model info...", output =>
        {
            // Parse model from output - typically shows "Current model: model-name"
            CurrentModel = ParseModelFromOutput(output);
        });
    }

    private string ParseModelFromOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "Unknown";

        // First, try to find a known model name anywhere in the output
        var lowerOutput = output.ToLowerInvariant();
        foreach (var model in AvailableModels)
        {
            if (lowerOutput.Contains(model.ToLowerInvariant()))
                return model;
        }

        // Look for patterns like "model: xxx" or "Current model: xxx"
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var lower = trimmed.ToLowerInvariant();
            
            // Skip lines that are just instructions/menu items
            if (lower.Contains("select") || lower.Contains("choose") || 
                lower.Contains("available") || lower.Contains("options") ||
                lower.StartsWith("-") || lower.StartsWith("*"))
                continue;

            // Look for "model:" or "current:" patterns
            if (lower.Contains("model:") || lower.Contains("current:") || lower.Contains("using:"))
            {
                var colonIndex = trimmed.IndexOf(':');
                if (colonIndex >= 0 && colonIndex < trimmed.Length - 1)
                {
                    var modelPart = trimmed[(colonIndex + 1)..].Trim();
                    if (!string.IsNullOrWhiteSpace(modelPart))
                        return modelPart;
                }
            }
            
            // If line contains "model" and looks like a status line (not a menu)
            if (lower.Contains("model") && !lower.Contains("?") && trimmed.Length < 80)
            {
                // Extract any quoted or highlighted model name
                var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"[`'""]([^`'""]+)[`'""]");
                if (match.Success)
                    return match.Groups[1].Value;
            }
        }
        
        // Return first non-empty, non-menu line as fallback
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var lower = trimmed.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(trimmed) && 
                !lower.Contains("select") && !lower.Contains("choose") &&
                !trimmed.StartsWith("-") && !trimmed.StartsWith("*") &&
                trimmed.Length < 100)
            {
                return trimmed;
            }
        }
        
        return "Unknown";
    }

    /// <summary>
    /// Changes the AI model.
    /// </summary>
    [RelayCommand]
    private async Task ChangeModelAsync()
    {
        if (_session == null || string.IsNullOrEmpty(SelectedModel))
            return;

        await RunCommandAsync($"/model {SelectedModel}", $"Switching to {SelectedModel}...", output =>
        {
            CurrentModel = SelectedModel!;
            _session.ModelId = SelectedModel!;
        });
        
        await _sessionManager.SaveActiveSessionAsync();
    }

    /// <summary>
    /// Changes the working directory using /cwd command.
    /// Skips the call if directory is already set to the same path.
    /// </summary>
    [RelayCommand]
    private async Task ChangeDirectoryAsync()
    {
        if (_session == null || string.IsNullOrWhiteSpace(NewDirectoryPath))
            return;

        var targetPath = Path.GetFullPath(NewDirectoryPath.Trim());
        var currentPath = string.IsNullOrEmpty(WorkingDirectory) 
            ? string.Empty 
            : Path.GetFullPath(WorkingDirectory);

        // Skip if directory is unchanged
        if (string.Equals(targetPath, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            CommandOutput = "Already set to the same directory.";
            StatusMessage = "No change needed";
            _ = ClearStatusAfterDelayAsync();
            return;
        }

        if (!Directory.Exists(targetPath))
        {
            MessageBox.Show($"Directory not found: {targetPath}", "Invalid Directory", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RunCommandAsync($"/cwd {targetPath}", "Changing working directory...", async output =>
        {
            _session!.WorkingDirectory = targetPath;
            WorkingDirectory = targetPath;
            await _sessionManager.SaveActiveSessionAsync();
        });
    }

    /// <summary>
    /// Opens a folder browser dialog to select a directory.
    /// </summary>
    [RelayCommand]
    private void BrowseDirectory()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Working Directory",
            UseDescriptionForTitle = true,
            SelectedPath = WorkingDirectory
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            NewDirectoryPath = dialog.SelectedPath;
        }
    }

    /// <summary>
    /// Sends /context command to show context window usage.
    /// </summary>
    [RelayCommand]
    private async Task ShowContextAsync()
    {
        await RunCommandAsync("/context", "Analyzing context window...");
    }

    /// <summary>
    /// Sends /compact command to summarize and reduce token usage.
    /// </summary>
    [RelayCommand]
    private async Task CompactSessionAsync()
    {
        await RunCommandAsync("/compact", "Compacting conversation...", async output =>
        {
            if (_session != null)
            {
                _session.TokenBudget.CompactionCount++;
                _session.TokenBudget.LastCompactionAt = DateTime.UtcNow;
                await _sessionManager.SaveActiveSessionAsync();
            }
        });
    }

    /// <summary>
    /// Sends /usage command to show usage statistics.
    /// </summary>
    [RelayCommand]
    private async Task ShowUsageAsync()
    {
        await RunCommandAsync("/usage", "Fetching usage statistics...");
    }

    /// <summary>
    /// Sends /session command to show session info from Copilot.
    /// </summary>
    [RelayCommand]
    private async Task ShowSessionAsync()
    {
        await RunCommandAsync("/session", "Fetching session details...");
    }

    /// <summary>
    /// Sends /list-dirs command to show allowed directories.
    /// </summary>
    [RelayCommand]
    private async Task ListDirsAsync()
    {
        await RunCommandAsync("/list-dirs", "Listing allowed directories...");
    }

    /// <summary>
    /// Sends /mcp show command to display MCP server configuration.
    /// </summary>
    [RelayCommand]
    private async Task ShowMcpServersAsync()
    {
        await RunCommandAsync("/mcp show", "Fetching MCP server configuration...");
    }

    /// <summary>
    /// Sends /clear command to clear conversation history.
    /// </summary>
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var result = MessageBox.Show(
            "This will clear the Copilot session's conversation history. Continue?",
            "Confirm Clear",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await RunCommandAsync("/clear", "Clearing conversation history...");
        }
    }

    /// <summary>
    /// Opens file explorer at the working directory.
    /// </summary>
    [RelayCommand]
    private void OpenInExplorer()
    {
        if (string.IsNullOrEmpty(WorkingDirectory) || !Directory.Exists(WorkingDirectory))
        {
            MessageBox.Show("Working directory not found", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", WorkingDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open explorer");
            MessageBox.Show($"Failed to open explorer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Toggles full autonomous mode (YOLO mode).
    /// Equivalent to --allow-all or --yolo flag.
    /// </summary>
    [RelayCommand]
    private async Task ToggleAllowAllAsync()
    {
        if (_session == null) return;

        AllowAll = !AllowAll;
        _session.AutonomousMode.AllowAll = AllowAll;

        // When AllowAll is enabled, it supersedes individual settings
        if (AllowAll)
        {
            // Don't modify individual settings, AllowAll takes precedence
            _logger.LogInformation("Enabled full autonomous mode (YOLO) for session {SessionId}", _session.SessionId);
        }
        else
        {
            _logger.LogInformation("Disabled full autonomous mode for session {SessionId}", _session.SessionId);
        }

        AutonomousModeStatus = _session.AutonomousMode.GetDisplayString();
        await _sessionManager.SaveActiveSessionAsync();
        
        StatusMessage = AllowAll ? "🚀 YOLO Mode Enabled" : "YOLO Mode Disabled";
        _ = ClearStatusAfterDelayAsync();
    }

    /// <summary>
    /// Toggles allow all MCP tools permission.
    /// </summary>
    [RelayCommand]
    private async Task ToggleAllowAllToolsAsync()
    {
        if (_session == null) return;

        AllowAllTools = !AllowAllTools;
        _session.AutonomousMode.AllowAllTools = AllowAllTools;

        _logger.LogInformation("Set AllowAllTools={AllowAllTools} for session {SessionId}", 
            AllowAllTools, _session.SessionId);

        AutonomousModeStatus = _session.AutonomousMode.GetDisplayString();
        await _sessionManager.SaveActiveSessionAsync();
        
        StatusMessage = AllowAllTools ? "✅ All Tools Allowed" : "Tools require approval";
        _ = ClearStatusAfterDelayAsync();
    }

    /// <summary>
    /// Toggles allow all file system paths permission.
    /// </summary>
    [RelayCommand]
    private async Task ToggleAllowAllPathsAsync()
    {
        if (_session == null) return;

        AllowAllPaths = !AllowAllPaths;
        _session.AutonomousMode.AllowAllPaths = AllowAllPaths;

        _logger.LogInformation("Set AllowAllPaths={AllowAllPaths} for session {SessionId}", 
            AllowAllPaths, _session.SessionId);

        AutonomousModeStatus = _session.AutonomousMode.GetDisplayString();
        await _sessionManager.SaveActiveSessionAsync();
        
        StatusMessage = AllowAllPaths ? "✅ All Paths Allowed" : "Paths require approval";
        _ = ClearStatusAfterDelayAsync();
    }

    /// <summary>
    /// Toggles allow all URLs permission.
    /// </summary>
    [RelayCommand]
    private async Task ToggleAllowAllUrlsAsync()
    {
        if (_session == null) return;

        AllowAllUrls = !AllowAllUrls;
        _session.AutonomousMode.AllowAllUrls = AllowAllUrls;

        _logger.LogInformation("Set AllowAllUrls={AllowAllUrls} for session {SessionId}", 
            AllowAllUrls, _session.SessionId);

        AutonomousModeStatus = _session.AutonomousMode.GetDisplayString();
        await _sessionManager.SaveActiveSessionAsync();
        
        StatusMessage = AllowAllUrls ? "✅ All URLs Allowed" : "URLs require approval";
        _ = ClearStatusAfterDelayAsync();
    }

    /// <summary>
    /// Copies the Copilot session ID to clipboard.
    /// </summary>
    [RelayCommand]
    private void CopyCopilotSessionId()
    {
        if (string.IsNullOrEmpty(_session?.CopilotSessionId))
        {
            MessageBox.Show("No Copilot session ID captured yet", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Clipboard.SetText(_session.CopilotSessionId);
            StatusMessage = "Copied to clipboard";
            _ = ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy to clipboard");
        }
    }

    /// <summary>
    /// Runs a Copilot command on a background thread with proper UI updates.
    /// </summary>
    private async Task RunCommandAsync(string command, string statusMessage, Action<string>? onComplete = null)
    {
        if (_session == null)
        {
            StatusMessage = "No active session";
            return;
        }

        if (IsCommandRunning)
        {
            StatusMessage = "Please wait for current operation to complete";
            return;
        }

        try
        {
            IsCommandRunning = true;
            StatusMessage = statusMessage;
            CommandOutput = string.Empty;

            _logger.LogInformation("Executing command: {Command}", command);

            // Run on background thread
            await Task.Run(async () =>
            {
                await foreach (var chunk in _copilotService.SendMessageStreamingAsync(_session, command))
                {
                    // Update UI on dispatcher thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        CommandOutput = chunk.Content;
                    });
                }
            });

            StatusMessage = "Completed";
            
            // Execute completion callback
            if (onComplete != null)
            {
                if (onComplete.Method.ReturnType == typeof(Task))
                {
                    await ((Func<string, Task>)(object)onComplete)(CommandOutput);
                }
                else
                {
                    onComplete(CommandOutput);
                }
            }

            RefreshLocalSessionInfo();
            _ = ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _logger.LogError(ex, "Command failed: {Command}", command);
            _ = ClearStatusAfterDelayAsync(5000);
        }
        finally
        {
            IsCommandRunning = false;
        }
    }

    /// <summary>
    /// Overload for async callbacks.
    /// </summary>
    private async Task RunCommandAsync(string command, string statusMessage, Func<string, Task> onCompleteAsync)
    {
        if (_session == null)
        {
            StatusMessage = "No active session";
            return;
        }

        if (IsCommandRunning)
        {
            StatusMessage = "Please wait for current operation to complete";
            return;
        }

        try
        {
            IsCommandRunning = true;
            StatusMessage = statusMessage;
            CommandOutput = string.Empty;

            _logger.LogInformation("Executing command: {Command}", command);

            // Run on background thread
            await Task.Run(async () =>
            {
                await foreach (var chunk in _copilotService.SendMessageStreamingAsync(_session, command))
                {
                    // Update UI on dispatcher thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        CommandOutput = chunk.Content;
                    });
                }
            });

            StatusMessage = "Completed";
            
            // Execute async completion callback
            await onCompleteAsync(CommandOutput);

            RefreshLocalSessionInfo();
            _ = ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _logger.LogError(ex, "Command failed: {Command}", command);
            _ = ClearStatusAfterDelayAsync(5000);
        }
        finally
        {
            IsCommandRunning = false;
        }
    }

    private async Task ClearStatusAfterDelayAsync(int delayMs = 3000)
    {
        await Task.Delay(delayMs);
        if (!IsCommandRunning)
        {
            StatusMessage = string.Empty;
        }
    }
}