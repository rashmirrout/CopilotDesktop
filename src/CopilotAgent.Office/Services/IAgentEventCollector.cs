using CopilotAgent.Office.Models;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Collects SDK session events (tool calls, completions) during assistant execution
/// and produces a list of <see cref="ToolExecution"/> records for the result.
///
/// Implementations subscribe to the concrete CopilotSdkService.SessionEventReceived
/// event, filtering by session ID, and track tool start/complete pairs.
/// </summary>
public interface IAgentEventCollector : IDisposable
{
    /// <summary>
    /// Begins collecting events for the given session ID.
    /// Call <see cref="Complete"/> when the assistant finishes to get the results.
    /// </summary>
    void Start(string sessionId);

    /// <summary>
    /// Stops collecting and returns all captured tool executions.
    /// </summary>
    IReadOnlyList<ToolExecution> Complete();
}