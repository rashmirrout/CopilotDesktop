using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotAgent.App.Helpers;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.MultiAgent.Events;
using CopilotAgent.MultiAgent.Models;
using CopilotAgent.MultiAgent.Services;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for the Agent Team orchestration view.
/// Drives the orchestrator state machine, renders worker lifecycle, session health,
/// and provides rich visual feedback throughout orchestration phases.
/// </summary>
public sealed partial class AgentTeamViewModel : ViewModelBase, IDisposable
{
    private readonly IOrchestratorService _orchestrator;
    private readonly ICopilotService _copilotService;
    private readonly ISessionManager _sessionManager;
    private readonly IApprovalQueue _approvalQueue;
    private readonly AppSettings _appSettings;
    private readonly ILogger<AgentTeamViewModel> _logger;
    private readonly Dispatcher _dispatcher;

    // Animation infrastructure
    private readonly DispatcherTimer _pulseTimer;
    private readonly DispatcherTimer _sessionHealthTimer;
    private CancellationTokenSource? _cts;
    private int _animationDotCount;
    private DateTime _executionStartTime;
    private bool _pulseToggle;

    /// <summary>
    /// Guard flag: true between SubmitTaskAsync setting the "Planning" UI state
    /// and HandleOrchestratorResponse processing the first response. While set,
    /// OnOrchestratorEvent will not overwrite phase/team-status when the
    /// orchestrator's CurrentPhase still reads as Idle (race window).
    /// </summary>
    private bool _isAwaitingInitialResponse;

    public AgentTeamViewModel(
        IOrchestratorService orchestrator,
        ICopilotService copilotService,
        ISessionManager sessionManager,
        IApprovalQueue approvalQueue,
        AppSettings appSettings,
        ILogger<AgentTeamViewModel> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _approvalQueue = approvalQueue ?? throw new ArgumentNullException(nameof(approvalQueue));
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = Application.Current.Dispatcher;

        _orchestrator.EventReceived += OnOrchestratorEvent;
        _approvalQueue.PendingCountChanged += OnPendingApprovalsChanged;

        // Unified pulse timer: drives both the execution animation and the session indicator blink
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _pulseTimer.Tick += OnPulseTimerTick;
        _pulseTimer.Start(); // Always running for session indicator

        // Session health polling timer — configurable interval, clamped [5, 60]s
        var healthIntervalSec = Math.Clamp(appSettings.SessionHealthCheckIntervalSeconds, 5, 60);
        _sessionHealthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(healthIntervalSec) };
        _sessionHealthTimer.Tick += OnSessionHealthTimerTick;
        _sessionHealthTimer.Start();

