using CopilotAgent.MultiAgent.Models;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Aggregates results from multiple worker agents into a consolidated report.
/// Uses a Synthesis-role LLM session to produce a conversational summary.
/// </summary>
public interface IResultAggregator
{
    /// <summary>
    /// Aggregate all worker results into a consolidated report with a conversational summary.
    /// </summary>
    Task<ConsolidatedReport> AggregateAsync(
        OrchestrationPlan plan,
        List<AgentResult> results,
        string orchestratorSessionId,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default);
}