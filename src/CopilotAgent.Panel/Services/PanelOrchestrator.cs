// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reactive.Subjects;
using System.Text.RegularExpressions;
using CopilotAgent.Panel.Agents;
using CopilotAgent.Panel.Domain.Entities;
using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.Events;
using CopilotAgent.Panel.Domain.Interfaces;
using CopilotAgent.Panel.Domain.Policies;
using CopilotAgent.Panel.Domain.ValueObjects;
using CopilotAgent.Panel.Models;
using CopilotAgent.Panel.StateMachine;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Panel.Services;

/// <summary>
/// Central orchestrator for panel discussions. Coordinates the state machine,
/// agent lifecycle, discussion loop, and event emission.
///
/// ARCHITECTURE:
///   - Owns the <see cref="PanelStateMachine"/> (FSM) and <see cref="PanelSession"/> (aggregate root).
///   - Creates agents via <see cref="IPanelAgentFactory"/>.
///   - Runs the discussion loop on a background task.
///   - Uses <see cref="SemaphoreSlim"/> for pause/resume mechanism.
///   - Emits all events via <see cref="ISubject{PanelEvent}"/> (IObservable for UI binding).
///
/// THREAD SAFETY:
///   - Public methods are safe to call from the UI thread.
///   - The discussion loop runs on a background task.
///   - <see cref="_lock"/> guards state transitions from concurrent access.
///
/// LIFECYCLE:
///   1. <see cref="StartAsync"/> — Creates session, fires Clarifying, Head asks questions
///   2. <see cref="SendUserMessageAsync"/> — User replies during Clarifying phase
///   3. <see cref="ApproveAndStartPanelAsync"/> — User approves plan → Preparing → Running
///   4. Discussion loop runs automatically (Moderator + Panelists take turns)
///   5. Convergence detected → Synthesizing → Completed
///   6. <see cref="ResetAsync"/> — Clean up and return to Idle
/// </summary>
public sealed class PanelOrchestrator : IPanelOrchestrator, IAsyncDisposable
{
    private readonly IPanelAgentFactory _agentFactory;
    private readonly IConvergenceDetector _convergenceDetector;
    private readonly IKnowledgeBriefService _knowledgeBriefService;
    private readonly ISubject<PanelEvent> _eventStream;
    private readonly ILogger<PanelOrchestrator> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SemaphoreSlim _pauseGate = new(1, 1);

    private PanelSession? _session;
    private PanelStateMachine? _stateMachine;
    private PanelSettings? _settings;

    // Agent references (typed for role-specific methods)
    private HeadAgent? _headAgent;
    private ModeratorAgent? _moderatorAgent;
    private readonly List<IPanelAgent> _panelistAgents = [];
    private readonly List<IPanelAgent> _allAgents = [];

    // Discussion loop state
    private CancellationTokenSource? _discussionCts;
    private Task? _discussionTask;
    private TurnNumber _currentTurn = new(0);
    private DiscussionDepth _detectedDepth = DiscussionDepth.Standard;
    private bool _disposed;

    public PanelSessionId? ActiveSessionId => _session?.Id;
    public PanelPhase CurrentPhase => _stateMachine?.CurrentPhase ?? PanelPhase.Idle;
    public IObservable<PanelEvent> Events => _eventStream;

    public PanelOrchestrator(
        IPanelAgentFactory agentFactory,
        IConvergenceDetector convergenceDetector,
        IKnowledgeBriefService knowledgeBriefService,
        ISubject<PanelEvent> eventStream,
        ILoggerFactory loggerFactory)
    {
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _convergenceDetector = convergenceDetector ?? throw new ArgumentNullException(nameof(convergenceDetector));
        _knowledgeBriefService = knowledgeBriefService ?? throw new ArgumentNullException(nameof(knowledgeBriefService));
        _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<PanelOrchestrator>();
    }

    /// <inheritdoc/>
    public async Task<PanelSessionId> StartAsync(
        string userPrompt, PanelSettings settings, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_session is not null && CurrentPhase is not PanelPhase.Idle)
                throw new InvalidOperationException(
                    $"Cannot start: current phase is {CurrentPhase}. Call ResetAsync first.");

            _settings = settings;
            var sessionId = PanelSessionId.New();
            var policy = GuardRailPolicy.FromSettings(settings);
            _session = new PanelSession(sessionId, userPrompt, policy);
            _stateMachine = new PanelStateMachine(
                _session, _eventStream, _loggerFactory.CreateLogger<PanelStateMachine>());

            _logger.LogInformation(
                "[Orchestrator] Starting panel session {SessionId} with prompt: {Prompt}",
                sessionId, Truncate(userPrompt, 80));

