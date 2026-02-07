using CopilotAgent.MultiAgent.Events;

namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Structured log entry for orchestration observability.
/// </summary>
public sealed class LogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public OrchestrationLogLevel Level { get; set; } = OrchestrationLogLevel.Info;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ChunkId { get; set; }
    public string? PlanId { get; set; }
    public AgentRole? Role { get; set; }
    public OrchestratorEventType? EventType { get; set; }

    /// <summary>Structured data for machine-readable analysis.</summary>
    public Dictionary<string, object>? Data { get; set; }

    /// <summary>Duration of the operation (if applicable).</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Token count (if applicable).</summary>
    public int? TokensUsed { get; set; }

    /// <summary>Error details (if Level == Error).</summary>
    public string? ErrorDetails { get; set; }
    public string? StackTrace { get; set; }
}

/// <summary>
/// Log level for orchestration observability.
/// Named to avoid collision with Microsoft.Extensions.Logging.LogLevel.
/// </summary>
public enum OrchestrationLogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
    Critical
}