        _logger.LogInformation(
            "[AgentTeamVM] ViewModel initialized. Health check interval={Interval}s",
            healthIntervalSec);
    }

    // ══════════════════════════════════════════════════════════════
    // Observable Properties
    // ══════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitTaskCommand))]
    private string _taskPrompt = string.Empty;

    [ObservableProperty]
    private string _currentPhaseDisplay = "Idle";

    /// <summary>Hex color for the phase badge, driven by the current orchestration phase.</summary>
    [ObservableProperty]
    private string _currentPhaseColor = "#9E9E9E";

    /// <summary>Opacity for the phase badge — animated for non-idle phases to create a pulse/blink effect.</summary>
    [ObservableProperty]
    private double _currentPhaseBadgeOpacity = 1.0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitTaskCommand))]
    private bool _isOrchestrating;

    [ObservableProperty]
    private bool _isAwaitingApproval;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApprovePlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(RejectPlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestPlanChangesCommand))]
    private bool _showPlanReview;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RespondToClarificationCommand))]
    private bool _showClarification;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RespondToClarificationCommand))]
    private string _clarificationResponse = string.Empty;

    [ObservableProperty]
    private string _planFeedback = string.Empty;

    [ObservableProperty]
    private string _injectionText = string.Empty;

    [ObservableProperty]
    private string _orchestratorMessage = string.Empty;

    // ── Execution animation properties ──────────────────────────

    /// <summary>Animated status text shown during execution (e.g., "⚡ Executing... (12s, 3 workers)").</summary>
    [ObservableProperty]
    private string _executionStatusText = string.Empty;

    /// <summary>Opacity toggled by pulse timer for visual pulsing effect.</summary>
    [ObservableProperty]
    private double _executionPulseOpacity = 1.0;

    /// <summary>Whether the execution indicator strip is visible.</summary>
    [ObservableProperty]
    private bool _isExecutionIndicatorVisible;

    // ── Team status display (center header) ─────────────────────

    /// <summary>Compact status text like "Coordinating • 2/3 Active".</summary>
    [ObservableProperty]
    private string _teamStatusText = "Waiting for task \u2022 0/3 Active";

    /// <summary>Emoji icon displayed to the left of the status text.</summary>
    [ObservableProperty]
    private string _teamStatusIcon = "\u2699\uFE0F";

    /// <summary>Hex color for the team status text, driven by orchestration phase.</summary>
    [ObservableProperty]
    private string _teamStatusColor = "#9E9E9E";

    /// <summary>Whether the team status icon should animate (rotate). True only during active execution with running workers.</summary>
    [ObservableProperty]
    private bool _teamStatusIconRotating;

    // ── Session health indicator ────────────────────────────────

    /// <summary>"LIVE", "IDLE", "CLOSED", "ERROR", or "DISCONNECTED".</summary>
    [ObservableProperty]
    private string _sessionIndicatorText = "WAITING";

    /// <summary>Brush color for the indicator dot (green=live, gray=idle, red=error/closed).</summary>
    [ObservableProperty]
    private string _sessionIndicatorColor = "#FFC107";

    /// <summary>Opacity toggled for blinking effect on LIVE state.</summary>
    [ObservableProperty]
    private double _sessionIndicatorOpacity = 1.0;

    // ── Settings ────────────────────────────────────────────────

    [ObservableProperty]
    private bool _showSettings;

    [ObservableProperty]
    private int _pendingApprovals;

    [ObservableProperty]
    private int _settingsMaxParallelWorkers = 3;

    [ObservableProperty]
    private string _settingsWorkspaceStrategy = "InMemory";

    [ObservableProperty]
    private int _settingsWorkerTimeoutMinutes = 10;

    [ObservableProperty]
    private int _settingsMaxRetries = 2;

    [ObservableProperty]
    private int _settingsRetryDelaySeconds = 5;

    [ObservableProperty]
    private bool _settingsAutoApproveReadOnly = true;

    // ── Plan & Report ───────────────────────────────────────────

    [ObservableProperty]
    private OrchestrationPlan? _currentPlan;

    [ObservableProperty]
    private ConsolidatedReport? _lastReport;

    [ObservableProperty]
    private bool _showReport;

    // ── Event Log ───────────────────────────────────────────────

    /// <summary>Controls the expanded/collapsed state of the event log expander. Auto-collapsed when report is shown.</summary>
    [ObservableProperty]
    private bool _isEventLogExpanded;

    // ── Next Steps ──────────────────────────────────────────────

    /// <summary>Clickable next-step actions parsed from the orchestrator's completion summary.</summary>
    public ObservableCollection<string> NextStepActions { get; } = new();

    /// <summary>Whether the next steps section is visible (at least one action available).</summary>
    [ObservableProperty]
    private bool _hasNextSteps;

    // ── Collections ─────────────────────────────────────────────

    public ObservableCollection<string> EventLog { get; } = new();
    public ObservableCollection<WorkerStatusItem> Workers { get; } = new();
    public ObservableCollection<string> ClarifyingQuestions { get; } = new();

    public string[] WorkspaceStrategies { get; } = { "InMemory", "FileLocking", "GitWorktree" };
    public int[] ParallelWorkerOptions { get; } = { 1, 2, 3, 5, 8 };

    // ══════════════════════════════════════════════════════════════
    // Commands
    // ══════════════════════════════════════════════════════════════

    [RelayCommand(CanExecute = nameof(CanSubmitTask))]
    private async Task SubmitTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(TaskPrompt)) return;

        var prompt = TaskPrompt;
        _logger.LogInformation("[AgentTeamVM] SubmitTask: prompt='{Prompt}'", Truncate(prompt, 120));

        ClearState();
        TaskPrompt = string.Empty;
        IsOrchestrating = true;

        // Immediate visual feedback — show "Planning" state before the async LLM call.
        // NOTE: We set team status properties directly instead of calling UpdateTeamStatus()
        // because the orchestrator's CurrentPhase is still Idle at this point (the async
        // call hasn't started yet). UpdateTeamStatus() reads _orchestrator.CurrentPhase
        // which would overwrite our Planning state with "Waiting for task".
        CurrentPhaseDisplay = "Planning";
        CurrentPhaseColor = GetPhaseColor(OrchestrationPhase.Planning);
        OrchestratorMessage = "📋 Analyzing task and creating execution plan...";
        AddEvent($"[{DateTime.UtcNow:HH:mm:ss}] 🚀 Task submitted. Planning...");
        TeamStatusIcon = "";
        TeamStatusText = $"Planning  0/{SettingsMaxParallelWorkers} Active";
        TeamStatusColor = "#9C27B0";
        TeamStatusIconRotating = false;

        _cts = new CancellationTokenSource();
        _isAwaitingInitialResponse = true;

        try
        {
            var config = BuildConfig();
            _logger.LogDebug("[AgentTeamVM] Config built: workers={Workers}, strategy={Strategy}, timeout={Timeout}min",
                config.MaxParallelSessions, config.WorkspaceStrategy, config.WorkerTimeout.TotalMinutes);

            var response = await _orchestrator.SubmitTaskAsync(prompt, config, _cts.Token);
            _logger.LogInformation("[AgentTeamVM] SubmitTask response: phase={Phase}", response.Phase);
            _isAwaitingInitialResponse = false;
            HandleOrchestratorResponse(response);
        }
        catch (OperationCanceledException)
        {
            _isAwaitingInitialResponse = false;
            _logger.LogInformation("[AgentTeamVM] Task cancelled by user.");
            AddEvent("🛑 Task cancelled.");
            StopExecutionAnimation();
            IsOrchestrating = false;
        }
        catch (TimeoutException tex)
        {
            _isAwaitingInitialResponse = false;
            _logger.LogWarning(tex, "[AgentTeamVM] Orchestrator LLM call timed out.");
            StopExecutionAnimation();
            AddEvent("⏱ Operation timed out. The task may be too complex or the connection was lost.");
            SetError($"Timeout: {tex.Message}");
            IsOrchestrating = false;
        }
        catch (InvalidOperationException iex) when (IsConnectionError(iex))
        {
            _isAwaitingInitialResponse = false;
            _logger.LogWarning(iex, "[AgentTeamVM] Connection lost to Copilot service.");
            StopExecutionAnimation();
            AddEvent("⚠ Lost connection to Copilot service. Please reset and try again.");
            SetError($"Connection lost: {iex.Message}");
            IsOrchestrating = false;
        }
        catch (Exception ex)
        {
            _isAwaitingInitialResponse = false;
            _logger.LogError(ex, "[AgentTeamVM] Unexpected error during task submission.");
            StopExecutionAnimation();
            SetError($"Error: {ex.Message}");
            IsOrchestrating = false;
        }
    }

    private bool CanSubmitTask() => !IsOrchestrating && !string.IsNullOrWhiteSpace(TaskPrompt);

    [RelayCommand(CanExecute = nameof(CanRespondToClarification))]
    private async Task RespondToClarificationAsync()
    {
        if (string.IsNullOrWhiteSpace(ClarificationResponse)) return;

        _logger.LogInformation("[AgentTeamVM] Responding to clarification.");
        try
        {
            var response = await _orchestrator.RespondToClarificationAsync(
                ClarificationResponse, _cts?.Token ?? CancellationToken.None);
            ClarificationResponse = string.Empty;
            HandleOrchestratorResponse(response);
        }
        catch (InvalidOperationException iex) when (iex.Message.Contains("Cannot respond to clarification", StringComparison.OrdinalIgnoreCase))
        {
            // Backend phase moved away from Clarifying before user submitted response.
            // Gracefully dismiss the clarification panel and inform the user.
            _logger.LogWarning(iex, "[AgentTeamVM] Clarification response rejected — phase already changed.");
            ShowClarification = false;
            ClarifyingQuestions.Clear();
            ClarificationResponse = string.Empty;

            var currentPhase = _orchestrator.CurrentPhase;
            CurrentPhaseDisplay = currentPhase.ToString();
            CurrentPhaseColor = GetPhaseColor(currentPhase);
            OrchestratorMessage = $"⚠ Could not send clarification — the orchestrator already moved to {currentPhase}. Your response was not processed.";
            AddEvent($"⚠ Clarification send failed: orchestrator phase is now {currentPhase}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AgentTeamVM] Failed to respond to clarification.");
            SetError($"Error: {ex.Message}");
        }
    }

    private bool CanRespondToClarification() => ShowClarification && !string.IsNullOrWhiteSpace(ClarificationResponse);

    [RelayCommand(CanExecute = nameof(CanApprovePlan))]
    private async Task ApprovePlanAsync()
    {
        _logger.LogInformation("[AgentTeamVM] Plan approved by user.");
        try
        {
            ShowPlanReview = false;
            var response = await _orchestrator.ApprovePlanAsync(
                PlanApprovalDecision.Approve, cancellationToken: _cts?.Token ?? CancellationToken.None);
            HandleOrchestratorResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AgentTeamVM] Failed to approve plan.");
            StopExecutionAnimation();
            SetError($"Error: {ex.Message}");
        }
    }

    private bool CanApprovePlan() => ShowPlanReview;

    [RelayCommand(CanExecute = nameof(CanApprovePlan))]
    private async Task RequestPlanChangesAsync()
    {
        if (string.IsNullOrWhiteSpace(PlanFeedback)) return;

        _logger.LogInformation("[AgentTeamVM] User requested plan changes: {Feedback}", Truncate(PlanFeedback, 100));
        try
        {
            ShowPlanReview = false;
            var response = await _orchestrator.ApprovePlanAsync(
                PlanApprovalDecision.RequestChanges, PlanFeedback,
                _cts?.Token ?? CancellationToken.None);
            PlanFeedback = string.Empty;
            HandleOrchestratorResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AgentTeamVM] Failed to request plan changes.");
            SetError($"Error: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanApprovePlan))]
    private async Task RejectPlanAsync()
    {
        _logger.LogInformation("[AgentTeamVM] Plan rejected by user.");
        try
        {
            ShowPlanReview = false;
            var response = await _orchestrator.ApprovePlanAsync(
                PlanApprovalDecision.Reject, cancellationToken: _cts?.Token ?? CancellationToken.None);
            HandleOrchestratorResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AgentTeamVM] Failed to reject plan.");
            SetError($"Error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task InjectInstructionAsync()
    {
        if (string.IsNullOrWhiteSpace(InjectionText) || !IsOrchestrating) return;

        _logger.LogInformation("[AgentTeamVM] Injecting instruction: {Text}", Truncate(InjectionText, 80));
        try
        {
            await _orchestrator.InjectInstructionAsync(
                InjectionText, _cts?.Token ?? CancellationToken.None);
            AddEvent($"💉 Injected: {Truncate(InjectionText, 80)}");
            InjectionText = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AgentTeamVM] Failed to inject instruction.");
            SetError($"Injection failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CancelOrchestrationAsync()
    {
        _logger.LogInformation("[AgentTeamVM] User cancelling orchestration.");
        try
        {
            await _orchestrator.CancelAsync();
            StopExecutionAnimation();
            IsOrchestrating = false;
            AddEvent("🛑 Orchestration cancelled by user.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AgentTeamVM] Error cancelling orchestration.");
        }
    }

    [RelayCommand]
    private void ResetOrchestrator()
    {
        _logger.LogInformation("[AgentTeamVM] Resetting orchestrator.");
        _orchestrator.ResetContext();
        StopExecutionAnimation();
        ClearState();
        TaskPrompt = string.Empty;
        EventLog.Clear();
        Workers.Clear();
        // After reset, session is destroyed - health poll will pick up WAITING on next tick
        ApplySessionHealth("WAITING", "#FFC107");
        AddEvent("🔄 Orchestrator reset.");
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        ShowSettings = !ShowSettings;
        _logger.LogDebug("[AgentTeamVM] Settings panel toggled: {Visible}", ShowSettings);
    }

    [RelayCommand]
    private void RestoreDefaultSettings()
    {
        _logger.LogInformation("[AgentTeamVM] Restoring default settings.");
        SettingsMaxParallelWorkers = 3;
        SettingsWorkspaceStrategy = "InMemory";
        SettingsWorkerTimeoutMinutes = 10;
        SettingsMaxRetries = 2;
        SettingsRetryDelaySeconds = 5;
        SettingsAutoApproveReadOnly = true;
    }

    [RelayCommand]
    private void DismissReport()
    {
        ShowReport = false;
    }

    /// <summary>
    /// Copies the orchestrator response message text to the system clipboard.
    /// </summary>
    [RelayCommand]
    private void CopyOrchestratorMessage()
    {
        var text = OrchestratorMessage;
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("[AgentTeamVM] CopyOrchestratorMessage: no text to copy.");
            return;
        }

        try
        {
            Clipboard.SetText(text);
            _logger.LogInformation("[AgentTeamVM] Orchestrator message copied to clipboard ({Length} chars).", text.Length);
            AddEvent("📋 Response text copied to clipboard.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AgentTeamVM] Failed to copy orchestrator message to clipboard.");
        }
    }

    /// <summary>
    /// Copies the raw report summary text to the system clipboard.
    /// </summary>
    [RelayCommand]
    private void CopyReportText()
    {
        var text = LastReport?.ConversationalSummary;
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("[AgentTeamVM] CopyReportText: no text to copy.");
            return;
        }

        try
        {
            Clipboard.SetText(text);
            _logger.LogInformation("[AgentTeamVM] Report text copied to clipboard ({Length} chars).", text.Length);
            AddEvent("📋 Report text copied to clipboard.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AgentTeamVM] Failed to copy report text to clipboard.");
        }
    }

    /// <summary>
    /// Populates the task input with the selected next-step action text
    /// so the user can submit it as a follow-up task with one click.
    /// </summary>
    [RelayCommand]
    private void SelectNextStep(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return;

        _logger.LogInformation("[AgentTeamVM] Next step selected: {Action}", Truncate(action, 100));
        TaskPrompt = action;

        // Clear the next steps UI since user has made a selection
        NextStepActions.Clear();
        HasNextSteps = false;
    }

    // ══════════════════════════════════════════════════════════════
    // Session Health Polling (Ground Truth)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Fires on the configurable health check interval (default 15s).
    /// Queries ICopilotService.HasActiveSession as ground truth and
    /// combines with IOrchestratorService.IsRunning to determine state:
    ///   No session ID        → WAITING (yellow solid)
    ///   HasActiveSession=false → DISCONNECTED (red solid)
    ///   HasActiveSession=true + IsRunning=true  → LIVE (green blink)
    ///   HasActiveSession=true + IsRunning=false → IDLE (gray solid)
    ///   Exception            → ERROR   (red solid)
    /// </summary>
    private void OnSessionHealthTimerTick(object? sender, EventArgs e)
    {
        try
        {
            var sessionId = _orchestrator.OrchestratorSessionId;

            if (string.IsNullOrEmpty(sessionId))
            {
                ApplySessionHealth("WAITING", "#FFC107");
                return;
            }

            var isAlive = _copilotService.HasActiveSession(sessionId);

            if (!isAlive)
            {
                ApplySessionHealth("DISCONNECTED", "#F44336");
                return;
            }

            if (_orchestrator.IsRunning)
            {
                ApplySessionHealth("LIVE", "#4CAF50");
            }
            else
            {
                ApplySessionHealth("IDLE", "#9E9E9E");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AgentTeamVM] Session health check failed.");
            ApplySessionHealth("ERROR", "#F44336");
        }
    }

    /// <summary>
    /// Applies session health indicator state. Only logs on state transitions
    /// to avoid flooding the log every 15 seconds.
    /// </summary>
    private void ApplySessionHealth(string text, string color)
    {
        if (SessionIndicatorText == text && SessionIndicatorColor == color)
            return; // No change — skip update and log noise

        SessionIndicatorText = text;
        SessionIndicatorColor = color;
        _logger.LogDebug("[AgentTeamVM] Session health: [{Text}] color={Color}", text, color);
    }

    // ══════════════════════════════════════════════════════════════
    // Event Handlers
    // ══════════════════════════════════════════════════════════════

    private void OnOrchestratorEvent(object? sender, OrchestratorEvent e)
    {
        _dispatcher.InvokeAsync(() =>
        {
            var phase = _orchestrator.CurrentPhase;

            // Guard: During the SubmitTaskAsync await window, the orchestrator may fire
            // events while its CurrentPhase still reads as Idle. If we're awaiting the
            // initial response, do NOT overwrite the manually-set "Planning" UI state
            // with Idle. Allow real phase transitions (non-Idle) to flow through.
            var suppressPhaseOverwrite = _isAwaitingInitialResponse && phase == OrchestrationPhase.Idle;

            if (!suppressPhaseOverwrite)
            {
                CurrentPhaseDisplay = phase.ToString();
                CurrentPhaseColor = GetPhaseColor(phase);
            }

            _logger.LogDebug("[AgentTeamVM] Event: type={EventType}, phase={Phase}, suppress={Suppress}, msg={Message}",
                e.EventType, phase, suppressPhaseOverwrite, Truncate(e.Message, 150));

            AddEvent($"[{e.TimestampUtc:HH:mm:ss}] {e.EventType}: {e.Message}");

            // ── Sync UI panels with phase transitions ──
            // If the backend transitioned away from Clarifying but the panel is still showing,
            // dismiss it and inform the user. This prevents orphaned clarification UI.
            if (!suppressPhaseOverwrite && ShowClarification && phase != OrchestrationPhase.Clarifying)
            {
                _logger.LogWarning(
                    "[AgentTeamVM] Phase moved to {Phase} while clarification panel was visible. Dismissing.",
                    phase);
                ShowClarification = false;
                ClarifyingQuestions.Clear();
                ClarificationResponse = string.Empty;
                OrchestratorMessage = $"⚠ Clarification was dismissed — the orchestrator moved to {phase}.";
                AddEvent($"⚠ Clarification auto-dismissed: phase transitioned to {phase}");
            }

            // Same guard for plan review panel
            if (!suppressPhaseOverwrite && ShowPlanReview && phase != OrchestrationPhase.AwaitingApproval)
            {
                _logger.LogWarning(
                    "[AgentTeamVM] Phase moved to {Phase} while plan review panel was visible. Dismissing.",
                    phase);
                ShowPlanReview = false;
                IsAwaitingApproval = false;
                PlanFeedback = string.Empty;
                AddEvent($"⚠ Plan review auto-dismissed: phase transitioned to {phase}");
            }

            // Keep team status display in sync with every orchestrator event —
            // but skip when we're suppressing phase overwrites to preserve the
            // manually-set "Planning" state during the initial submit window.
            if (!suppressPhaseOverwrite)
            {
                UpdateTeamStatus();
            }

            if (e is WorkerProgressEvent workerEvent)
            {
                UpdateWorkerStatus(workerEvent);
            }
            else if (e is OrchestrationCompletedEvent completedEvent)
            {
                _logger.LogInformation("[AgentTeamVM] Orchestration completed. Report={HasReport}",
                    completedEvent.Report != null);

                StopExecutionAnimation();
                CurrentPhaseColor = GetPhaseColor(OrchestrationPhase.Completed);
                LastReport = completedEvent.Report;
                ShowReport = completedEvent.Report != null;
                IsOrchestrating = false;

                if (completedEvent.Report != null
                    && string.IsNullOrWhiteSpace(completedEvent.Report.ConversationalSummary))
                {
                    completedEvent.Report.ConversationalSummary =
                        BuildLocalFallbackSummary(completedEvent.Report);
                }

                // Mark all remaining workers as completed
                FinalizeWorkerStates();
                ProcessNextSteps(completedEvent.Report);

                // Auto-collapse event log when report is shown
                if (ShowReport) IsEventLogExpanded = false;
            }
        });
    }

    private void OnPendingApprovalsChanged(object? sender, int count)
    {
        _dispatcher.InvokeAsync(() =>
        {
            PendingApprovals = count;
            _logger.LogDebug("[AgentTeamVM] Pending approvals changed: {Count}", count);
        });
    }

    // ══════════════════════════════════════════════════════════════
    // Worker Lifecycle Management
    // ══════════════════════════════════════════════════════════════

    private void UpdateWorkerStatus(WorkerProgressEvent e)
    {
        var chunkId = e.ChunkId ?? "unknown";
        var existing = FindWorker(chunkId);

        _logger.LogDebug("[AgentTeamVM] Worker update: chunk={ChunkId}, status={Status}, activity={Activity}",
            chunkId, e.WorkerStatus, e.CurrentActivity ?? "(none)");

        if (existing != null)
        {
            // Update existing worker
            existing.Status = e.WorkerStatus;
            existing.StatusDisplay = FormatWorkerStatus(e.WorkerStatus);
            existing.Activity = e.CurrentActivity ?? string.Empty;
            existing.StatusColor = GetWorkerStatusColor(e.WorkerStatus);
            existing.RetryAttempt = e.RetryAttempt;

            // Worker state changed — refresh team status counts
            UpdateTeamStatus();
        }
        else
        {
            // Create new worker entry — show from the moment it appears
            var worker = new WorkerStatusItem
            {
                ChunkId = chunkId,
                Title = e.ChunkTitle ?? "Worker",
                Status = e.WorkerStatus,
                StatusDisplay = FormatWorkerStatus(e.WorkerStatus),
                Activity = e.CurrentActivity ?? string.Empty,
                StatusColor = GetWorkerStatusColor(e.WorkerStatus),
                WorkerRole = e.WorkerRole.ToString(),
                WorkerIndex = e.WorkerIndex,
                RetryAttempt = e.RetryAttempt
            };
            Workers.Add(worker);
            _logger.LogDebug("[AgentTeamVM] New worker added: {ChunkId} ({Title})", chunkId, worker.Title);

            // New worker added — refresh team status counts
            UpdateTeamStatus();
        }
    }

    /// <summary>
    /// After orchestration completes, mark any workers still in a non-terminal state
    /// as completed so the UI reflects final state.
    /// </summary>
    private void FinalizeWorkerStates()
    {
        foreach (var w in Workers)
        {
            if (w.Status is AgentStatus.Running or AgentStatus.Queued or AgentStatus.Pending
                or AgentStatus.WaitingForDependencies or AgentStatus.Retrying)
            {
                w.Status = AgentStatus.Succeeded;
                w.StatusDisplay = FormatWorkerStatus(AgentStatus.Succeeded);
                w.StatusColor = GetWorkerStatusColor(AgentStatus.Succeeded);
                w.Activity = "Done";
            }
        }
    }

    private WorkerStatusItem? FindWorker(string chunkId) =>
        Workers.FirstOrDefault(w => w.ChunkId == chunkId);

    private static string FormatWorkerStatus(AgentStatus status) => status switch
    {
        AgentStatus.Pending => "⏳ Pending",
        AgentStatus.WaitingForDependencies => "⏸ Waiting",
        AgentStatus.Queued => "📋 Queued",
        AgentStatus.Running => "🔄 Running",
        AgentStatus.Succeeded => "✅ Done",
        AgentStatus.Failed => "❌ Failed",
        AgentStatus.Retrying => "🔁 Retrying",
        AgentStatus.Aborted => "⛔ Aborted",
        AgentStatus.Skipped => "⏭ Skipped",
        _ => status.ToString()
    };

    private static string GetWorkerStatusColor(AgentStatus status) => status switch
    {
        AgentStatus.Running => "#4CAF50",        // Green
        AgentStatus.Succeeded => "#2196F3",       // Blue
        AgentStatus.Failed => "#F44336",          // Red
        AgentStatus.Retrying => "#FF9800",        // Orange
        AgentStatus.Aborted => "#9E9E9E",         // Gray
        AgentStatus.Skipped => "#9E9E9E",         // Gray
        AgentStatus.Queued => "#FFC107",          // Amber
        AgentStatus.Pending => "#BDBDBD",         // Light gray
        AgentStatus.WaitingForDependencies => "#CE93D8", // Light purple
        _ => "#9E9E9E"
    };

    // ══════════════════════════════════════════════════════════════
    // Response Handling
    // ══════════════════════════════════════════════════════════════

    private void HandleOrchestratorResponse(OrchestratorResponse response)
    {
        _logger.LogInformation("[AgentTeamVM] HandleResponse: phase={Phase}, msg={Message}",
            response.Phase, Truncate(response.Message ?? "", 100));

        CurrentPhaseDisplay = response.Phase.ToString();
        CurrentPhaseColor = GetPhaseColor(response.Phase);

        // Only overwrite OrchestratorMessage when the response carries actual content.
        // This prevents the "📋 Analyzing task..." feedback set during SubmitTaskAsync()
        // from being cleared by an empty/null response.Message on the first callback.
        if (!string.IsNullOrWhiteSpace(response.Message))
        {
            OrchestratorMessage = response.Message;
        }

        ShowClarification = false;
        ShowPlanReview = false;
        ShowReport = false;

        // Refresh team status display for every response phase transition
        UpdateTeamStatus();

        switch (response.Phase)
        {
            case OrchestrationPhase.Clarifying:
                StopExecutionAnimation();
                ShowClarification = true;
                ClarifyingQuestions.Clear();
                foreach (var q in response.ClarifyingQuestions ?? Enumerable.Empty<string>())
                    ClarifyingQuestions.Add(q);
                _logger.LogDebug("[AgentTeamVM] Clarification phase: {Count} questions", ClarifyingQuestions.Count);
                break;

            case OrchestrationPhase.AwaitingApproval:
                StopExecutionAnimation();
                ShowPlanReview = true;
                CurrentPlan = response.Plan;
                IsAwaitingApproval = true;
                _logger.LogDebug("[AgentTeamVM] Awaiting approval: {ChunkCount} chunks in plan",
                    response.Plan?.Chunks.Count ?? 0);

                // Pre-populate workers from the plan chunks so user sees them immediately
                PopulateWorkersFromPlan(response.Plan);
                break;

            case OrchestrationPhase.Executing:
                IsAwaitingApproval = false;
                StartExecutionAnimation();
                break;

            case OrchestrationPhase.Completed:
                StopExecutionAnimation();
                IsOrchestrating = false;
                LastReport = response.Report;
                ShowReport = response.Report != null;
                if (response.Report != null
                    && string.IsNullOrWhiteSpace(response.Report.ConversationalSummary))
                {
                    response.Report.ConversationalSummary =
                        BuildLocalFallbackSummary(response.Report);
                }
                FinalizeWorkerStates();
                ProcessNextSteps(response.Report);
                // Auto-collapse event log when report is shown
                if (ShowReport) IsEventLogExpanded = false;
                break;

            case OrchestrationPhase.Cancelled:
                StopExecutionAnimation();
                IsOrchestrating = false;
                break;

            case OrchestrationPhase.Idle:
                StopExecutionAnimation();
                IsOrchestrating = false;
                break;
        }
    }

    /// <summary>
    /// When a plan is shown for approval, pre-populate workers in Pending state
    /// so the user can see what will execute.
    /// </summary>
    private void PopulateWorkersFromPlan(OrchestrationPlan? plan)
    {
        if (plan?.Chunks is null) return;

        Workers.Clear();
        for (var i = 0; i < plan.Chunks.Count; i++)
        {
            var chunk = plan.Chunks[i];
            Workers.Add(new WorkerStatusItem
            {
                ChunkId = chunk.ChunkId,
                Title = chunk.Title,
                Status = AgentStatus.Pending,
                StatusDisplay = FormatWorkerStatus(AgentStatus.Pending),
                StatusColor = GetWorkerStatusColor(AgentStatus.Pending),
                Activity = chunk.AssignedRole.ToString(),
                WorkerRole = chunk.AssignedRole.ToString(),
                WorkerIndex = i
            });
        }

        _logger.LogDebug("[AgentTeamVM] Pre-populated {Count} workers from plan", Workers.Count);
    }

    // ══════════════════════════════════════════════════════════════
    // Execution Animation (Pulsing + Status Text)
    // ══════════════════════════════════════════════════════════════

    private void StartExecutionAnimation()
    {
        _executionStartTime = DateTime.UtcNow;
        _animationDotCount = 0;
        _pulseToggle = false;
        IsExecutionIndicatorVisible = true;
        ExecutionStatusText = "⚡ Executing...";
        ExecutionPulseOpacity = 1.0;
        _logger.LogDebug("[AgentTeamVM] Execution animation started.");
    }

    private void StopExecutionAnimation()
    {
        IsExecutionIndicatorVisible = false;
        ExecutionStatusText = string.Empty;
        ExecutionPulseOpacity = 1.0;
        _logger.LogDebug("[AgentTeamVM] Execution animation stopped.");
    }

    /// <summary>
    /// Fires every 500ms. Drives:
    /// 1) Execution status text animation (dots + elapsed time)
    /// 2) Execution indicator pulsing opacity
    /// 3) Session indicator blinking when LIVE
    /// 4) Phase badge pulsing for non-idle phases
    /// </summary>
    private void OnPulseTimerTick(object? sender, EventArgs e)
    {
        _pulseToggle = !_pulseToggle;

        // ── Session indicator blink ──
        if (SessionIndicatorText == "LIVE")
        {
            SessionIndicatorOpacity = _pulseToggle ? 1.0 : 0.4;
        }
        else
        {
            SessionIndicatorOpacity = 1.0;
        }

        // ── Phase badge pulse (non-idle, non-completed, non-cancelled) ──
        var phase = ParseCurrentPhase();
        if (phase is OrchestrationPhase.Clarifying or OrchestrationPhase.Planning
            or OrchestrationPhase.AwaitingApproval or OrchestrationPhase.Executing
            or OrchestrationPhase.Aggregating)
        {
            // Faster pulse for executing phase
            CurrentPhaseBadgeOpacity = phase == OrchestrationPhase.Executing
                ? (_pulseToggle ? 1.0 : 0.35)
                : (_pulseToggle ? 1.0 : 0.5);
        }
        else
        {
            CurrentPhaseBadgeOpacity = 1.0;
        }

        // ── Execution indicator animation ──
        if (IsExecutionIndicatorVisible)
        {
            _animationDotCount = (_animationDotCount + 1) % 4;
            var dots = new string('.', _animationDotCount + 1);
            var elapsed = DateTime.UtcNow - _executionStartTime;
            var runningCount = Workers.Count(w => w.Status == AgentStatus.Running);
            var queuedCount = Workers.Count(w => w.Status is AgentStatus.Queued or AgentStatus.WaitingForDependencies);

            var parts = new List<string> { $"{elapsed.TotalSeconds:F0}s" };
            if (runningCount > 0) parts.Add($"{runningCount} running");
            if (queuedCount > 0) parts.Add($"{queuedCount} queued");

            ExecutionStatusText = $"⚡ Executing{dots} ({string.Join(", ", parts)})";
            ExecutionPulseOpacity = _pulseToggle ? 1.0 : 0.6;
        }
    }

    /// <summary>
    /// Parses the current phase display string back to the enum for animation logic.
    /// </summary>
    private OrchestrationPhase ParseCurrentPhase()
    {
        return Enum.TryParse<OrchestrationPhase>(CurrentPhaseDisplay, out var phase)
            ? phase
            : OrchestrationPhase.Idle;
    }

    // ══════════════════════════════════════════════════════════════
    // Phase Color Mapping
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Maps an orchestration phase to its display color (hex).
    /// Used for the phase badge background tinting and pulsing.
    /// </summary>
    private static string GetPhaseColor(OrchestrationPhase phase) => phase switch
    {
        OrchestrationPhase.Idle => "#9E9E9E",            // Gray
        OrchestrationPhase.Clarifying => "#FFA726",       // Amber
        OrchestrationPhase.Planning => "#9C27B0",         // Purple
        OrchestrationPhase.AwaitingApproval => "#2196F3", // Blue
        OrchestrationPhase.Executing => "#4CAF50",        // Green
        OrchestrationPhase.Aggregating => "#00897B",      // Teal
        OrchestrationPhase.Completed => "#66BB6A",        // Light green
        OrchestrationPhase.Cancelled => "#FF9800",        // Orange
        _ => "#9E9E9E"
    };

    // ══════════════════════════════════════════════════════════════
    // Next Steps Processing
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Extracts next-step actions from the report summary and populates
    /// the <see cref="NextStepActions"/> collection for UI button rendering.
    /// Also strips the [ACTION:...] markers from the summary for clean display.
    /// </summary>
    private void ProcessNextSteps(ConsolidatedReport? report)
    {
        NextStepActions.Clear();
        HasNextSteps = false;

        if (report is null || string.IsNullOrWhiteSpace(report.ConversationalSummary))
        {
            _logger.LogDebug("[AgentTeamVM] No report or summary to extract next steps from.");
            return;
        }

        var actions = NextStepsParser.ExtractNextSteps(report.ConversationalSummary);
        _logger.LogInformation("[AgentTeamVM] Extracted {Count} next-step actions from summary.", actions.Count);

        // Store parsed next steps on the report model
        report.NextSteps = actions;

        // Strip ACTION markers from the displayed summary for clean markdown rendering
        report.ConversationalSummary = NextStepsParser.StripActionMarkers(report.ConversationalSummary);

        foreach (var action in actions)
        {
            NextStepActions.Add(action);
            _logger.LogDebug("[AgentTeamVM] Next step: {Action}", action);
        }

        HasNextSteps = actions.Count > 0;
    }

    // ══════════════════════════════════════════════════════════════
    // Config Builder
    // ══════════════════════════════════════════════════════════════

    private MultiAgentConfig BuildConfig()
    {
        var activeSession = _sessionManager.ActiveSession;

        return new MultiAgentConfig
        {
            MaxParallelSessions = SettingsMaxParallelWorkers,
            WorkspaceStrategy = Enum.TryParse<WorkspaceStrategyType>(SettingsWorkspaceStrategy, out var ws)
                ? ws : WorkspaceStrategyType.InMemory,
            OrchestratorModelId = activeSession?.ModelId ?? "gpt-4",
            WorkerModelId = activeSession?.ModelId ?? "gpt-4",
            WorkingDirectory = activeSession?.WorkingDirectory
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            EnabledMcpServers = activeSession?.EnabledMcpServers,
            DisabledSkills = activeSession?.DisabledSkills,
            AutoApproveReadOnlyTools = SettingsAutoApproveReadOnly,
            RetryPolicy = new RetryPolicy
            {
                MaxRetriesPerChunk = SettingsMaxRetries,
                RetryDelay = TimeSpan.FromSeconds(SettingsRetryDelaySeconds)
            },
            WorkerTimeout = TimeSpan.FromMinutes(SettingsWorkerTimeoutMinutes)
        };
    }

    // ══════════════════════════════════════════════════════════════
    // Team Status Display (Center Header)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Recomputes the compact team status display based on the current orchestration
    /// phase and active worker count. Called from multiple lifecycle points to keep
    /// the header indicator in sync with the orchestrator state machine.
    /// </summary>
    private void UpdateTeamStatus()
    {
        var phase = _orchestrator.CurrentPhase;
        var activeWorkers = Workers.Count(w => w.Status is AgentStatus.Running or AgentStatus.Retrying);
        var poolCapacity = SettingsMaxParallelWorkers;

        var (action, icon, color, rotating) = phase switch
        {
            OrchestrationPhase.Executing when activeWorkers > 0
                => ("Coordinating", "\u2699\uFE0F", "#4CAF50", true),
            OrchestrationPhase.Executing
                => ("Starting", "\u26A1", "#4CAF50", false),
            OrchestrationPhase.Planning
                => ("Planning", "\uD83D\uDCCB", "#9C27B0", false),
            OrchestrationPhase.Clarifying
                => ("Clarifying", "\uD83D\uDCAC", "#FFA726", false),
            OrchestrationPhase.AwaitingApproval
                => ("Awaiting Approval", "\u23F3", "#2196F3", false),
            OrchestrationPhase.Aggregating
                => ("Aggregating", "\uD83D\uDCCA", "#00897B", false),
            OrchestrationPhase.Completed
                => ("Completed", "\u2705", "#66BB6A", false),
            OrchestrationPhase.Cancelled
                => ("Cancelled", "\u26D4", "#FF9800", false),
            _ => ("Waiting for task", "\u2699\uFE0F", "#9E9E9E", false)
        };

        TeamStatusIcon = icon;
        TeamStatusText = $"{action} \u2022 {activeWorkers}/{poolCapacity} Active";
        TeamStatusColor = color;
        TeamStatusIconRotating = rotating;
    }

    // ══════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════

    private void ClearState()
    {
        ClearError();
        ShowClarification = false;
        ShowPlanReview = false;
        ShowReport = false;
        ShowSettings = false;
        IsAwaitingApproval = false;
        OrchestratorMessage = string.Empty;
        ExecutionStatusText = string.Empty;
        IsExecutionIndicatorVisible = false;
        ClarificationResponse = string.Empty;
        PlanFeedback = string.Empty;
        InjectionText = string.Empty;
        CurrentPlan = null;
        LastReport = null;
        Workers.Clear();
        ClarifyingQuestions.Clear();
        NextStepActions.Clear();
        HasNextSteps = false;
        CurrentPhaseDisplay = "Idle";
        CurrentPhaseColor = GetPhaseColor(OrchestrationPhase.Idle);
        CurrentPhaseBadgeOpacity = 1.0;
        IsEventLogExpanded = false;

        // Reset team status to idle defaults
        UpdateTeamStatus();
    }

    private void AddEvent(string message)
    {
        EventLog.Insert(0, message);
        while (EventLog.Count > 500)
            EventLog.RemoveAt(EventLog.Count - 1);
    }

    private static bool IsConnectionError(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("JSON RPC", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Copilot service", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a local UI-level fallback summary when the LLM/aggregator summary is empty.
    /// </summary>
    private static string BuildLocalFallbackSummary(ConsolidatedReport report)
    {
        var lines = new List<string>
        {
            $"## Task Completed",
            $"**{report.Stats.SucceededChunks}/{report.Stats.TotalChunks}** chunks succeeded in **{report.Stats.TotalDuration.TotalSeconds:F1}s**."
        };

        if (report.Stats.FailedChunks > 0)
            lines.Add($"⚠ {report.Stats.FailedChunks} chunk(s) failed.");

        lines.Add("");
        foreach (var result in report.WorkerResults)
        {
            var icon = result.IsSuccess ? "✅" : "❌";
            var summary = result.IsSuccess
                ? Truncate(result.Response ?? "(no output)", 120)
                : $"Error: {result.ErrorMessage ?? "Unknown"}";
            lines.Add($"- {icon} **{result.ChunkId}**: {summary}");
        }

        // Add generic next steps for the fallback case
        lines.Add("");
        lines.Add("### Recommended Next Steps");
        lines.Add("- [ACTION:Review the results and verify correctness]");
        lines.Add("- [ACTION:Run tests to validate the changes]");

        if (report.Stats.FailedChunks > 0)
            lines.Add("- [ACTION:Investigate and fix failed chunks]");

        return string.Join("\n", lines);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    public void Dispose()
    {
        _logger.LogInformation("[AgentTeamVM] Disposing.");
        _pulseTimer.Stop();
        _pulseTimer.Tick -= OnPulseTimerTick;
        _sessionHealthTimer.Stop();
        _sessionHealthTimer.Tick -= OnSessionHealthTimerTick;
        _orchestrator.EventReceived -= OnOrchestratorEvent;
        _approvalQueue.PendingCountChanged -= OnPendingApprovalsChanged;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

// ══════════════════════════════════════════════════════════════════
// Worker Status Item — full lifecycle observable model
// ══════════════════════════════════════════════════════════════════

/// <summary>
/// Observable model representing a single worker's lifecycle state in the UI.
/// Persists across all status transitions (Pending → Running → Succeeded/Failed).
/// </summary>
public sealed class WorkerStatusItem : ObservableObject
{
    public string ChunkId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string WorkerRole { get; set; } = string.Empty;
    public int WorkerIndex { get; set; }

    private AgentStatus _status = AgentStatus.Pending;
    public AgentStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private string _statusDisplay = "⏳ Pending";
    public string StatusDisplay
    {
        get => _statusDisplay;
        set => SetProperty(ref _statusDisplay, value);
    }

    private string _statusColor = "#BDBDBD";
    public string StatusColor
    {
        get => _statusColor;
        set => SetProperty(ref _statusColor, value);
    }

    private string _activity = string.Empty;
    public string Activity
    {
        get => _activity;
        set => SetProperty(ref _activity, value);
    }

    private int _retryAttempt;
    public int RetryAttempt
    {
        get => _retryAttempt;
        set => SetProperty(ref _retryAttempt, value);
    }
}