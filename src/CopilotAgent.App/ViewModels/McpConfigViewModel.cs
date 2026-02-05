using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// Represents the pending state of an MCP server toggle
/// </summary>
public enum McpPendingState
{
    /// <summary>No pending change</summary>
    None,
    /// <summary>Server will be enabled when applied</summary>
    PendingEnable,
    /// <summary>Server will be disabled when applied</summary>
    PendingDisable
}

/// <summary>
/// ViewModel for MCP server configuration with live session view
/// </summary>
public partial class McpConfigViewModel : ObservableObject
{
    private readonly IMcpService _mcpService;
    private readonly ISessionManager _sessionManager;
    private readonly ICopilotService _copilotService;
    private readonly ILogger<McpConfigViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<LiveMcpServerViewModel> _liveServers = new();

    [ObservableProperty]
    private ObservableCollection<McpServerWithToolsViewModel> _configuredServers = new();

    [ObservableProperty]
    private McpServerWithToolsViewModel? _selectedServer;

    [ObservableProperty]
    private bool _hasPendingChanges;

    [ObservableProperty]
    private int _pendingChangesCount;

    [ObservableProperty]
    private string _mcpConfigFilePath = string.Empty;

    [ObservableProperty]
    private bool _isApplying;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasActiveSession;

    [ObservableProperty]
    private string _sessionStatusText = "No active session";

    public McpConfigViewModel(
        IMcpService mcpService,
        ISessionManager sessionManager,
        ICopilotService copilotService,
        ILogger<McpConfigViewModel> logger)
    {
        _mcpService = mcpService;
        _sessionManager = sessionManager;
        _copilotService = copilotService;
        _logger = logger;

        _mcpService.ServerStatusChanged += OnServerStatusChanged;
        
        // Set MCP config file path
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        McpConfigFilePath = Path.Combine(homeDir, ".copilot", "mcp-config.json");
    }

