namespace CopilotAgent.MultiAgent.Services;

using System.Diagnostics;
using CopilotAgent.MultiAgent.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Workspace isolation using Git worktrees. Each worker chunk gets its own
/// worktree branch, enabling true parallel file modifications without conflicts.
/// Requires the base working directory to be a Git repository.
/// </summary>
public sealed class GitWorktreeStrategy : IWorkspaceStrategy
{
    private readonly ILogger<GitWorktreeStrategy> _logger;
    private readonly SemaphoreSlim _gitLock = new(1, 1);

    public GitWorktreeStrategy(ILogger<GitWorktreeStrategy> logger)
    {
        _logger = logger;
    }

    public WorkspaceStrategyType StrategyType => WorkspaceStrategyType.GitWorktree;

    public async Task<string> PrepareWorkspaceAsync(
        WorkChunk chunk,
        string baseWorkingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseWorkingDirectory);

        var branchName = $"multi-agent/{chunk.ChunkId[..8]}";
        var worktreePath = Path.Combine(
            baseWorkingDirectory,
            ".worktrees",
            $"worker-{chunk.ChunkId[..8]}");

        _logger.LogInformation(
            "Preparing Git worktree for chunk '{Title}' at {WorktreePath}",
            chunk.Title, worktreePath);

        await _gitLock.WaitAsync(cancellationToken);
        try
        {
            // Clean up any stale worktree at the same path
            if (Directory.Exists(worktreePath))
            {
                _logger.LogWarning("Stale worktree found at {Path}, removing", worktreePath);
                await RunGitAsync(baseWorkingDirectory,
                    $"worktree remove \"{worktreePath}\" --force", cancellationToken);
            }

            // Create worktree with a new branch from HEAD
            var result = await RunGitAsync(baseWorkingDirectory,
                $"worktree add -b \"{branchName}\" \"{worktreePath}\" HEAD",
                cancellationToken);

            if (!result.Success)
            {
                // Branch might already exist from a previous failed run — try without -b
                _logger.LogWarning(
                    "Branch {Branch} may already exist, attempting worktree add without -b",
                    branchName);

                // Delete the branch first if it exists
                await RunGitAsync(baseWorkingDirectory,
                    $"branch -D \"{branchName}\"", cancellationToken);

                result = await RunGitAsync(baseWorkingDirectory,
                    $"worktree add -b \"{branchName}\" \"{worktreePath}\" HEAD",
                    cancellationToken);

                if (!result.Success)
                {
                    throw new InvalidOperationException(
                        $"Failed to create Git worktree: {result.Error}");
                }
            }

            _logger.LogInformation(
                "Git worktree created for chunk '{Title}': branch={Branch}, path={Path}",
                chunk.Title, branchName, worktreePath);

            return worktreePath;
        }
        finally
        {
            _gitLock.Release();
        }
    }

    public async Task CleanupWorkspaceAsync(
        string workspacePath,
        WorkChunk chunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        if (!Directory.Exists(workspacePath))
        {
            _logger.LogDebug("Worktree path {Path} already removed", workspacePath);
            return;
        }

        var branchName = $"multi-agent/{chunk.ChunkId[..8]}";

        // Find the base directory (parent of .worktrees)
        var baseDir = Path.GetFullPath(Path.Combine(workspacePath, "..", ".."));

        await _gitLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Cleaning up worktree at {Path}", workspacePath);

            await RunGitAsync(baseDir,
                $"worktree remove \"{workspacePath}\" --force", cancellationToken);

            await RunGitAsync(baseDir,
                $"branch -D \"{branchName}\"", cancellationToken);

            _logger.LogDebug("Worktree and branch cleaned up for chunk '{Title}'", chunk.Title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to clean up worktree at {Path} — may require manual cleanup",
                workspacePath);
        }
        finally
        {
            _gitLock.Release();
        }
    }

    public async Task MergeResultsAsync(
        string workspacePath,
        string baseWorkingDirectory,
        WorkChunk chunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseWorkingDirectory);

        var branchName = $"multi-agent/{chunk.ChunkId[..8]}";

        await _gitLock.WaitAsync(cancellationToken);
        try
        {
            // Check if there are any changes to merge
            var diffResult = await RunGitAsync(workspacePath,
                "diff --stat HEAD", cancellationToken);

            if (string.IsNullOrWhiteSpace(diffResult.Output))
            {
                _logger.LogDebug(
                    "No changes to merge from chunk '{Title}'", chunk.Title);
                return;
            }

            // Commit changes in worktree
            await RunGitAsync(workspacePath, "add -A", cancellationToken);
            await RunGitAsync(workspacePath,
                $"commit -m \"[multi-agent] {chunk.Title}\"", cancellationToken);

            // Merge back to main branch
            var mergeResult = await RunGitAsync(baseWorkingDirectory,
                $"merge --no-ff \"{branchName}\" -m \"Merge multi-agent: {chunk.Title}\"",
                cancellationToken);

            if (!mergeResult.Success)
            {
                _logger.LogError(
                    "Merge conflict for chunk '{Title}': {Error}",
                    chunk.Title, mergeResult.Error);

                // Abort the failed merge
                await RunGitAsync(baseWorkingDirectory, "merge --abort", cancellationToken);

                throw new InvalidOperationException(
                    $"Merge conflict for chunk '{chunk.Title}': {mergeResult.Error}");
            }

            _logger.LogInformation(
                "Successfully merged results from chunk '{Title}'", chunk.Title);
        }
        finally
        {
            _gitLock.Release();
        }
    }

    public async Task<bool> IsAvailableAsync(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return false;

        var gitDir = Path.Combine(workingDirectory, ".git");
        if (!Directory.Exists(gitDir) && !File.Exists(gitDir))
            return false;

        var result = await RunGitAsync(workingDirectory, "rev-parse --is-inside-work-tree",
            CancellationToken.None);

        return result.Success && result.Output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<GitResult> RunGitAsync(
        string workingDirectory, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        return new GitResult
        {
            Success = process.ExitCode == 0,
            Output = output,
            Error = error,
            ExitCode = process.ExitCode
        };
    }

    private sealed class GitResult
    {
        public bool Success { get; init; }
        public string Output { get; init; } = string.Empty;
        public string Error { get; init; } = string.Empty;
        public int ExitCode { get; init; }
    }
}