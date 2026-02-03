using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for the New Worktree Session dialog
/// </summary>
public partial class NewWorktreeSessionDialogViewModel : ViewModelBase
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<NewWorktreeSessionDialogViewModel> _logger;

    [ObservableProperty]
    private string _issueUrl = string.Empty;

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _canCreate;

    public Session? CreatedSession { get; private set; }

    public NewWorktreeSessionDialogViewModel(
        ISessionManager sessionManager,
        ILogger<NewWorktreeSessionDialogViewModel> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
        
        // Set default working directory to current directory
        WorkingDirectory = Environment.CurrentDirectory;
    }

    partial void OnIssueUrlChanged(string value)
    {
        UpdateCanCreate();
    }

    partial void OnWorkingDirectoryChanged(string value)
    {
        UpdateCanCreate();
    }

    private void UpdateCanCreate()
    {
        CanCreate = !string.IsNullOrWhiteSpace(IssueUrl) && 
                    !string.IsNullOrWhiteSpace(WorkingDirectory) &&
                    !IsLoading &&
                    Uri.TryCreate(IssueUrl, UriKind.Absolute, out var uri) &&
                    uri.Host.Contains("github.com");
    }

    public async Task<bool> CreateWorktreeSessionAsync()
    {
        if (!CanCreate)
            return false;

        IsLoading = true;
        StatusMessage = "Creating worktree session...";

        try
        {
            _logger.LogInformation("Creating worktree session for issue: {IssueUrl}", IssueUrl);
            
            // Change to working directory
            var originalDir = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = WorkingDirectory;
                
                // Create the worktree session
                CreatedSession = await _sessionManager.CreateWorktreeSessionAsync(IssueUrl);
                
                _logger.LogInformation("Worktree session created: {SessionId}", CreatedSession.SessionId);
                return true;
            }
            finally
            {
                Environment.CurrentDirectory = originalDir;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create worktree session");
            
            StatusMessage = string.Empty;
            IsLoading = false;
            
            MessageBox.Show(
                $"Failed to create worktree session:\n\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            
            return false;
        }
    }
}