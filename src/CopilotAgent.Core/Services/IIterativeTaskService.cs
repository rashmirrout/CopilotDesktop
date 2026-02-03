using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Event arguments for task status changes
/// </summary>
public class TaskStatusChangedEventArgs : EventArgs
{
    public string SessionId { get; }
    public IterativeTaskStatus OldStatus { get; }
    public IterativeTaskStatus NewStatus { get; }
    public string? Reason { get; }

    public TaskStatusChangedEventArgs(string sessionId, IterativeTaskStatus oldStatus, IterativeTaskStatus newStatus, string? reason = null)
    {
        SessionId = sessionId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        Reason = reason;
    }
}

/// <summary>
/// Event arguments for iteration completion
/// </summary>
public class IterationCompletedEventArgs : EventArgs
{
    public string SessionId { get; }
    public IterationResult Iteration { get; }

    public IterationCompletedEventArgs(string sessionId, IterationResult iteration)
    {
        SessionId = sessionId;
        Iteration = iteration;
    }
}

/// <summary>
/// Service for managing iterative agent tasks
/// </summary>
public interface IIterativeTaskService
{
    /// <summary>
    /// Raised when a task's status changes
    /// </summary>
    event EventHandler<TaskStatusChangedEventArgs>? TaskStatusChanged;

    /// <summary>
    /// Raised when an iteration completes
    /// </summary>
    event EventHandler<IterationCompletedEventArgs>? IterationCompleted;

    /// <summary>
    /// Gets the current task for a session, if any
    /// </summary>
    IterativeTaskConfig? GetTask(string sessionId);

    /// <summary>
    /// Creates a new iterative task for a session
    /// </summary>
    IterativeTaskConfig CreateTask(string sessionId, string taskDescription, string successCriteria, int maxIterations = 10);

    /// <summary>
    /// Starts or resumes a task
    /// </summary>
    Task StartTaskAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a running task
    /// </summary>
    void StopTask(string sessionId);

    /// <summary>
    /// Clears a task from a session
    /// </summary>
    void ClearTask(string sessionId);

    /// <summary>
    /// Gets all active tasks
    /// </summary>
    IReadOnlyDictionary<string, IterativeTaskConfig> GetAllTasks();
}