    public async Task InitializeAsync()
    {
        // Load servers from Copilot MCP config file first
        await _mcpService.LoadServersFromCopilotConfigAsync();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            // Reload from Copilot config to get latest
            await _mcpService.LoadServersFromCopilotConfigAsync();
            
            // Check for active session
            var session = _sessionManager.ActiveSession;
            HasActiveSession = session != null && _copilotService.HasActiveSession(session.SessionId);
            
            if (HasActiveSession && session != null)
            {
                SessionStatusText = $"Session: {session.DisplayName} (Active)";
                
                // Query live MCP servers from SDK session
                await RefreshLiveServersAsync(session.SessionId);
            }
            else
            {
                SessionStatusText = session != null ? $"Session: {session.DisplayName} (Not connected)" : "No active session";
                LiveServers.Clear();
            }
            
            // Also load configured servers for the "Configure" view
            await RefreshConfiguredServersAsync();

            UpdatePendingChangesState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh MCP servers");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshLiveServersAsync(string sessionId)
    {
        LiveServers.Clear();
        
        try
        {
            var liveServers = await _copilotService.GetLiveMcpServersAsync(sessionId);
            
            foreach (var serverInfo in liveServers)
            {
                var serverVm = new LiveMcpServerViewModel(serverInfo);
                LiveServers.Add(serverVm);
            }
            
            _logger.LogInformation("Loaded {Count} live MCP servers from session", LiveServers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get live MCP servers from session {SessionId}", sessionId);
        }
    }

    private async Task RefreshConfiguredServersAsync()
    {
        var servers = _mcpService.GetServers();
        var session = _sessionManager.ActiveSession;
        
        // EnabledMcpServers behavior:
        // - null: Use all servers from mcp-config.json (default for new sessions - all enabled)
        // - Empty list: Explicitly no servers (user choice)
        // - Non-empty list: Only these specific servers are enabled
        var sessionEnabledServers = session?.EnabledMcpServers;
        
        var enabledStateDescription = sessionEnabledServers == null 
            ? "null (use all)" 
            : sessionEnabledServers.Count == 0 
                ? "empty (none)" 
                : $"list with {sessionEnabledServers.Count} items";

        _logger.LogDebug("RefreshConfiguredServersAsync: Found {Count} servers, EnabledMcpServers is {State}",
            servers.Count, enabledStateDescription);

        ConfiguredServers.Clear();
        foreach (var server in servers)
        {
            var status = _mcpService.GetServerStatus(server.Name);
            
            // Determine if server is enabled based on EnabledMcpServers state:
            bool isEnabled;
            if (sessionEnabledServers == null)
            {
                // null = use server's Enabled property from config (default behavior - typically all enabled)
                isEnabled = server.Enabled;
            }
            else if (sessionEnabledServers.Count == 0)
            {
                // Empty list = user explicitly chose no servers
                isEnabled = false;
            }
            else
            {
                // Non-empty list = check if server is in the list
                isEnabled = sessionEnabledServers.Contains(server.Name);
            }
            
            _logger.LogDebug("Server {Name}: Enabled={Enabled}, ConfigEnabled={ConfigEnabled}, Status={Status}",
                server.Name, isEnabled, server.Enabled, status);
            
            var serverVm = new McpServerWithToolsViewModel(server, status, isEnabled);
            
            // Try to load tools if server is running
            if (status == McpServerStatus.Running)
            {
                try
                {
                    var tools = await _mcpService.GetToolsAsync(server.Name);
                    foreach (var tool in tools)
                    {
                        serverVm.Tools.Add(new McpToolViewModel(tool));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get tools for server {Server}", server.Name);
                }
            }
            
            ConfiguredServers.Add(serverVm);
        }
        
        _logger.LogInformation("Loaded {Count} configured MCP servers", ConfiguredServers.Count);
    }

    [RelayCommand]
    private void ToggleServer(McpServerWithToolsViewModel? server)
    {
        if (server == null) return;

        // Toggle the pending state (not the actual enabled state)
        server.TogglePendingState();

        // Update pending changes tracking
        UpdatePendingChangesState();
        
        _logger.LogDebug("MCP server {ServerName} pending state: {State}", server.Name, server.PendingState);
    }

    [RelayCommand(CanExecute = nameof(CanApplyChanges))]
    private async Task ApplyChangesAsync()
    {
        var session = _sessionManager.ActiveSession;
        if (session == null) return;

        // Show confirmation dialog
        var result = MessageBox.Show(
            "Applying MCP server changes requires recreating the session.\n\n" +
            "• Message history will be preserved\n" +
            "• Current operation (if any) will be interrupted\n\n" +
            "Continue?",
            "Restart Session Required",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsApplying = true;

        try
        {
            // Initialize EnabledMcpServers if it's null (first time user makes changes)
            // When transitioning from null (all enabled) to explicit list, we need to
            // populate the list with all currently enabled servers first
            if (session.EnabledMcpServers == null)
            {
                _logger.LogInformation("Initializing EnabledMcpServers list for session (was null)");
                session.EnabledMcpServers = new List<string>();
                
                // Add all servers that are currently enabled (based on config defaults)
                foreach (var server in ConfiguredServers)
                {
                    if (server.IsEnabled)
                    {
                        session.EnabledMcpServers.Add(server.Name);
                    }
                }
                _logger.LogDebug("Initialized EnabledMcpServers with {Count} servers: {Names}",
                    session.EnabledMcpServers.Count, string.Join(", ", session.EnabledMcpServers));
            }

            // Apply all pending changes to the session
            foreach (var server in ConfiguredServers)
            {
                if (server.PendingState == McpPendingState.PendingEnable)
                {
                    if (!session.EnabledMcpServers.Contains(server.Name))
                    {
                        session.EnabledMcpServers.Add(server.Name);
                    }
                    server.CommitPendingState();
                }
                else if (server.PendingState == McpPendingState.PendingDisable)
                {
                    session.EnabledMcpServers.Remove(server.Name);
                    server.CommitPendingState();
                }
            }

            _logger.LogInformation("Final EnabledMcpServers: {Count} servers: {Names}",
                session.EnabledMcpServers.Count, string.Join(", ", session.EnabledMcpServers));

            // Recreate the session with new MCP configuration
            await _copilotService.RecreateSessionAsync(session, new SessionRecreateOptions
            {
                // Keep same model and working directory, just update MCP servers
            });

            UpdatePendingChangesState();
            _logger.LogInformation("Applied MCP server changes and recreated session");
            
            // Refresh to show new live servers
            await RefreshAsync();

            MessageBox.Show(
                "MCP servers updated successfully. Session has been restarted.",
                "Success",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply MCP server changes");
            MessageBox.Show(
                $"Failed to apply changes: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsApplying = false;
        }
    }

    private bool CanApplyChanges() => HasPendingChanges && !IsApplying;

    [RelayCommand]
    private void DiscardChanges()
    {
        // Reset all pending states
        foreach (var server in ConfiguredServers)
        {
            server.ResetPendingState();
        }
        UpdatePendingChangesState();
    }

    [RelayCommand]
    private void OpenConfigFile()
    {
        try
        {
            // Ensure the config file exists
            var dir = Path.GetDirectoryName(McpConfigFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            if (!File.Exists(McpConfigFilePath))
            {
                // Create a template config file
                var template = """
                {
                  "mcpServers": {
                    "example-server": {
                      "command": "npx",
                      "args": ["-y", "@modelcontextprotocol/server-example"],
                      "env": {}
                    }
                  }
                }
                """;
                File.WriteAllText(McpConfigFilePath, template);
            }
            
            // Open in default editor
            Process.Start(new ProcessStartInfo
            {
                FileName = McpConfigFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open MCP config file");
            MessageBox.Show($"Failed to open config file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(McpConfigFilePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open MCP config folder");
            MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdatePendingChangesState()
    {
        var pendingCount = ConfiguredServers.Count(s => s.PendingState != McpPendingState.None);
        PendingChangesCount = pendingCount;
        HasPendingChanges = pendingCount > 0;
        ApplyChangesCommand.NotifyCanExecuteChanged();
    }

    private void OnServerStatusChanged(object? sender, McpServerStatusChangedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var server = ConfiguredServers.FirstOrDefault(s => s.Name == e.ServerName);
            if (server != null)
            {
                server.Status = e.NewStatus;
            }
        });
    }
}

/// <summary>
/// ViewModel for live MCP server from SDK session
/// </summary>
public partial class LiveMcpServerViewModel : ObservableObject
{
    public LiveMcpServerInfo ServerInfo { get; }

    public string Name => ServerInfo.Name;
    public bool IsActive => ServerInfo.IsActive;
    public string Status => ServerInfo.Status;
    public string Transport => ServerInfo.Transport;
    public string ConnectionInfo => ServerInfo.ConnectionInfo;
    public string? ErrorMessage => ServerInfo.ErrorMessage;
    public int ToolsCount => ServerInfo.Tools.Count;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<LiveMcpToolViewModel> _tools = new();

    /// <summary>
    /// Display color based on status
    /// </summary>
    public string StateColor => Status switch
    {
        "running" => "#4CAF50",   // Green
        "error" => "#F44336",     // Red
        "unknown" => "#FF9800",   // Orange
        _ => "#9E9E9E"            // Gray
    };

    public string StatusIcon => Status switch
    {
        "running" => "✓",
        "error" => "✗",
        "unknown" => "?",
        _ => "○"
    };

    public LiveMcpServerViewModel(LiveMcpServerInfo serverInfo)
    {
        ServerInfo = serverInfo;
        
        // Populate tools
        foreach (var tool in serverInfo.Tools)
        {
            Tools.Add(new LiveMcpToolViewModel(tool));
        }
    }
}

/// <summary>
/// ViewModel for live MCP tool from SDK session
/// </summary>
public partial class LiveMcpToolViewModel : ObservableObject
{
    public LiveMcpToolInfo ToolInfo { get; }

    public string Name => ToolInfo.Name;
    public string? Description => ToolInfo.Description;
    public bool HasParameters => ToolInfo.Parameters != null && ToolInfo.Parameters.Count > 0;

    public string ParametersDisplay
    {
        get
        {
            if (ToolInfo.Parameters == null || ToolInfo.Parameters.Count == 0)
                return "No parameters";
            
            var parts = ToolInfo.Parameters.Select(p => 
                $"• {p.Key} ({p.Value.Type}){(p.Value.Required ? " *" : "")}: {p.Value.Description ?? "No description"}");
            return string.Join("\n", parts);
        }
    }

    [ObservableProperty]
    private bool _isExpanded;

    public LiveMcpToolViewModel(LiveMcpToolInfo toolInfo)
    {
        ToolInfo = toolInfo;
    }
}

/// <summary>
/// ViewModel for MCP server with tools and pending state (for configuration)
/// </summary>
public partial class McpServerWithToolsViewModel : ObservableObject
{
    public McpServerConfig Config { get; }

    public string Name => Config.Name;
    public string? Description => Config.Description;
    public string TransportDisplay => Config.Transport.ToString();
    public string CommandDisplay => Config.Transport == McpTransport.Stdio
        ? $"{Config.Command} {string.Join(" ", Config.Args ?? new())}"
        : Config.Url ?? "";

    [ObservableProperty]
    private McpServerStatus _status;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private McpPendingState _pendingState = McpPendingState.None;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<McpToolViewModel> _tools = new();

    public string StatusDisplay => Status.ToString();
    public bool IsRunning => Status == McpServerStatus.Running;
    public bool IsStopped => Status == McpServerStatus.Stopped || Status == McpServerStatus.Error;

    /// <summary>
    /// Display color based on current and pending state
    /// </summary>
    public string StateColor => PendingState switch
    {
        McpPendingState.PendingEnable => "#FF9800",   // Orange - pending enable
        McpPendingState.PendingDisable => "#FF9800", // Orange - pending disable
        _ => IsEnabled ? "#4CAF50" : "#9E9E9E"       // Green if enabled, Gray if disabled
    };

    /// <summary>
    /// Status text for display
    /// </summary>
    public string StatusText => PendingState switch
    {
        McpPendingState.PendingEnable => "Will be enabled",
        McpPendingState.PendingDisable => "Will be disabled",
        _ => IsEnabled ? "Enabled" : "Disabled"
    };

    public int ToolsCount => Tools.Count;

    public McpServerWithToolsViewModel(McpServerConfig config, McpServerStatus status, bool isEnabled)
    {
        Config = config;
        _status = status;
        _isEnabled = isEnabled;
    }

    /// <summary>
    /// Toggle the pending state when user clicks
    /// </summary>
    public void TogglePendingState()
    {
        if (PendingState == McpPendingState.None)
        {
            // Start a pending change
            PendingState = IsEnabled ? McpPendingState.PendingDisable : McpPendingState.PendingEnable;
        }
        else
        {
            // Cancel the pending change
            PendingState = McpPendingState.None;
        }

        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// Commit the pending state to actual state
    /// </summary>
    public void CommitPendingState()
    {
        if (PendingState == McpPendingState.PendingEnable)
        {
            IsEnabled = true;
        }
        else if (PendingState == McpPendingState.PendingDisable)
        {
            IsEnabled = false;
        }
        PendingState = McpPendingState.None;

        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// Reset pending state without committing
    /// </summary>
    public void ResetPendingState()
    {
        PendingState = McpPendingState.None;
        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnStatusChanged(McpServerStatus value)
    {
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsStopped));
    }

    partial void OnPendingStateChanged(McpPendingState value)
    {
        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(StatusText));
    }
}

/// <summary>
/// ViewModel for MCP tool display
/// </summary>
public partial class McpToolViewModel : ObservableObject
{
    public McpToolInfo Tool { get; }

    public string Name => Tool.Name;
    public string? Description => Tool.Description;
    public bool HasParameters => Tool.Parameters != null && Tool.Parameters.Count > 0;
    
    public string ParametersDisplay
    {
        get
        {
            if (Tool.Parameters == null || Tool.Parameters.Count == 0)
                return "No parameters";
            
            var parts = Tool.Parameters.Select(p => 
                $"• {p.Key} ({p.Value.Type}){(p.Value.Required ? " *" : "")}: {p.Value.Description ?? "No description"}");
            return string.Join("\n", parts);
        }
    }

    [ObservableProperty]
    private bool _isExpanded;

    public McpToolViewModel(McpToolInfo tool)
    {
        Tool = tool;
    }
}