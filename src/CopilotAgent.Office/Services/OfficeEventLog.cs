using CopilotAgent.Office.Events;
using CopilotAgent.Office.Models;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Thread-safe in-memory event log with query support.
/// </summary>
public sealed class OfficeEventLog : IOfficeEventLog
{
    private readonly List<OfficeEvent> _events = [];
    private readonly object _lock = new();

    /// <inheritdoc />
    public void Log(OfficeEvent officeEvent)
    {
        ArgumentNullException.ThrowIfNull(officeEvent);
        lock (_lock)
        {
            _events.Add(officeEvent);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<OfficeEvent> GetAll()
    {
        lock (_lock)
        {
            return [.. _events];
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<OfficeEvent> GetByIteration(int iterationNumber)
    {
        lock (_lock)
        {
            return _events.Where(e => e.IterationNumber == iterationNumber).ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<OfficeEvent> GetByType(OfficeEventType eventType)
    {
        lock (_lock)
        {
            return _events.Where(e => e.EventType == eventType).ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SchedulingDecision> GetSchedulingLog()
    {
        lock (_lock)
        {
            return _events
                .OfType<SchedulingEvent>()
                .Select(e => e.Decision)
                .ToList();
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
        }
    }
}