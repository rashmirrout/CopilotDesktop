namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Strategy for workspace isolation during parallel worker execution.
/// </summary>
public enum WorkspaceStrategyType
{
    /// <summary>Each worker gets a dedicated Git worktree branch.</summary>
    GitWorktree,

    /// <summary>Workers share the same directory with file-level locking.</summary>
    FileLocking,

    /// <summary>Workers operate in read-only/analysis mode — no file isolation needed.</summary>
    InMemory
}