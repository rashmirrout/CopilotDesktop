namespace CopilotAgent.Panel.Domain.Enums;

/// <summary>
/// Lifecycle status of an agent instance.
///
/// STATE TRANSITIONS:
///   Created → Active     (session initialized)
///   Active → Thinking    (LLM call started, first turn)
///   Thinking → Contributed (LLM call succeeded — agent has spoken at least once)
///   Contributed → Thinking (subsequent LLM calls)
///   Thinking → Active    (LLM call failed, agent hadn't contributed yet)
///   Thinking → Contributed (LLM call failed, agent had already contributed)
///   Active/Thinking/Contributed → Paused (user paused discussion)
///   Paused → Active      (resumed, hadn't contributed)
///   Paused → Contributed  (resumed, had contributed)
///   Any → Disposed        (agent terminated)
///
/// SEMANTIC DISTINCTION:
///   Active      = initialized, ready to participate, hasn't spoken yet
///   Contributed = has completed at least one turn, waiting for next turn
///   Idle        = legacy/transitional; prefer Active or Contributed
/// </summary>
public enum PanelAgentStatus
{
    /// <summary>Agent constructed but session not yet initialized.</summary>
    Created,

    /// <summary>Session initialized, ready to participate, hasn't spoken yet.</summary>
    Active,

    /// <summary>Currently calling the LLM / performing work.</summary>
    Thinking,

    /// <summary>
    /// Legacy transitional state. Prefer <see cref="Active"/> or <see cref="Contributed"/>.
    /// Retained for backward compatibility with external event consumers.
    /// </summary>
    Idle,

    /// <summary>Agent has completed at least one turn and is waiting for its next turn.</summary>
    Contributed,

    /// <summary>User paused the discussion; agent is suspended.</summary>
    Paused,

    /// <summary>Agent terminated and session cleaned up.</summary>
    Disposed
}