            // Transition: Idle → Clarifying
            await _stateMachine.FireAsync(PanelTrigger.UserSubmitted);

            // Create Head agent for clarification
            var headIpAgent = await _agentFactory.CreateHeadAgentAsync(settings, ct);
            _headAgent = (HeadAgent)headIpAgent;
            _allAgents.Add(headIpAgent);

            // Set active session so Head emits status events during LLM calls
            ((PanelAgentBase)_headAgent).SetActivePanelSession(sessionId);

            // Head analyzes the prompt and generates clarification questions
            var clarification = await _headAgent.ClarifyAsync(userPrompt, sessionId, ct);

            // Determine discussion depth: manual override or LLM-detected
            var depthOverride = settings.DiscussionDepthOverride?.Trim();
            if (!string.IsNullOrEmpty(depthOverride)
                && !depthOverride.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<DiscussionDepth>(depthOverride, ignoreCase: true, out var manualDepth)
                && manualDepth != DiscussionDepth.Auto)
            {
                // Manual override — skip LLM depth detection entirely
                _detectedDepth = manualDepth;
                ApplyDepthPreset(settings, _detectedDepth);
                _logger.LogInformation(
                    "[Orchestrator] Manual depth override: {Depth} — MaxTurns={MaxTurns}",
                    _detectedDepth, settings.MaxTurns);

                _eventStream.OnNext(new CommentaryEvent(
                    sessionId, Guid.Empty, "Orchestrator", PanelAgentRole.Head,
                    $"📏 Discussion depth (manual): **{_detectedDepth}**",
                    CommentaryMode.Brief, DateTimeOffset.UtcNow));
            }
            else
            {
                // Auto mode — parse depth from Head's clarification response
                _detectedDepth = ParseDiscussionDepth(clarification);
                if (_detectedDepth != DiscussionDepth.Standard)
                {
                    ApplyDepthPreset(settings, _detectedDepth);
                    _logger.LogInformation(
                        "[Orchestrator] Detected discussion depth: {Depth} — adjusted MaxTurns={MaxTurns}",
                        _detectedDepth, settings.MaxTurns);

                    _eventStream.OnNext(new CommentaryEvent(
                        sessionId, Guid.Empty, "Orchestrator", PanelAgentRole.Head,
                        $"📏 Discussion depth detected: **{_detectedDepth}**",
                        CommentaryMode.Brief, DateTimeOffset.UtcNow));
                }
            }

            // Record the user's original prompt
            var userMessage = PanelMessage.Create(
                sessionId, Guid.Empty, "User", PanelAgentRole.Head,
                userPrompt, PanelMessageType.UserMessage);
            _session.AddMessage(userMessage);

            // Record the Head's clarification response
            var headMessage = PanelMessage.Create(
                sessionId, _headAgent.Id, _headAgent.Name, PanelAgentRole.Head,
                clarification, PanelMessageType.Clarification);
            _session.AddMessage(headMessage);

            _eventStream.OnNext(new AgentMessageEvent(sessionId, headMessage, DateTimeOffset.UtcNow));

            // If the Head says "CLEAR:", skip clarification and go straight to approval
            if (clarification.Contains("CLEAR:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[Orchestrator] Prompt is clear — skipping to approval");
                await BuildTopicAndTransitionToApproval(userPrompt, sessionId, ct);
            }

