using CopilotAgent.Office.Events;
using CopilotAgent.Office.Models;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Core orchestration service — owns the Manager state machine and iteration loop.
/// </summary>
public interface IOfficeManagerService
{
    /// <summary>Current phase of the Manager state machine.</summary>
    ManagerPhase CurrentPhase { get; }

    /// <summary>Current iteration number.</summary>
    int CurrentIteration { get; }

    /// <summary>Whether the Manager is actively running.</summary>
    bool IsRunning { get; }

    /// <summary>Whether the Manager is waiting for user clarification.</summary>
    bool IsWaitingForClarification { get; }

    /// <summary>Whether a plan is awaiting user approval.</summary>
    bool IsPlanAwaitingApproval { get; }

    /// <summary>The current plan text, if generated.</summary>
    string? CurrentPlan { get; }

    /// <summary>Event raised for every office event (phase changes, assistant events, etc.).</summary>
    event Action<OfficeEvent>? OnEvent;

    /// <summary>Start the Office run with the given configuration.</summary>
    Task StartAsync(OfficeConfig config, CancellationToken ct = default);

    /// <summary>Approve the current plan and begin iteration loop.</summary>
    Task ApprovePlanAsync(CancellationToken ct = default);

    /// <summary>Reject the current plan with optional feedback.</summary>
    Task RejectPlanAsync(string? feedback = null, CancellationToken ct = default);

    /// <summary>Inject a user instruction mid-run.</summary>
    Task InjectInstructionAsync(string instruction, CancellationToken ct = default);

    /// <summary>Respond to a Manager clarification question.</summary>
    Task RespondToClarificationAsync(string response, CancellationToken ct = default);

    /// <summary>Pause the iteration loop.</summary>
    Task PauseAsync(CancellationToken ct = default);

    /// <summary>Resume a paused iteration loop.</summary>
    Task ResumeAsync(CancellationToken ct = default);

    /// <summary>Stop the Office run gracefully.</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Reset to Idle state, clearing all context.</summary>
    Task ResetAsync(CancellationToken ct = default);

    /// <summary>Update the check interval for the next rest period.</summary>
    void UpdateCheckInterval(int minutes);
}