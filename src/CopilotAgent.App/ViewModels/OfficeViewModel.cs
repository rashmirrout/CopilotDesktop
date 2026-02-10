using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.Office.Events;
using CopilotAgent.Office.Models;
using CopilotAgent.Office.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for the Agent Office view — bridges IOfficeManagerService events to the WPF UI.
/// Manages the chat plane, status bar, side panel, and all user interactions.
///
/// Settings architecture:
///   - UI-bound properties are the "pending" (editable) values.
///   - On Apply, pending values are persisted to AppSettings.Office via IPersistenceService.
///   - On Start, pending values are snapshotted into the OfficeConfig for the running session.
///   - HasPendingChanges tracks whether any UI value differs from the last-persisted value.
///   - SettingsRequireRestart indicates that settings were applied while a session was running
///     (the running session still uses the old config until reset/restart).
/// </summary>
public sealed partial class OfficeViewModel : ViewModelBase, IDisposable
{
    private readonly IOfficeManagerService _manager;
    private readonly IOfficeEventLog _eventLog;
    private readonly ISessionManager _sessionManager;
    private readonly ICopilotService _copilotService;
    private readonly IPersistenceService _persistenceService;
    private readonly AppSettings _appSettings;
    private readonly ILogger<OfficeViewModel> _logger;
    private readonly Dispatcher _dispatcher;

    private const int MaxCommentaryEntries = 200;
    private const int MaxEventLogEntries = 500;

    // ── Streaming commentary accumulation ───────────────────────
    private readonly Dictionary<string, (LiveCommentary Entry, int CollectionIndex)> _activeStreamingCommentary = new();

    private static readonly HashSet<CommentaryType> s_streamingCommentaryTypes = new()
    {
        CommentaryType.ManagerThinking,
        CommentaryType.AssistantProgress
    };

    /// <summary>
    /// Snapshot of persisted OfficeSettings values — used to compute HasPendingChanges.
    /// Updated on load and after each successful Apply.
    /// Nullable during construction before initial snapshot is captured.
    /// </summary>
    private OfficeSettingsSnapshot? _persistedSnapshot;

    public ObservableCollection<string> AvailableModels { get; } = new();

