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
/// Provides session metadata display and SDK-based session configuration.
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
    private ObservableCollection<string> _availableModels = new();

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
        
        // Only auto-fetch models on first load
        if (!_hasLoadedOnce)
        {
            _hasLoadedOnce = true;
            await RefreshModelAsync();
        }
    }

    /// <summary>
    /// Refreshes local session info from the app Session object.
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
        CurrentModel = _session.ModelId ?? "Unknown";

        // Refresh autonomous mode settings
        AllowAll = _session.AutonomousMode.AllowAll;
        AllowAllTools = _session.AutonomousMode.AllowAllTools;
        AllowAllPaths = _session.AutonomousMode.AllowAllPaths;
        AllowAllUrls = _session.AutonomousMode.AllowAllUrls;
        AutonomousModeStatus = _session.AutonomousMode.GetDisplayString();

        _logger.LogDebug("Refreshed local session info for {SessionId}", _session.SessionId);
    }

    /// <summary>
    /// Refreshes all information including models from SDK.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        RefreshLocalSessionInfo();
        await RefreshModelAsync();
    }

    /// <summary>
    /// Refreshes the available models from SDK.
    /// </summary>
    [RelayCommand]
    private async Task RefreshModelAsync()
    {
        if (_session == null) return;

        try
        {
            IsCommandRunning = true;
            StatusMessage = "Fetching available models...";

            // Get models from SDK
            var models = await _copilotService.GetAvailableModelsAsync();
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AvailableModels.Clear();
                foreach (var model in models)
                {
                    AvailableModels.Add(model);
                }
                
                // Set current model from session
                CurrentModel = _session.ModelId ?? "Unknown";
                SelectedModel = CurrentModel;
            });

            StatusMessage = $"Loaded {models.Count} models";
            _ = ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch models from SDK");
            StatusMessage = $"Error: {ex.Message}";
            _ = ClearStatusAfterDelayAsync(5000);
        }
        finally
        {
            IsCommandRunning = false;
        }
    }

    /// <summary>
    /// Changes the AI model by recreating the session.
    /// Shows a confirmation dialog before proceeding.
    /// </summary>
    [RelayCommand]
    private async Task ChangeModelAsync()
    {
        if (_session == null || string.IsNullOrEmpty(SelectedModel))
            return;

        // Skip if unchanged
        if (SelectedModel == CurrentModel)
        {
            StatusMessage = "Model unchanged";
            _ = ClearStatusAfterDelayAsync();
            return;
        }

        // Show confirmation dialog
        var result = MessageBox.Show(
            $"⚠️ Changing model to '{SelectedModel}' requires recreating the session.\n\n" +
            "Your message history and settings will be preserved, but:\n" +
            "• The current SDK session will be terminated\n" +
            "• Any running operations will be cancelled\n" +
            "• A new session will be created with the updated model\n\n" +
            "Continue?",
            "Confirm Model Change",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            // Reset selection to current model
            SelectedModel = CurrentModel;
            return;
        }

        try
        {
            IsCommandRunning = true;
            StatusMessage = $"Switching to {SelectedModel}...";

            await _copilotService.RecreateSessionAsync(_session, new SessionRecreateOptions
            {
                NewModel = SelectedModel
            });

            CurrentModel = SelectedModel;
            _session.ModelId = SelectedModel;
            await _sessionManager.SaveActiveSessionAsync();

            StatusMessage = $"Model changed to {SelectedModel}";
            RefreshLocalSessionInfo();
            _ = ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change model");
            MessageBox.Show($"Failed to change model: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            SelectedModel = CurrentModel; // Revert
            StatusMessage = "Model change failed";
            _ = ClearStatusAfterDelayAsync(3000);
        }
        finally
        {
            IsCommandRunning = false;
        }
    }

    /// <summary>
    /// Changes the working directory by recreating the session.
    /// Shows a confirmation dialog before proceeding.
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
            StatusMessage = "Already set to the same directory";
            _ = ClearStatusAfterDelayAsync();
            return;
        }

        if (!Directory.Exists(targetPath))
        {
            MessageBox.Show($"Directory not found: {targetPath}", "Invalid Directory", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Show confirmation dialog
        var result = MessageBox.Show(
            $"⚠️ Changing working directory to:\n{targetPath}\n\n" +
            "This requires recreating the session.\n\n" +
            "Your message history and settings will be preserved, but:\n" +
            "• The current SDK session will be terminated\n" +
            "• Any running operations will be cancelled\n" +
            "• A new session will be created in the new directory\n\n" +
            "Continue?",
            "Confirm Directory Change",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            NewDirectoryPath = WorkingDirectory; // Revert
            return;
        }

        try
        {
            IsCommandRunning = true;
            StatusMessage = "Changing working directory...";

            await _copilotService.RecreateSessionAsync(_session, new SessionRecreateOptions
            {
                NewWorkingDirectory = targetPath
            });

            _session.WorkingDirectory = targetPath;
            WorkingDirectory = targetPath;
            await _sessionManager.SaveActiveSessionAsync();

            StatusMessage = "Working directory changed";
            RefreshLocalSessionInfo();
            _ = ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change working directory");
            MessageBox.Show($"Failed to change directory: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            NewDirectoryPath = WorkingDirectory; // Revert
            StatusMessage = "Directory change failed";
            _ = ClearStatusAfterDelayAsync(3000);
        }
        finally
        {
            IsCommandRunning = false;
        }
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
    /// Show context command - disabled, SDK API not available.
    /// </summary>
    [RelayCommand]
    private void ShowContext()
    {
        // Not available in SDK - button is disabled in UI
        StatusMessage = "Context info not available in SDK mode";
        _ = ClearStatusAfterDelayAsync();
    }

    /// <summary>
    /// Compact session command - disabled, SDK handles this automatically.
    /// </summary>
    [RelayCommand]
    private void CompactSession()
    {
        // Not available in SDK - handled automatically by infinite sessions
        StatusMessage = "Compaction is automatic in SDK mode";
        _ = ClearStatusAfterDelayAsync();
    }

    /// <summary>
    /// Show usage command - disabled, SDK API not available.
    /// </summary>
    [RelayCommand]
    private void ShowUsage()
    {
        // Not available in SDK
        StatusMessage = "Usage statistics not available in SDK mode";
        _ = ClearStatusAfterDelayAsync();
    }

    /// <summary>
    /// Show session details - displays local session info.
    /// </summary>
    [RelayCommand]
    private void ShowSession()
    {
        // Show local session info in command output
        CommandOutput = $"Session: {SessionName}\n" +
                       $"ID: {SessionId}\n" +
                       $"Copilot Session: {CopilotSessionId}\n" +
                       $"Model: {CurrentModel}\n" +
                       $"Working Directory: {WorkingDirectory}\n" +
                       $"Messages: {MessageCount}\n" +
                       $"Created: {CreatedAt}\n" +
                       $"Last Active: {LastActiveAt}\n" +
                       $"Autonomous Mode: {AutonomousModeStatus}";
        StatusMessage = "Session info displayed";
        _ = ClearStatusAfterDelayAsync();
    }

    /// <summary>
    /// List directories command - disabled, SDK API not available.
    /// </summary>
    [RelayCommand]
    private void ListDirs()
    {
        // Not available in SDK
        StatusMessage = "List directories not available in SDK mode";
        _ = ClearStatusAfterDelayAsync();
    }

    /// <summary>
    /// Show MCP servers command - disabled, use MCP Config tab instead.
    /// </summary>
    [RelayCommand]
    private void ShowMcpServers()
    {
        // Not available - use MCP Config tab
        StatusMessage = "Use MCP Config tab to view servers";
        _ = ClearStatusAfterDelayAsync();
    }

    /// <summary>
    /// Clear history - shows warning that session must be deleted to clear.
    /// </summary>
    [RelayCommand]
    private void ClearHistory()
    {
        MessageBox.Show(
            "To clear session history in SDK mode, you need to delete the session and create a new one.\n\n" +
            "The SDK doesn't support clearing history within an existing session.",
            "Clear History Not Available",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
    /// </summary>
    [RelayCommand]
    private async Task ToggleAllowAllAsync()
    {
        if (_session == null) return;

        AllowAll = !AllowAll;
        _session.AutonomousMode.AllowAll = AllowAll;

        if (AllowAll)
        {
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

    private async Task ClearStatusAfterDelayAsync(int delayMs = 3000)
    {
        await Task.Delay(delayMs);
        if (!IsCommandRunning)
        {
            StatusMessage = string.Empty;
        }
    }
}