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
/// Event arguments for iteration progress updates (tool execution, reasoning, etc.)
/// </summary>
public class IterationProgressEventArgs : EventArgs
{
    public string SessionId { get; }
    public int IterationNumber { get; }
    public IterationProgressType ProgressType { get; }
    public string? ToolName { get; }
    public string? ToolCallId { get; }
    public string? Message { get; }

    public IterationProgressEventArgs(
        string sessionId,
        int iterationNumber,
        IterationProgressType progressType,
        string? toolName = null,
        string? toolCallId = null,
        string? message = null)
    {
        SessionId = sessionId;
        IterationNumber = iterationNumber;
        ProgressType = progressType;
        ToolName = toolName;
        ToolCallId = toolCallId;
        Message = message;
    }
}

/// <summary>
/// Types of progress updates during an iteration
/// </summary>
public enum IterationProgressType
{
    /// <summary>Iteration has started</summary>
    Started,
    
    /// <summary>A tool is starting execution</summary>
    ToolStarted,
    
    /// <summary>A tool has completed</summary>
    ToolCompleted,
    
    /// <summary>Agent is reasoning/thinking</summary>
    Reasoning,
    
    /// <summary>Assistant message received</summary>
    AssistantMessage,
    
    /// <summary>Iteration is waiting for approval</summary>
    WaitingForApproval,
    
    /// <summary>General progress message</summary>
    Progress
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
    /// Raised when there's progress during an iteration (tool execution, reasoning, etc.)
    /// </summary>
    event EventHandler<IterationProgressEventArgs>? IterationProgress;

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
    /// Stops a running task gracefully (uses cancellation token and SDK abort)
    /// </summary>
    Task StopTaskAsync(string sessionId);

    /// <summary>
    /// Clears a task from a session (removes all history)
    /// </summary>
    void ClearTask(string sessionId);

    /// <summary>
    /// Gets all active tasks
    /// </summary>
    IReadOnlyDictionary<string, IterativeTaskConfig> GetAllTasks();

    /// <summary>
    /// Checks if a task is currently running for the session
    /// </summary>
    bool IsTaskRunning(string sessionId);
}