            return sessionId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EmitError("StartAsync", ex);
            await TryTransitionToFailed();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SendUserMessageAsync(string message, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            EnsureSession();

            // Record user message
            var userMsg = PanelMessage.Create(
                _session!.Id, Guid.Empty, "User", PanelAgentRole.Head,
                message, PanelMessageType.UserMessage);
            _session.AddMessage(userMsg);
            _eventStream.OnNext(new AgentMessageEvent(_session.Id, userMsg, DateTimeOffset.UtcNow));

            switch (CurrentPhase)
            {
                case PanelPhase.Clarifying:
                    await HandleClarificationReply(message, ct);
                    break;

                case PanelPhase.Completed:
                    await HandleFollowUpQuestion(message, ct);
                    break;

                default:
                    _logger.LogWarning(
                        "[Orchestrator] User message received in unexpected phase {Phase}", CurrentPhase);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EmitError("SendUserMessageAsync", ex);
            // Don't rethrow — session is still valid. UI sees the ErrorEvent and can retry.
            _logger.LogWarning(ex, "[Orchestrator] SendUserMessageAsync error handled gracefully.");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ApproveAndStartPanelAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            EnsureSession();

            if (CurrentPhase != PanelPhase.AwaitingApproval)
                throw new InvalidOperationException(
                    $"Cannot approve: current phase is {CurrentPhase}, expected AwaitingApproval.");

            _logger.LogInformation("[Orchestrator] User approved — preparing panelists");

            // Transition: AwaitingApproval → Preparing
            await _stateMachine!.FireAsync(PanelTrigger.UserApproved);

            // Create Moderator and Panelists
            await CreatePanelAgents(ct);

            // Transition: Preparing → Running
            await _stateMachine.FireAsync(PanelTrigger.PanelistsReady);

            // Start the discussion loop on a background task
            _discussionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _discussionTask = Task.Run(
                () => RunDiscussionLoopAsync(_discussionCts.Token),
                _discussionCts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EmitError("ApproveAndStartPanelAsync", ex);
            await TryTransitionToFailed();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task RejectPlanAsync(string? feedback, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            EnsureSession();

            if (CurrentPhase != PanelPhase.AwaitingApproval)
                throw new InvalidOperationException(
                    $"Cannot reject: current phase is {CurrentPhase}, expected AwaitingApproval.");

            _logger.LogInformation("[Orchestrator] User rejected plan — returning to Clarifying");

            // Transition: AwaitingApproval → Clarifying
            await _stateMachine!.FireAsync(PanelTrigger.UserRejected);

            // Build rejection context for the Head agent
            var rejectionPrompt = string.IsNullOrWhiteSpace(feedback)
                ? "The user rejected your proposed discussion plan. Please ask targeted clarification questions to understand what they want changed, then propose a revised plan."
                : $"The user rejected your proposed discussion plan with this feedback: \"{feedback}\"\n\nPlease ask targeted clarification questions to address their concerns, then propose a revised plan.";

            var response = await _headAgent!.ProcessClarificationReplyAsync(
                rejectionPrompt, _session!.Id, ct);

            var headMsg = PanelMessage.Create(
                _session.Id, _headAgent.Id, _headAgent.Name, PanelAgentRole.Head,
                response, PanelMessageType.Clarification);
            _session.AddMessage(headMsg);
            _eventStream.OnNext(new AgentMessageEvent(_session.Id, headMsg, DateTimeOffset.UtcNow));

            // If Head says "CLEAR:", transition back to approval
            if (response.Contains("CLEAR:", StringComparison.OrdinalIgnoreCase))
            {
                await BuildTopicAndTransitionToApproval(
                    _session.OriginalUserPrompt, _session.Id, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EmitError("RejectPlanAsync", ex);
            _logger.LogWarning(ex, "[Orchestrator] RejectPlanAsync error handled gracefully.");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task PauseAsync()
    {
        if (CurrentPhase != PanelPhase.Running)
            return;

        _logger.LogInformation("[Orchestrator] Pausing discussion");

        // Drain the pause gate semaphore to block the loop
        if (_pauseGate.CurrentCount > 0)
            await _pauseGate.WaitAsync();

        await _stateMachine!.FireAsync(PanelTrigger.UserPaused);

        foreach (var agent in _allAgents)
            await agent.PauseAsync();
    }

    /// <inheritdoc/>
    public async Task ResumeAsync()
    {
        if (CurrentPhase != PanelPhase.Paused)
            return;

        _logger.LogInformation("[Orchestrator] Resuming discussion");

        await _stateMachine!.FireAsync(PanelTrigger.UserResumed);

        foreach (var agent in _allAgents)
            await agent.ResumeAsync();

        // Release the pause gate to unblock the loop
        _pauseGate.Release();
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        if (CurrentPhase is PanelPhase.Idle or PanelPhase.Completed or PanelPhase.Stopped or PanelPhase.Failed)
            return;

        _logger.LogInformation("[Orchestrator] Stopping discussion");

        // Cancel the discussion loop
        _discussionCts?.Cancel();

        // Unblock if paused
        if (CurrentPhase == PanelPhase.Paused && _pauseGate.CurrentCount == 0)
            _pauseGate.Release();

        // Wait for the discussion task to complete
        if (_discussionTask is not null)
        {
            try { await _discussionTask; }
            catch (OperationCanceledException) { /* expected */ }
        }

        // Emit non-Thinking status for all agents so the UI stops blinking indicators.
        // Use the agent's actual status (Active/Contributed) for semantic correctness.
        if (_session is not null)
        {
            foreach (var agent in _allAgents)
            {
                if (agent is PanelAgentBase baseAgent && baseAgent.Status is not PanelAgentStatus.Disposed)
                {
                    // If agent is currently Thinking, emit its resting state;
                    // otherwise emit its current status as-is
                    var restingStatus = baseAgent.Status == PanelAgentStatus.Thinking
                        ? PanelAgentStatus.Contributed
                        : baseAgent.Status;

                    _eventStream.OnNext(new AgentStatusChangedEvent(
                        _session.Id, agent.Id, agent.Name,
                        agent.Role, restingStatus, DateTimeOffset.UtcNow));
                }
            }
        }

        if (_stateMachine?.CanFire(PanelTrigger.UserStopped) == true)
            await _stateMachine.FireAsync(PanelTrigger.UserStopped);
    }

    /// <inheritdoc/>
    public async Task ResetAsync()
    {
        _logger.LogInformation("[Orchestrator] Resetting panel");

        // Cancel any running discussion
        _discussionCts?.Cancel();
        if (CurrentPhase == PanelPhase.Paused && _pauseGate.CurrentCount == 0)
            _pauseGate.Release();

        if (_discussionTask is not null)
        {
            try { await _discussionTask; }
            catch (OperationCanceledException) { /* expected */ }
        }

        // Emit Disposed status for all agents so the UI clears their indicators
        if (_session is not null)
        {
            foreach (var agent in _allAgents)
            {
                _eventStream.OnNext(new AgentStatusChangedEvent(
                    _session.Id, agent.Id, agent.Name,
                    agent.Role, PanelAgentStatus.Disposed, DateTimeOffset.UtcNow));
            }
        }

        // Dispose all agents
        foreach (var agent in _allAgents)
            await agent.DisposeAsync();

        _allAgents.Clear();
        _panelistAgents.Clear();
        _headAgent = null;
        _moderatorAgent = null;
        _currentTurn = new TurnNumber(0);
        _discussionCts?.Dispose();
        _discussionCts = null;
        _discussionTask = null;

        // Dispose session
        if (_session is not null)
        {
            if (_stateMachine?.CanFire(PanelTrigger.Reset) == true)
                await _stateMachine.FireAsync(PanelTrigger.Reset);

            await _session.DisposeAsync();
            _session = null;
        }

        _stateMachine = null;
        _settings = null;
    }

    #region Discussion Loop

    /// <summary>
    /// The core discussion loop. Runs on a background task after ApproveAndStartPanelAsync.
    /// Each iteration: Moderator evaluates → selected panelist(s) respond → check convergence.
    /// </summary>
    private async Task RunDiscussionLoopAsync(CancellationToken ct)
    {
        var sessionId = _session!.Id;
        _logger.LogInformation(
            "[Orchestrator] Discussion loop started for session {SessionId}", sessionId);

        try
        {
            while (!ct.IsCancellationRequested && CurrentPhase == PanelPhase.Running)
            {
                // Check pause gate — blocks here if paused
                await _pauseGate.WaitAsync(ct);
                _pauseGate.Release();

                ct.ThrowIfCancellationRequested();

                if (CurrentPhase != PanelPhase.Running)
                    break;

                _currentTurn = _currentTurn.Increment();

                // Emit progress immediately so UI shows updated turn count
                EmitProgress();

                // 1. Moderator evaluates and decides next turn
                var decision = await _moderatorAgent!.DecideNextTurnAsync(
                    _session.Messages, _currentTurn, sessionId, ct);

                // 2. Check if Moderator says stop
                if (decision.StopDiscussion)
                {
                    _logger.LogInformation(
                        "[Orchestrator] Moderator stopped discussion: {Reason}", decision.Reason);
                    await _stateMachine!.FireAsync(PanelTrigger.ConvergenceDetected);
                    break;
                }

                // 3. Run panelist turn(s) — parallel or sequential based on Moderator decision
                bool forceConverge;
                if (decision.AllowParallelThinking && decision.ParallelGroup.Count >= 2)
                {
                    forceConverge = await ExecuteParallelTurnAsync(decision, sessionId, ct);
                }
                else
                {
                    var panelists = SelectPanelists(decision);
                    forceConverge = await ExecuteSequentialTurnAsync(panelists, sessionId, ct);
                }

                if (forceConverge)
                {
                    await _stateMachine!.FireAsync(PanelTrigger.ConvergenceDetected);
                    break;
                }

                // 4. Emit progress
                EmitProgress();

                // 5. Check convergence (heuristic)
                var convergence = await _convergenceDetector.CheckConvergenceAsync(
                    _session.Messages, _currentTurn, _session.GuardRails, ct);

                if (convergence.IsConverged)
                {
                    _logger.LogInformation(
                        "[Orchestrator] Convergence detected: Score={Score}, Reason={Reason}",
                        convergence.Score, convergence.Reason);
                    await _stateMachine!.FireAsync(PanelTrigger.ConvergenceDetected);
                    break;
                }

                // 6. Check turn limit
                if (_currentTurn.Exceeds(_session.GuardRails.MaxTurnsPerDiscussion))
                {
                    _logger.LogWarning("[Orchestrator] Turn limit exceeded — forcing convergence");
                    await _stateMachine!.FireAsync(PanelTrigger.ConvergenceDetected);
                    break;
                }
            }

            // If we converged, proceed to synthesis
            if (CurrentPhase == PanelPhase.Converging)
            {
                await _stateMachine!.FireAsync(PanelTrigger.StartSynthesis);
                await RunSynthesisAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[Orchestrator] Discussion loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Orchestrator] Discussion loop failed");
            EmitError("DiscussionLoop", ex);
            await TryTransitionToFailed();
        }
    }

    /// <summary>
    /// Execute a sequential turn: each panelist speaks one at a time, with moderation after each.
    /// </summary>
    /// <returns>True if convergence was forced during this turn.</returns>
    private async Task<bool> ExecuteSequentialTurnAsync(
        IReadOnlyList<IPanelAgent> panelists, PanelSessionId sessionId, CancellationToken ct)
    {
        foreach (var panelist in panelists)
        {
            ct.ThrowIfCancellationRequested();

            var input = new AgentInput(
                sessionId, _session!.Messages, string.Empty, _currentTurn, ToolOutputs: null);

            var output = await panelist.ProcessAsync(input, ct);

            _session.AddMessage(output.Message);
            _eventStream.OnNext(new AgentMessageEvent(sessionId, output.Message, DateTimeOffset.UtcNow));

            var modResult = await _moderatorAgent!.ValidateMessageAsync(
                output.Message, _currentTurn, sessionId, ct);

            if (modResult.Action == ModerationAction.ForceConverge)
            {
                _logger.LogInformation("[Orchestrator] Moderator forced convergence");
                return true;
            }

            if (modResult.Action == ModerationAction.Blocked)
            {
                _logger.LogWarning("[Orchestrator] Message blocked: {Reason}", modResult.Reason);
            }
        }

        return false;
    }

    /// <summary>
    /// Execute a parallel turn: panelists think concurrently via Task.WhenAll,
    /// then their messages are recorded sequentially to maintain conversation coherence.
    /// Falls back to sequential if any named panelist is not found.
    /// </summary>
    /// <returns>True if convergence was forced during this turn.</returns>
    private async Task<bool> ExecuteParallelTurnAsync(
        ModeratorDecision decision, PanelSessionId sessionId, CancellationToken ct)
    {
        // Resolve panelists by name from the parallel group
        var parallelAgents = decision.ParallelGroup
            .Select(name => _panelistAgents.FirstOrDefault(
                p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Where(p => p is not null)
            .Cast<IPanelAgent>()
            .ToList();

        // Fallback to sequential if we couldn't resolve at least 2 agents
        if (parallelAgents.Count < 2)
        {
            _logger.LogWarning(
                "[Orchestrator] Parallel group resolved only {Count} agents — falling back to sequential",
                parallelAgents.Count);
            var panelists = SelectPanelists(decision);
            return await ExecuteSequentialTurnAsync(panelists, sessionId, ct);
        }

        _logger.LogInformation(
            "[Orchestrator] ⚙ Parallel turn: {Names} ({Rationale})",
            string.Join(", ", parallelAgents.Select(a => a.Name)),
            decision.ParallelRationale ?? "orthogonal perspectives");

        _eventStream.OnNext(new CommentaryEvent(
            sessionId, Guid.Empty, "Orchestrator", PanelAgentRole.Moderator,
            $"⚙ Parallel thinking: {string.Join(", ", parallelAgents.Select(a => a.Name))}",
            CommentaryMode.Brief, DateTimeOffset.UtcNow));

        // All panelists think concurrently
        var input = new AgentInput(
            sessionId, _session!.Messages, string.Empty, _currentTurn, ToolOutputs: null);

        var tasks = parallelAgents
            .Select(p => p.ProcessAsync(input, ct))
            .ToList();

        var outputs = await Task.WhenAll(tasks);

        // Record messages sequentially to maintain coherent conversation order
        foreach (var output in outputs)
        {
            _session.AddMessage(output.Message);
            _eventStream.OnNext(new AgentMessageEvent(sessionId, output.Message, DateTimeOffset.UtcNow));

            var modResult = await _moderatorAgent!.ValidateMessageAsync(
                output.Message, _currentTurn, sessionId, ct);

            if (modResult.Action == ModerationAction.ForceConverge)
            {
                _logger.LogInformation("[Orchestrator] Moderator forced convergence during parallel turn");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Select panelists for the current turn based on Moderator decision.
    /// </summary>
    private IReadOnlyList<IPanelAgent> SelectPanelists(ModeratorDecision decision)
    {
        if (decision.NextSpeaker is not null)
        {
            var targeted = _panelistAgents.FirstOrDefault(
                p => p.Name.Equals(decision.NextSpeaker, StringComparison.OrdinalIgnoreCase));

            if (targeted is not null)
                return [targeted];
        }

        // Round-robin: all panelists speak each turn
        return _panelistAgents;
    }

    /// <summary>
    /// Run the synthesis phase using the Head agent, then generate a KnowledgeBrief
    /// for follow-up Q&amp;A.
    /// </summary>
    /// <remarks>
    /// For long discussions, panelist messages are compressed before synthesis to stay
    /// within LLM context limits and avoid streaming timeouts. Each message is truncated
    /// to ~500 chars and only the most recent 40 messages are sent in full — older
    /// messages are represented as one-line summaries.
    /// </remarks>
    private async Task RunSynthesisAsync(CancellationToken ct)
    {
        var sessionId = _session!.Id;
        _logger.LogInformation("[Orchestrator] Starting synthesis");

        var panelistMessages = _session.Messages
            .Where(m => m.AuthorRole == PanelAgentRole.Panelist)
            .ToList();

        // Compress messages for synthesis to avoid timeout on long discussions.
        // Keep the last 40 messages in detail; summarize older ones.
        var compressedMessages = CompressForSynthesis(panelistMessages, maxDetailMessages: 40, maxContentLength: 500);

        var synthesis = await _headAgent!.SynthesizeAsync(compressedMessages, sessionId, ct);

        var synthMessage = PanelMessage.Create(
            sessionId, _headAgent.Id, _headAgent.Name, PanelAgentRole.Head,
            synthesis, PanelMessageType.Synthesis);
        _session.AddMessage(synthMessage);
        _eventStream.OnNext(new AgentMessageEvent(sessionId, synthMessage, DateTimeOffset.UtcNow));

        // Generate KnowledgeBrief for follow-up Q&A
        try
        {
            _logger.LogInformation("[Orchestrator] Generating KnowledgeBrief for follow-up Q&A");
            var brief = await _knowledgeBriefService.GenerateAsync(_session, ct);
            _headAgent.KnowledgeBrief = brief;
            _logger.LogInformation(
                "[Orchestrator] KnowledgeBrief generated: {KeyArgs} arguments, {Consensus} consensus points",
                brief.KeyArguments.Count, brief.ConsensusPoints.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Non-fatal: synthesis succeeded, follow-up just won't use structured brief.
            // HeadAgent.AnswerFollowUpAsync will show error message but session is still valid.
            _logger.LogWarning(ex,
                "[Orchestrator] KnowledgeBrief generation failed — follow-up Q&A may be limited");
        }

        // Transition: Synthesizing → Completed
        await _stateMachine!.FireAsync(PanelTrigger.SynthesisComplete);

        _logger.LogInformation(
            "[Orchestrator] Panel completed: {TurnCount} turns, {MessageCount} messages",
            _currentTurn, _session.Messages.Count);
    }

    /// <summary>
    /// Compress panelist messages for synthesis to avoid LLM context overflow and streaming timeouts.
    /// Keeps <paramref name="maxDetailMessages"/> most recent messages with content capped at
    /// <paramref name="maxContentLength"/>. Older messages are collapsed to a one-line summary.
    /// </summary>
    private static IReadOnlyList<Domain.Entities.PanelMessage> CompressForSynthesis(
        IReadOnlyList<Domain.Entities.PanelMessage> messages,
        int maxDetailMessages,
        int maxContentLength)
    {
        if (messages.Count <= maxDetailMessages)
            return messages;

        var result = new List<Domain.Entities.PanelMessage>(maxDetailMessages + 1);

        // Summarize older messages as a single aggregate message
        var olderMessages = messages.Take(messages.Count - maxDetailMessages).ToList();
        var olderSummary = string.Join("\n",
            olderMessages.Select(m =>
            {
                var snippet = m.Content.Length > 120
                    ? m.Content[..120].Replace("\n", " ") + "..."
                    : m.Content.Replace("\n", " ");
                return $"- {m.AuthorName}: {snippet}";
            }));

        var summaryMsg = Domain.Entities.PanelMessage.Create(
            messages[0].SessionId,
            Guid.Empty,
            "Summary",
            PanelAgentRole.Panelist,
            $"[Condensed summary of {olderMessages.Count} earlier messages]\n{olderSummary}",
            PanelMessageType.PanelistArgument);
        result.Add(summaryMsg);

        // Add recent messages with content truncation
        var recentMessages = messages.Skip(messages.Count - maxDetailMessages);
        foreach (var msg in recentMessages)
        {
            if (msg.Content.Length > maxContentLength)
            {
                var truncated = Domain.Entities.PanelMessage.Create(
                    msg.SessionId, msg.AuthorAgentId, msg.AuthorName, msg.AuthorRole,
                    msg.Content[..maxContentLength] + "\n[...truncated]",
                    msg.Type);
                result.Add(truncated);
            }
            else
            {
                result.Add(msg);
            }
        }

        return result;
    }

    #endregion

    #region Clarification Handling

    private async Task HandleClarificationReply(string message, CancellationToken ct)
    {
        var sessionId = _session!.Id;
        var response = await _headAgent!.ProcessClarificationReplyAsync(message, sessionId, ct);

        var headMsg = PanelMessage.Create(
            sessionId, _headAgent.Id, _headAgent.Name, PanelAgentRole.Head,
            response, PanelMessageType.Clarification);
        _session.AddMessage(headMsg);
        _eventStream.OnNext(new AgentMessageEvent(sessionId, headMsg, DateTimeOffset.UtcNow));

        // If Head says "CLEAR:", transition to approval
        if (response.Contains("CLEAR:", StringComparison.OrdinalIgnoreCase))
        {
            await BuildTopicAndTransitionToApproval(
                _session.OriginalUserPrompt, sessionId, ct);
        }
    }

    private async Task BuildTopicAndTransitionToApproval(
        string originalPrompt, PanelSessionId sessionId, CancellationToken ct)
    {
        var clarificationMessages = _session!.Messages
            .Where(m => m.Type == PanelMessageType.Clarification
                     || m.Type == PanelMessageType.UserMessage)
            .ToList();

        var topic = await _headAgent!.BuildTopicOfDiscussionAsync(
            originalPrompt, clarificationMessages, sessionId, ct);

        _session.SetRefinedTopic(topic);

        var topicMsg = PanelMessage.Create(
            sessionId, _headAgent.Id, _headAgent.Name, PanelAgentRole.Head,
            topic, PanelMessageType.TopicOfDiscussion);
        _session.AddMessage(topicMsg);
        _eventStream.OnNext(new AgentMessageEvent(sessionId, topicMsg, DateTimeOffset.UtcNow));

        // Transition: Clarifying → AwaitingApproval
        await _stateMachine!.FireAsync(PanelTrigger.ClarificationsComplete);
    }

    private async Task HandleFollowUpQuestion(string question, CancellationToken ct)
    {
        var sessionId = _session!.Id;
        var answer = await _headAgent!.AnswerFollowUpAsync(question, sessionId, ct);

        var answerMsg = PanelMessage.Create(
            sessionId, _headAgent.Id, _headAgent.Name, PanelAgentRole.Head,
            answer, PanelMessageType.Clarification);
        _session.AddMessage(answerMsg);
        _eventStream.OnNext(new AgentMessageEvent(sessionId, answerMsg, DateTimeOffset.UtcNow));
    }

    #endregion

    #region Agent Creation

    private async Task CreatePanelAgents(CancellationToken ct)
    {
        // Create Moderator
        var modAgent = await _agentFactory.CreateModeratorAgentAsync(_settings!, ct);
        _moderatorAgent = (ModeratorAgent)modAgent;
        _allAgents.Add(modAgent);

        _session!.RegisterAgent(new AgentInstance(
            "Moderator", PanelAgentRole.Moderator,
            new ModelIdentifier("copilot", _settings!.PrimaryModel)));

        // Emit Active status for Moderator — ready to participate, hasn't contributed yet
        _eventStream.OnNext(new AgentStatusChangedEvent(
            _session.Id, modAgent.Id, modAgent.Name,
            PanelAgentRole.Moderator, PanelAgentStatus.Active, DateTimeOffset.UtcNow));

        // Select panelist profiles
        var profiles = SelectPanelistProfiles(_settings);

        // Create panelists
        foreach (var profile in profiles)
        {
            ct.ThrowIfCancellationRequested();

            var panelist = await _agentFactory.CreatePanelistAgentAsync(profile, _settings, ct);
            _panelistAgents.Add(panelist);
            _allAgents.Add(panelist);

            var model = _settings.PanelistModels is { Count: > 0 } models
                ? models[Math.Abs(profile.Id.GetHashCode()) % models.Count]
                : _settings.PrimaryModel;

            _session.RegisterAgent(new AgentInstance(
                profile.DisplayName, PanelAgentRole.Panelist,
                new ModelIdentifier("copilot", model)));

            // Emit Active status for each panelist — ready to participate, hasn't contributed yet
            _eventStream.OnNext(new AgentStatusChangedEvent(
                _session.Id, panelist.Id, panelist.Name,
                PanelAgentRole.Panelist, PanelAgentStatus.Active, DateTimeOffset.UtcNow));
        }

        // Emit Contributed status for the Head agent — it already spoke during clarification
        if (_headAgent is not null)
        {
            _eventStream.OnNext(new AgentStatusChangedEvent(
                _session.Id, _headAgent.Id, _headAgent.Name,
                PanelAgentRole.Head, PanelAgentStatus.Contributed, DateTimeOffset.UtcNow));
        }

        // Wire SetActivePanelSession on all agents so SendToLlmAsync emits status events
        foreach (var agent in _allAgents)
        {
            if (agent is PanelAgentBase baseAgent)
                baseAgent.SetActivePanelSession(_session.Id);
        }

        _logger.LogInformation(
            "[Orchestrator] Created {Count} panelists: {Names}",
            _panelistAgents.Count,
            string.Join(", ", _panelistAgents.Select(p => p.Name)));
    }

    /// <summary>
    /// Select panelist profiles based on settings. Uses the Balanced panel by default,
    /// capped to MaxPanelists.
    /// </summary>
    private static IReadOnlyList<PanelistProfile> SelectPanelistProfiles(PanelSettings settings)
    {
        var maxPanelists = Math.Clamp(settings.MaxPanelists, 2, 8);
        return DefaultPanelistProfiles.BalancedPanel
            .Take(maxPanelists)
            .ToList();
    }

    #endregion

    #region Helpers

    private void EnsureSession()
    {
        if (_session is null || _stateMachine is null)
            throw new InvalidOperationException("No active panel session. Call StartAsync first.");
    }

    private void EmitProgress()
    {
        if (_session is null) return;

        var activePanelists = _panelistAgents.Count(p => p.Status is PanelAgentStatus.Active
            or PanelAgentStatus.Thinking or PanelAgentStatus.Idle or PanelAgentStatus.Contributed);

        _eventStream.OnNext(new ProgressEvent(
            _session.Id,
            _currentTurn.Value,
            _session.GuardRails.MaxTurnsPerDiscussion,
            activePanelists,
            _panelistAgents.Count - activePanelists,
            DateTimeOffset.UtcNow));
    }

    private void EmitError(string source, Exception ex)
    {
        if (_session is null) return;

        _eventStream.OnNext(new ErrorEvent(
            _session.Id, source, ex.Message, ex, DateTimeOffset.UtcNow));
    }

    private async Task TryTransitionToFailed()
    {
        try
        {
            if (_stateMachine?.CanFire(PanelTrigger.Error) == true)
                await _stateMachine.FireAsync(PanelTrigger.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Orchestrator] Failed to transition to Failed state");
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    /// <summary>
    /// Parse the DISCUSSION_DEPTH tag from the Head agent's clarification response.
    /// Returns <see cref="DiscussionDepth.Standard"/> if the tag is absent or unrecognized.
    /// </summary>
    private static DiscussionDepth ParseDiscussionDepth(string headResponse)
    {
        var match = Regex.Match(headResponse, @"DISCUSSION_DEPTH:\s*(Quick|Standard|Deep)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        if (!match.Success)
            return DiscussionDepth.Standard;

        return match.Groups[1].Value.ToLowerInvariant() switch
        {
            "quick" => DiscussionDepth.Quick,
            "deep" => DiscussionDepth.Deep,
            _ => DiscussionDepth.Standard
        };
    }

    /// <summary>
    /// Apply depth-specific presets to the settings. Modifies MaxTurns and ConvergenceThreshold
    /// based on the detected depth. Standard is a no-op (uses user-configured values).
    /// </summary>
    private static void ApplyDepthPreset(PanelSettings settings, DiscussionDepth depth)
    {
        switch (depth)
        {
            case DiscussionDepth.Quick:
                settings.MaxTurns = Math.Min(settings.MaxTurns, 10);
                settings.ConvergenceThreshold = 60;
                break;

            case DiscussionDepth.Deep:
                settings.MaxTurns = Math.Max(settings.MaxTurns, 50);
                settings.ConvergenceThreshold = 90;
                break;

            case DiscussionDepth.Standard:
            default:
                // No changes — use configured values
                break;
        }
    }

    #endregion

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await ResetAsync();

        _lock.Dispose();
        _pauseGate.Dispose();
    }
}