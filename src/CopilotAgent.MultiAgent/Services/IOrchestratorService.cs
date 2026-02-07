using CopilotAgent.MultiAgent.Events;
using CopilotAgent.MultiAgent.Models;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Core orchestration service that manages the full lifecycle of multi-agent tasks:
/// clarification → planning → approval → execution → aggregation → completion.
/// </summary>
public interface IOrchestratorService
{
    /// <summary>
    /// Submit a new task prompt. The orchestrator will either ask clarifying questions
    /// or proceed directly to planning, depending on LLM evaluation.
    /// </summary>
    Task<OrchestratorResponse> SubmitTaskAsync(
        string taskPrompt, MultiAgentConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Respond to a clarification question from the orchestrator.
    /// </summary>
    Task<OrchestratorResponse> RespondToClarificationAsync(
        string userResponse, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approve, request changes to, or reject the generated plan.
    /// </summary>
    Task<OrchestratorResponse> ApprovePlanAsync(
        PlanApprovalDecision decision, string? feedback = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inject an instruction to the orchestrator during worker execution.
    /// The orchestrator will route the instruction to the appropriate worker(s).
    /// </summary>
    Task<OrchestratorResponse> InjectInstructionAsync(
        string instruction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a follow-up prompt after task completion. The orchestrator retains
    /// context from previous tasks for continuity.
    /// </summary>
    Task<OrchestratorResponse> SendFollowUpAsync(
        string followUpPrompt, CancellationToken cancellationToken = default);

    /// <summary>Current phase of the orchestration state machine.</summary>
    OrchestrationPhase CurrentPhase { get; }

    /// <summary>The current or most recent execution plan.</summary>
    OrchestrationPlan? CurrentPlan { get; }

    /// <summary>Whether the orchestrator is actively processing a task.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// The session ID of the orchestrator's long-lived LLM session, or null if no session exists.
    /// Used by the UI to poll session health via <c>ICopilotService.HasActiveSession</c>.
    /// </summary>
    string? OrchestratorSessionId { get; }

    /// <summary>Cancel the current orchestration, aborting all active workers.</summary>
    Task CancelAsync();

    /// <summary>Reset the orchestrator context, clearing all history.</summary>
    void ResetContext();

    /// <summary>Event stream for all orchestration lifecycle events.</summary>
    event EventHandler<OrchestratorEvent>? EventReceived;
}