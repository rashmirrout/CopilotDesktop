namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Status of a worker agent or work chunk during orchestration.
/// </summary>
public enum AgentStatus
{
    /// <summary>Chunk created but not yet scheduled.</summary>
    Pending,

    /// <summary>Chunk is waiting for upstream dependencies to complete.</summary>
    WaitingForDependencies,

    /// <summary>Chunk is queued for execution (dependencies met, awaiting pool slot).</summary>
    Queued,

    /// <summary>Worker is actively executing the chunk.</summary>
    Running,

    /// <summary>Worker completed the chunk successfully.</summary>
    Succeeded,

    /// <summary>Worker failed to complete the chunk.</summary>
    Failed,

    /// <summary>Worker is retrying after a failure.</summary>
    Retrying,

    /// <summary>Chunk was aborted (e.g., abort threshold reached).</summary>
    Aborted,

    /// <summary>Chunk was skipped (e.g., upstream dependency failed).</summary>
    Skipped
}