using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Service for managing iterative agent tasks with state machine
/// </summary>
public class IterativeTaskService : IIterativeTaskService
{
    private readonly ICopilotService _copilotService;
    private readonly ILogger<IterativeTaskService> _logger;
    private readonly ConcurrentDictionary<string, IterativeTaskConfig> _tasks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();

    public event EventHandler<TaskStatusChangedEventArgs>? TaskStatusChanged;
    public event EventHandler<IterationCompletedEventArgs>? IterationCompleted;

    public IterativeTaskService(
        ICopilotService copilotService,
        ILogger<IterativeTaskService> logger)
    {
        _copilotService = copilotService;
        _logger = logger;
    }

    public IterativeTaskConfig? GetTask(string sessionId)
    {
        _tasks.TryGetValue(sessionId, out var task);
        return task;
    }

    public IterativeTaskConfig CreateTask(string sessionId, string taskDescription, string successCriteria, int maxIterations = 10)
    {
        var task = new IterativeTaskConfig
        {
            TaskDescription = taskDescription,
            SuccessCriteria = successCriteria,
            MaxIterations = maxIterations,
            State = new IterativeTaskState
            {
                Status = IterativeTaskStatus.NotStarted,
                CurrentIteration = 0,
                Iterations = new List<IterationResult>()
            }
        };

        _tasks[sessionId] = task;
        _logger.LogInformation("Created iterative task for session {SessionId}: {Description}", sessionId, taskDescription);
        
        return task;
    }

