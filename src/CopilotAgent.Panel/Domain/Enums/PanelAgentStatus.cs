namespace CopilotAgent.Panel.Domain.Enums;

/// <summary>
/// Lifecycle status of an agent instance.
/// </summary>
public enum PanelAgentStatus
{
    Created,
    Active,
    Thinking,
    Idle,
    Paused,
    Disposed
}