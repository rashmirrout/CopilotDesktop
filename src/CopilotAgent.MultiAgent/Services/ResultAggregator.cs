using System.Diagnostics;
using System.Text;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.MultiAgent.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Aggregates results from multiple worker agents into a <see cref="ConsolidatedReport"/>.
/// Uses a Synthesis-role LLM session to produce a conversational summary that
/// weaves individual worker outputs into a coherent narrative for the user.
/// </summary>
public sealed class ResultAggregator : IResultAggregator
{
    private readonly ICopilotService _copilotService;
    private readonly ISessionManager _sessionManager;
    private readonly IAgentRoleProvider _roleProvider;
    private readonly ILogger<ResultAggregator> _logger;

    public ResultAggregator(
        ICopilotService copilotService,
        ISessionManager sessionManager,
        IAgentRoleProvider roleProvider,
        ILogger<ResultAggregator> logger)
    {
        ArgumentNullException.ThrowIfNull(copilotService);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(roleProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _copilotService = copilotService;
        _sessionManager = sessionManager;
        _roleProvider = roleProvider;
        _logger = logger;
    }

    public async Task<ConsolidatedReport> AggregateAsync(
        OrchestrationPlan plan,
        List<AgentResult> results,
        string orchestratorSessionId,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentException.ThrowIfNullOrWhiteSpace(orchestratorSessionId);
        ArgumentNullException.ThrowIfNull(config);

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Aggregating {ResultCount} worker results for plan {PlanId}",
            results.Count, plan.PlanId);

        // Compute statistics
        var stats = ComputeStats(results, stopwatch);

        // Build a conversational summary via LLM (Synthesis role)
        string summary;
        try
        {
            summary = await GenerateLlmSummaryAsync(
                plan, results, orchestratorSessionId, config, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LLM summary generation failed for plan {PlanId}; using fallback summary",
                plan.PlanId);
            summary = BuildFallbackSummary(plan, results, stats);
        }

        stopwatch.Stop();
        stats.TotalDuration = stopwatch.Elapsed;

        var report = new ConsolidatedReport
        {
            PlanId = plan.PlanId,
            ConversationalSummary = summary,
            WorkerResults = results,
            Stats = stats,
            CompletedAtUtc = DateTime.UtcNow
        };

        _logger.LogInformation(
            "Aggregation complete for plan {PlanId}: {Succeeded}/{Total} succeeded in {Duration:F1}s",
            plan.PlanId, stats.SucceededChunks, stats.TotalChunks,
            stopwatch.Elapsed.TotalSeconds);

        return report;
    }

    private async Task<string> GenerateLlmSummaryAsync(
        OrchestrationPlan plan,
        List<AgentResult> results,
        string orchestratorSessionId,
        MultiAgentConfig config,
        CancellationToken cancellationToken)
    {
        var prompt = BuildSynthesisPrompt(plan, results);

        // Reuse the orchestrator session for the synthesis prompt
        var session = GetOrCreateOrchestratorSession(orchestratorSessionId, config);

        _logger.LogDebug(
            "Sending synthesis prompt ({Length} chars) for plan {PlanId}",
            prompt.Length, plan.PlanId);

        var response = await _copilotService.SendMessageAsync(session, prompt, cancellationToken);
        var summary = response.Content ?? string.Empty;

        if (string.IsNullOrWhiteSpace(summary))
        {
            _logger.LogWarning("LLM returned empty synthesis for plan {PlanId}", plan.PlanId);
            return BuildFallbackSummary(plan, results, ComputeStats(results, Stopwatch.StartNew()));
        }

        return summary;
    }

    private static string BuildSynthesisPrompt(OrchestrationPlan plan, List<AgentResult> results)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are a synthesis agent. Your job is to combine the outputs of multiple");
        sb.AppendLine("parallel worker agents into a single, coherent, conversational summary.");
        sb.AppendLine();
        sb.AppendLine("## Original Task");
        sb.AppendLine(plan.TaskDescription);
        sb.AppendLine();
        sb.AppendLine("## Plan Summary");
        sb.AppendLine(plan.PlanSummary);
        sb.AppendLine();
        sb.AppendLine("## Worker Results");

        // Build a lookup from chunkId to chunk for title info
        var chunkLookup = plan.Chunks.ToDictionary(c => c.ChunkId, c => c);

