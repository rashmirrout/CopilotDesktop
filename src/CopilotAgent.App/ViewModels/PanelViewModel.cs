using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.Events;
using CopilotAgent.Panel.Domain.Interfaces;
using CopilotAgent.Panel.Services;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for the Panel Discussion view — three-pane layout:
///   Left:   User ↔ Head Conversation
///   Center: Panel Discussion Stream (live panelist messages)
///   Right:  Agent Inspector (selected agent details)
///
/// Subscribes to IPanelOrchestrator.Events (IObservable&lt;PanelEvent&gt;) via Rx.NET.
/// All event handlers marshal to the dispatcher for thread-safe UI updates.
///
/// Settings follow the same snapshot-based dirty tracking pattern as AgentTeamViewModel.
/// </summary>
public sealed partial class PanelViewModel : ViewModelBase, IDisposable
{
    private readonly IPanelOrchestrator _orchestrator;
    private readonly IKnowledgeBriefService _knowledgeBriefService;
    private readonly ICopilotService _copilotService;
    private readonly IPersistenceService _persistenceService;
    private readonly AppSettings _appSettings;
    private readonly ILogger<PanelViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly List<IDisposable> _subscriptions = new();

    // Animation infrastructure
    private readonly DispatcherTimer _pulseTimer;
    private bool _pulseToggle;

    // Active agent tracking — drives pulse timer lifecycle and elapsed time display
    private int _activeAgentCount;
    private int _animationDotCount;
    private DateTime _discussionStartTime;

    /// <summary>
    /// Snapshot of persisted PanelSettings values — used to compute HasPendingChanges.
    /// </summary>
    private PanelSettingsSnapshot? _persistedSnapshot;

    /// <summary>Available models for ComboBox dropdowns.</summary>
    public ObservableCollection<string> AvailableModels { get; } = new();

    public PanelViewModel(
        IPanelOrchestrator orchestrator,
        IKnowledgeBriefService knowledgeBriefService,
        ICopilotService copilotService,
        IPersistenceService persistenceService,
        AppSettings appSettings,
        ILogger<PanelViewModel> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _knowledgeBriefService = knowledgeBriefService ?? throw new ArgumentNullException(nameof(knowledgeBriefService));
        _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
        _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = Application.Current.Dispatcher;

        // Load settings from persisted defaults
        LoadSettingsFromPersistence();
        _persistedSnapshot = CaptureCurrentSnapshot();

        // Subscribe to orchestrator event stream via Rx
        SubscribeToEvents();

        // Pulse timer for animations
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _pulseTimer.Tick += OnPulseTimerTick;
        _pulseTimer.Start();

        _logger.LogInformation("[PanelVM] ViewModel initialized.");
    }

    // ══════════════════════════════════════════════════════════════
    // Observable Properties — Phase & Status
    // ══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string _currentPhaseDisplay = "Idle";

    [ObservableProperty]
    private string _currentPhaseColor = "#9E9E9E";

