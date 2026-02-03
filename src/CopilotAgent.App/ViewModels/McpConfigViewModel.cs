using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for MCP server configuration
/// </summary>
public partial class McpConfigViewModel : ObservableObject
{
    private readonly IMcpService _mcpService;
    private readonly ILogger<McpConfigViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<McpServerViewModel> _servers = new();

    [ObservableProperty]
    private McpServerViewModel? _selectedServer;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    [ObservableProperty]
    private McpTransport _editTransport = McpTransport.Stdio;

    [ObservableProperty]
    private string _editCommand = string.Empty;

    [ObservableProperty]
    private string _editArgs = string.Empty;

    [ObservableProperty]
    private string _editUrl = string.Empty;

    [ObservableProperty]
    private int _editTimeout = 30;

    [ObservableProperty]
    private bool _editEnabled = true;

    private bool _isNewServer;

    public McpConfigViewModel(IMcpService mcpService, ILogger<McpConfigViewModel> logger)
    {
        _mcpService = mcpService;
        _logger = logger;

        _mcpService.ServerStatusChanged += OnServerStatusChanged;
    }

    public async Task InitializeAsync()
    {
        await RefreshServersAsync();
    }

    [RelayCommand]
    private async Task RefreshServersAsync()
    {
        try
        {
            var servers = _mcpService.GetServers();
            Servers.Clear();
            foreach (var server in servers)
            {
                Servers.Add(new McpServerViewModel(server, _mcpService.GetServerStatus(server.Name)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh MCP servers");
        }
    }

    [RelayCommand]
    private void AddServer()
    {
        _isNewServer = true;
        IsEditing = true;
        EditName = string.Empty;
        EditDescription = string.Empty;
        EditTransport = McpTransport.Stdio;
        EditCommand = string.Empty;
        EditArgs = string.Empty;
        EditUrl = string.Empty;
        EditTimeout = 30;
        EditEnabled = true;
    }

    [RelayCommand]
    private void EditServer()
    {
        if (SelectedServer == null) return;

        _isNewServer = false;
        IsEditing = true;
        EditName = SelectedServer.Name;
        EditDescription = SelectedServer.Description ?? string.Empty;
        EditTransport = SelectedServer.Config.Transport;
        EditCommand = SelectedServer.Config.Command ?? string.Empty;
        EditArgs = SelectedServer.Config.Args != null ? string.Join(" ", SelectedServer.Config.Args) : string.Empty;
        EditUrl = SelectedServer.Config.Url ?? string.Empty;
        EditTimeout = SelectedServer.Config.TimeoutSeconds;
        EditEnabled = SelectedServer.Config.Enabled;
    }

    [RelayCommand]
    private async Task SaveServerAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            MessageBox.Show("Server name is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var config = new McpServerConfig
            {
                Name = EditName.Trim(),
                Description = string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim(),
                Transport = EditTransport,
                Command = EditTransport == McpTransport.Stdio ? EditCommand.Trim() : null,
                Args = EditTransport == McpTransport.Stdio && !string.IsNullOrWhiteSpace(EditArgs)
                    ? EditArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()
                    : null,
                Url = EditTransport == McpTransport.Http ? EditUrl.Trim() : null,
                TimeoutSeconds = EditTimeout,
                Enabled = EditEnabled
            };

            if (_isNewServer)
            {
                await _mcpService.AddServerAsync(config);
                _logger.LogInformation("Added MCP server: {Name}", config.Name);
            }
            else
            {
                await _mcpService.UpdateServerAsync(config);
                _logger.LogInformation("Updated MCP server: {Name}", config.Name);
            }

            IsEditing = false;
            await RefreshServersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save MCP server");
            MessageBox.Show($"Failed to save server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private async Task DeleteServerAsync()
    {
        if (SelectedServer == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete '{SelectedServer.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _mcpService.RemoveServerAsync(SelectedServer.Name);
            _logger.LogInformation("Deleted MCP server: {Name}", SelectedServer.Name);
            await RefreshServersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete MCP server");
            MessageBox.Show($"Failed to delete server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task StartServerAsync()
    {
        if (SelectedServer == null) return;

        try
        {
            var success = await _mcpService.StartServerAsync(SelectedServer.Name);
            if (!success)
            {
                MessageBox.Show($"Failed to start server '{SelectedServer.Name}'", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start MCP server");
            MessageBox.Show($"Failed to start server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task StopServerAsync()
    {
        if (SelectedServer == null) return;

        try
        {
            await _mcpService.StopServerAsync(SelectedServer.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop MCP server");
            MessageBox.Show($"Failed to stop server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnServerStatusChanged(object? sender, McpServerStatusChangedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var server = Servers.FirstOrDefault(s => s.Name == e.ServerName);
            if (server != null)
            {
                server.Status = e.NewStatus;
            }
        });
    }
}

/// <summary>
/// ViewModel wrapper for MCP server display
/// </summary>
public partial class McpServerViewModel : ObservableObject
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

    public string StatusDisplay => Status.ToString();
    public bool IsRunning => Status == McpServerStatus.Running;
    public bool IsStopped => Status == McpServerStatus.Stopped || Status == McpServerStatus.Error;

    public McpServerViewModel(McpServerConfig config, McpServerStatus status)
    {
        Config = config;
        _status = status;
    }

    partial void OnStatusChanged(McpServerStatus value)
    {
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsStopped));
    }
}