        foreach (var result in results)
        {
            var title = chunkLookup.TryGetValue(result.ChunkId, out var chunk)
                ? chunk.Title
                : result.ChunkId;

            sb.AppendLine();
            sb.AppendLine($"### {title} ({(result.IsSuccess ? "✅ Success" : "❌ Failed")})");

            if (result.IsSuccess)
            {
                // Truncate very long worker responses to fit token budget
                var response = result.Response ?? "(no output)";
                if (response.Length > 3000)
                {
                    response = response[..3000] + "\n\n[...truncated for synthesis...]";
                }
                sb.AppendLine(response);
            }
            else
            {
                sb.AppendLine($"Error: {result.ErrorMessage ?? "Unknown error"}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Instructions");
        sb.AppendLine("1. Produce a clear, conversational summary of all the work that was done.");
        sb.AppendLine("2. Highlight key accomplishments and any issues encountered.");
        sb.AppendLine("3. If any workers failed, explain what went wrong and suggest next steps.");
        sb.AppendLine("4. Keep the summary concise but comprehensive.");
        sb.AppendLine("5. Use markdown formatting for readability.");
        sb.AppendLine("6. At the end, include a section titled '### Recommended Next Steps' with 2-5 actionable next steps.");
        sb.AppendLine("   Each next step MUST be formatted as a markdown list item with an [ACTION:description] marker.");
        sb.AppendLine("   Example:");
        sb.AppendLine("   ### Recommended Next Steps");
        sb.AppendLine("   - [ACTION:Run the full test suite to verify all changes]");
        sb.AppendLine("   - [ACTION:Review the generated code for edge cases]");
        sb.AppendLine("   - [ACTION:Deploy to staging environment for integration testing]");

        return sb.ToString();
    }

    private static string BuildFallbackSummary(
        OrchestrationPlan plan,
        List<AgentResult> results,
        OrchestrationStats stats)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## Task Completion Report");
        sb.AppendLine();
        sb.AppendLine($"**Task:** {plan.TaskDescription}");
        sb.AppendLine();
        sb.AppendLine($"### Summary");
        sb.AppendLine($"- **Total chunks:** {stats.TotalChunks}");
        sb.AppendLine($"- **Succeeded:** {stats.SucceededChunks}");
        sb.AppendLine($"- **Failed:** {stats.FailedChunks}");

        if (stats.RetriedChunks > 0)
        {
            sb.AppendLine($"- **Retried:** {stats.RetriedChunks}");
        }

        sb.AppendLine();
        sb.AppendLine("### Worker Results");

        var chunkLookup = plan.Chunks.ToDictionary(c => c.ChunkId, c => c);

        foreach (var result in results)
        {
            var title = chunkLookup.TryGetValue(result.ChunkId, out var chunk)
                ? chunk.Title
                : result.ChunkId;

            var status = result.IsSuccess ? "✅" : "❌";
            var duration = result.Duration.TotalSeconds;
            sb.AppendLine($"- {status} **{title}** ({duration:F1}s)");

            if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                sb.AppendLine($"  - Error: {result.ErrorMessage}");
            }
        }

        return sb.ToString();
    }

    private Session GetOrCreateOrchestratorSession(string orchestratorSessionId, MultiAgentConfig config)
    {
        var sessions = _sessionManager.Sessions;
        var existing = sessions.FirstOrDefault(s => s.SessionId == orchestratorSessionId);
        if (existing != null)
        {
            return existing;
        }

        return new Session
        {
            SessionId = orchestratorSessionId,
            DisplayName = "Orchestrator-Synthesis",
            WorkingDirectory = config.WorkingDirectory,
            ModelId = config.OrchestratorModelId ?? "gpt-4",
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        };
    }

    private static OrchestrationStats ComputeStats(List<AgentResult> results, Stopwatch stopwatch)
    {
        return new OrchestrationStats
        {
            TotalChunks = results.Count,
            SucceededChunks = results.Count(r => r.IsSuccess),
            FailedChunks = results.Count(r => !r.IsSuccess),
            RetriedChunks = 0, // Retry count tracked at chunk level, not result level
            TotalDuration = stopwatch.Elapsed
        };
    }
}