    public OfficeViewModel(
        IOfficeManagerService manager,
        IOfficeEventLog eventLog,
        ISessionManager sessionManager,
        ICopilotService copilotService,
        IPersistenceService persistenceService,
        AppSettings appSettings,
        ILogger<OfficeViewModel> logger)
    {
        _manager = manager;
        _eventLog = eventLog;
        _sessionManager = sessionManager;
        _copilotService = copilotService;
        _persistenceService = persistenceService;
        _appSettings = appSettings;
        _logger = logger;
        _dispatcher = Application.Current.Dispatcher;

        // Wire events
        _manager.OnEvent += HandleEvent;

        // Load all settings from persisted defaults
        LoadSettingsFromPersistence();

        // Only fall back to active session path when no persisted workspace path exists
        if (string.IsNullOrWhiteSpace(_workspacePath))
        {
            var active = _sessionManager.ActiveSession;
            _workspacePath = active?.WorkingDirectory
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        // Capture initial snapshot for dirty tracking
        _persistedSnapshot = CaptureCurrentSnapshot();

        _logger.LogInformation("[OfficeVM] ViewModel initialized with persisted settings");
    }

    // ══════════════════════════════════════════════════════════════
    // Observable Properties — Status
    // ══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string _currentPhaseDisplay = "Idle";

    [ObservableProperty]
    private string _currentPhaseColor = "#9E9E9E";

    [ObservableProperty]
    private int _currentIteration;

    [ObservableProperty]
    private int _completedTasks;

    [ObservableProperty]
    private int _totalTasks;

    [ObservableProperty]
    private int _queueDepth;

    [ObservableProperty]
    private double _taskProgressPercent;

    // ── Rest countdown ──────────────────────────────────────────

    [ObservableProperty]
    private bool _isResting;

    [ObservableProperty]
    private double _restProgressPercent;

    [ObservableProperty]
    private string _restCountdownText = string.Empty;

    // ── Clarification & Plan ────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private bool _isWaitingForClarification;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApprovePlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectPlanCommand))]
    private bool _isPlanAwaitingApproval;

    // ── Input ───────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private string _messageInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _objectiveInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private bool _isRunning;

    // ── Side panel ──────────────────────────────────────────────

    [ObservableProperty]
    private bool _isSidePanelOpen;

    [ObservableProperty]
    private bool _autoScrollCommentary = true;

    // ══════════════════════════════════════════════════════════════
    // Configuration Properties — UI-bound "pending" values
    // All changes trigger dirty tracking via partial OnChanged methods.
    // ══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private int _checkIntervalMinutes;
    partial void OnCheckIntervalMinutesChanged(int value) => RecalculateDirtyState();

    [ObservableProperty]
    private int _maxAssistants;
    partial void OnMaxAssistantsChanged(int value) => RecalculateDirtyState();

    [ObservableProperty]
    private string _workspacePath = string.Empty;
    partial void OnWorkspacePathChanged(string value) => RecalculateDirtyState();

    [ObservableProperty]
    private string _selectedManagerModel = "gpt-4";
    partial void OnSelectedManagerModelChanged(string value) => RecalculateDirtyState();

    [ObservableProperty]
    private string _selectedAssistantModel = "gpt-4";
    partial void OnSelectedAssistantModelChanged(string value) => RecalculateDirtyState();

    [ObservableProperty]
    private int _assistantTimeoutSeconds;
    partial void OnAssistantTimeoutSecondsChanged(int value) => RecalculateDirtyState();

    [ObservableProperty]
    private int _managerLlmTimeoutSeconds;
    partial void OnManagerLlmTimeoutSecondsChanged(int value) => RecalculateDirtyState();

    [ObservableProperty]
    private int _maxRetries;
    partial void OnMaxRetriesChanged(int value) => RecalculateDirtyState();

    [ObservableProperty]
    private int _maxQueueDepth;
    partial void OnMaxQueueDepthChanged(int value) => RecalculateDirtyState();

    [ObservableProperty]
    private bool _requirePlanApproval;
    partial void OnRequirePlanApprovalChanged(bool value) => RecalculateDirtyState();

    /// <summary>
    /// When true, LLM reasoning streams word-by-word in live commentary.
    /// When false (default), reasoning is buffered and emitted as a complete thought.
    /// </summary>
    [ObservableProperty]
    private bool _isStreamingTokens;
    partial void OnIsStreamingTokensChanged(bool value) => RecalculateDirtyState();

    [ObservableProperty]
    private bool _isLoadingModels;

    // ══════════════════════════════════════════════════════════════
    // Dirty Tracking — Settings Change Detection
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// True when any UI-bound config value differs from the last-persisted value.
    /// Drives the "Apply Changes" button visibility.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplySettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardSettingsCommand))]
    private bool _hasPendingChanges;

    /// <summary>
    /// Number of individual settings that have been modified.
    /// </summary>
    [ObservableProperty]
    private int _pendingChangesCount;

    /// <summary>
    /// True when settings were applied (persisted) but the running session
    /// still uses the old OfficeConfig. User must reset/restart for changes to take effect.
    /// Drives the "PENDING — Reset session to take effect" indicator.
    /// </summary>
    [ObservableProperty]
    private bool _settingsRequireRestart;

    // ── Activity Status Panel (Issue #7) ────────────────────────

    [ObservableProperty]
    private string _activityStatusText = string.Empty;

    [ObservableProperty]
    private bool _isActivityStatusVisible;

    [ObservableProperty]
    private bool _isActivityPulsing;

    /// <summary>Tracks which assistant indices are currently running (for live status updates).</summary>
    private readonly HashSet<int> _activeAssistantIndices = new();
    private int _totalAssistantsDispatched;

    // ── Stats ───────────────────────────────────────────────────

    [ObservableProperty]
    private int _totalIterations;

    [ObservableProperty]
    private int _totalTasksCompleted;

    [ObservableProperty]
    private string _successRate = "0%";

    [ObservableProperty]
    private string _averageDuration = "0s";

    // ══════════════════════════════════════════════════════════════
    // Collections
    // ══════════════════════════════════════════════════════════════

    public ObservableCollection<OfficeChatMessage> Messages { get; } = new();
    public ObservableCollection<LiveCommentary> LiveCommentaries { get; } = new();
    public ObservableCollection<string> EventLog { get; } = new();

    // ══════════════════════════════════════════════════════════════
    // Commands
    // ══════════════════════════════════════════════════════════════

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(ObjectiveInput)) return;

        _logger.LogInformation("[OfficeVM] Starting with objective: {Objective}", ObjectiveInput);

        // Refresh the model cache on each start
        await RefreshAvailableModelsAsync();

        var activeSession = _sessionManager.ActiveSession;

