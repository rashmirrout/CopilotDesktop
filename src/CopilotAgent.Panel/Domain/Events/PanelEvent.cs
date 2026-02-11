using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.Entities;
using CopilotAgent.Panel.Domain.ValueObjects;

namespace CopilotAgent.Panel.Domain.Events;

/// <summary>
/// Base class for all panel events. Carries session context and timestamp.
/// All events are immutable records — safe to share across threads.
/// 
/// UI subscribes via IObservable{PanelEvent} and filters by subtype.
/// </summary>
public abstract record PanelEvent(
    PanelSessionId SessionId,
    DateTimeOffset Timestamp);