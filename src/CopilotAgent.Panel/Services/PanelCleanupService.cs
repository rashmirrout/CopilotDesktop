// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Panel.Services;

/// <summary>
/// Periodic cleanup service that scans for zombie panel sessions.
/// Runs every 5 minutes and terminates any sessions that have been
/// in <see cref="PanelPhase.Running"/> or <see cref="PanelPhase.Paused"/>
/// state for longer than 2× <see cref="Domain.Policies.GuardRailPolicy.MaxDiscussionDuration"/>.
///
/// DESIGN:
///   - Uses <see cref="System.Threading.Timer"/> for low-overhead periodic checks.
///   - Does NOT own the orchestrator — only reads state and triggers stop.
///   - Fail-safe: if the timer callback throws, it logs and continues.
///
/// THREAD SAFETY: Timer callbacks are serialized by the CLR ThreadPool.
/// The orchestrator's public API is already thread-safe.
///
/// LIFECYCLE: Created as a singleton in DI. Disposed when the application shuts down.
/// </summary>
public sealed class PanelCleanupService : IDisposable
{
    private readonly IPanelOrchestrator _orchestrator;
    private readonly ILogger<PanelCleanupService> _logger;
    private readonly Timer _timer;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _maxStaleDuration;
    private DateTimeOffset _lastActivePhaseDetectedAt = DateTimeOffset.UtcNow;
    private bool _disposed;

    /// <summary>
    /// Creates a new cleanup service.
    /// </summary>
    /// <param name="orchestrator">The panel orchestrator to monitor.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="checkInterval">How often to check for zombie sessions. Default: 5 minutes.</param>
    /// <param name="maxStaleDuration">
    /// Maximum time a session can remain in Running/Paused before forced termination.
    /// Default: 60 minutes (2× the default 30-minute MaxDiscussionDuration).
    /// </param>
    public PanelCleanupService(
        IPanelOrchestrator orchestrator,
        ILogger<PanelCleanupService> logger,
        TimeSpan? checkInterval = null,
        TimeSpan? maxStaleDuration = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _checkInterval = checkInterval ?? TimeSpan.FromMinutes(5);
        _maxStaleDuration = maxStaleDuration ?? TimeSpan.FromMinutes(60);

        _timer = new Timer(
            callback: OnCleanupTick,
            state: null,
            dueTime: _checkInterval,
            period: _checkInterval);

        _logger.LogInformation(
            "[PanelCleanup] Service started. Check interval: {Interval}, Max stale duration: {MaxStale}",
            _checkInterval, _maxStaleDuration);
    }

    /// <summary>
    /// Timer callback — checks for zombie sessions and terminates them.
    /// </summary>
    private void OnCleanupTick(object? state)
    {
        if (_disposed) return;

        try
        {
            var sessionId = _orchestrator.ActiveSessionId;
            var phase = _orchestrator.CurrentPhase;

            if (sessionId is null)
            {
                // No active session — reset tracking
                _lastActivePhaseDetectedAt = DateTimeOffset.UtcNow;
                return;
            }

            if (phase is PanelPhase.Running or PanelPhase.Paused)
            {
                var staleDuration = DateTimeOffset.UtcNow - _lastActivePhaseDetectedAt;

                if (staleDuration > _maxStaleDuration)
                {
                    _logger.LogWarning(
                        "[PanelCleanup] Session {SessionId} has been in {Phase} for {Duration:hh\\:mm\\:ss} " +
                        "(exceeds max stale duration of {MaxStale:hh\\:mm\\:ss}). Forcing stop.",
                        sessionId, phase, staleDuration, _maxStaleDuration);

                    // Fire-and-forget stop — the orchestrator handles its own thread safety
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _orchestrator.StopAsync();
                            _logger.LogInformation(
                                "[PanelCleanup] Successfully stopped zombie session {SessionId}",
                                sessionId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "[PanelCleanup] Failed to stop zombie session {SessionId}",
                                sessionId);
                        }
                    });
                }
                else
                {
                    _logger.LogDebug(
                        "[PanelCleanup] Session {SessionId} in {Phase} for {Duration:hh\\:mm\\:ss} — within limits",
                        sessionId, phase, staleDuration);
                }
            }
            else if (phase is PanelPhase.Idle or PanelPhase.Completed or PanelPhase.Stopped or PanelPhase.Failed)
            {
                // Session is in a terminal/idle state — reset tracking for next session
                _lastActivePhaseDetectedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                // Transitional states (Clarifying, AwaitingApproval, Preparing, Converging, Synthesizing)
                // These are expected to be brief — don't reset the timer but don't force-stop either
                _logger.LogDebug(
                    "[PanelCleanup] Session {SessionId} in transitional phase {Phase}",
                    sessionId, phase);
            }
        }
        catch (Exception ex)
        {
            // Never let the timer callback crash — log and continue
            _logger.LogError(ex, "[PanelCleanup] Cleanup tick failed — will retry next interval");
        }
    }

    /// <summary>
    /// Reset the stale-session tracker. Call when a new session starts
    /// or when the user explicitly resumes a paused session.
    /// </summary>
    public void ResetStaleTimer()
    {
        _lastActivePhaseDetectedAt = DateTimeOffset.UtcNow;
        _logger.LogDebug("[PanelCleanup] Stale timer reset");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        _logger.LogInformation("[PanelCleanup] Service disposed");
    }
}