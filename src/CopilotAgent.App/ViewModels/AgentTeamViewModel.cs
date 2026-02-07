using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly ISessionManager _sessionManager;
    private readonly IApprovalQueue _approvalQueue;
    private readonly AppSettings _appSettings;
    private readonly ILogger<AgentTeamViewModel> _logger;
    private readonly Dispatcher _dispatcher;

    // Animation infrastructure
    private readonly DispatcherTimer _pulseTimer;
    private CancellationTokenSource? _cts;
    private int _animationDotCount;
    private DateTime _executionStartTime;
    private bool _pulseToggle;

    public AgentTeamViewModel(
        IOrchestratorService orchestrator,
        ISessionManager sessionManager,
        IApprovalQueue approvalQueue,
        AppSettings appSettings,
        ILogger<AgentTeamViewModel> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
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

        _logger.LogInformation("[AgentTeamVM] ViewModel initialized.");
    }

    // ══════════════════════════════════════════════════════════════
    // Observable Properties
    // ══════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitTaskCommand))]
    private string _taskPrompt = string.Empty;

    [ObservableProperty]
    private string _currentPhaseDisplay = "Idle";

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

    // ── Session health indicator ────────────────────────────────

    /// <summary>"LIVE" or "IDLE" or "ERROR" etc.</summary>
    [ObservableProperty]
    private string _sessionIndicatorText = "IDLE";

    /// <summary>Brush color for the indicator dot (green=live, gray=idle, red=error).</summary>
    [ObservableProperty]
    private string _sessionIndicatorColor = "#9E9E9E";

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
        UpdateSessionIndicator("LIVE", "#4CAF50");
        _cts = new CancellationTokenSource();

        try
        {
            var config = BuildConfig();
            _logger.LogDebug("[AgentTeamVM] Config built: workers={Workers}, strategy={Strategy}, timeout={Timeout}min",
                config.MaxParallelSessions, config.WorkspaceStrategy, config.WorkerTimeout.TotalMinutes);

            var response = await _orchestrator.SubmitTaskAsync(prompt, config, _cts.Token);
            _logger.LogInformation("[AgentTeamVM] SubmitTask response: phase={Phase}", response.Phase);
            HandleOrchestratorResponse(response);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[AgentTeamVM] Task cancelled by user.");
            AddEvent("🛑 Task cancelled.");
            StopExecutionAnimation();
            UpdateSessionIndicator("IDLE", "#9E9E9E");
            IsOrchestrating = false;
        }
        catch (TimeoutException tex)
        {
            _logger.LogWarning(tex, "[AgentTeamVM] Orchestrator LLM call timed out.");
            StopExecutionAnimation();
            AddEvent("⏱ Operation timed out. The task may be too complex or the connection was lost.");
            SetError($"Timeout: {tex.Message}");
            UpdateSessionIndicator("TIMEOUT", "#FF9800");
            IsOrchestrating = false;
        }
        catch (InvalidOperationException iex) when (IsConnectionError(iex))
        {
            _logger.LogWarning(iex, "[AgentTeamVM] Connection lost to Copilot service.");
            StopExecutionAnimation();
            AddEvent("⚠ Lost connection to Copilot service. Please reset and try again.");
            SetError($"Connection lost: {iex.Message}");
            UpdateSessionIndicator("DISCONNECTED", "#F44336");
            IsOrchestrating = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AgentTeamVM] Unexpected error during task submission.");
            StopExecutionAnimation();
            SetError($"Error: {ex.Message}");
            UpdateSessionIndicator("ERROR", "#F44336");
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
            UpdateSessionIndicator("CANCELLED", "#FF9800");
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
        UpdateSessionIndicator("IDLE", "#9E9E9E");
        AddEvent("🔄 Orchestrator reset.");
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        ShowSettings = !ShowSettings;
        _logger.LogDebug("[AgentTeamVM] Settings panel toggled: {Visible}", ShowSettings);
    }

    [RelayCommand]
    private void DismissReport()
    {
        ShowReport = false;
    }

    // ══════════════════════════════════════════════════════════════
    // Event Handlers
    // ══════════════════════════════════════════════════════════════

    private void OnOrchestratorEvent(object? sender, OrchestratorEvent e)
    {
        _dispatcher.InvokeAsync(() =>
        {
            var phase = _orchestrator.CurrentPhase;
            CurrentPhaseDisplay = phase.ToString();

            _logger.LogDebug("[AgentTeamVM] Event: type={EventType}, msg={Message}",
                e.EventType, Truncate(e.Message, 150));

            AddEvent($"[{e.TimestampUtc:HH:mm:ss}] {e.EventType}: {e.Message}");

            if (e is WorkerProgressEvent workerEvent)
            {
                UpdateWorkerStatus(workerEvent);
            }
            else if (e is OrchestrationCompletedEvent completedEvent)
            {
                _logger.LogInformation("[AgentTeamVM] Orchestration completed. Report={HasReport}",
                    completedEvent.Report != null);

                StopExecutionAnimation();
                UpdateSessionIndicator("COMPLETED", "#4CAF50");
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
        OrchestratorMessage = response.Message ?? string.Empty;

        ShowClarification = false;
        ShowPlanReview = false;
        ShowReport = false;

        switch (response.Phase)
        {
            case OrchestrationPhase.Clarifying:
                StopExecutionAnimation();
                UpdateSessionIndicator("LIVE", "#4CAF50");
                ShowClarification = true;
                ClarifyingQuestions.Clear();
                foreach (var q in response.ClarifyingQuestions ?? Enumerable.Empty<string>())
                    ClarifyingQuestions.Add(q);
                _logger.LogDebug("[AgentTeamVM] Clarification phase: {Count} questions", ClarifyingQuestions.Count);
                break;

            case OrchestrationPhase.AwaitingApproval:
                StopExecutionAnimation();
                UpdateSessionIndicator("LIVE", "#4CAF50");
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
                UpdateSessionIndicator("LIVE", "#4CAF50");
                StartExecutionAnimation();
                break;

            case OrchestrationPhase.Completed:
                StopExecutionAnimation();
                UpdateSessionIndicator("COMPLETED", "#4CAF50");
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
                break;

            case OrchestrationPhase.Cancelled:
                StopExecutionAnimation();
                UpdateSessionIndicator("CANCELLED", "#FF9800");
                IsOrchestrating = false;
                break;

            case OrchestrationPhase.Idle:
                StopExecutionAnimation();
                UpdateSessionIndicator("IDLE", "#9E9E9E");
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

    // ══════════════════════════════════════════════════════════════
    // Session Indicator
    // ══════════════════════════════════════════════════════════════

    private void UpdateSessionIndicator(string text, string color)
    {
        SessionIndicatorText = text;
        SessionIndicatorColor = color;
        _logger.LogDebug("[AgentTeamVM] Session indicator: [{Text}] color={Color}", text, color);
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
        CurrentPhaseDisplay = "Idle";
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

        return string.Join("\n", lines);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    public void Dispose()
    {
        _logger.LogInformation("[AgentTeamVM] Disposing.");
        _pulseTimer.Stop();
        _pulseTimer.Tick -= OnPulseTimerTick;
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