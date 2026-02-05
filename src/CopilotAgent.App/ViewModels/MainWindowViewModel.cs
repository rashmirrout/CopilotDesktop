using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for the main application window
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISessionManager _sessionManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly AppSettings _appSettings;
    private readonly IToolApprovalService _toolApprovalService;
    private readonly IPersistenceService _persistenceService;
    private readonly IBrowserAutomationService _browserService;

    [ObservableProperty]
    private ObservableCollection<Session> _sessions = new();

    [ObservableProperty]
    private Session? _activeSession;

    [ObservableProperty]
    private ChatViewModel? _activeChatViewModel;

    [ObservableProperty]
    private bool _hasActiveSession;

    public MainWindowViewModel(
        ISessionManager sessionManager,
        IServiceProvider serviceProvider,
        ILogger<MainWindowViewModel> logger,
        AppSettings appSettings,
        IToolApprovalService toolApprovalService,
        IPersistenceService persistenceService,
        IBrowserAutomationService browserService)
    {
        _sessionManager = sessionManager;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _appSettings = appSettings;
        _toolApprovalService = toolApprovalService;
        _persistenceService = persistenceService;
        _browserService = browserService;
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        
        // Load sessions from SessionManager
        foreach (var session in _sessionManager.Sessions)
        {
            Sessions.Add(session);
        }

        // Set active session
        if (_sessionManager.ActiveSession != null)
        {
            await SetActiveSessionAsync(_sessionManager.ActiveSession);
        }
        
        _logger.LogInformation("MainWindow initialized with {Count} sessions", Sessions.Count);
    }

    private async Task SetActiveSessionAsync(Session session)
    {
        // Update IsActive on all sessions for UI styling
        foreach (var s in Sessions)
        {
            s.IsActive = (s.SessionId == session.SessionId);
        }
        
        ActiveSession = session;
        HasActiveSession = true;
        
        // Update session manager
        _sessionManager.ActiveSession = session;

        // Create ChatViewModel for this session
        var chatViewModel = _serviceProvider.GetRequiredService<ChatViewModel>();
        await chatViewModel.InitializeAsync(session);
        ActiveChatViewModel = chatViewModel;
    }

    [RelayCommand]
    private async Task CreateSessionAsync()
    {
        _logger.LogInformation("Creating new session");
        var session = await _sessionManager.CreateSessionAsync();
        Sessions.Add(session);
        await SetActiveSessionAsync(session);
    }

    [RelayCommand]
    private async Task CreateWorktreeSessionAsync()
    {
        _logger.LogInformation("Opening worktree session dialog");
        
        var dialogViewModel = _serviceProvider.GetRequiredService<NewWorktreeSessionDialogViewModel>();
        var dialog = new Views.NewWorktreeSessionDialog(dialogViewModel);
        
        if (System.Windows.Application.Current.MainWindow != null)
        {
            dialog.Owner = System.Windows.Application.Current.MainWindow;
        }
        
        var result = dialog.ShowDialog();
        
        if (result == true && dialogViewModel.CreatedSession != null)
        {
            Sessions.Add(dialogViewModel.CreatedSession);
            await SetActiveSessionAsync(dialogViewModel.CreatedSession);
            _logger.LogInformation("Worktree session added to sessions list");
        }
    }

    [RelayCommand]
    private async Task SwitchSessionAsync(Session session)
    {
        _logger.LogInformation("Switching to session {SessionId}", session.SessionId);
        await SetActiveSessionAsync(session);
    }

    [RelayCommand]
    private async Task CloseSessionAsync(Session session)
    {
        _logger.LogInformation("Closing session {SessionId}", session.SessionId);
        await _sessionManager.DeleteSessionAsync(session.SessionId);
        Sessions.Remove(session);

        if (ActiveSession?.SessionId == session.SessionId)
        {
            var nextSession = Sessions.FirstOrDefault();
            if (nextSession != null)
            {
                await SetActiveSessionAsync(nextSession);
            }
            else
            {
                ActiveSession = null;
                ActiveChatViewModel = null;
                HasActiveSession = false;
                _sessionManager.ActiveSession = null;
            }
        }
    }

    [RelayCommand]
    private void ShowSettings()
    {
        _logger.LogInformation("Opening settings dialog");
        
        var saved = Views.SettingsDialog.ShowSettings(
            _appSettings,
            _toolApprovalService,
            _persistenceService,
            _browserService,
            System.Windows.Application.Current.MainWindow);
        
        if (saved)
        {
            _logger.LogInformation("Settings saved");
            
            // Note: Some settings (like UseSdkMode) require app restart to take effect
            // because the ICopilotService is resolved at startup based on the feature flag
        }
    }
}