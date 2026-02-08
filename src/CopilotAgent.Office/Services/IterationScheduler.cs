using CopilotAgent.Office.Events;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Countdown timer for rest periods between iterations.
/// Uses PeriodicTimer for tick events and TaskCompletionSource for early cancellation.
/// </summary>
public sealed class IterationScheduler : IIterationScheduler
{
    private readonly ILogger<IterationScheduler> _logger;
    private TaskCompletionSource? _cancelSource;
    private int _totalSeconds;
    private readonly object _lock = new();

    /// <inheritdoc />
    public event Action<RestCountdownEvent>? OnCountdownTick;

    public IterationScheduler(ILogger<IterationScheduler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task WaitForNextIterationAsync(int intervalMinutes, CancellationToken ct = default)
    {
        _totalSeconds = Math.Max(1, intervalMinutes * 60);
        var remaining = _totalSeconds;

        lock (_lock)
        {
            _cancelSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _logger.LogInformation("Rest period started: {Minutes} minutes ({Seconds} seconds)", intervalMinutes, _totalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();

            RaiseTick(remaining, _totalSeconds);

            // Wait for either: next tick, cancellation, or early cancel
            var tickTask = timer.WaitForNextTickAsync(ct).AsTask();
            TaskCompletionSource? cancelSource;
            lock (_lock)
            {
                cancelSource = _cancelSource;
            }

            if (cancelSource is not null)
            {
                var completed = await Task.WhenAny(tickTask, cancelSource.Task).ConfigureAwait(false);
                if (completed == cancelSource.Task)
                {
                    _logger.LogInformation("Rest period cancelled early at {Remaining}s remaining", remaining);
                    RaiseTick(0, _totalSeconds);
                    return;
                }
            }
            else
            {
                await tickTask.ConfigureAwait(false);
            }

            remaining--;
        }

        RaiseTick(0, _totalSeconds);
        _logger.LogInformation("Rest period completed");
    }

    /// <inheritdoc />
    public void CancelRest()
    {
        lock (_lock)
        {
            if (_cancelSource is not null && !_cancelSource.Task.IsCompleted)
            {
                _cancelSource.TrySetResult();
                _logger.LogInformation("Rest cancellation requested");
            }
        }
    }

    /// <inheritdoc />
    public Task OverrideRestDurationAsync(int minutes)
    {
        var newTotal = Math.Max(1, minutes * 60);
        _logger.LogInformation("Rest duration overridden to {Minutes} minutes", minutes);

        // Cancel current rest and let caller re-invoke with new duration
        lock (_lock)
        {
            _totalSeconds = newTotal;
            if (_cancelSource is not null && !_cancelSource.Task.IsCompleted)
            {
                _cancelSource.TrySetResult();
            }
        }

        return Task.CompletedTask;
    }

    private void RaiseTick(int secondsRemaining, int totalSeconds)
    {
        try
        {
            OnCountdownTick?.Invoke(new RestCountdownEvent
            {
                SecondsRemaining = secondsRemaining,
                TotalSeconds = totalSeconds,
                Description = $"Rest: {secondsRemaining}s remaining of {totalSeconds}s"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in countdown tick handler");
        }
    }
}