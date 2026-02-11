using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.Policies;
using CopilotAgent.Panel.Domain.ValueObjects;

namespace CopilotAgent.Panel.Domain.Entities;

/// <summary>
/// Aggregate root for a panel discussion. Encapsulates all state for one discussion
/// session including phase, agents, messages, and configuration.
/// 
/// IMMUTABLE AFTER COMPLETION: Once phase reaches Completed/Stopped/Failed,
/// only Reset transitions are allowed.
/// 
/// THREAD SAFETY: Internal collections are guarded by PanelOrchestrator.
/// Direct mutation is only allowed from the orchestrator's execution context.
/// </summary>
public sealed class PanelSession : IAsyncDisposable
{
    public PanelSessionId Id { get; }
    public PanelPhase Phase { get; private set; }
    public string OriginalUserPrompt { get; }
    public string? RefinedTopicOfDiscussion { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public GuardRailPolicy GuardRails { get; }

    private readonly List<PanelMessage> _messages = [];
    public IReadOnlyList<PanelMessage> Messages => _messages.AsReadOnly();

    private readonly List<AgentInstance> _agents = [];
    public IReadOnlyList<AgentInstance> Agents => _agents.AsReadOnly();

    public PanelSession(
        PanelSessionId id,
        string userPrompt,
        GuardRailPolicy guardRails)
    {
        Id = id;
        OriginalUserPrompt = userPrompt
            ?? throw new ArgumentNullException(nameof(userPrompt));
        GuardRails = guardRails
            ?? throw new ArgumentNullException(nameof(guardRails));
        Phase = PanelPhase.Idle;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void TransitionTo(PanelPhase newPhase)
    {
        Phase = newPhase;
        if (newPhase is PanelPhase.Completed or PanelPhase.Stopped or PanelPhase.Failed)
            CompletedAt = DateTimeOffset.UtcNow;
    }

    public void SetRefinedTopic(string topic) =>
        RefinedTopicOfDiscussion = topic
            ?? throw new ArgumentNullException(nameof(topic));

    public void AddMessage(PanelMessage message) => _messages.Add(message);
    public void RegisterAgent(AgentInstance agent) => _agents.Add(agent);
    public void UnregisterAgent(AgentInstance agent) => _agents.Remove(agent);

    public async ValueTask DisposeAsync()
    {
        _messages.Clear();
        foreach (var agent in _agents)
            agent.MarkDisposed();
        _agents.Clear();
        await ValueTask.CompletedTask;
    }
}