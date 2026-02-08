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
/// </summary>
public sealed partial class OfficeViewModel : ViewModelBase, IDisposable
{
    private readonly IOfficeManagerService _manager;
    private readonly IOfficeEventLog _eventLog;
    private readonly ISessionManager _sessionManager;
    private readonly ICopilotService _copilotService;
    private readonly AppSettings _appSettings;
    private readonly ILogger<OfficeViewModel> _logger;
    private readonly Dispatcher _dispatcher;

    private const int MaxCommentaryEntries = 200;
    private const int MaxEventLogEntries = 500;

    /// <summary>
    /// Cached list of available models, populated once on each agent start
    /// via <see cref="RefreshAvailableModelsAsync"/>. Survives across runs
    /// until the next explicit refresh (triggered by Start or manual reload).
    /// </summary>
    public ObservableCollection<string> AvailableModels { get; } = new();

    public OfficeViewModel(
        IOfficeManagerService manager,
        IOfficeEventLog eventLog,
        ISessionManager sessionManager,
        ICopilotService copilotService,
        AppSettings appSettings,
        ILogger<OfficeViewModel> logger)
    {
        _manager = manager;
        _eventLog = eventLog;
        _sessionManager = sessionManager;
        _copilotService = copilotService;
        _appSettings = appSettings;
        _logger = logger;
        _dispatcher = Application.Current.Dispatcher;

        // Wire events
        _manager.OnEvent += HandleEvent;

        // Load defaults from settings
        _checkIntervalMinutes = appSettings.Office.DefaultCheckIntervalMinutes;
        _maxAssistants = appSettings.Office.DefaultMaxAssistants;

        // Initialize config fields from active session (if one exists)
        var active = _sessionManager.ActiveSession;
        _workspacePath = active?.WorkingDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _selectedManagerModel = active?.ModelId ?? "gpt-4";
        _selectedAssistantModel = active?.ModelId ?? "gpt-4";

        _logger.LogInformation("[OfficeVM] ViewModel initialized");
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

    // ── Config ──────────────────────────────────────────────────

    [ObservableProperty]
    private int _checkIntervalMinutes;

    [ObservableProperty]
    private int _maxAssistants;

    [ObservableProperty]
    private string _workspacePath = string.Empty;

    [ObservableProperty]
    private string _selectedManagerModel = "gpt-4";

    [ObservableProperty]
    private string _selectedAssistantModel = "gpt-4";

    /// <summary>
    /// When true, LLM reasoning streams word-by-word in live commentary.
    /// When false (default), reasoning is buffered and emitted as a complete thought.
    /// </summary>
    [ObservableProperty]
    private bool _isStreamingTokens;

    [ObservableProperty]
    private bool _isLoadingModels;

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
            RequirePlanApproval = true,
            ManagerModel = string.IsNullOrWhiteSpace(SelectedManagerModel)
                ? activeSession?.ModelId ?? "gpt-4"
                : SelectedManagerModel,
            AssistantModel = string.IsNullOrWhiteSpace(SelectedAssistantModel)
                ? activeSession?.ModelId ?? "gpt-4"
                : SelectedAssistantModel,
            CommentaryStreamingMode = IsStreamingTokens
                ? CommentaryStreamingMode.StreamingTokens
                : CommentaryStreamingMode.CompleteThought,

            // Issue #5/#6: Propagate MCP servers and skills from the active session
            // so Manager and Assistant sessions inherit the user's tool/skill configuration.
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

        // Add user message to chat
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
        try
        {
            await _manager.PauseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OfficeVM] Failed to pause");
        }
    }

    private bool CanPause() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeAsync()
    {
        _logger.LogInformation("[OfficeVM] Resuming");
        try
        {
            await _manager.ResumeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OfficeVM] Failed to resume");
        }
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
        _logger.LogInformation("[OfficeVM] Interval updated to {Minutes} min", clamped);
    }

    [RelayCommand]
    private void SkipRest()
    {
        _logger.LogInformation("[OfficeVM] Skipping rest period");
        // Cancel the current rest via the scheduler
        // The manager service handles this internally
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
    // Model Cache — queried once per agent start, reused across runs
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Queries the SDK for available models and populates the <see cref="AvailableModels"/>
    /// cache. Called automatically on each Start, or manually via the refresh button.
    /// Previous entries are cleared and repopulated.
    /// </summary>
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
        // Skip rest countdown ticks from the event log — they fire every second
        // and create excessive noise. The UI progress bar still updates via HandleRestCountdown.
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
        CurrentPhaseDisplay = evt.NewPhase.ToString();
        CurrentPhaseColor = GetPhaseColor(evt.NewPhase);
        CurrentIteration = evt.IterationNumber;

        IsWaitingForClarification = evt.NewPhase == ManagerPhase.Clarifying;
        IsPlanAwaitingApproval = evt.NewPhase == ManagerPhase.AwaitingApproval;
        IsResting = evt.NewPhase == ManagerPhase.Resting;

        if (evt.NewPhase == ManagerPhase.Stopped || evt.NewPhase == ManagerPhase.Error)
        {
            IsRunning = false;
        }
    }

    private void HandleAssistantEvent(AssistantEvent evt)
    {
        switch (evt.Status)
        {
            case AssistantTaskStatus.Running:
                QueueDepth = Math.Max(0, QueueDepth - 1);
                break;
            case AssistantTaskStatus.Completed:
                CompletedTasks++;
                UpdateTaskProgress();
                break;
            case AssistantTaskStatus.Failed:
                CompletedTasks++;
                UpdateTaskProgress();
                break;
        }

        // Add assistant messages to chat
        if (evt.Result is not null)
        {
            AddChatMessage(new OfficeChatMessage
            {
                Role = OfficeChatRole.Assistant,
                SenderName = $"Assistant #{evt.AssistantIndex}",
                Content = $"**{evt.Task.Title}**\n\n{evt.Result.Summary}",
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

        // Update stats
        var totalAttempted = TotalTasksCompleted + evt.Report.TasksFailed;
        SuccessRate = totalAttempted > 0
            ? $"{(TotalTasksCompleted * 100.0 / totalAttempted):F0}%"
            : "0%";

        var duration = evt.Report.CompletedAt - evt.Report.StartedAt;
        AverageDuration = $"{duration.TotalSeconds:F1}s";

        // Reset per-iteration counters
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
        LiveCommentaries.Add(commentary);
        while (LiveCommentaries.Count > MaxCommentaryEntries)
        {
            LiveCommentaries.RemoveAt(0);
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

    private void ClearState()
    {
        ClearError();
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