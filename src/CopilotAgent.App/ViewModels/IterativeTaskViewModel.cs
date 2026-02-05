using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for iterative task panel with drill-down history and proper button states.
/// Shows detailed tool execution history for each iteration.
/// </summary>
public partial class IterativeTaskViewModel : ViewModelBase
{
    private readonly IIterativeTaskService _taskService;
    private readonly ILogger<IterativeTaskViewModel> _logger;
    private string? _currentSessionId;

    [ObservableProperty]
    private string _taskDescription = string.Empty;

    [ObservableProperty]
    private string _successCriteria = string.Empty;

    [ObservableProperty]
    private int _maxIterations = 10;

    [ObservableProperty]
    private IterativeTaskConfig? _currentTask;

    [ObservableProperty]
    private ObservableCollection<IterationResult> _iterations = new();

    [ObservableProperty]
    private IterationResult? _selectedIteration;

    [ObservableProperty]
    private string _statusText = "No task configured";

    [ObservableProperty]
    private string _statusColor = "#757575";

    [ObservableProperty]
    private string _currentToolText = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isIdle = true;

    [ObservableProperty]
    private bool _hasHistory;

    [ObservableProperty]
    private bool _hasTask;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private bool _showProgress;

    public IterativeTaskViewModel(
        IIterativeTaskService taskService,
        ILogger<IterativeTaskViewModel> logger)
    {
        _taskService = taskService;
        _logger = logger;

        _taskService.TaskStatusChanged += OnTaskStatusChanged;
        _taskService.IterationCompleted += OnIterationCompleted;
        _taskService.IterationProgress += OnIterationProgress;
    }

    /// <summary>
    /// Computed property for Start button enabled state.
    /// Enabled when idle and task is configured.
    /// </summary>
    public bool CanStart => IsIdle && 
                            !string.IsNullOrWhiteSpace(TaskDescription) && 
                            !string.IsNullOrWhiteSpace(SuccessCriteria);

    /// <summary>
    /// Computed property for Stop button enabled state.
    /// Enabled only when running.
    /// </summary>
    public bool CanStop => IsRunning;

    /// <summary>
    /// Computed property for Clear button enabled state.
    /// Enabled when idle and has history.
    /// </summary>
    public bool CanClear => IsIdle && HasHistory;

    public void SetSession(string sessionId)
    {
        _currentSessionId = sessionId;
        LoadTask();
    }

    private void LoadTask()
    {
        if (string.IsNullOrEmpty(_currentSessionId))
            return;

        CurrentTask = _taskService.GetTask(_currentSessionId);
        
        if (CurrentTask != null)
        {
            TaskDescription = CurrentTask.TaskDescription;
            SuccessCriteria = CurrentTask.SuccessCriteria;
            MaxIterations = CurrentTask.MaxIterations;
            
            // Load iteration history
            Iterations.Clear();
            foreach (var iteration in CurrentTask.State.Iterations)
            {
                Iterations.Add(iteration);
            }

            HasTask = true;
            HasHistory = Iterations.Count > 0;
            IsRunning = CurrentTask.State.Status == IterativeTaskStatus.Running;
            IsIdle = !IsRunning;
            ShowProgress = IsRunning;
            
            UpdateStatusFromTask();
        }
        else
        {
            HasTask = false;
            HasHistory = false;
            IsRunning = false;
            IsIdle = true;
            ShowProgress = false;
            Iterations.Clear();
            UpdateStatus(IterativeTaskStatus.NotStarted);
        }

        NotifyButtonStates();
    }

