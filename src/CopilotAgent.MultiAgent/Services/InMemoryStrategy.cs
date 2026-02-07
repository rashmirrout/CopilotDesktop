using CopilotAgent.MultiAgent.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// In-memory workspace strategy for read-only analysis tasks.
/// All workers share the same working directory with no isolation.
/// Suitable for code analysis, diagnostics, and reporting tasks that don't modify files.
/// </summary>
public sealed class InMemoryStrategy : IWorkspaceStrategy
{
    private readonly ILogger<InMemoryStrategy> _logger;

    public InMemoryStrategy(ILogger<InMemoryStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public WorkspaceStrategyType StrategyType => WorkspaceStrategyType.InMemory;

    public Task<string> PrepareWorkspaceAsync(
        WorkChunk chunk,
        string baseWorkingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseWorkingDirectory);

        _logger.LogDebug(
            "InMemory workspace for chunk {ChunkId} ({Title}): using shared directory {Directory}",
            chunk.ChunkId, chunk.Title, baseWorkingDirectory);

        // All workers share the same directory — no isolation needed for read-only tasks
        return Task.FromResult(baseWorkingDirectory);
    }

    public Task CleanupWorkspaceAsync(
        string workspacePath,
        WorkChunk chunk,
        CancellationToken cancellationToken = default)
    {
        // No-op: nothing to clean up since no workspace was created
        _logger.LogDebug(
            "InMemory cleanup (no-op) for chunk {ChunkId} at {Path}",
            chunk?.ChunkId ?? "unknown", workspacePath);
        return Task.CompletedTask;
    }

    public Task MergeResultsAsync(
        string workspacePath,
        string baseWorkingDirectory,
        WorkChunk chunk,
        CancellationToken cancellationToken = default)
    {
        // No-op: read-only analysis tasks don't produce file changes to merge
        _logger.LogDebug(
            "InMemory merge (no-op) for chunk {ChunkId}",
            chunk?.ChunkId ?? "unknown");
        return Task.CompletedTask;
    }

    public Task<bool> IsAvailableAsync(string workingDirectory)
    {
        // Always available — no external dependencies
        return Task.FromResult(true);
    }
}