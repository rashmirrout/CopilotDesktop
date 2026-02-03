using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Configuration for iterative agent task execution
/// </summary>
public class IterativeTaskConfig
{
    /// <summary>Task description</summary>
    [JsonPropertyName("taskDescription")]
    public string TaskDescription { get; set; } = string.Empty;

    /// <summary>Success criteria</summary>
    [JsonPropertyName("successCriteria")]
    public string SuccessCriteria { get; set; } = string.Empty;

    /// <summary>Maximum number of iterations</summary>
    [JsonPropertyName("maxIterations")]
    public int MaxIterations { get; set; } = 10;

    /// <summary>Current state of the task</summary>
    [JsonPropertyName("state")]
    public IterativeTaskState State { get; set; } = new();

    /// <summary>When the task was started</summary>
    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    /// <summary>When the task completed or was stopped</summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// State tracking for an iterative task
/// </summary>
public class IterativeTaskState
{
    /// <summary>Current iteration number (0-based)</summary>
    [JsonPropertyName("currentIteration")]
    public int CurrentIteration { get; set; }

    /// <summary>Overall status of the task</summary>
    [JsonPropertyName("status")]
    public IterativeTaskStatus Status { get; set; } = IterativeTaskStatus.NotStarted;

    /// <summary>History of iteration results</summary>
    [JsonPropertyName("iterations")]
    public List<IterationResult> Iterations { get; set; } = new();

    /// <summary>Reason for completion or stoppage</summary>
    [JsonPropertyName("completionReason")]
    public string? CompletionReason { get; set; }
}

/// <summary>
/// Result of a single iteration in a task
/// </summary>
public class IterationResult
{
    /// <summary>Iteration number</summary>
    [JsonPropertyName("iterationNumber")]
    public int IterationNumber { get; set; }

    /// <summary>What action was taken</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>Result of the action</summary>
    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    /// <summary>Self-evaluation: Is task complete?</summary>
    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; set; }

    /// <summary>Explanation of evaluation</summary>
    [JsonPropertyName("evaluation")]
    public string Evaluation { get; set; } = string.Empty;

    /// <summary>When this iteration started</summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this iteration completed</summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>Duration in milliseconds</summary>
    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; set; }
}

/// <summary>
/// Status of an iterative task
/// </summary>
public enum IterativeTaskStatus
{
    /// <summary>Task has not started</summary>
    NotStarted,
    
    /// <summary>Task is currently running</summary>
    Running,
    
    /// <summary>Task completed successfully</summary>
    Completed,
    
    /// <summary>Task failed</summary>
    Failed,
    
    /// <summary>Task was stopped by user</summary>
    Stopped,
    
    /// <summary>Task reached max iterations without completion</summary>
    MaxIterationsReached
}