using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.Office.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Subscribes to <see cref="CopilotSdkService.StreamingProgressChanged"/> to track
/// tool start/complete pairs during an assistant's execution, producing a list of
/// <see cref="ToolExecution"/> records.
///
/// The collector uses <see cref="StreamingProgressEventArgs"/> which carries
/// <c>CurrentToolName</c>, <c>ToolsCompletedCount</c>, and <c>State</c> — all
/// defined in Core, avoiding a direct dependency on the GitHub.Copilot.SDK package.
///
/// If the <see cref="ICopilotService"/> is not a <see cref="CopilotSdkService"/>
/// (e.g., CLI-based fallback), the collector gracefully returns an empty list.
/// </summary>
public sealed class AgentEventCollector : IAgentEventCollector
{
    private readonly CopilotSdkService? _sdkService;
    private readonly ILogger _logger;

    private string? _sessionId;
    private bool _collecting;

    // Tracking state
    private string? _currentToolName;
    private DateTimeOffset _currentToolStartedAt;
    private int _lastToolsCompletedCount;
    private readonly List<ToolExecution> _executions = [];
    private readonly object _lock = new();

    public AgentEventCollector(ICopilotService copilotService, ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sdkService = copilotService as CopilotSdkService;

        if (_sdkService is null)
        {
            _logger.LogDebug("AgentEventCollector: ICopilotService is not CopilotSdkService — tool tracking disabled");
        }
    }

    /// <inheritdoc />
    public void Start(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        lock (_lock)
        {
            _sessionId = sessionId;
            _collecting = true;
            _currentToolName = null;
            _lastToolsCompletedCount = 0;
            _executions.Clear();
        }

        if (_sdkService is not null)
        {
            _sdkService.StreamingProgressChanged += OnProgressChanged;
            _logger.LogDebug("AgentEventCollector: Started collecting for session {SessionId}", sessionId);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolExecution> Complete()
    {
        if (_sdkService is not null)
        {
            _sdkService.StreamingProgressChanged -= OnProgressChanged;
        }

        lock (_lock)
        {
            _collecting = false;

            // Finalize any in-flight tool execution
            if (_currentToolName is not null)
            {
                _executions.Add(new ToolExecution
                {
                    ToolName = _currentToolName,
                    StartedAt = _currentToolStartedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Success = true, // Assume success if we didn't get an explicit failure
                    Description = "Completed (finalized at collection end)"
                });
                _currentToolName = null;
            }

            _logger.LogDebug(
                "AgentEventCollector: Completed for session {SessionId} — {Count} tool executions captured",
                _sessionId, _executions.Count);

            return _executions.ToList().AsReadOnly();
        }
    }

    private void OnProgressChanged(object? sender, StreamingProgressEventArgs e)
    {
        lock (_lock)
        {
            if (!_collecting || e.SessionId != _sessionId)
                return;

            // Detect tool start: state is ToolExecuting and we have a new tool name
            if (e.State == SessionStreamingState.ToolExecuting
                && e.CurrentToolName is not null
                && e.CurrentToolName != _currentToolName)
            {
                // If there was a previous in-flight tool, close it out
                if (_currentToolName is not null)
                {
                    _executions.Add(new ToolExecution
                    {
                        ToolName = _currentToolName,
                        StartedAt = _currentToolStartedAt,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Success = true,
                        Description = "Completed (superseded by next tool)"
                    });
                }

                _currentToolName = e.CurrentToolName;
                _currentToolStartedAt = DateTimeOffset.UtcNow;
                _logger.LogDebug("AgentEventCollector: Tool started — {Tool}", _currentToolName);
            }

            // Detect tool complete: completed count increased
            if (e.ToolsCompletedCount > _lastToolsCompletedCount)
            {
                if (_currentToolName is not null)
                {
                    _executions.Add(new ToolExecution
                    {
                        ToolName = _currentToolName,
                        StartedAt = _currentToolStartedAt,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Success = true,
                        Description = "Completed"
                    });

                    _logger.LogDebug(
                        "AgentEventCollector: Tool completed — {Tool} ({Duration})",
                        _currentToolName, DateTimeOffset.UtcNow - _currentToolStartedAt);

                    _currentToolName = null;
                }

                _lastToolsCompletedCount = e.ToolsCompletedCount;
            }
        }
    }

    public void Dispose()
    {
        if (_sdkService is not null && _collecting)
        {
            _sdkService.StreamingProgressChanged -= OnProgressChanged;
        }

        _collecting = false;
    }
}