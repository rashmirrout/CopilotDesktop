using CopilotAgent.Office.Events;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Countdown timer for rest periods between iterations, with early cancellation support.
/// </summary>
public interface IIterationScheduler
{
    /// <summary>Wait for the next iteration, ticking every second.</summary>
    /// <param name="intervalMinutes">Rest duration in minutes.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WaitForNextIterationAsync(int intervalMinutes, CancellationToken ct = default);

    /// <summary>Cancel the current rest period immediately.</summary>
    void CancelRest();

    /// <summary>Override the remaining rest duration.</summary>
    /// <param name="minutes">New rest duration in minutes.</param>
    Task OverrideRestDurationAsync(int minutes);

    /// <summary>Raised every second during the rest countdown.</summary>
    event Action<RestCountdownEvent>? OnCountdownTick;
}