        var config = new OfficeConfig
        {
            Objective = ObjectiveInput,
            WorkspacePath = string.IsNullOrWhiteSpace(WorkspacePath)
                ? activeSession?.WorkingDirectory
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : WorkspacePath,
            CheckIntervalMinutes = CheckIntervalMinutes,
            MaxAssistants = MaxAssistants,
            RequirePlanApproval = RequirePlanApproval,
            ManagerModel = string.IsNullOrWhiteSpace(SelectedManagerModel)
                ? activeSession?.ModelId ?? "gpt-4"
                : SelectedManagerModel,
            AssistantModel = string.IsNullOrWhiteSpace(SelectedAssistantModel)
                ? activeSession?.ModelId ?? "gpt-4"
                : SelectedAssistantModel,
            AssistantTimeoutSeconds = Math.Clamp(AssistantTimeoutSeconds, 60, 3600),
            ManagerLlmTimeoutSeconds = Math.Clamp(ManagerLlmTimeoutSeconds, 10, 300),
            MaxRetries = Math.Clamp(MaxRetries, 0, 10),
            MaxQueueDepth = Math.Clamp(MaxQueueDepth, 1, 100),
            CommentaryStreamingMode = IsStreamingTokens
                ? CommentaryStreamingMode.StreamingTokens
                : CommentaryStreamingMode.CompleteThought,

            // Propagate MCP servers and skills from the active session
            EnabledMcpServers = activeSession?.EnabledMcpServers,
            DisabledSkills = activeSession?.DisabledSkills,
            SkillDirectories = activeSession?.SkillDirectories
        };

        // Add user message to chat
        AddChatMessage(new OfficeChatMessage
        {
            Role = OfficeChatRole.User,
            SenderName = "You",
            Content = ObjectiveInput,
            AccentColor = OfficeColorScheme.UserColor
        });

        ObjectiveInput = string.Empty;
        IsRunning = true;

        // Starting a new session with current settings clears the restart-required flag
        SettingsRequireRestart = false;