    private void OnTaskStatusChanged(object? sender, TaskStatusChangedEventArgs e)
    {
        if (e.SessionId != _currentSessionId)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            IsRunning = e.NewStatus == IterativeTaskStatus.Running;
            IsIdle = !IsRunning;
            ShowProgress = IsRunning;
            
            if (!IsRunning)
            {
                CurrentToolText = string.Empty;
            }
            
            LoadTask();
        });
    }

    private void OnIterationCompleted(object? sender, IterationCompletedEventArgs e)
    {
        if (e.SessionId != _currentSessionId)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            // Add or update the iteration in the collection
            var existing = Iterations.FirstOrDefault(i => i.IterationNumber == e.Iteration.IterationNumber);
            if (existing != null)
            {
                var index = Iterations.IndexOf(existing);
                Iterations[index] = e.Iteration;
            }
            else
            {
                Iterations.Add(e.Iteration);
            }
            
            HasHistory = Iterations.Count > 0;
            UpdateProgressFromTask();
            NotifyButtonStates();
        });
    }

    private void OnIterationProgress(object? sender, IterationProgressEventArgs e)
    {
        if (e.SessionId != _currentSessionId)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            // Update current tool being executed
            switch (e.ProgressType)
            {
                case IterationProgressType.Started:
                    CurrentToolText = $"Iteration {e.IterationNumber} started...";
                    break;
                    
                case IterationProgressType.ToolStarted:
                    CurrentToolText = $"⚡ {e.Message ?? $"Executing {e.ToolName}..."}";
                    break;
                    
                case IterationProgressType.ToolCompleted:
                    CurrentToolText = $"✓ {e.Message ?? $"Completed {e.ToolName}"}";
                    break;
                    
                case IterationProgressType.Reasoning:
                    CurrentToolText = "🤔 Agent is thinking...";
                    break;
                    
                case IterationProgressType.AssistantMessage:
                    CurrentToolText = "📝 Processing response...";
                    break;
                    
                case IterationProgressType.WaitingForApproval:
                    CurrentToolText = "⏳ Waiting for approval...";
                    break;
                    
                default:
                    if (!string.IsNullOrEmpty(e.Message))
                        CurrentToolText = e.Message;
                    break;
            }
        });
    }

    private void UpdateStatusFromTask()
    {
        if (CurrentTask == null)
        {
            UpdateStatus(IterativeTaskStatus.NotStarted);
            return;
        }

        UpdateStatus(CurrentTask.State.Status);
        UpdateProgressFromTask();
    }

    private void UpdateProgressFromTask()
    {
        if (CurrentTask == null || MaxIterations == 0)
        {
            ProgressPercent = 0;
            return;
        }

        ProgressPercent = (int)((double)CurrentTask.State.CurrentIteration / MaxIterations * 100);
    }

    private void UpdateStatus(IterativeTaskStatus status)
    {
        (StatusText, StatusColor) = status switch
        {
            IterativeTaskStatus.NotStarted => ("Ready to start", "#757575"),
            IterativeTaskStatus.Running => ($"Running iteration {CurrentTask?.State.CurrentIteration ?? 0}/{MaxIterations}...", "#1976D2"),
            IterativeTaskStatus.Completed => ("✓ Task completed successfully", "#4CAF50"),
            IterativeTaskStatus.Failed => ("✗ Task failed", "#D32F2F"),
            IterativeTaskStatus.Stopped => ("⏸ Task stopped by user", "#FF9800"),
            IterativeTaskStatus.MaxIterationsReached => ("⚠ Max iterations reached", "#FF9800"),
            _ => ("Unknown status", "#757575")
        };

        if (CurrentTask?.State.CompletionReason != null && status != IterativeTaskStatus.Running)
        {
            // For non-running states, show reason on a new line if it adds value
            if (!StatusText.Contains(CurrentTask.State.CompletionReason))
            {
                // Keep status text concise
            }
        }
    }

    private void NotifyButtonStates()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanClear));
        
        // Also notify the RelayCommands to re-evaluate their CanExecute
        StartTaskCommand.NotifyCanExecuteChanged();
        StopTaskCommand.NotifyCanExecuteChanged();
        ClearTaskCommand.NotifyCanExecuteChanged();
    }

    partial void OnTaskDescriptionChanged(string value) => NotifyButtonStates();
    partial void OnSuccessCriteriaChanged(string value) => NotifyButtonStates();
    partial void OnIsRunningChanged(bool value)
    {
        IsIdle = !value;
        NotifyButtonStates();
    }
    partial void OnIsIdleChanged(bool value) => NotifyButtonStates();
    partial void OnHasHistoryChanged(bool value) => NotifyButtonStates();

    [RelayCommand]
    private void CreateTask()
    {
        if (string.IsNullOrEmpty(_currentSessionId))
            return;

        if (string.IsNullOrWhiteSpace(TaskDescription) || string.IsNullOrWhiteSpace(SuccessCriteria))
        {
            MessageBox.Show("Please enter both task description and success criteria.", 
                "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _taskService.CreateTask(_currentSessionId, TaskDescription, SuccessCriteria, MaxIterations);
        LoadTask();
        _logger.LogInformation("Created task for session {SessionId}", _currentSessionId);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartTaskAsync()
    {
        if (string.IsNullOrEmpty(_currentSessionId))
            return;

        if (CurrentTask == null)
        {
            CreateTask();
        }

        IsRunning = true;
        IsIdle = false;
        ShowProgress = true;
        CurrentToolText = "Initializing...";
        NotifyButtonStates();

        try
        {
            await _taskService.StartTaskAsync(_currentSessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start task");
            MessageBox.Show($"Failed to start task: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsRunning = false;
            IsIdle = true;
            ShowProgress = false;
            CurrentToolText = string.Empty;
            NotifyButtonStates();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopTaskAsync()
    {
        if (string.IsNullOrEmpty(_currentSessionId))
            return;

        CurrentToolText = "Stopping...";
        
        try
        {
            await _taskService.StopTaskAsync(_currentSessionId);
            _logger.LogInformation("Stopped task for session {SessionId}", _currentSessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping task");
            MessageBox.Show($"Error stopping task: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsRunning = false;
            IsIdle = true;
            ShowProgress = false;
            CurrentToolText = string.Empty;
            NotifyButtonStates();
        }
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void ClearTask()
    {
        if (string.IsNullOrEmpty(_currentSessionId))
            return;

        var result = MessageBox.Show(
            "Clear this task and all iteration history?\n\nThis will remove all recorded tool executions and results.", 
            "Confirm Clear", 
            MessageBoxButton.YesNo, 
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _taskService.ClearTask(_currentSessionId);
            TaskDescription = string.Empty;
            SuccessCriteria = string.Empty;
            MaxIterations = 10;
            Iterations.Clear();
            CurrentTask = null;
            HasTask = false;
            HasHistory = false;
            SelectedIteration = null;
            CurrentToolText = string.Empty;
            UpdateStatus(IterativeTaskStatus.NotStarted);
            NotifyButtonStates();
            _logger.LogInformation("Cleared task for session {SessionId}", _currentSessionId);
        }
    }
}