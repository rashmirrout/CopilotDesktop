using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.ValueObjects;

namespace CopilotAgent.Panel.Domain.Entities;

/// <summary>
/// Tracks the identity and status of an agent within a panel session.
/// This is a domain descriptor — the actual LLM session is managed by PanelAgentBase.
/// </summary>
public sealed class AgentInstance
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public PanelAgentRole Role { get; }
    public ModelIdentifier Model { get; }
    public PanelAgentStatus Status { get; private set; } = PanelAgentStatus.Created;
    public int TurnsCompleted { get; private set; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    public AgentInstance(string name, PanelAgentRole role, ModelIdentifier model)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Role = role;
        Model = model;
    }

    public void Activate() => Status = PanelAgentStatus.Active;
    public void SetThinking() => Status = PanelAgentStatus.Thinking;
    public void SetIdle() => Status = PanelAgentStatus.Idle;
    public void SetPaused() => Status = PanelAgentStatus.Paused;
    public void IncrementTurn() => TurnsCompleted++;
    public void MarkDisposed() => Status = PanelAgentStatus.Disposed;
}