        try
        {
            await _manager.StartAsync(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OfficeVM] Failed to start Office");
            SetError($"Failed to start: {ex.Message}");
            IsRunning = false;
        }
    }

    private bool CanStart() => !IsRunning && !string.IsNullOrWhiteSpace(ObjectiveInput);

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(MessageInput)) return;

        var input = MessageInput;
        MessageInput = string.Empty;

        AddChatMessage(new OfficeChatMessage
        {
            Role = OfficeChatRole.User,
            SenderName = "You",
            Content = input,
            AccentColor = OfficeColorScheme.UserColor
        });

        try
        {
            if (IsWaitingForClarification)
            {
                await _manager.RespondToClarificationAsync(input);
            }
            else
            {
                await _manager.InjectInstructionAsync(input);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OfficeVM] Failed to send message");
            SetError($"Failed to send: {ex.Message}");
        }
    }

    private bool CanSendMessage() => !string.IsNullOrWhiteSpace(MessageInput);

    [RelayCommand(CanExecute = nameof(CanApprovePlan))]
    private async Task ApprovePlanAsync()
    {
        _logger.LogInformation("[OfficeVM] Plan approved");
        try
        {
            await _manager.ApprovePlanAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OfficeVM] Failed to approve plan");
            SetError($"Failed to approve: {ex.Message}");
        }
    }

    private bool CanApprovePlan() => IsPlanAwaitingApproval;

    [RelayCommand(CanExecute = nameof(CanApprovePlan))]
    private async Task RejectPlanAsync()
    {
        _logger.LogInformation("[OfficeVM] Plan rejected");
        try
        {
            await _manager.RejectPlanAsync(MessageInput);
            MessageInput = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OfficeVM] Failed to reject plan");
            SetError($"Failed to reject: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private async Task PauseAsync()
    {
        _logger.LogInformation("[OfficeVM] Pausing");
        try { await _manager.PauseAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "[OfficeVM] Failed to pause"); }
    }

    private bool CanPause() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeAsync()
    {
        _logger.LogInformation("[OfficeVM] Resuming");
        try { await _manager.ResumeAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "[OfficeVM] Failed to resume"); }
    }

    private bool CanResume() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        _logger.LogInformation("[OfficeVM] Stopping");
        try
        {
            await _manager.StopAsync();
            IsRunning = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OfficeVM] Failed to stop");
        }
    }

    private bool CanStop() => IsRunning;

    [RelayCommand]
    private async Task ResetAsync()
    {
        _logger.LogInformation("[OfficeVM] Resetting");
        try
        {
            await _manager.ResetAsync();
            ClearState();
            // After reset, pending settings will take effect on next Start
            SettingsRequireRestart = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OfficeVM] Failed to reset");
        }
    }

    [RelayCommand]
    private void ToggleSidePanel()
    {
        IsSidePanelOpen = !IsSidePanelOpen;
    }

    [RelayCommand]
    private void UpdateInterval()
    {
        var clamped = Math.Clamp(CheckIntervalMinutes, 1, 60);
        CheckIntervalMinutes = clamped;
        _manager.UpdateCheckInterval(clamped);
        _logger.LogInformation("[OfficeVM] Live interval updated to {Minutes} min", clamped);
    }

    [RelayCommand]
    private void SkipRest()
    {
        _logger.LogInformation("[OfficeVM] Skipping rest period");
        _manager.SkipRest();
    }

    [RelayCommand]
    private void BrowseWorkspace()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Workspace Directory",
            InitialDirectory = string.IsNullOrWhiteSpace(WorkspacePath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : WorkspacePath
        };

        if (dialog.ShowDialog() == true)
        {
            WorkspacePath = dialog.FolderName;
            _logger.LogInformation("[OfficeVM] Workspace path set to: {Path}", WorkspacePath);
        }
    }

    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        await RefreshAvailableModelsAsync();
    }

    // ══════════════════════════════════════════════════════════════
    // Settings Commands — Apply / Discard with Persistence
    // ══════════════════════════════════════════════════════════════

    [RelayCommand(CanExecute = nameof(CanApplySettings))]
    private async Task ApplySettingsAsync()
    {
        _logger.LogInformation("[OfficeVM] Applying settings to persistence");

        try
        {
            // Write current UI values to the in-memory AppSettings.Office
            var office = _appSettings.Office;
            office.DefaultCheckIntervalMinutes = CheckIntervalMinutes;
            office.DefaultMaxAssistants = MaxAssistants;
            office.DefaultManagerModel = SelectedManagerModel;
            office.DefaultAssistantModel = SelectedAssistantModel;
            office.DefaultAssistantTimeoutSeconds = AssistantTimeoutSeconds;
            office.DefaultManagerLlmTimeoutSeconds = ManagerLlmTimeoutSeconds;
            office.DefaultMaxRetries = MaxRetries;
            office.DefaultMaxQueueDepth = MaxQueueDepth;
            office.DefaultRequirePlanApproval = RequirePlanApproval;
            office.DefaultCommentaryStreamingMode = IsStreamingTokens
                ? "StreamingTokens"
                : "CompleteThought";
            office.DefaultWorkspacePath = WorkspacePath;

            // Persist to disk
            await _persistenceService.SaveSettingsAsync(_appSettings);

            // Update snapshot so dirty tracking resets
            _persistedSnapshot = CaptureCurrentSnapshot();
            RecalculateDirtyState();

            // If session is running, flag that a restart is needed for changes to take effect
            if (IsRunning)
            {
                SettingsRequireRestart = true;
                _logger.LogInformation("[OfficeVM] Settings persisted — session restart required for changes to take effect");
            }
            else
            {
                SettingsRequireRestart = false;
                _logger.LogInformation("[OfficeVM] Settings persisted — will take effect on next Start");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OfficeVM] Failed to persist settings");
            SetError($"Failed to save settings: {ex.Message}");
        }
    }

    private bool CanApplySettings() => HasPendingChanges;

    [RelayCommand(CanExecute = nameof(CanDiscardSettings))]
    private void DiscardSettings()
    {
        _logger.LogInformation("[OfficeVM] Discarding pending settings changes");
        LoadSettingsFromSnapshot(_persistedSnapshot);
        RecalculateDirtyState();
    }

    private bool CanDiscardSettings() => HasPendingChanges;

    // ══════════════════════════════════════════════════════════════
    // Settings — Load / Snapshot / Dirty Tracking
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads all configuration properties from persisted AppSettings.Office.
    /// Called on construction and could be called to reload from disk.
    /// Issue #1 fix: Uses property setters instead of backing fields so that
    /// PropertyChanged fires and the UI (especially model ComboBoxes) updates correctly.
    /// </summary>
    private void LoadSettingsFromPersistence()
    {
        var office = _appSettings.Office;
        CheckIntervalMinutes = office.DefaultCheckIntervalMinutes;
        MaxAssistants = office.DefaultMaxAssistants;
        SelectedManagerModel = office.DefaultManagerModel;
        SelectedAssistantModel = office.DefaultAssistantModel;
        AssistantTimeoutSeconds = office.DefaultAssistantTimeoutSeconds;
        ManagerLlmTimeoutSeconds = office.DefaultManagerLlmTimeoutSeconds;
        MaxRetries = office.DefaultMaxRetries;
        MaxQueueDepth = office.DefaultMaxQueueDepth;
        RequirePlanApproval = office.DefaultRequirePlanApproval;
        IsStreamingTokens = string.Equals(
            office.DefaultCommentaryStreamingMode,
            "StreamingTokens",
            StringComparison.OrdinalIgnoreCase);
        WorkspacePath = office.DefaultWorkspacePath;
    }

    /// <summary>
    /// Restores UI properties from a snapshot (used by Discard).
    /// Uses property setters to trigger UI change notification.
    /// </summary>
    private void LoadSettingsFromSnapshot(OfficeSettingsSnapshot snapshot)
    {
        CheckIntervalMinutes = snapshot.CheckIntervalMinutes;
        MaxAssistants = snapshot.MaxAssistants;
        SelectedManagerModel = snapshot.ManagerModel;
        SelectedAssistantModel = snapshot.AssistantModel;
        AssistantTimeoutSeconds = snapshot.AssistantTimeoutSeconds;
        ManagerLlmTimeoutSeconds = snapshot.ManagerLlmTimeoutSeconds;
        MaxRetries = snapshot.MaxRetries;
        MaxQueueDepth = snapshot.MaxQueueDepth;
        RequirePlanApproval = snapshot.RequirePlanApproval;
        IsStreamingTokens = snapshot.IsStreamingTokens;
        WorkspacePath = snapshot.WorkspacePath;
    }

    /// <summary>
    /// Captures the current UI property values as an immutable snapshot.
    /// </summary>
    private OfficeSettingsSnapshot CaptureCurrentSnapshot() => new(
        CheckIntervalMinutes,
        MaxAssistants,
        SelectedManagerModel,
        SelectedAssistantModel,
        AssistantTimeoutSeconds,
        ManagerLlmTimeoutSeconds,
        MaxRetries,
        MaxQueueDepth,
        RequirePlanApproval,
        IsStreamingTokens,
        WorkspacePath);

    /// <summary>
    /// Compares current UI values against the persisted snapshot and updates
    /// <see cref="HasPendingChanges"/> and <see cref="PendingChangesCount"/>.
    /// Safe to call during construction before the snapshot is initialized.
    /// </summary>
    private void RecalculateDirtyState()
    {
        // During construction, property setters fire OnChanged callbacks before
        // _persistedSnapshot is initialized. Guard against that to avoid NRE.
        if (_persistedSnapshot is null)
        {
            HasPendingChanges = false;
            PendingChangesCount = 0;
            return;
        }

        var current = CaptureCurrentSnapshot();
        var count = 0;

        if (current.CheckIntervalMinutes != _persistedSnapshot.CheckIntervalMinutes) count++;
        if (current.MaxAssistants != _persistedSnapshot.MaxAssistants) count++;
        if (!string.Equals(current.ManagerModel, _persistedSnapshot.ManagerModel, StringComparison.Ordinal)) count++;
        if (!string.Equals(current.AssistantModel, _persistedSnapshot.AssistantModel, StringComparison.Ordinal)) count++;
        if (current.AssistantTimeoutSeconds != _persistedSnapshot.AssistantTimeoutSeconds) count++;
        if (current.ManagerLlmTimeoutSeconds != _persistedSnapshot.ManagerLlmTimeoutSeconds) count++;
        if (current.MaxRetries != _persistedSnapshot.MaxRetries) count++;
        if (current.MaxQueueDepth != _persistedSnapshot.MaxQueueDepth) count++;
        if (current.RequirePlanApproval != _persistedSnapshot.RequirePlanApproval) count++;
        if (current.IsStreamingTokens != _persistedSnapshot.IsStreamingTokens) count++;
        if (!string.Equals(current.WorkspacePath, _persistedSnapshot.WorkspacePath, StringComparison.Ordinal)) count++;

        PendingChangesCount = count;
        HasPendingChanges = count > 0;
    }

    /// <summary>
    /// Immutable snapshot of settings values for dirty-tracking comparison.
    /// </summary>
    private sealed record OfficeSettingsSnapshot(
        int CheckIntervalMinutes,
        int MaxAssistants,
        string ManagerModel,
        string AssistantModel,
        int AssistantTimeoutSeconds,
        int ManagerLlmTimeoutSeconds,
        int MaxRetries,
        int MaxQueueDepth,
        bool RequirePlanApproval,
        bool IsStreamingTokens,
        string WorkspacePath);

    // ══════════════════════════════════════════════════════════════
    // Model Cache
    // ══════════════════════════════════════════════════════════════

    private async Task RefreshAvailableModelsAsync()
    {
        if (IsLoadingModels) return;

        IsLoadingModels = true;
        _logger.LogInformation("[OfficeVM] Refreshing available models cache...");

        try
        {
            var models = await _copilotService.GetAvailableModelsAsync();

            _dispatcher.Invoke(() =>
            {
                AvailableModels.Clear();
                foreach (var model in models.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
                {
                    AvailableModels.Add(model);
                }
            });

            _logger.LogInformation("[OfficeVM] Model cache refreshed: {Count} models available", models.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[OfficeVM] Failed to refresh available models — user can still type manually");
        }
        finally
        {
            IsLoadingModels = false;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Event Handling — marshalled to UI thread
    // ══════════════════════════════════════════════════════════════

    private void HandleEvent(OfficeEvent evt)
    {
        _dispatcher.InvokeAsync(() =>
        {
            try
            {
                ProcessEvent(evt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OfficeVM] Error processing event: {EventType}", evt.EventType);
            }
        });
    }

    private void ProcessEvent(OfficeEvent evt)
    {
        if (evt is not RestCountdownEvent)
        {
            AddEventLog($"[{evt.Timestamp:HH:mm:ss}] {evt.EventType}: {evt.Description}");
        }

        switch (evt)
        {
            case PhaseChangedEvent phase:
                HandlePhaseChanged(phase);
                break;

            case ChatMessageEvent chat:
                AddChatMessage(chat.Message);
                break;

            case CommentaryEvent commentary:
                AddCommentary(commentary.Commentary);
                break;

            case ActivityStatusEvent activity:
                HandleActivityStatusEvent(activity);
                break;

            case AssistantEvent assistant:
                HandleAssistantEvent(assistant);
                break;

            case SchedulingEvent scheduling:
                HandleSchedulingEvent(scheduling);
                break;

            case IterationCompletedEvent iteration:
                HandleIterationCompleted(iteration);
                break;

            case RestCountdownEvent countdown:
                HandleRestCountdown(countdown);
                break;

            case RunStoppedEvent:
                IsRunning = false;
                CurrentPhaseDisplay = "Stopped";
                CurrentPhaseColor = "#FF9800";
                break;

            case ErrorEvent error:
                IsRunning = false;
                CurrentPhaseDisplay = "Error";
                CurrentPhaseColor = "#F44336";
                SetError(error.ErrorMessage);
                break;
        }
    }

    private void HandlePhaseChanged(PhaseChangedEvent evt)
    {
        FinalizeAllActiveStreams();

        CurrentPhaseDisplay = evt.NewPhase.ToString();
        CurrentPhaseColor = GetPhaseColor(evt.NewPhase);
        CurrentIteration = evt.IterationNumber;

        // Core state flags — driven directly from the phase
        IsWaitingForClarification = evt.NewPhase == ManagerPhase.Clarifying;
        IsPlanAwaitingApproval = evt.NewPhase == ManagerPhase.AwaitingApproval;
        IsResting = evt.NewPhase == ManagerPhase.Resting;

        // Bug fix: When pausing/stopping/erroring, explicitly clear interactive UI states
        // so stale approval panels and clarification banners don't persist.
        if (evt.NewPhase is ManagerPhase.Paused or ManagerPhase.Stopped or ManagerPhase.Error)
        {
            IsPlanAwaitingApproval = false;
            IsWaitingForClarification = false;
            IsResting = false;
        }

        if (evt.NewPhase is ManagerPhase.Stopped or ManagerPhase.Error)
        {
            IsRunning = false;
        }
    }

    private void HandleActivityStatusEvent(ActivityStatusEvent evt)
    {
        if (evt.StatusType == ActivityStatusType.Idle)
        {
            // Session ended / reset / error — hide the panel
            ActivityStatusText = string.Empty;
            IsActivityStatusVisible = false;
            IsActivityPulsing = false;
            _activeAssistantIndices.Clear();
            _totalAssistantsDispatched = 0;
            return;
        }

        // For Delegated events, seed the assistant tracking
        if (evt.StatusType == ActivityStatusType.Delegated)
        {
            _totalAssistantsDispatched = evt.TotalAssistantsDispatched;
            _activeAssistantIndices.Clear();
            // Assistants haven't started yet — the "Delegated" message is shown as-is
        }

        ActivityStatusText = evt.StatusMessage;
        IsActivityStatusVisible = true;

        // Pulse for active work states; don't pulse for awaiting/resting
        IsActivityPulsing = evt.StatusType is
            ActivityStatusType.ManagerThinking or
            ActivityStatusType.Delegated or
            ActivityStatusType.AssistantsWorking or
            ActivityStatusType.ManagerAggregating;
    }

    private void HandleAssistantEvent(AssistantEvent evt)
    {
        // Update activity status panel with per-assistant tracking
        switch (evt.Status)
        {
            case AssistantTaskStatus.Running:
                _activeAssistantIndices.Add(evt.AssistantIndex);
                UpdateAssistantActivityStatus();
                QueueDepth = Math.Max(0, QueueDepth - 1);
                break;
            case AssistantTaskStatus.Completed:
            case AssistantTaskStatus.Failed:
                _activeAssistantIndices.Remove(evt.AssistantIndex);
                UpdateAssistantActivityStatus();
                CompletedTasks++;
                UpdateTaskProgress();
                break;
        }

        if (evt.Result is not null)
        {
            var resultContent = !string.IsNullOrWhiteSpace(evt.Result.Content)
                ? evt.Result.Content
                : evt.Result.Summary;

            var statusIcon = evt.Result.Success ? "✅" : "❌";
            var errorInfo = evt.Result.Success
                ? ""
                : $"\n\n**Error:** {evt.Result.ErrorMessage}";

            AddChatMessage(new OfficeChatMessage
            {
                Role = OfficeChatRole.Assistant,
                SenderName = $"Assistant #{evt.AssistantIndex}",
                Content = $"{statusIcon} **{evt.Task.Title}** ({evt.Result.Duration.TotalSeconds:F1}s){errorInfo}\n\n{resultContent}",
                IterationNumber = evt.IterationNumber,
                AccentColor = OfficeColorScheme.GetAssistantColor(evt.AssistantIndex),
                IsCollapsible = true
            });
        }
    }

    private void HandleSchedulingEvent(SchedulingEvent evt)
    {
        TotalTasks += evt.Decision.Action == SchedulingAction.Dispatched ? 1 : 0;
        QueueDepth += evt.Decision.Action == SchedulingAction.Queued ? 1 : 0;
        UpdateTaskProgress();
    }

    private void HandleIterationCompleted(IterationCompletedEvent evt)
    {
        TotalIterations = evt.Report.IterationNumber;
        TotalTasksCompleted += evt.Report.TasksSucceeded;

        var totalAttempted = TotalTasksCompleted + evt.Report.TasksFailed;
        SuccessRate = totalAttempted > 0
            ? $"{(TotalTasksCompleted * 100.0 / totalAttempted):F0}%"
            : "0%";

        var duration = evt.Report.CompletedAt - evt.Report.StartedAt;
        AverageDuration = $"{duration.TotalSeconds:F1}s";

        CompletedTasks = 0;
        TotalTasks = 0;
        QueueDepth = 0;
        TaskProgressPercent = 0;
    }

    private void HandleRestCountdown(RestCountdownEvent evt)
    {
        IsResting = true;
        RestCountdownText = $"{evt.SecondsRemaining / 60}:{evt.SecondsRemaining % 60:D2}";
        RestProgressPercent = evt.TotalSeconds > 0
            ? ((evt.TotalSeconds - evt.SecondsRemaining) * 100.0 / evt.TotalSeconds)
            : 0;

        if (evt.SecondsRemaining <= 0)
        {
            IsResting = false;
            RestCountdownText = string.Empty;
            RestProgressPercent = 0;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════

    private void AddChatMessage(OfficeChatMessage message)
    {
        Messages.Add(message);
    }

    private void AddCommentary(LiveCommentary commentary)
    {
        if (s_streamingCommentaryTypes.Contains(commentary.Type))
        {
            AccumulateStreamingCommentary(commentary);
        }
        else
        {
            var agentStreamKey = $"{commentary.AgentName}|streaming";
            FinalizeActiveStream(agentStreamKey);
            InsertCommentaryEntry(commentary);
        }
    }

    private void AccumulateStreamingCommentary(LiveCommentary delta)
    {
        var key = $"{delta.AgentName}|streaming";

        if (_activeStreamingCommentary.TryGetValue(key, out var active))
        {
            var updated = active.Entry with { Message = active.Entry.Message + delta.Message };
            _activeStreamingCommentary[key] = (updated, active.CollectionIndex);

            var idx = active.CollectionIndex;
            if (idx >= 0 && idx < LiveCommentaries.Count)
            {
                LiveCommentaries[idx] = updated;
            }
        }
        else
        {
            InsertCommentaryEntry(delta);
            _activeStreamingCommentary[key] = (delta, 0);

            var keysToUpdate = _activeStreamingCommentary.Keys
                .Where(k => k != key)
                .ToList();
            foreach (var otherKey in keysToUpdate)
            {
                var other = _activeStreamingCommentary[otherKey];
                _activeStreamingCommentary[otherKey] = (other.Entry, other.CollectionIndex + 1);
            }
        }
    }

    private void FinalizeActiveStream(string key)
    {
        _activeStreamingCommentary.Remove(key);
    }

    private void FinalizeAllActiveStreams()
    {
        _activeStreamingCommentary.Clear();
    }

    private void InsertCommentaryEntry(LiveCommentary entry)
    {
        LiveCommentaries.Insert(0, entry);
        while (LiveCommentaries.Count > MaxCommentaryEntries)
        {
            LiveCommentaries.RemoveAt(LiveCommentaries.Count - 1);
        }
    }

    private void AddEventLog(string entry)
    {
        EventLog.Insert(0, entry);
        while (EventLog.Count > MaxEventLogEntries)
        {
            EventLog.RemoveAt(EventLog.Count - 1);
        }
    }

    private void UpdateTaskProgress()
    {
        TaskProgressPercent = TotalTasks > 0
            ? (CompletedTasks * 100.0 / TotalTasks)
            : 0;
    }

    /// <summary>
    /// Builds a human-readable activity status line from the set of active assistant indices.
    /// Examples: "Assistant 1, 2, 3 are working on tasks..."  /  "Assistant 2 is working..."
    /// </summary>
    private void UpdateAssistantActivityStatus()
    {
        if (_activeAssistantIndices.Count == 0)
        {
            // All assistants finished — Manager will aggregate next
            return;
        }

        var sorted = _activeAssistantIndices.OrderBy(i => i).ToList();
        var indices = string.Join(", ", sorted);
        var verb = sorted.Count == 1 ? "is" : "are";

        ActivityStatusText = $"Assistant {indices} {verb} working on tasks...";
        IsActivityStatusVisible = true;
        IsActivityPulsing = true;
    }

    private void ClearState()
    {
        ClearError();
        FinalizeAllActiveStreams();
        Messages.Clear();
        LiveCommentaries.Clear();
        EventLog.Clear();
        CurrentPhaseDisplay = "Idle";
        CurrentPhaseColor = "#9E9E9E";
        CurrentIteration = 0;
        CompletedTasks = 0;
        TotalTasks = 0;
        QueueDepth = 0;
        TaskProgressPercent = 0;
        IsResting = false;
        RestProgressPercent = 0;
        RestCountdownText = string.Empty;
        IsWaitingForClarification = false;
        IsPlanAwaitingApproval = false;
        IsRunning = false;
        TotalIterations = 0;
        TotalTasksCompleted = 0;
        SuccessRate = "0%";
        AverageDuration = "0s";

        // Clear activity status
        ActivityStatusText = string.Empty;
        IsActivityStatusVisible = false;
        IsActivityPulsing = false;
        _activeAssistantIndices.Clear();
        _totalAssistantsDispatched = 0;
    }

    private static string GetPhaseColor(ManagerPhase phase) => phase switch
    {
        ManagerPhase.Idle => "#9E9E9E",
        ManagerPhase.Clarifying => "#FFA726",
        ManagerPhase.Planning => "#9C27B0",
        ManagerPhase.AwaitingApproval => "#2196F3",
        ManagerPhase.FetchingEvents => "#00ACC1",
        ManagerPhase.Scheduling => "#7B1FA2",
        ManagerPhase.Executing => "#4CAF50",
        ManagerPhase.Aggregating => "#00897B",
        ManagerPhase.Resting => "#78909C",
        ManagerPhase.Paused => "#FF9800",
        ManagerPhase.Stopped => "#FF9800",
        ManagerPhase.Error => "#F44336",
        _ => "#9E9E9E"
    };

    public void Dispose()
    {
        _manager.OnEvent -= HandleEvent;
        _logger.LogInformation("[OfficeVM] Disposed");
    }
}