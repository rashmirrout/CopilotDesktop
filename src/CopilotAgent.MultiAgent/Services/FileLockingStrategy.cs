namespace CopilotAgent.MultiAgent.Services;

using System.Collections.Concurrent;
using CopilotAgent.MultiAgent.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Workspace isolation using named semaphores per file scope.
/// All workers share the same base directory but acquire exclusive locks
/// on their working scope (files/directories) to prevent conflicts.
/// Suitable for non-Git repositories or when worktrees are unavailable.
/// </summary>
public sealed class FileLockingStrategy : IWorkspaceStrategy
{
    private readonly ILogger<FileLockingStrategy> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _scopeLocks = new();

    public FileLockingStrategy(ILogger<FileLockingStrategy> logger)
    {
        _logger = logger;
    }

    public WorkspaceStrategyType StrategyType => WorkspaceStrategyType.FileLocking;

    public Task<string> PrepareWorkspaceAsync(
        WorkChunk chunk,
        string baseWorkingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseWorkingDirectory);

        var scope = NormalizeScope(chunk.WorkingScope, baseWorkingDirectory);
        var semaphore = _scopeLocks.GetOrAdd(scope, _ => new SemaphoreSlim(1, 1));

        _logger.LogInformation(
            "Acquiring file lock for chunk '{Title}', scope: {Scope}",
            chunk.Title, scope);

        // Acquire the lock synchronously within the task
        // The caller is responsible for timeout via CancellationToken
        semaphore.Wait(cancellationToken);

        _logger.LogDebug(
            "File lock acquired for chunk '{Title}', scope: {Scope}",
            chunk.Title, scope);

        // Return the base directory — all workers share the same workspace
        return Task.FromResult(baseWorkingDirectory);
    }

    public Task CleanupWorkspaceAsync(
        string workspacePath,
        WorkChunk chunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var scope = NormalizeScope(chunk.WorkingScope, workspacePath);

        if (_scopeLocks.TryGetValue(scope, out var semaphore))
        {
            try
            {
                semaphore.Release();
                _logger.LogDebug(
                    "File lock released for chunk '{Title}', scope: {Scope}",
                    chunk.Title, scope);
            }
            catch (SemaphoreFullException)
            {
                _logger.LogWarning(
                    "Semaphore already released for scope {Scope}", scope);
            }
        }

        return Task.CompletedTask;
    }

    public Task MergeResultsAsync(
        string workspacePath,
        string baseWorkingDirectory,
        WorkChunk chunk,
        CancellationToken cancellationToken = default)
    {
        // No merge needed — all workers modify the same directory
        _logger.LogDebug(
            "FileLocking: no merge needed for chunk '{Title}' (shared workspace)",
            chunk.Title);

        return Task.CompletedTask;
    }

    public Task<bool> IsAvailableAsync(string workingDirectory)
    {
        // File locking is always available as long as the directory exists
        var exists = !string.IsNullOrWhiteSpace(workingDirectory)
                     && Directory.Exists(workingDirectory);
        return Task.FromResult(exists);
    }

    /// <summary>
    /// Normalize a working scope into a consistent key for lock management.
    /// If no scope is specified, uses the base directory as the scope.
    /// </summary>
    private static string NormalizeScope(string? workingScope, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(workingScope))
            return Path.GetFullPath(baseDir).ToUpperInvariant();

        var fullPath = Path.IsPathRooted(workingScope)
            ? workingScope
            : Path.Combine(baseDir, workingScope);

        return Path.GetFullPath(fullPath).ToUpperInvariant();
    }
}