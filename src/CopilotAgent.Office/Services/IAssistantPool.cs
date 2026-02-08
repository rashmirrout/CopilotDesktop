using CopilotAgent.Office.Events;
using CopilotAgent.Office.Models;

namespace CopilotAgent.Office.Services;

/// <summary>
/// SemaphoreSlim-gated concurrent execution pool for assistant tasks.
/// </summary>
public interface IAssistantPool
{
    /// <summary>Execute a batch of tasks with concurrency gating.</summary>
    /// <param name="tasks">Tasks to execute, sorted by priority.</param>
    /// <param name="config">Office configuration for timeouts and limits.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Results for all tasks.</returns>
    Task<IReadOnlyList<AssistantResult>> ExecuteTasksAsync(
        IReadOnlyList<AssistantTask> tasks,
        OfficeConfig config,
        CancellationToken ct = default);

    /// <summary>Cancel all currently running and queued tasks.</summary>
    Task CancelAllAsync();

    /// <summary>Raised for assistant lifecycle events.</summary>
    event Action<AssistantEvent>? OnAssistantEvent;

    /// <summary>Raised for scheduling decisions.</summary>
    event Action<SchedulingEvent>? OnSchedulingEvent;
}