    public async Task StartTaskAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(sessionId, out var task))
        {
            _logger.LogWarning("No task found for session {SessionId}", sessionId);
            return;
        }

        if (task.State.Status == IterativeTaskStatus.Running)
        {
            _logger.LogWarning("Task already running for session {SessionId}", sessionId);
            return;
        }

        // Create linked cancellation token
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokens[sessionId] = cts;

        var oldStatus = task.State.Status;
        task.State.Status = IterativeTaskStatus.Running;
        task.StartedAt = DateTime.UtcNow;

        TaskStatusChanged?.Invoke(this, new TaskStatusChangedEventArgs(sessionId, oldStatus, IterativeTaskStatus.Running));

        _logger.LogInformation("Starting iterative task for session {SessionId}", sessionId);

        try
        {
            await RunIterativeLoopAsync(sessionId, task, cts.Token);
        }
        catch (OperationCanceledException)
        {
            UpdateTaskStatus(sessionId, task, IterativeTaskStatus.Stopped, "Task stopped by user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Iterative task failed for session {SessionId}", sessionId);
            UpdateTaskStatus(sessionId, task, IterativeTaskStatus.Failed, ex.Message);
        }
        finally
        {
            _cancellationTokens.TryRemove(sessionId, out _);
        }
    }

    private async Task RunIterativeLoopAsync(string sessionId, IterativeTaskConfig task, CancellationToken cancellationToken)
    {
        while (task.State.CurrentIteration < task.MaxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var iterationNumber = task.State.CurrentIteration + 1;
            _logger.LogInformation("Starting iteration {Iteration}/{Max} for session {SessionId}", 
                iterationNumber, task.MaxIterations, sessionId);

            var iteration = new IterationResult
            {
                IterationNumber = iterationNumber,
                StartedAt = DateTime.UtcNow
            };

            try
            {
                // Build the prompt for this iteration
                var prompt = BuildIterationPrompt(task, iterationNumber);

                // Simulate getting response from Copilot (in real implementation, this would call the actual service)
                // For now, we simulate the iteration
                await Task.Delay(1000, cancellationToken); // Simulate processing time

                iteration.Action = $"Iteration {iterationNumber}: Analyzing task and taking action";
                iteration.Result = $"Completed step {iterationNumber} of the task";
                
                // Evaluate if task is complete
                var (isComplete, evaluation) = await EvaluateCompletionAsync(task, iteration, cancellationToken);
                iteration.IsComplete = isComplete;
                iteration.Evaluation = evaluation;
                iteration.CompletedAt = DateTime.UtcNow;
                iteration.DurationMs = (long)(iteration.CompletedAt.Value - iteration.StartedAt).TotalMilliseconds;

                task.State.Iterations.Add(iteration);
                task.State.CurrentIteration = iterationNumber;

                IterationCompleted?.Invoke(this, new IterationCompletedEventArgs(sessionId, iteration));

                if (isComplete)
                {
                    UpdateTaskStatus(sessionId, task, IterativeTaskStatus.Completed, "Task completed successfully");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                iteration.Result = $"Error: {ex.Message}";
                iteration.Evaluation = "Iteration failed due to error";
                iteration.CompletedAt = DateTime.UtcNow;
                task.State.Iterations.Add(iteration);
                
                _logger.LogError(ex, "Iteration {Iteration} failed for session {SessionId}", iterationNumber, sessionId);
                // Continue to next iteration unless it's a critical error
            }
        }

        // Max iterations reached
        UpdateTaskStatus(sessionId, task, IterativeTaskStatus.MaxIterationsReached, 
            $"Reached maximum of {task.MaxIterations} iterations without completing");
    }

    private string BuildIterationPrompt(IterativeTaskConfig task, int iterationNumber)
    {
        var previousIterations = string.Join("\n", task.State.Iterations.Select(i => 
            $"- Iteration {i.IterationNumber}: {i.Action} -> {i.Result}"));

        return $@"You are working on an iterative task.

**Task Description:** {task.TaskDescription}

**Success Criteria:** {task.SuccessCriteria}

**Current Iteration:** {iterationNumber} of {task.MaxIterations}

**Previous Iterations:**
{(string.IsNullOrEmpty(previousIterations) ? "None" : previousIterations)}

Please take the next step to accomplish the task. After your action, evaluate whether the success criteria has been met.

Respond with:
1. ACTION: What action you are taking
2. RESULT: The outcome of your action
3. COMPLETE: Yes/No - whether the success criteria is now met
4. EVALUATION: Brief explanation of your evaluation";
    }

    private async Task<(bool isComplete, string evaluation)> EvaluateCompletionAsync(
        IterativeTaskConfig task, 
        IterationResult iteration, 
        CancellationToken cancellationToken)
    {
        // In a real implementation, this would use the AI to evaluate
        // For now, simulate evaluation based on iteration count
        await Task.Delay(100, cancellationToken);

        // Simple simulation: complete after 3 iterations or based on task description
        if (task.State.CurrentIteration >= 2)
        {
            return (true, "Task objectives appear to be met based on the actions taken.");
        }

        return (false, "More work needed to meet success criteria.");
    }

    private void UpdateTaskStatus(string sessionId, IterativeTaskConfig task, IterativeTaskStatus newStatus, string reason)
    {
        var oldStatus = task.State.Status;
        task.State.Status = newStatus;
        task.State.CompletionReason = reason;
        task.CompletedAt = DateTime.UtcNow;

        _logger.LogInformation("Task status changed for session {SessionId}: {OldStatus} -> {NewStatus} ({Reason})", 
            sessionId, oldStatus, newStatus, reason);

        TaskStatusChanged?.Invoke(this, new TaskStatusChangedEventArgs(sessionId, oldStatus, newStatus, reason));
    }

    public void StopTask(string sessionId)
    {
        if (_cancellationTokens.TryGetValue(sessionId, out var cts))
        {
            _logger.LogInformation("Stopping task for session {SessionId}", sessionId);
            cts.Cancel();
        }
    }

    public void ClearTask(string sessionId)
    {
        StopTask(sessionId);
        _tasks.TryRemove(sessionId, out _);
        _logger.LogInformation("Cleared task for session {SessionId}", sessionId);
    }

    public IReadOnlyDictionary<string, IterativeTaskConfig> GetAllTasks()
    {
        return _tasks;
    }
}