    [ObservableProperty]
    private double _currentPhaseBadgeOpacity = 1.0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDiscussionCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseDiscussionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeDiscussionCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopDiscussionCommand))]
    private bool _isDiscussionActive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResumeDiscussionCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isAwaitingApproval;

    [ObservableProperty]
    private string _statusText = "Ready to discuss";

    [ObservableProperty]
    private string _statusIcon = "💬";

    // ── Unified Send/Start Button ───────────────────────────────

    [ObservableProperty]
    private string _sendInputButtonText = "🚀 Start";

    [ObservableProperty]
    private string _sendInputButtonColor = "#1976D2";

    // ── Left Pane: User ↔ Head Chat ─────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartDiscussionCommand))]
    private string _userInput = string.Empty;

    [ObservableProperty]
    private string _headResponse = string.Empty;

    /// <summary>Chat messages between user and Head agent.</summary>
    public ObservableCollection<PanelChatItem> HeadChatMessages { get; } = new();

    // ── Center Pane: Panel Discussion Stream ─────────────────────

    /// <summary>Live discussion messages from all panelists, moderator, etc.</summary>
    public ObservableCollection<PanelDiscussionItem> DiscussionMessages { get; } = new();

    // ── Right Pane: Agent Inspector ─────────────────────────────

    [ObservableProperty]
    private PanelAgentInspectorItem? _selectedAgent;

    /// <summary>All agents participating in the current panel.</summary>
    public ObservableCollection<PanelAgentInspectorItem> PanelAgents { get; } = new();

    // ── Convergence Progress ────────────────────────────────────

    [ObservableProperty]
    private double _convergenceScore;

    [ObservableProperty]
    private string _convergenceDisplay = "0%";

    [ObservableProperty]
    private int _completedTurns;

    [ObservableProperty]
    private int _estimatedTotalTurns;

    [ObservableProperty]
    private string _turnsDisplay = "0 / ?";

    // ── Cost Tracking ───────────────────────────────────────────

    [ObservableProperty]
    private string _costDisplay = "$0.00";

    [ObservableProperty]
    private int _totalTokensUsed;

    // ── Execution Animation ─────────────────────────────────────

    [ObservableProperty]
    private bool _isExecutionIndicatorVisible;

    [ObservableProperty]
    private string _executionStatusText = string.Empty;

    [ObservableProperty]
    private double _executionPulseOpacity = 1.0;

    [ObservableProperty]
    private string _activeAgentName = string.Empty;

    [ObservableProperty]
    private bool _showParallelIndicator;

    // ── Depth Badge ─────────────────────────────────────────────

    [ObservableProperty]
    private string _detectedDepthBadge = string.Empty;

    [ObservableProperty]
    private bool _showDepthBadge;

    /// <summary>Tracks whether the current turn involves parallel execution (⚙ indicator).</summary>
    private bool _isParallelExecutionActive;

    // ── Side Panel (Settings) ───────────────────────────────────

    [ObservableProperty]
    private bool _isSidePanelOpen;

    // ── Synthesis / Report ──────────────────────────────────────

    [ObservableProperty]
    private string _synthesisReport = string.Empty;

    [ObservableProperty]
    private bool _showSynthesis;

    // ── Follow-up Q&A ───────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskFollowUpCommand))]
    private string _followUpQuestion = string.Empty;

    [ObservableProperty]
    private bool _isFollowUpAvailable;

    // ── Event Log ───────────────────────────────────────────────

    public ObservableCollection<string> EventLog { get; } = new();

    [ObservableProperty]
    private bool _isEventLogExpanded;

    // ══════════════════════════════════════════════════════════════
    // Settings Properties (dirty-tracked)
    // ══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string _settingsPrimaryModel = string.Empty;
    partial void OnSettingsPrimaryModelChanged(string value) => RecalculateDirtyState();

    [ObservableProperty]
    private int _settingsMaxPanelists = 5;
    partial void OnSettingsMaxPanelistsChanged(int value) => RecalculateDirtyState();

    [ObservableProperty]
    private int _settingsMaxTurns = 30;
    partial void OnSettingsMaxTurnsChanged(int value) => RecalculateDirtyState();

    [ObservableProperty]
    private int _settingsMaxDurationMinutes = 30;
    partial void OnSettingsMaxDurationMinutesChanged(int value) => RecalculateDirtyState();

    [ObservableProperty]
    private string _settingsCommentaryMode = "Brief";
    partial void OnSettingsCommentaryModeChanged(string value) => RecalculateDirtyState();

    [ObservableProperty]
    private int _settingsConvergenceThreshold = 80;
    partial void OnSettingsConvergenceThresholdChanged(int value) => RecalculateDirtyState();

    [ObservableProperty]
    private bool _settingsAllowFileSystemAccess = true;
    partial void OnSettingsAllowFileSystemAccessChanged(bool value) => RecalculateDirtyState();

    [ObservableProperty]
    private string _settingsDiscussionDepth = "Auto";
    partial void OnSettingsDiscussionDepthChanged(string value) => RecalculateDirtyState();

    [ObservableProperty]
    private string _settingsWorkingDirectory = string.Empty;
    partial void OnSettingsWorkingDirectoryChanged(string value) => RecalculateDirtyState();

    /// <summary>Comma-separated list of model names for the panelist model pool (used for snapshot tracking).</summary>
    [ObservableProperty]
    private string _settingsPanelistModels = string.Empty;
    partial void OnSettingsPanelistModelsChanged(string value)
    {
        RecalculateDirtyState();
        SelectedPanelistModelsCount = ParsePanelistModels(value).Count;
    }

    /// <summary>Selectable model items for the panelist model pool checkbox list.</summary>
    public ObservableCollection<SelectableModelItem> PanelistModelItems { get; } = new();

    [ObservableProperty]
    private int _selectedPanelistModelsCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplySettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardSettingsCommand))]
    private bool _hasPendingChanges;

    [ObservableProperty]
    private int _pendingChangesCount;

    [ObservableProperty]
    private bool _settingsRequireRestart;

    [ObservableProperty]
    private bool _isLoadingModels;

    public string[] CommentaryModes { get; } = ["Detailed", "Brief", "Off"];
    public string[] DiscussionDepthOptions { get; } = ["Auto", "Quick", "Standard", "Deep"];
    public int[] PanelistOptions { get; } = [2, 3, 4, 5, 7];
    public int[] TurnOptions { get; } = [10, 15, 20, 30, 50];
    public int[] DurationOptions { get; } = [5, 10, 15, 30, 60];

    // ══════════════════════════════════════════════════════════════
    // Commands
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Starts a new panel discussion. Sends the user's prompt to the Head agent
    /// which will clarify and propose panelists before the discussion begins.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartDiscussion))]
    private async Task StartDiscussionAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput)) return;

        var prompt = UserInput.Trim();
        _logger.LogInformation("[PanelVM] Starting discussion: '{Prompt}'", Truncate(prompt, 120));

        // Add user message to head chat
        AddHeadChatMessage("You", prompt, isUser: true);
        UserInput = string.Empty;

        CurrentPhaseDisplay = "Clarifying";
        CurrentPhaseColor = GetPhaseColor(PanelPhase.Clarifying);
        StatusText = "🧠 Head is analyzing your request...";
        StatusIcon = "🧠";
        AddEvent("🚀 Discussion started. Head is analyzing...");

        try
        {
            var settings = BuildPanelSettings();
            await _orchestrator.StartAsync(prompt, settings);
            _logger.LogInformation("[PanelVM] StartAsync completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelVM] Failed to start discussion.");
            SetError($"Failed to start: {ex.Message}");
            CurrentPhaseDisplay = "Failed";
            CurrentPhaseColor = GetPhaseColor(PanelPhase.Failed);
            StatusText = "❌ Failed to start. Reset to try again.";
            StatusIcon = "❌";
            StopExecutionAnimation();
            UpdateSendInputButton();
        }
    }

    private bool CanStartDiscussion() => !IsDiscussionActive && !string.IsNullOrWhiteSpace(UserInput);

    /// <summary>
    /// Sends a message to the Head agent during clarification or follow-up.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput)) return;

        var message = UserInput.Trim();
        _logger.LogInformation("[PanelVM] Sending message to Head: '{Msg}'", Truncate(message, 80));

        AddHeadChatMessage("You", message, isUser: true);
        UserInput = string.Empty;

        try
        {
            await _orchestrator.SendUserMessageAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelVM] Failed to send message.");
            SetError($"Send failed: {ex.Message}");
        }
    }

    private bool CanSendMessage() => !string.IsNullOrWhiteSpace(UserInput);

    /// <summary>
    /// Approves the Head's discussion plan and starts the panel.
    /// </summary>
    [RelayCommand]
    private async Task ApproveAndStartPanelAsync()
    {
        _logger.LogInformation("[PanelVM] User approved panel. Starting discussion...");
        IsAwaitingApproval = false;

        try
        {
            await _orchestrator.ApproveAndStartPanelAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelVM] Failed to approve and start panel.");
            SetError($"Approval failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Rejects the Head's discussion plan and returns to the Clarifying phase.
    /// The Head will ask targeted questions to refine the plan based on user feedback.
    /// </summary>
    [RelayCommand]
    private async Task RejectPlanAsync()
    {
        _logger.LogInformation("[PanelVM] User rejected panel plan. Returning to clarification...");
        IsAwaitingApproval = false;

        var feedback = string.IsNullOrWhiteSpace(UserInput) ? null : UserInput.Trim();

        if (!string.IsNullOrWhiteSpace(feedback))
        {
            AddHeadChatMessage("You", $"❌ Rejected plan: {feedback}", isUser: true);
            UserInput = string.Empty;
        }
        else
        {
            AddHeadChatMessage("You", "❌ Rejected the plan — please refine.", isUser: true);
        }

        AddEvent("❌ Plan rejected — returning to clarification.");

        try
        {
            await _orchestrator.RejectPlanAsync(feedback);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelVM] Failed to reject plan.");
            SetError($"Reject failed: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanPauseDiscussion))]
    private async Task PauseDiscussionAsync()
    {
        _logger.LogInformation("[PanelVM] Pausing discussion.");
        try
        {
            await _orchestrator.PauseAsync();
            IsPaused = true;
            AddEvent("⏸ Discussion paused by user.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelVM] Failed to pause.");
            SetError($"Pause failed: {ex.Message}");
        }
    }

    private bool CanPauseDiscussion() => IsDiscussionActive && !IsPaused;

    [RelayCommand(CanExecute = nameof(CanResumeDiscussion))]
    private async Task ResumeDiscussionAsync()
    {
        _logger.LogInformation("[PanelVM] Resuming discussion.");
        try
        {
            await _orchestrator.ResumeAsync();
            IsPaused = false;
            AddEvent("▶ Discussion resumed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelVM] Failed to resume.");
            SetError($"Resume failed: {ex.Message}");
        }
    }

    private bool CanResumeDiscussion() => IsDiscussionActive && IsPaused;

    [RelayCommand(CanExecute = nameof(CanStopDiscussion))]
    private async Task StopDiscussionAsync()
    {
        _logger.LogInformation("[PanelVM] Stopping discussion.");
        try
        {
            await _orchestrator.StopAsync();
            StopExecutionAnimation();
            IsDiscussionActive = false;
            IsPaused = false;
            AddEvent("🛑 Discussion stopped by user.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelVM] Failed to stop.");
            SetError($"Stop failed: {ex.Message}");
        }
    }

    private bool CanStopDiscussion() => IsDiscussionActive;

    [RelayCommand]
    private async Task ResetPanelAsync()
    {
        _logger.LogInformation("[PanelVM] Resetting panel.");
        try
        {
            await _orchestrator.ResetAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PanelVM] Reset threw (may be expected if no session).");
        }

        StopExecutionAnimation();
        ClearState();
        AddEvent("🔄 Panel reset.");
        SettingsRequireRestart = false;
    }

    /// <summary>
    /// Asks a follow-up question via the KnowledgeBrief service after discussion completes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAskFollowUp))]
    private async Task AskFollowUpAsync()
    {
        if (string.IsNullOrWhiteSpace(FollowUpQuestion)) return;

        var question = FollowUpQuestion.Trim();
        _logger.LogInformation("[PanelVM] Follow-up question: '{Q}'", Truncate(question, 80));

        AddHeadChatMessage("You", question, isUser: true);
        FollowUpQuestion = string.Empty;

        try
        {
            await _orchestrator.SendUserMessageAsync(question);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelVM] Follow-up failed.");
            SetError($"Follow-up failed: {ex.Message}");
        }
    }

    private bool CanAskFollowUp() => IsFollowUpAvailable && !string.IsNullOrWhiteSpace(FollowUpQuestion);

    /// <summary>
    /// Unified input command — routes to Start, Send, or Follow-up based on current phase.
    /// This replaces the dual Start/Send buttons in the bottom input bar.
    /// </summary>
    [RelayCommand]
    private async Task SendInputAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput)) return;

        var phase = _orchestrator.CurrentPhase;
        _logger.LogDebug("[PanelVM] SendInput routed at phase={Phase}", phase);

        switch (phase)
        {
            case PanelPhase.Idle:
            case PanelPhase.Stopped:
            case PanelPhase.Failed:
                // Start a new discussion
                await StartDiscussionAsync();
                break;

            case PanelPhase.Completed:
                // Follow-up through Head agent
                await SendMessageAsync();
                break;

            default:
                // Clarifying, Running, AwaitingApproval, etc. — send to Head
                await SendMessageAsync();
                break;
        }
    }

    /// <summary>
    /// Rebuilds the PanelistModelItems checkbox list from AvailableModels,
    /// preserving current selections from SettingsPanelistModels.
    /// </summary>
    private void RebuildPanelistModelItems()
    {
        var selected = new HashSet<string>(
            ParsePanelistModels(SettingsPanelistModels),
            StringComparer.OrdinalIgnoreCase);

        PanelistModelItems.Clear();
        foreach (var model in AvailableModels)
        {
            var item = new SelectableModelItem { Name = model, IsSelected = selected.Contains(model) };
            item.SelectionChanged += OnPanelistModelSelectionChanged;
            PanelistModelItems.Add(item);
        }

        // Also add any previously-selected models not currently in AvailableModels
        foreach (var s in selected)
        {
            if (!AvailableModels.Contains(s, StringComparer.OrdinalIgnoreCase))
            {
                var item = new SelectableModelItem { Name = s, IsSelected = true };
                item.SelectionChanged += OnPanelistModelSelectionChanged;
                PanelistModelItems.Add(item);
            }
        }

        SelectedPanelistModelsCount = PanelistModelItems.Count(i => i.IsSelected);
    }

    private void OnPanelistModelSelectionChanged(object? sender, EventArgs e)
    {
        // Sync checkbox states → comma-separated string for dirty tracking
        var selectedNames = PanelistModelItems
            .Where(i => i.IsSelected)
            .Select(i => i.Name);
        SettingsPanelistModels = string.Join(", ", selectedNames);
    }

    [RelayCommand]
    private void BrowseWorkingDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Working Directory for Panel Discussion",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(SettingsWorkingDirectory)
            && System.IO.Directory.Exists(SettingsWorkingDirectory))
        {
            dialog.InitialDirectory = SettingsWorkingDirectory;
        }

        if (dialog.ShowDialog() == true)
        {
            SettingsWorkingDirectory = dialog.FolderName;
            _logger.LogInformation("[PanelVM] Working directory set to: {Dir}", dialog.FolderName);
        }
    }

    [RelayCommand]
    private void ToggleSidePanel()
    {
        IsSidePanelOpen = !IsSidePanelOpen;
    }

    [RelayCommand]
    private void SelectAgent(PanelAgentInspectorItem? agent)
    {
        SelectedAgent = agent;
        _logger.LogDebug("[PanelVM] Agent selected: {Name}", agent?.Name ?? "(none)");
    }

    [RelayCommand]
    private void DismissSynthesis()
    {
        ShowSynthesis = false;
    }

    [RelayCommand]
    private void CopySynthesisText()
    {
        if (string.IsNullOrWhiteSpace(SynthesisReport)) return;
        try
        {
            Clipboard.SetText(SynthesisReport);
            AddEvent("📋 Synthesis copied to clipboard.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PanelVM] Failed to copy synthesis.");
        }
    }

    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        if (IsLoadingModels) return;
        IsLoadingModels = true;
        _logger.LogInformation("[PanelVM] Refreshing available models...");

        try
        {
            var models = await _copilotService.GetAvailableModelsAsync();
            _dispatcher.Invoke(() =>
            {
                AvailableModels.Clear();
                foreach (var model in models.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
                    AvailableModels.Add(model);
            });
            _logger.LogInformation("[PanelVM] Models refreshed: {Count}", models.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PanelVM] Failed to refresh models.");
        }
        finally
        {
            IsLoadingModels = false;
            _dispatcher.Invoke(RebuildPanelistModelItems);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Settings Commands
    // ══════════════════════════════════════════════════════════════

    [RelayCommand(CanExecute = nameof(CanApplySettings))]
    private async Task ApplySettingsAsync()
    {
        _logger.LogInformation("[PanelVM] Applying settings to persistence.");
        try
        {
            var ps = _appSettings.Panel;
            ps.PrimaryModel = SettingsPrimaryModel;
            ps.MaxPanelists = SettingsMaxPanelists;
            ps.MaxTurns = SettingsMaxTurns;
            ps.MaxDurationMinutes = SettingsMaxDurationMinutes;
            ps.CommentaryMode = SettingsCommentaryMode;
            ps.ConvergenceThreshold = SettingsConvergenceThreshold;
            ps.AllowFileSystemAccess = SettingsAllowFileSystemAccess;
            ps.DiscussionDepthOverride = SettingsDiscussionDepth;
            ps.WorkingDirectory = SettingsWorkingDirectory;
            ps.PanelistModels = ParsePanelistModels(SettingsPanelistModels);

            await _persistenceService.SaveSettingsAsync(_appSettings);

            _persistedSnapshot = CaptureCurrentSnapshot();
            RecalculateDirtyState();

            if (IsDiscussionActive)
            {
                SettingsRequireRestart = true;
                _logger.LogInformation("[PanelVM] Settings persisted — restart required.");
            }
            else
            {
                SettingsRequireRestart = false;
                _logger.LogInformation("[PanelVM] Settings persisted — will apply on next Start.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelVM] Failed to persist settings.");
            SetError($"Failed to save settings: {ex.Message}");
        }
    }

    private bool CanApplySettings() => HasPendingChanges;

    [RelayCommand(CanExecute = nameof(CanDiscardSettings))]
    private void DiscardSettings()
    {
        _logger.LogInformation("[PanelVM] Discarding pending settings changes.");
        if (_persistedSnapshot is not null)
            LoadSettingsFromSnapshot(_persistedSnapshot);
        RecalculateDirtyState();
    }

    private bool CanDiscardSettings() => HasPendingChanges;

    [RelayCommand]
    private void RestoreDefaultSettings()
    {
        _logger.LogInformation("[PanelVM] Restoring default settings.");
        SettingsPrimaryModel = _appSettings.DefaultModel;
        SettingsMaxPanelists = 5;
        SettingsMaxTurns = 30;
        SettingsMaxDurationMinutes = 30;
        SettingsCommentaryMode = "Brief";
        SettingsConvergenceThreshold = 80;
        SettingsAllowFileSystemAccess = true;
        SettingsDiscussionDepth = "Auto";
        SettingsWorkingDirectory = string.Empty;
        SettingsPanelistModels = string.Empty;
    }

    // ══════════════════════════════════════════════════════════════
    // Rx Event Subscriptions
    // ══════════════════════════════════════════════════════════════

    private void SubscribeToEvents()
    {
        var events = _orchestrator.Events;

        // Phase changes
        _subscriptions.Add(events.OfType<PhaseChangedEvent>()
            .Subscribe(e => _dispatcher.InvokeAsync(() => OnPhaseChanged(e))));

        // Agent messages → discussion stream
        _subscriptions.Add(events.OfType<AgentMessageEvent>()
            .Subscribe(e => _dispatcher.InvokeAsync(() => OnAgentMessage(e))));

        // Agent status changes → inspector
        _subscriptions.Add(events.OfType<AgentStatusChangedEvent>()
            .Subscribe(e => _dispatcher.InvokeAsync(() => OnAgentStatusChanged(e))));

        // Progress updates
        _subscriptions.Add(events.OfType<ProgressEvent>()
            .Subscribe(e => _dispatcher.InvokeAsync(() => OnProgress(e))));

        // Cost updates
        _subscriptions.Add(events.OfType<CostUpdateEvent>()
            .Subscribe(e => _dispatcher.InvokeAsync(() => OnCostUpdate(e))));

        // Commentary (moderator insights)
        _subscriptions.Add(events.OfType<CommentaryEvent>()
            .Subscribe(e => _dispatcher.InvokeAsync(() => OnCommentary(e))));

        // Moderation events
        _subscriptions.Add(events.OfType<ModerationEvent>()
            .Subscribe(e => _dispatcher.InvokeAsync(() => OnModeration(e))));

        // Tool calls
        _subscriptions.Add(events.OfType<ToolCallEvent>()
            .Subscribe(e => _dispatcher.InvokeAsync(() => OnToolCall(e))));

        // Errors
        _subscriptions.Add(events.OfType<ErrorEvent>()
            .Subscribe(e => _dispatcher.InvokeAsync(() => OnError(e))));

        _logger.LogDebug("[PanelVM] Rx subscriptions established ({Count} streams).", _subscriptions.Count);
    }

    // ══════════════════════════════════════════════════════════════
    // Event Handlers
    // ══════════════════════════════════════════════════════════════

    private void OnPhaseChanged(PhaseChangedEvent e)
    {
        var phase = e.NewPhase;
        _logger.LogInformation("[PanelVM] Phase: {Old} → {New}", e.OldPhase, phase);

        CurrentPhaseDisplay = phase.ToString();
        CurrentPhaseColor = GetPhaseColor(phase);
        AddEvent($"[{e.Timestamp:HH:mm:ss}] Phase: {e.OldPhase} → {phase}");

        switch (phase)
        {
            case PanelPhase.Clarifying:
                StatusText = "🧠 Head is asking clarification questions...";
                StatusIcon = "🧠";
                IsDiscussionActive = true;
                break;

            case PanelPhase.AwaitingApproval:
                StatusText = "📋 Review the panel plan and approve to start...";
                StatusIcon = "📋";
                IsAwaitingApproval = true;
                break;

            case PanelPhase.Preparing:
                StatusText = "⚙ Setting up panelists and tools...";
                StatusIcon = "⚙";
                IsAwaitingApproval = false;
                break;

            case PanelPhase.Running:
                StatusText = "🔥 Panel discussion in progress";
                StatusIcon = "🔥";
                IsDiscussionActive = true;
                IsPaused = false;
                StartExecutionAnimation();
                break;

            case PanelPhase.Paused:
                StatusText = "⏸ Discussion paused";
                StatusIcon = "⏸";
                IsPaused = true;
                StopExecutionAnimation();
                break;

            case PanelPhase.Converging:
                StatusText = "🎯 Convergence detected — collecting final positions...";
                StatusIcon = "🎯";
                break;

            case PanelPhase.Synthesizing:
                StatusText = "📊 Head is synthesizing findings...";
                StatusIcon = "📊";
                StopExecutionAnimation();
                break;

            case PanelPhase.Completed:
                StatusText = "✅ Discussion complete. Follow-up available.";
                StatusIcon = "✅";
                IsDiscussionActive = false;
                IsPaused = false;
                IsFollowUpAvailable = true;
                StopExecutionAnimation();
                StopPulseAnimations();
                break;

            case PanelPhase.Stopped:
                StatusText = "🛑 Discussion stopped.";
                StatusIcon = "🛑";
                IsDiscussionActive = false;
                IsPaused = false;
                StopExecutionAnimation();
                StopPulseAnimations();
                break;

            case PanelPhase.Failed:
                StatusText = "❌ Discussion failed. Reset to try again.";
                StatusIcon = "❌";
                IsDiscussionActive = false;
                IsPaused = false;
                StopExecutionAnimation();
                StopPulseAnimations();
                break;

            case PanelPhase.Idle:
                StatusText = "Ready to discuss";
                StatusIcon = "💬";
                IsDiscussionActive = false;
                IsPaused = false;
                IsFollowUpAvailable = false;
                break;
        }

        UpdateSendInputButton();
    }

    private void OnAgentMessage(AgentMessageEvent e)
    {
        var msg = e.Message;
        _logger.LogDebug("[PanelVM] Message from {Author} ({Role}): {Content}",
            msg.AuthorName, msg.AuthorRole, Truncate(msg.Content, 80));

        // Head messages go to left pane (head chat)
        if (msg.AuthorRole == PanelAgentRole.Head)
        {
            AddHeadChatMessage(msg.AuthorName, msg.Content, isUser: false);

            // If we're awaiting approval, the Head's message contains the plan
            if (_orchestrator.CurrentPhase == PanelPhase.AwaitingApproval)
            {
                IsAwaitingApproval = true;
            }

            // If synthesis phase, capture the synthesis report
            if (_orchestrator.CurrentPhase == PanelPhase.Synthesizing
                || _orchestrator.CurrentPhase == PanelPhase.Completed)
            {
                SynthesisReport = msg.Content;
                ShowSynthesis = true;
            }
        }

        // All messages go to center pane discussion stream
        var item = new PanelDiscussionItem
        {
            AuthorName = msg.AuthorName,
            Role = msg.AuthorRole,
            Content = msg.Content,
            MessageType = msg.Type,
            Timestamp = msg.Timestamp,
            RoleColor = GetRoleColor(msg.AuthorRole),
            RoleIcon = GetRoleIcon(msg.AuthorRole)
        };
        DiscussionMessages.Add(item);

        // Update agent inspector
        UpdateAgentActivity(msg.AuthorName, msg.AuthorRole, msg.Content);

        // Keep discussion stream bounded
        while (DiscussionMessages.Count > 500)
            DiscussionMessages.RemoveAt(0);
    }

    private void OnAgentStatusChanged(AgentStatusChangedEvent e)
    {
        _logger.LogInformation("[PanelVM] Agent status: {Name} -> {New}", e.AgentName, e.NewStatus);

        // Deterministic: only Active/Thinking are "working" states.
        // Every other status (Created, Idle, Paused, Disposed) clears the flag.
        var isActivelyWorking = e.NewStatus is PanelAgentStatus.Thinking;

        var agent = PanelAgents.FirstOrDefault(a => a.Name == e.AgentName);
        if (agent is not null)
        {
            var wasWorking = agent.IsActivelyWorking;
            agent.Status = e.NewStatus.ToString();
            agent.StatusColor = GetAgentStatusColor(e.NewStatus);
            agent.IsActivelyWorking = isActivelyWorking;

            // Immediately reset pulse opacity when going idle
            if (!isActivelyWorking)
                agent.PulseOpacity = 1.0;

            // Track active agent count transitions
            if (!wasWorking && isActivelyWorking)
                _activeAgentCount++;
            else if (wasWorking && !isActivelyWorking)
                _activeAgentCount = Math.Max(0, _activeAgentCount - 1);
        }
        else
        {
            PanelAgents.Add(new PanelAgentInspectorItem
            {
                Name = e.AgentName,
                Role = e.Role,
                Status = e.NewStatus.ToString(),
                StatusColor = GetAgentStatusColor(e.NewStatus),
                RoleIcon = GetRoleIcon(e.Role),
                IsActivelyWorking = isActivelyWorking,
                PulseOpacity = 1.0
            });

            if (isActivelyWorking)
                _activeAgentCount++;
        }

        _logger.LogDebug("[PanelVM] Active agent count: {Count}", _activeAgentCount);

        // Always update active agent display — handles both 0→N and N→0 transitions.
        // Execution bar visibility is driven by phase transitions (OnPhaseChanged),
        // NOT by agent count. Between turns all agents may be in Contributed/Active
        // with _activeAgentCount == 0 — that's normal, not a signal to kill the bar.
        UpdateActiveAgentDisplay();

        AddEvent($"[{e.Timestamp:HH:mm:ss}] {e.AgentName}: {e.NewStatus}");
    }

    private void OnProgress(ProgressEvent e)
    {
        CompletedTurns = e.CompletedTurns;
        EstimatedTotalTurns = e.EstimatedTotalTurns;
        TurnsDisplay = $"{e.CompletedTurns} / {e.EstimatedTotalTurns}";
    }

    private void OnCostUpdate(CostUpdateEvent e)
    {
        TotalTokensUsed = e.TotalTokensConsumed;
        // Rough cost estimate: ~$0.003 per 1K tokens (blended rate)
        var estimatedCost = e.TotalTokensConsumed * 0.003 / 1000.0;
        CostDisplay = $"${estimatedCost:F4}";
    }

    private void OnCommentary(CommentaryEvent e)
    {
        _logger.LogDebug("[PanelVM] Commentary: {Text}", Truncate(e.Commentary, 80));

        DiscussionMessages.Add(new PanelDiscussionItem
        {
            AuthorName = "💭 Moderator",
            Role = PanelAgentRole.Moderator,
            Content = e.Commentary,
            MessageType = PanelMessageType.Commentary,
            Timestamp = e.Timestamp,
            RoleColor = "#9C27B0",
            RoleIcon = "💭",
            IsCommentary = true
        });

        // Fix: Track moderator activity so message count increments
        UpdateAgentActivity("Moderator", PanelAgentRole.Moderator, e.Commentary);

        // Detect depth badge from orchestrator commentary
        if (e.Commentary.Contains("Discussion depth:", StringComparison.OrdinalIgnoreCase))
        {
            if (e.Commentary.Contains("Quick", StringComparison.OrdinalIgnoreCase))
            {
                DetectedDepthBadge = "⚡ Quick";
                ShowDepthBadge = true;
            }
            else if (e.Commentary.Contains("Deep", StringComparison.OrdinalIgnoreCase))
            {
                DetectedDepthBadge = "🔬 Deep";
                ShowDepthBadge = true;
            }
            else if (e.Commentary.Contains("Standard", StringComparison.OrdinalIgnoreCase))
            {
                DetectedDepthBadge = "📐 Standard";
                ShowDepthBadge = true;
            }
        }
    }

    private void OnModeration(ModerationEvent e)
    {
        _logger.LogDebug("[PanelVM] Moderation: {Action}", e.Action);
        AddEvent($"[{e.Timestamp:HH:mm:ss}] 🔒 Moderation: {e.Action}");

        // Fix: Track moderator activity so message count increments
        UpdateAgentActivity("Moderator", PanelAgentRole.Moderator, e.Action);

        // Update convergence if provided
        if (e.ConvergenceScore is > 0 and var score)
        {
            ConvergenceScore = score;
            ConvergenceDisplay = $"{score:F0}%";
        }
    }

    private void OnToolCall(ToolCallEvent e)
    {
        _logger.LogDebug("[PanelVM] Tool call by {Agent}: {Tool}", e.AgentName, e.ToolName);
        AddEvent($"[{e.Timestamp:HH:mm:ss}] 🔧 {e.AgentName}: {e.ToolName}");

        // Update agent inspector with tool info
        var agent = PanelAgents.FirstOrDefault(a => a.Name == e.AgentName);
        if (agent is not null)
        {
            agent.LastToolCall = $"{e.ToolName} ({e.Timestamp:HH:mm:ss})";
            agent.ToolCallCount++;
        }
    }

    private void OnError(ErrorEvent e)
    {
        _logger.LogError("[PanelVM] Error event: {Message}", e.ErrorMessage);
        SetError(e.ErrorMessage);
        AddEvent($"[{e.Timestamp:HH:mm:ss}] ❌ Error: {e.ErrorMessage}");
    }

    // ══════════════════════════════════════════════════════════════
    // Animation
    // ══════════════════════════════════════════════════════════════

    private void StartExecutionAnimation()
    {
        IsExecutionIndicatorVisible = true;
        _animationDotCount = 0;
        ExecutionStatusText = "🔥 Discussion.";
        ExecutionPulseOpacity = 1.0;
        _discussionStartTime = DateTime.UtcNow;
    }

    private void StopExecutionAnimation()
    {
        IsExecutionIndicatorVisible = false;
        ExecutionStatusText = string.Empty;
        ExecutionPulseOpacity = 1.0;
    }

    /// <summary>
    /// Terminates all per-agent pulse animations, clears the active agent display,
    /// and hides the execution indicator. Called on terminal phases (Completed/Stopped/Failed)
    /// and when the last active agent transitions to idle.
    /// </summary>
    private void StopPulseAnimations()
    {
        foreach (var agent in PanelAgents)
        {
            agent.IsActivelyWorking = false;
            agent.PulseOpacity = 1.0;
        }

        ActiveAgentName = string.Empty;
        _isParallelExecutionActive = false;
        ShowParallelIndicator = false;
        _activeAgentCount = 0;
        _animationDotCount = 0;
        CurrentPhaseBadgeOpacity = 1.0;
        IsExecutionIndicatorVisible = false;
        ExecutionStatusText = string.Empty;
        ExecutionPulseOpacity = 1.0;

        _logger.LogDebug("[PanelVM] Pulse animations stopped — all agents idle.");
    }

    /// <summary>
    /// Recalculates ActiveAgentName, parallel execution flag, and ExecutionStatusText
    /// from the current PanelAgents collection. Includes elapsed time display.
    /// Called from OnAgentStatusChanged and OnPulseTimerTick.
    /// </summary>
    private void UpdateActiveAgentDisplay()
    {
        var activeAgents = PanelAgents.Where(a => a.IsActivelyWorking).ToList();

        ActiveAgentName = activeAgents.Count > 0
            ? string.Join("  ", activeAgents.Select(a => $"{a.RoleIcon} {a.Name}"))
            : string.Empty;

        _isParallelExecutionActive = activeAgents.Count >= 2;
        ShowParallelIndicator = _isParallelExecutionActive;

        if (IsExecutionIndicatorVisible)
        {
            _animationDotCount = (_animationDotCount + 1) % 4;
            var dots = new string('.', _animationDotCount + 1);
            var elapsed = DateTime.UtcNow - _discussionStartTime;
            var activePanelists = activeAgents.Count;

            ExecutionStatusText = $"🔥 Discussion{dots} ({elapsed.TotalSeconds:F0}s, {activePanelists} active, Turn {CompletedTurns}/{EstimatedTotalTurns})";
        }
    }

    private void OnPulseTimerTick(object? sender, EventArgs e)
    {
        var phase = Enum.TryParse<PanelPhase>(CurrentPhaseDisplay, out var p) ? p : PanelPhase.Idle;
        var isActivePhase = phase is PanelPhase.Running or PanelPhase.Converging
                                  or PanelPhase.Synthesizing or PanelPhase.Preparing;

        // Nothing to animate if no agents are working AND we're not in an active phase
        if (_activeAgentCount == 0 && !isActivePhase)
            return;

        _pulseToggle = !_pulseToggle;

        // Phase badge pulse for active phases (continues between turns)
        if (isActivePhase)
        {
            CurrentPhaseBadgeOpacity = _pulseToggle ? 1.0 : 0.5;
        }
        else
        {
            CurrentPhaseBadgeOpacity = 1.0;
        }

        // Pulse active agents' opacity for visual blinking
        foreach (var agent in PanelAgents)
        {
            agent.PulseOpacity = agent.IsActivelyWorking ? (_pulseToggle ? 1.0 : 0.5) : 1.0;
        }

        // Update execution indicator with elapsed time and active agent names
        if (IsExecutionIndicatorVisible)
        {
            UpdateActiveAgentDisplay();
            ExecutionPulseOpacity = _pulseToggle ? 1.0 : 0.6;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Settings — Load / Snapshot / Dirty Tracking
    // ══════════════════════════════════════════════════════════════

    private void LoadSettingsFromPersistence()
    {
        var ps = _appSettings.Panel;
        SettingsPrimaryModel = !string.IsNullOrWhiteSpace(ps.PrimaryModel) ? ps.PrimaryModel : _appSettings.DefaultModel;
        SettingsMaxPanelists = ps.MaxPanelists;
        SettingsMaxTurns = ps.MaxTurns;
        SettingsMaxDurationMinutes = ps.MaxDurationMinutes;
        SettingsCommentaryMode = ps.CommentaryMode;
        SettingsConvergenceThreshold = ps.ConvergenceThreshold;
        SettingsAllowFileSystemAccess = ps.AllowFileSystemAccess;
        SettingsDiscussionDepth = !string.IsNullOrWhiteSpace(ps.DiscussionDepthOverride) ? ps.DiscussionDepthOverride : "Auto";
        SettingsWorkingDirectory = ps.WorkingDirectory ?? string.Empty;
        SettingsPanelistModels = ps.PanelistModels is { Count: > 0 }
            ? string.Join(", ", ps.PanelistModels)
            : string.Empty;
    }

    private void LoadSettingsFromSnapshot(PanelSettingsSnapshot snapshot)
    {
        SettingsPrimaryModel = snapshot.PrimaryModel;
        SettingsMaxPanelists = snapshot.MaxPanelists;
        SettingsMaxTurns = snapshot.MaxTurns;
        SettingsMaxDurationMinutes = snapshot.MaxDurationMinutes;
        SettingsCommentaryMode = snapshot.CommentaryMode;
        SettingsConvergenceThreshold = snapshot.ConvergenceThreshold;
        SettingsAllowFileSystemAccess = snapshot.AllowFileSystemAccess;
        SettingsDiscussionDepth = snapshot.DiscussionDepth;
        SettingsWorkingDirectory = snapshot.WorkingDirectory;
        SettingsPanelistModels = snapshot.PanelistModels;
    }

    private PanelSettingsSnapshot CaptureCurrentSnapshot() => new(
        SettingsPrimaryModel,
        SettingsMaxPanelists,
        SettingsMaxTurns,
        SettingsMaxDurationMinutes,
        SettingsCommentaryMode,
        SettingsConvergenceThreshold,
        SettingsAllowFileSystemAccess,
        SettingsDiscussionDepth,
        SettingsWorkingDirectory,
        SettingsPanelistModels);

    private void RecalculateDirtyState()
    {
        if (_persistedSnapshot is null)
        {
            HasPendingChanges = false;
            PendingChangesCount = 0;
            return;
        }

        var current = CaptureCurrentSnapshot();
        var count = 0;

        if (!string.Equals(current.PrimaryModel, _persistedSnapshot.PrimaryModel, StringComparison.Ordinal)) count++;
        if (current.MaxPanelists != _persistedSnapshot.MaxPanelists) count++;
        if (current.MaxTurns != _persistedSnapshot.MaxTurns) count++;
        if (current.MaxDurationMinutes != _persistedSnapshot.MaxDurationMinutes) count++;
        if (!string.Equals(current.CommentaryMode, _persistedSnapshot.CommentaryMode, StringComparison.Ordinal)) count++;
        if (current.ConvergenceThreshold != _persistedSnapshot.ConvergenceThreshold) count++;
        if (current.AllowFileSystemAccess != _persistedSnapshot.AllowFileSystemAccess) count++;
        if (!string.Equals(current.DiscussionDepth, _persistedSnapshot.DiscussionDepth, StringComparison.Ordinal)) count++;
        if (!string.Equals(current.WorkingDirectory, _persistedSnapshot.WorkingDirectory, StringComparison.Ordinal)) count++;
        if (!string.Equals(current.PanelistModels, _persistedSnapshot.PanelistModels, StringComparison.Ordinal)) count++;

        PendingChangesCount = count;
        HasPendingChanges = count > 0;
    }

    /// <summary>Parses a comma-separated model string into a List&lt;string&gt;.</summary>
    private static List<string> ParsePanelistModels(string commaDelimited)
    {
        if (string.IsNullOrWhiteSpace(commaDelimited))
            return [];

        return commaDelimited
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record PanelSettingsSnapshot(
        string PrimaryModel,
        int MaxPanelists,
        int MaxTurns,
        int MaxDurationMinutes,
        string CommentaryMode,
        int ConvergenceThreshold,
        bool AllowFileSystemAccess,
        string DiscussionDepth,
        string WorkingDirectory,
        string PanelistModels);

    // ══════════════════════════════════════════════════════════════
    // Config Builder
    // ══════════════════════════════════════════════════════════════

    private CopilotAgent.Core.Models.PanelSettings BuildPanelSettings()
    {
        return new CopilotAgent.Core.Models.PanelSettings
        {
            PrimaryModel = SettingsPrimaryModel,
            MaxPanelists = SettingsMaxPanelists,
            MaxTurns = SettingsMaxTurns,
            MaxDurationMinutes = SettingsMaxDurationMinutes,
            CommentaryMode = SettingsCommentaryMode,
            ConvergenceThreshold = SettingsConvergenceThreshold,
            AllowFileSystemAccess = SettingsAllowFileSystemAccess,
            DiscussionDepthOverride = SettingsDiscussionDepth,
            PanelistModels = ParsePanelistModels(SettingsPanelistModels),
            MaxTotalTokens = _appSettings.Panel.MaxTotalTokens,
            MaxToolCalls = _appSettings.Panel.MaxToolCalls,
            WorkingDirectory = SettingsWorkingDirectory,
            EnabledMcpServers = _appSettings.Panel.EnabledMcpServers
        };
    }

    // ══════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════

    private void ClearState()
    {
        ClearError();
        IsDiscussionActive = false;
        IsPaused = false;
        IsAwaitingApproval = false;
        IsFollowUpAvailable = false;
        ShowSynthesis = false;
        SynthesisReport = string.Empty;
        HeadChatMessages.Clear();
        DiscussionMessages.Clear();
        PanelAgents.Clear();
        EventLog.Clear();
        SelectedAgent = null;
        ConvergenceScore = 0;
        ConvergenceDisplay = "0%";
        CompletedTurns = 0;
        EstimatedTotalTurns = 0;
        TurnsDisplay = "0 / ?";
        CostDisplay = "$0.00";
        TotalTokensUsed = 0;
        UserInput = string.Empty;
        FollowUpQuestion = string.Empty;
        HeadResponse = string.Empty;
        CurrentPhaseDisplay = "Idle";
        CurrentPhaseColor = GetPhaseColor(PanelPhase.Idle);
        CurrentPhaseBadgeOpacity = 1.0;
        StatusText = "Ready to discuss";
        StatusIcon = "💬";
        IsEventLogExpanded = false;
        IsExecutionIndicatorVisible = false;
        DetectedDepthBadge = string.Empty;
        ShowDepthBadge = false;
        _isParallelExecutionActive = false;
        ShowParallelIndicator = false;
        _activeAgentCount = 0;
        _animationDotCount = 0;
        _discussionStartTime = default;
        UpdateSendInputButton();
    }

    /// <summary>
    /// Updates the unified send/start button text and color based on the orchestrator's current phase.
    /// </summary>
    private void UpdateSendInputButton()
    {
        var phase = _orchestrator.CurrentPhase;
        (SendInputButtonText, SendInputButtonColor) = phase switch
        {
            PanelPhase.Completed => ("💬 Follow-up", "#00695C"),
            PanelPhase.Clarifying or PanelPhase.Running or PanelPhase.Paused
                or PanelPhase.Converging or PanelPhase.Synthesizing
                or PanelPhase.AwaitingApproval or PanelPhase.Preparing
                => ("💬 Send", "#7B1FA2"),
            _ => ("🚀 Start", "#1976D2"),
        };
    }

    private void AddHeadChatMessage(string author, string content, bool isUser)
    {
        HeadChatMessages.Add(new PanelChatItem
        {
            Author = author,
            Content = content,
            IsUser = isUser,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    private void UpdateAgentActivity(string agentName, PanelAgentRole role, string lastMessage)
    {
        var agent = PanelAgents.FirstOrDefault(a => a.Name == agentName);
        if (agent is not null)
        {
            agent.LastMessage = Truncate(lastMessage, 200);
            agent.MessageCount++;
        }
        else
        {
            PanelAgents.Add(new PanelAgentInspectorItem
            {
                Name = agentName,
                Role = role,
                Status = PanelAgentStatus.Active.ToString(),
                StatusColor = GetAgentStatusColor(PanelAgentStatus.Active),
                RoleIcon = GetRoleIcon(role),
                LastMessage = Truncate(lastMessage, 200),
                MessageCount = 1
            });
        }
    }

    private void AddEvent(string message)
    {
        EventLog.Insert(0, message);
        while (EventLog.Count > 500)
            EventLog.RemoveAt(EventLog.Count - 1);
    }

    private static string GetPhaseColor(PanelPhase phase) => phase switch
    {
        PanelPhase.Idle => "#9E9E9E",
        PanelPhase.Clarifying => "#FFA726",
        PanelPhase.AwaitingApproval => "#2196F3",
        PanelPhase.Preparing => "#9C27B0",
        PanelPhase.Running => "#4CAF50",
        PanelPhase.Paused => "#FF9800",
        PanelPhase.Converging => "#00897B",
        PanelPhase.Synthesizing => "#7B1FA2",
        PanelPhase.Completed => "#66BB6A",
        PanelPhase.Stopped => "#FF9800",
        PanelPhase.Failed => "#F44336",
        _ => "#9E9E9E"
    };

    private static string GetRoleColor(PanelAgentRole role) => role switch
    {
        PanelAgentRole.Head => "#2196F3",
        PanelAgentRole.Moderator => "#9C27B0",
        PanelAgentRole.Panelist => "#4CAF50",
        PanelAgentRole.User => "#FFA726",
        _ => "#9E9E9E"
    };

    private static string GetRoleIcon(PanelAgentRole role) => role switch
    {
        PanelAgentRole.Head => "🎓",
        PanelAgentRole.Moderator => "⚖",
        PanelAgentRole.Panelist => "🗣",
        PanelAgentRole.User => "👤",
        _ => "❓"
    };

    private static string GetAgentStatusColor(PanelAgentStatus status) => status switch
    {
        PanelAgentStatus.Created => "#FFC107",
        PanelAgentStatus.Active => "#4CAF50",
        PanelAgentStatus.Thinking => "#2196F3",
        PanelAgentStatus.Idle => "#9E9E9E",
        PanelAgentStatus.Contributed => "#00897B",
        PanelAgentStatus.Paused => "#FF9800",
        PanelAgentStatus.Disposed => "#757575",
        _ => "#9E9E9E"
    };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    public void Dispose()
    {
        _logger.LogInformation("[PanelVM] Disposing.");
        _pulseTimer.Stop();
        _pulseTimer.Tick -= OnPulseTimerTick;
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();
    }
}

// ══════════════════════════════════════════════════════════════════
// UI Model Classes
// ══════════════════════════════════════════════════════════════════

/// <summary>
/// A single chat message in the User ↔ Head conversation (left pane).
/// </summary>
public sealed class PanelChatItem : ObservableObject
{
    public string Author { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>
/// A single message in the panel discussion stream (center pane).
/// </summary>
public sealed class PanelDiscussionItem : ObservableObject
{
    public string AuthorName { get; set; } = string.Empty;
    public PanelAgentRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public PanelMessageType MessageType { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string RoleColor { get; set; } = "#9E9E9E";
    public string RoleIcon { get; set; } = "❓";
    public bool IsCommentary { get; set; }
}

/// <summary>
/// A selectable model item for the panelist model pool checkbox list.
/// </summary>
public sealed class SelectableModelItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised when IsSelected changes, so the parent ViewModel can sync the comma-separated string.</summary>
    public event EventHandler? SelectionChanged;
}

/// <summary>
/// Agent details for the inspector pane (right pane).
/// </summary>
public sealed class PanelAgentInspectorItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public PanelAgentRole Role { get; set; }
    public string RoleIcon { get; set; } = "❓";

    private string _status = "Initializing";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private string _statusColor = "#FFC107";
    public string StatusColor
    {
        get => _statusColor;
        set => SetProperty(ref _statusColor, value);
    }

    private string _lastMessage = string.Empty;
    public string LastMessage
    {
        get => _lastMessage;
        set => SetProperty(ref _lastMessage, value);
    }

    private int _messageCount;
    public int MessageCount
    {
        get => _messageCount;
        set => SetProperty(ref _messageCount, value);
    }

    private string _lastToolCall = string.Empty;
    public string LastToolCall
    {
        get => _lastToolCall;
        set => SetProperty(ref _lastToolCall, value);
    }

    private int _toolCallCount;
    public int ToolCallCount
    {
        get => _toolCallCount;
        set => SetProperty(ref _toolCallCount, value);
    }

    private bool _isActivelyWorking;
    public bool IsActivelyWorking
    {
        get => _isActivelyWorking;
        set => SetProperty(ref _isActivelyWorking, value);
    }

    private double _pulseOpacity = 1.0;
    public double PulseOpacity
    {
        get => _pulseOpacity;
        set => SetProperty(ref _pulseOpacity, value);
    }
}
