using CopilotAgent.Panel.Domain.ValueObjects;

namespace CopilotAgent.Panel.Domain.Events;

public sealed record ErrorEvent(
    PanelSessionId SessionId,
    string Source,
    string ErrorMessage,
    Exception? Exception,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);