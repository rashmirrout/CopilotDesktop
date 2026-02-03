using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for iterative task panel
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
    private string _statusText = "No task configured";

    [ObservableProperty]
    private string _statusColor = "#757575";

    [ObservableProperty]
    private bool _canStart;

    [ObservableProperty]
    private bool _canStop;

    [ObservableProperty]
    private bool _canClear;

    [ObservableProperty]
    private bool _hasTask;

    [ObservableProperty]
    private int _progressPercent;

    public IterativeTaskViewModel(
        IIterativeTaskService taskService,
        ILogger<IterativeTaskViewModel> logger)
    {
        _taskService = taskService;
        _logger = logger;

        _taskService.TaskStatusChanged += OnTaskStatusChanged;
        _taskService.IterationCompleted += OnIterationCompleted;
    }

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
            
            Iterations.Clear();
            foreach (var iteration in CurrentTask.State.Iterations)
            {
                Iterations.Add(iteration);
            }

            HasTask = true;
            UpdateStatusFromTask();
        }
        else
        {
            HasTask = false;
            Iterations.Clear();
            UpdateStatus(IterativeTaskStatus.NotStarted);
        }

        UpdateCommandStates();
    }

    private void OnTaskStatusChanged(object? sender, TaskStatusChangedEventArgs e)
    {
        if (e.SessionId != _currentSessionId)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            LoadTask();
        });
    }

    private void OnIterationCompleted(object? sender, IterationCompletedEventArgs e)
    {
        if (e.SessionId != _currentSessionId)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            Iterations.Add(e.Iteration);
            UpdateProgressFromTask();
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
            IterativeTaskStatus.NotStarted => ("Not Started", "#757575"),
            IterativeTaskStatus.Running => ($"Running... Iteration {CurrentTask?.State.CurrentIteration ?? 0}/{MaxIterations}", "#1976D2"),
            IterativeTaskStatus.Completed => ("✓ Completed", "#4CAF50"),
            IterativeTaskStatus.Failed => ("✗ Failed", "#D32F2F"),
            IterativeTaskStatus.Stopped => ("⏸ Stopped", "#FF9800"),
            IterativeTaskStatus.MaxIterationsReached => ("⚠ Max Iterations Reached", "#FF9800"),
            _ => ("Unknown", "#757575")
        };

        if (CurrentTask?.State.CompletionReason != null && status != IterativeTaskStatus.Running)
        {
            StatusText += $": {CurrentTask.State.CompletionReason}";
        }
    }

    private void UpdateCommandStates()
    {
        var status = CurrentTask?.State.Status ?? IterativeTaskStatus.NotStarted;
        
        CanStart = !string.IsNullOrWhiteSpace(TaskDescription) && 
                   !string.IsNullOrWhiteSpace(SuccessCriteria) &&
                   status != IterativeTaskStatus.Running;
        
        CanStop = status == IterativeTaskStatus.Running;
        
        CanClear = HasTask && status != IterativeTaskStatus.Running;
    }

    partial void OnTaskDescriptionChanged(string value) => UpdateCommandStates();
    partial void OnSuccessCriteriaChanged(string value) => UpdateCommandStates();

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

    [RelayCommand]
    private async Task StartTaskAsync()
    {
        if (string.IsNullOrEmpty(_currentSessionId))
            return;

        if (CurrentTask == null)
        {
            CreateTask();
        }

        IsBusy = true;
        UpdateCommandStates();

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
            IsBusy = false;
            UpdateCommandStates();
        }
    }

    [RelayCommand]
    private void StopTask()
    {
        if (string.IsNullOrEmpty(_currentSessionId))
            return;

        _taskService.StopTask(_currentSessionId);
        _logger.LogInformation("Stopped task for session {SessionId}", _currentSessionId);
    }

    [RelayCommand]
    private void ClearTask()
    {
        if (string.IsNullOrEmpty(_currentSessionId))
            return;

        var result = MessageBox.Show("Clear this task and all iteration history?", 
            "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _taskService.ClearTask(_currentSessionId);
            TaskDescription = string.Empty;
            SuccessCriteria = string.Empty;
            MaxIterations = 10;
            Iterations.Clear();
            CurrentTask = null;
            HasTask = false;
            UpdateStatus(IterativeTaskStatus.NotStarted);
            UpdateCommandStates();
            _logger.LogInformation("Cleared task for session {SessionId}", _currentSessionId);
        }
    }
}