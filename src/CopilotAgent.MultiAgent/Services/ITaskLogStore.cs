namespace CopilotAgent.MultiAgent.Services;

using CopilotAgent.MultiAgent.Models;

/// <summary>
/// Persistent structured log store for orchestration observability.
/// Supports per-plan and per-chunk log retrieval, timeline replay,
/// and JSON export for debugging and sharing.
/// </summary>
public interface ITaskLogStore
{
    /// <summary>
    /// Save a log entry associated with a plan and optionally a chunk.
    /// Thread-safe — concurrent writes from parallel workers are serialized.
    /// </summary>
    /// <param name="planId">The plan this log entry belongs to.</param>
    /// <param name="chunkId">The chunk this log entry belongs to, or null for plan-level entries.</param>
    /// <param name="entry">The structured log entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveLogEntryAsync(
        string planId,
        string? chunkId,
        LogEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all log entries for a specific chunk, ordered by timestamp.
    /// </summary>
    /// <param name="chunkId">The chunk identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ordered list of log entries for the chunk.</returns>
    Task<List<LogEntry>> GetChunkLogsAsync(
        string chunkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all log entries for a plan (all chunks + orchestrator), ordered by timestamp.
    /// </summary>
    /// <param name="planId">The plan identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ordered list of all log entries for the plan.</returns>
    Task<List<LogEntry>> GetPlanLogsAsync(
        string planId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get log entries filtered by minimum severity level.
    /// </summary>
    /// <param name="planId">The plan identifier.</param>
    /// <param name="minLevel">Minimum log level to include.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Filtered and ordered list of log entries.</returns>
    Task<List<LogEntry>> GetLogsByLevelAsync(
        string planId,
        OrchestrationLogLevel minLevel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Export all logs for a plan as a JSON string for debugging and sharing.
    /// </summary>
    /// <param name="planId">The plan identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JSON string containing all plan logs.</returns>
    Task<string> ExportPlanLogsAsJsonAsync(
        string planId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a timeline view of events for replay, ordered chronologically.
    /// </summary>
    /// <param name="planId">The plan identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Chronologically ordered list of log entries.</returns>
    Task<List<LogEntry>> GetTimelineAsync(
        string planId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prune logs older than the specified age to manage disk usage.
    /// </summary>
    /// <param name="maxAge">Maximum age of logs to retain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PruneLogsAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default);
}