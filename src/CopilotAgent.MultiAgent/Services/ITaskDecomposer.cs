using CopilotAgent.MultiAgent.Models;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Decomposes a high-level task prompt into an OrchestrationPlan via LLM.
/// V3: Includes JSON schema validation for LLM-generated plans.
/// </summary>
public interface ITaskDecomposer
{
    /// <summary>
    /// Send the task to the orchestrator's LLM session and parse the structured plan.
    /// Validates the returned JSON against a predefined schema before parsing.
    /// </summary>
    Task<OrchestrationPlan> DecomposeAsync(
        string taskPrompt,
        string orchestratorSessionId,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate a raw JSON plan string against the orchestration plan schema.
    /// Returns validation errors if the JSON is malformed or missing required fields.
    /// </summary>
    PlanValidationResult ValidatePlanJson(string rawJson);
}

/// <summary>
/// Result of JSON schema validation for an LLM-generated plan.
/// </summary>
public sealed class PlanValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public string? SanitizedJson { get; set; }
}