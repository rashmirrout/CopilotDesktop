using CopilotAgent.Core.Services;
using CopilotAgent.Office.Events;
using CopilotAgent.Office.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Office.Services;

/// <summary>
/// SemaphoreSlim-gated concurrent execution pool for assistant tasks.
/// Sorts tasks by priority, dispatches through semaphore gate, and collects results.
/// </summary>
public sealed class AssistantPool : IAssistantPool
{
    private readonly ICopilotService _copilotService;
    private readonly ILogger<AssistantPool> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<CancellationTokenSource> _activeCts = [];
    private readonly object _ctsLock = new();
    private int _nextAssistantIndex;

    /// <inheritdoc />
    public event Action<AssistantEvent>? OnAssistantEvent;

    /// <inheritdoc />
    public event Action<SchedulingEvent>? OnSchedulingEvent;

    public AssistantPool(
        ICopilotService copilotService,
        ILogger<AssistantPool> logger,
        ILoggerFactory loggerFactory)
    {
        _copilotService = copilotService;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AssistantResult>> ExecuteTasksAsync(
        IReadOnlyList<AssistantTask> tasks,
        OfficeConfig config,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(config);

        if (tasks.Count == 0)
        {
            return [];
        }

        var maxConcurrency = Math.Max(1, config.MaxAssistants);
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        // Sort by priority (lower = higher priority)
        var sortedTasks = tasks.OrderBy(t => t.Priority).ToList();

        _logger.LogInformation(
            "Executing {Count} tasks with pool size {PoolSize}",
            sortedTasks.Count, maxConcurrency);

        var resultTasks = new List<Task<AssistantResult>>(sortedTasks.Count);

        foreach (var task in sortedTasks)
        {
            ct.ThrowIfCancellationRequested();

            var taskCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lock (_ctsLock)
            {
                _activeCts.Add(taskCts);
            }

            // Raise scheduling decision
            RaiseSchedulingEvent(task, SchedulingAction.Dispatched,
                $"Task dispatched (priority={task.Priority})", task.IterationNumber);

            resultTasks.Add(ExecuteWithSemaphoreAsync(task, config, semaphore, taskCts, ct));
        }

        var results = await Task.WhenAll(resultTasks).ConfigureAwait(false);

        // Cleanup CTS list
        lock (_ctsLock)
        {
            foreach (var cts in _activeCts)
            {
                cts.Dispose();
            }
            _activeCts.Clear();
        }

        _logger.LogInformation(
            "All {Count} tasks completed: {Succeeded} succeeded, {Failed} failed",
            results.Length,
            results.Count(r => r.Success),
            results.Count(r => !r.Success));

        return results;
    }

    /// <inheritdoc />
    public Task CancelAllAsync()
    {
        lock (_ctsLock)
        {
            _logger.LogWarning("Cancelling all {Count} active tasks", _activeCts.Count);
            foreach (var cts in _activeCts)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, ignore
                }
            }
        }

        return Task.CompletedTask;
    }

    private async Task<AssistantResult> ExecuteWithSemaphoreAsync(
        AssistantTask task,
        OfficeConfig config,
        SemaphoreSlim semaphore,
        CancellationTokenSource taskCts,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var assistantIndex = Interlocked.Increment(ref _nextAssistantIndex) - 1;
            task.AssistantIndex = assistantIndex;
            task.Status = AssistantTaskStatus.Running;
            task.StartedAt = DateTimeOffset.UtcNow;

            // Raise started event
            RaiseAssistantEvent(task, null, assistantIndex, AssistantTaskStatus.Running,
                $"Assistant[{assistantIndex}] started: {task.Title}");

            var agent = new AssistantAgent(
                _copilotService,
                assistantIndex,
                _loggerFactory.CreateLogger<AssistantAgent>());

            // Wire up progress events as commentary
            agent.OnProgress += chunk =>
            {
                RaiseAssistantEvent(task, null, assistantIndex, AssistantTaskStatus.Running,
                    $"Assistant[{assistantIndex}] progress on: {task.Title}");
            };

            var result = await agent.ExecuteAsync(task, config, taskCts.Token).ConfigureAwait(false);

            // Update task status
            task.Status = result.Success ? AssistantTaskStatus.Completed : AssistantTaskStatus.Failed;
            task.CompletedAt = DateTimeOffset.UtcNow;
            task.ErrorMessage = result.ErrorMessage;

            // Raise completed/failed event
            RaiseAssistantEvent(task, result, assistantIndex, task.Status,
                result.Success
                    ? $"Assistant[{assistantIndex}] completed: {task.Title} ({result.Duration.TotalSeconds:F1}s)"
                    : $"Assistant[{assistantIndex}] failed: {task.Title} — {result.ErrorMessage}");

            return result;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private void RaiseAssistantEvent(
        AssistantTask task,
        AssistantResult? result,
        int assistantIndex,
        AssistantTaskStatus status,
        string description)
    {
        try
        {
            OnAssistantEvent?.Invoke(new AssistantEvent
            {
                Task = task,
                Result = result,
                AssistantIndex = assistantIndex,
                Status = status,
                IterationNumber = task.IterationNumber,
                Description = description
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in assistant event handler");
        }
    }

    private void RaiseSchedulingEvent(
        AssistantTask task,
        SchedulingAction action,
        string reason,
        int iterationNumber)
    {
        try
        {
            OnSchedulingEvent?.Invoke(new SchedulingEvent
            {
                Decision = new SchedulingDecision
                {
                    TaskId = task.Id,
                    TaskTitle = task.Title,
                    Action = action,
                    Reason = reason
                },
                IterationNumber = iterationNumber,
                Description = $"Scheduling: {action} — {task.Title}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in scheduling event handler");
        }
    }
}