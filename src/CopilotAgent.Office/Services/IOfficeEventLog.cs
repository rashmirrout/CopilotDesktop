using CopilotAgent.Office.Events;
using CopilotAgent.Office.Models;

namespace CopilotAgent.Office.Services;

/// <summary>
/// In-memory event log with query support for Office events.
/// </summary>
public interface IOfficeEventLog
{
    /// <summary>Log an event.</summary>
    /// <param name="officeEvent">The event to log.</param>
    void Log(OfficeEvent officeEvent);

    /// <summary>Get all logged events.</summary>
    IReadOnlyList<OfficeEvent> GetAll();

    /// <summary>Get events for a specific iteration.</summary>
    /// <param name="iterationNumber">The iteration number to filter by.</param>
    IReadOnlyList<OfficeEvent> GetByIteration(int iterationNumber);

    /// <summary>Get events of a specific type.</summary>
    /// <param name="eventType">The event type to filter by.</param>
    IReadOnlyList<OfficeEvent> GetByType(OfficeEventType eventType);

    /// <summary>Get all scheduling decisions from the log.</summary>
    IReadOnlyList<SchedulingDecision> GetSchedulingLog();

    /// <summary>Clear all logged events.</summary>
    void Clear();
}