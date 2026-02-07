namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Final consolidated report produced by the result aggregator after
/// all workers complete. Contains a conversational summary and per-worker results.
/// </summary>
public sealed class ConsolidatedReport
{
    public string PlanId { get; set; } = string.Empty;
    public string ConversationalSummary { get; set; } = string.Empty;
    public List<AgentResult> WorkerResults { get; set; } = new();
    public OrchestrationStats Stats { get; set; } = new();
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Aggregate statistics for an orchestration run.
/// </summary>
public sealed class OrchestrationStats
{
    public int TotalChunks { get; set; }
    public int SucceededChunks { get; set; }
    public int FailedChunks { get; set; }
    public int RetriedChunks { get; set; }
    public int SkippedChunks { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public int TotalTokensUsed { get; set; }
}