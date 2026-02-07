using CopilotAgent.MultiAgent.Models;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Strategy interface for workspace isolation during parallel worker execution.
/// Implementations provide different isolation mechanisms (git worktrees, file locks, in-memory).
/// </summary>
public interface IWorkspaceStrategy
{
    /// <summary>The type of workspace strategy this implementation provides.</summary>
    WorkspaceStrategyType StrategyType { get; }

    /// <summary>
    /// Prepare an isolated workspace for a work chunk.
    /// Returns the path to the workspace directory.
    /// </summary>
    Task<string> PrepareWorkspaceAsync(
        WorkChunk chunk, string baseWorkingDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up the workspace after chunk execution completes.
    /// </summary>
    Task CleanupWorkspaceAsync(
        string workspacePath, WorkChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merge results from the isolated workspace back into the base working directory.
    /// </summary>
    Task MergeResultsAsync(
        string workspacePath, string baseWorkingDirectory, WorkChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if this strategy is available in the given working directory
    /// (e.g., git worktree requires a .git repository).
    /// </summary>
    Task<bool> IsAvailableAsync(string workingDirectory);
}