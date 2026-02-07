namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Configurable retry behavior for failed worker chunks.
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>Maximum retry attempts per chunk before marking as failed.</summary>
    public int MaxRetriesPerChunk { get; set; } = 2;

    /// <summary>Total failure count across all chunks that triggers orchestration abort.</summary>
    public int AbortFailureThreshold { get; set; } = 3;

    /// <summary>Whether to modify the prompt on retry with error context.</summary>
    public bool RepromptOnRetry { get; set; } = true;

    /// <summary>Delay between retry attempts.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}