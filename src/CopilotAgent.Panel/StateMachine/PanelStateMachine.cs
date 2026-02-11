// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reactive.Subjects;
using CopilotAgent.Panel.Domain.Entities;
using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.Events;
using Microsoft.Extensions.Logging;
using Stateless;

namespace CopilotAgent.Panel.StateMachine;

/// <summary>
/// Deterministic finite state machine governing the panel discussion lifecycle.
/// Uses the Stateless library for a declarative, testable state configuration.
/// 
/// State transitions:
///   Idle → Clarifying (StartClarification)
///   Clarifying → AwaitingApproval (PlanReady)
///   Clarifying → Idle (Cancel)
///   AwaitingApproval → Preparing (UserApproved)
///   AwaitingApproval → Clarifying (UserRejected — user wants changes)
///   AwaitingApproval → Idle (Cancel)
///   Preparing → Running (PanelReady)
///   Preparing → Failed (Error)
///   Running → Paused (Pause)
///   Running → Converging (ConvergenceDetected)
///   Running → Stopped (Stop)
///   Running → Failed (Error)
///   Paused → Running (Resume)
///   Paused → Stopped (Stop)
///   Converging → Synthesizing (StartSynthesis)
///   Converging → Running (ResumeDebate — convergence was premature)
///   Converging → Failed (Error)
///   Synthesizing → Completed (SynthesisComplete)
///   Synthesizing → Failed (Error)
///   Completed → Idle (Reset)
///   Stopped → Idle (Reset)
///   Failed → Idle (Reset)
/// </summary>
public sealed class PanelStateMachine
{
    private readonly StateMachine<PanelPhase, PanelTrigger> _machine;
    private readonly PanelSession _session;
    private readonly ISubject<PanelEvent> _eventStream;
    private readonly ILogger<PanelStateMachine> _logger;

    public PanelStateMachine(
        PanelSession session,
        ISubject<PanelEvent> eventStream,
        ILogger<PanelStateMachine> logger)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _machine = new StateMachine<PanelPhase, PanelTrigger>(
            () => _session.Phase,
            phase => _session.TransitionTo(phase));

        ConfigureStateMachine();
    }

    /// <summary>Current state of the panel discussion.</summary>
    public PanelPhase CurrentPhase => _machine.State;

    /// <summary>Whether the given trigger can be fired in the current state.</summary>
    public bool CanFire(PanelTrigger trigger) => _machine.CanFire(trigger);

    /// <summary>Returns the set of triggers permitted in the current state.</summary>
    public IEnumerable<PanelTrigger> PermittedTriggers => _machine.PermittedTriggers;

    /// <summary>
    /// Fire a trigger to transition to a new state.
    /// Throws InvalidOperationException if the trigger is not valid for the current state.
    /// </summary>
    public async Task FireAsync(PanelTrigger trigger)
    {
        _logger.LogDebug(
            "Panel {SessionId}: Firing trigger {Trigger} from state {State}",
            _session.Id, trigger, _machine.State);

        await _machine.FireAsync(trigger);
    }

    private void ConfigureStateMachine()
    {
        // --- Idle ---
        _machine.Configure(PanelPhase.Idle)
            .Permit(PanelTrigger.UserSubmitted, PanelPhase.Clarifying)
            .OnEntry(EmitPhaseChanged);

        // --- Clarifying ---
        _machine.Configure(PanelPhase.Clarifying)
            .Permit(PanelTrigger.ClarificationsComplete, PanelPhase.AwaitingApproval)
            .Permit(PanelTrigger.UserCancelled, PanelPhase.Idle)
            .Permit(PanelTrigger.Error, PanelPhase.Failed)
            .OnEntry(EmitPhaseChanged);

        // --- AwaitingApproval ---
        _machine.Configure(PanelPhase.AwaitingApproval)
            .Permit(PanelTrigger.UserApproved, PanelPhase.Preparing)
            .Permit(PanelTrigger.UserRejected, PanelPhase.Clarifying)
            .Permit(PanelTrigger.UserCancelled, PanelPhase.Idle)
            .OnEntry(EmitPhaseChanged);

        // --- Preparing ---
        _machine.Configure(PanelPhase.Preparing)
            .Permit(PanelTrigger.PanelistsReady, PanelPhase.Running)
            .Permit(PanelTrigger.Error, PanelPhase.Failed)
            .OnEntry(EmitPhaseChanged);

        // --- Running ---
        _machine.Configure(PanelPhase.Running)
            .Permit(PanelTrigger.UserPaused, PanelPhase.Paused)
            .Permit(PanelTrigger.ConvergenceDetected, PanelPhase.Converging)
            .Permit(PanelTrigger.UserStopped, PanelPhase.Stopped)
            .Permit(PanelTrigger.Timeout, PanelPhase.Stopped)
            .Permit(PanelTrigger.Error, PanelPhase.Failed)
            .OnEntry(EmitPhaseChanged);

        // --- Paused ---
        _machine.Configure(PanelPhase.Paused)
            .Permit(PanelTrigger.UserResumed, PanelPhase.Running)
            .Permit(PanelTrigger.UserStopped, PanelPhase.Stopped)
            .OnEntry(EmitPhaseChanged);

        // --- Converging ---
        _machine.Configure(PanelPhase.Converging)
            .Permit(PanelTrigger.StartSynthesis, PanelPhase.Synthesizing)
            .Permit(PanelTrigger.ResumeDebate, PanelPhase.Running)
            .Permit(PanelTrigger.Error, PanelPhase.Failed)
            .OnEntry(EmitPhaseChanged);

        // --- Synthesizing ---
        _machine.Configure(PanelPhase.Synthesizing)
            .Permit(PanelTrigger.SynthesisComplete, PanelPhase.Completed)
            .Permit(PanelTrigger.Error, PanelPhase.Failed)
            .OnEntry(EmitPhaseChanged);

        // --- Terminal states: all can reset back to Idle ---
        _machine.Configure(PanelPhase.Completed)
            .Permit(PanelTrigger.Reset, PanelPhase.Idle)
            .OnEntry(EmitPhaseChanged);

        _machine.Configure(PanelPhase.Stopped)
            .Permit(PanelTrigger.Reset, PanelPhase.Idle)
            .OnEntry(EmitPhaseChanged);

        _machine.Configure(PanelPhase.Failed)
            .Permit(PanelTrigger.Reset, PanelPhase.Idle)
            .OnEntry(EmitPhaseChanged);

        // Global unhandled trigger handler — log and swallow rather than throw
        _machine.OnUnhandledTrigger((state, trigger) =>
        {
            _logger.LogWarning(
                "Panel {SessionId}: Unhandled trigger {Trigger} in state {State} — ignored",
                _session.Id, trigger, state);
        });
    }

    private void EmitPhaseChanged(StateMachine<PanelPhase, PanelTrigger>.Transition transition)
    {
        _logger.LogInformation(
            "Panel {SessionId}: {Source} → {Destination} (trigger: {Trigger})",
            _session.Id, transition.Source, transition.Destination, transition.Trigger);

        _eventStream.OnNext(new PhaseChangedEvent(
            _session.Id,
            transition.Source,
            transition.Destination,
            CorrelationId: null,
            DateTimeOffset.UtcNow));
    }
}