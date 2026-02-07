namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Unified return type for all orchestrator interactions.
/// Contains the current phase, message, and any phase-specific data.
/// </summary>
public sealed class OrchestratorResponse
{
    public OrchestrationPhase Phase { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string>? ClarifyingQuestions { get; set; }
    public OrchestrationPlan? Plan { get; set; }
    public ConsolidatedReport? Report { get; set; }
    public bool RequiresUserInput { get; set; }
}