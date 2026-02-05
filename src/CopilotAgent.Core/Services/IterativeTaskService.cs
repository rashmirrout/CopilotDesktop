using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using CopilotAgent.Core.Models;
using GitHub.Copilot.SDK;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Service for managing iterative agent tasks with SDK event integration.
/// Captures tool executions, reasoning, and assistant messages from the SDK
/// to provide detailed visibility into what the agent performed.
/// </summary>
public class IterativeTaskService : IIterativeTaskService
{
    private readonly ICopilotService _copilotService;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<IterativeTaskService> _logger;
    private readonly ConcurrentDictionary<string, IterativeTaskConfig> _tasks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();
    private readonly ConcurrentDictionary<string, IterationResult> _currentIterations = new();
    private readonly ConcurrentDictionary<string, bool> _runningTasks = new();
    
    // Event handler reference for cleanup
    private EventHandler<SdkSessionEventArgs>? _sdkEventHandler;

    public event EventHandler<TaskStatusChangedEventArgs>? TaskStatusChanged;
    public event EventHandler<IterationCompletedEventArgs>? IterationCompleted;
    public event EventHandler<IterationProgressEventArgs>? IterationProgress;

    public IterativeTaskService(
        ICopilotService copilotService,
        ISessionManager sessionManager,
        ILogger<IterativeTaskService> logger)
    {
        _copilotService = copilotService;
        _sessionManager = sessionManager;
        _logger = logger;
        
        // Subscribe to SDK events if the service supports it
        if (_copilotService is CopilotSdkService sdkService)
        {
            _sdkEventHandler = OnSdkSessionEvent;
            sdkService.SessionEventReceived += _sdkEventHandler;
            _logger.LogInformation("IterativeTaskService subscribed to SDK session events");
        }
        else
        {
            _logger.LogWarning("CopilotService is not SDK-based; tool execution tracking will be limited");
        }
    }

    /// <summary>
    /// Handles SDK session events to capture tool executions and reasoning
    /// </summary>
    private void OnSdkSessionEvent(object? sender, SdkSessionEventArgs args)
    {
        // Only process events for sessions that have active iterations
        if (!_currentIterations.TryGetValue(args.SessionId, out var currentIteration))
        {
            return; // Not tracking this session
        }

        try
        {
            ProcessSdkEvent(args.SessionId, currentIteration, args.Event);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing SDK event for session {SessionId}", args.SessionId);
        }
    }

    /// <summary>
    /// Processes individual SDK events and updates the current iteration
    /// </summary>
    private void ProcessSdkEvent(string sessionId, IterationResult iteration, SessionEvent evt)
    {
        switch (evt)
        {
            case ToolExecutionStartEvent startEvent:
                HandleToolStart(sessionId, iteration, startEvent);
                break;
                
            case ToolExecutionCompleteEvent completeEvent:
                HandleToolComplete(sessionId, iteration, completeEvent);
                break;
                
            case ToolExecutionProgressEvent progressEvent:
                // Update progress message for ongoing tool
                var progressTool = iteration.ToolExecutions
                    .FirstOrDefault(t => t.ToolCallId == progressEvent.Data?.ToolCallId);
                if (progressTool != null && progressEvent.Data?.ProgressMessage != null)
                {
                    _logger.LogDebug("Tool {Tool} progress: {Message}", 
                        progressTool.ToolName, progressEvent.Data.ProgressMessage);
                }
                break;
                
            case AssistantReasoningEvent reasoningEvent:
                HandleReasoning(sessionId, iteration, reasoningEvent);
                break;
                
            case AssistantMessageEvent messageEvent:
                HandleAssistantMessage(sessionId, iteration, messageEvent);
                break;
                
            case AssistantTurnStartEvent:
                _logger.LogDebug("Assistant turn started for session {SessionId}, iteration {Iter}", 
                    sessionId, iteration.IterationNumber);
                break;
                
            case AssistantTurnEndEvent:
                _logger.LogDebug("Assistant turn ended for session {SessionId}, iteration {Iter}", 
                    sessionId, iteration.IterationNumber);
                break;
                
            case SessionIdleEvent:
                _logger.LogDebug("Session idle for {SessionId}", sessionId);
                break;
        }
    }

    private void HandleToolStart(string sessionId, IterationResult iteration, ToolExecutionStartEvent startEvent)
    {
        var data = startEvent.Data;
        if (data == null) return;

        var toolDetail = new ToolExecutionDetail
        {
            ToolCallId = data.ToolCallId ?? Guid.NewGuid().ToString(),
            ToolName = data.ToolName ?? "unknown",
            DisplayName = ToolExecutionDetail.GetFriendlyToolName(data.ToolName ?? "unknown"),
            Arguments = data.Arguments,
            ArgumentsSummary = ToolArgumentSummarizer.Summarize(data.ToolName ?? "", data.Arguments),
            StartTime = DateTime.UtcNow,
            McpServerName = data.McpServerName
        };

        iteration.ToolExecutions.Add(toolDetail);
        iteration.CurrentToolName = toolDetail.DisplayName;

        _logger.LogInformation("Tool started: {Tool} (ID: {ToolCallId}) for session {SessionId}, iteration {Iter}",
            toolDetail.ToolName, toolDetail.ToolCallId, sessionId, iteration.IterationNumber);

        // Fire progress event
        IterationProgress?.Invoke(this, new IterationProgressEventArgs(
            sessionId,
            iteration.IterationNumber,
            IterationProgressType.ToolStarted,
            toolDetail.ToolName,
            toolDetail.ToolCallId,
            $"Executing: {toolDetail.DisplayName}"));
    }

    private void HandleToolComplete(string sessionId, IterationResult iteration, ToolExecutionCompleteEvent completeEvent)
    {
        var data = completeEvent.Data;
        if (data == null) return;

        var tool = iteration.ToolExecutions
            .FirstOrDefault(t => t.ToolCallId == data.ToolCallId);
        
        if (tool == null)
        {
            _logger.LogWarning("Tool complete event received for unknown tool ID: {ToolCallId}", data.ToolCallId);
            return;
        }

        tool.EndTime = DateTime.UtcNow;
        tool.Success = data.Success;
        tool.Result = data.Result?.Content;
        tool.DetailedResult = data.Result?.DetailedContent;
        tool.Error = data.Error?.Message;
        
        // Convert telemetry if present
        if (data.ToolTelemetry != null)
        {
            tool.Telemetry = new Dictionary<string, object>(data.ToolTelemetry);
        }

        iteration.CurrentToolName = null;

        _logger.LogInformation("Tool completed: {Tool} (Success: {Success}, Duration: {Duration}ms)",
            tool.ToolName, tool.Success, tool.Duration.TotalMilliseconds);

        // Fire progress event
        IterationProgress?.Invoke(this, new IterationProgressEventArgs(
            sessionId,
            iteration.IterationNumber,
            IterationProgressType.ToolCompleted,
            tool.ToolName,
            tool.ToolCallId,
            tool.Success 
                ? $"Completed: {tool.DisplayName}" 
                : $"Failed: {tool.DisplayName} - {tool.Error}"));
    }

    private void HandleReasoning(string sessionId, IterationResult iteration, AssistantReasoningEvent reasoningEvent)
    {
        var data = reasoningEvent.Data;
        if (data?.Content == null) return;

        var step = new ReasoningStep
        {
            Timestamp = DateTime.UtcNow,
            Content = data.Content,
            ReasoningType = "thinking"
        };

        iteration.AgentReasoning.Add(step);

        _logger.LogDebug("Agent reasoning captured for session {SessionId}: {Preview}...",
            sessionId, data.Content.Length > 50 ? data.Content[..50] : data.Content);

        // Fire progress event
        IterationProgress?.Invoke(this, new IterationProgressEventArgs(
            sessionId,
            iteration.IterationNumber,
            IterationProgressType.Reasoning,
            message: "Agent is reasoning..."));
    }

    private void HandleAssistantMessage(string sessionId, IterationResult iteration, AssistantMessageEvent messageEvent)
    {
        var data = messageEvent.Data;
        if (data == null) return;

        // Capture the assistant's final message as part of the iteration result
        if (!string.IsNullOrEmpty(data.Content))
        {
            iteration.Result = data.Content.Length > 500 
                ? data.Content[..500] + "..." 
                : data.Content;
        }

        // If the message contains reasoning text, capture it
        if (!string.IsNullOrEmpty(data.ReasoningText))
        {
            iteration.AgentReasoning.Add(new ReasoningStep
            {
                Timestamp = DateTime.UtcNow,
                Content = data.ReasoningText,
                ReasoningType = "conclusion"
            });
        }

        _logger.LogDebug("Assistant message received for session {SessionId}, iteration {Iter}",
            sessionId, iteration.IterationNumber);

        // Fire progress event
        IterationProgress?.Invoke(this, new IterationProgressEventArgs(
            sessionId,
            iteration.IterationNumber,
            IterationProgressType.AssistantMessage,
            message: "Response received"));
    }

    public IterativeTaskConfig? GetTask(string sessionId)
    {
        _tasks.TryGetValue(sessionId, out var task);
        return task;
    }

    public IterativeTaskConfig CreateTask(string sessionId, string taskDescription, string successCriteria, int maxIterations = 10)
    {
        var task = new IterativeTaskConfig
        {
            TaskDescription = taskDescription,
            SuccessCriteria = successCriteria,
            MaxIterations = maxIterations,
            State = new IterativeTaskState
            {
                Status = IterativeTaskStatus.NotStarted,
                CurrentIteration = 0,
                Iterations = new List<IterationResult>()
            }
        };

        _tasks[sessionId] = task;
        _logger.LogInformation("Created iterative task for session {SessionId}: {Description}", sessionId, taskDescription);
        
        return task;
    }

    public bool IsTaskRunning(string sessionId)
    {
        return _runningTasks.TryGetValue(sessionId, out var running) && running;
    }

    public async Task StartTaskAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(sessionId, out var task))
        {
            _logger.LogWarning("No task found for session {SessionId}", sessionId);
            return;
        }

        if (task.State.Status == IterativeTaskStatus.Running)
        {
            _logger.LogWarning("Task already running for session {SessionId}", sessionId);
            return;
        }

        // Get the session object
        var session = _sessionManager.GetSession(sessionId);
        if (session == null)
        {
            _logger.LogError("Session {SessionId} not found", sessionId);
            return;
        }

        // Create linked cancellation token
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokens[sessionId] = cts;
        _runningTasks[sessionId] = true;

        var oldStatus = task.State.Status;
        task.State.Status = IterativeTaskStatus.Running;
        task.StartedAt = DateTime.UtcNow;

        TaskStatusChanged?.Invoke(this, new TaskStatusChangedEventArgs(sessionId, oldStatus, IterativeTaskStatus.Running));

        _logger.LogInformation("Starting iterative task for session {SessionId}", sessionId);

        try
        {
            await RunIterativeLoopAsync(sessionId, session, task, cts.Token);
        }
        catch (OperationCanceledException)
        {
            UpdateTaskStatus(sessionId, task, IterativeTaskStatus.Stopped, "Task stopped by user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Iterative task failed for session {SessionId}", sessionId);
            UpdateTaskStatus(sessionId, task, IterativeTaskStatus.Failed, ex.Message);
        }
        finally
        {
            _cancellationTokens.TryRemove(sessionId, out _);
            _currentIterations.TryRemove(sessionId, out _);
            _runningTasks[sessionId] = false;
        }
    }

    private async Task RunIterativeLoopAsync(string sessionId, Session session, IterativeTaskConfig task, CancellationToken cancellationToken)
    {
        while (task.State.CurrentIteration < task.MaxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var iterationNumber = task.State.CurrentIteration + 1;
            _logger.LogInformation("Starting iteration {Iteration}/{Max} for session {SessionId}", 
                iterationNumber, task.MaxIterations, sessionId);

            var iteration = new IterationResult
            {
                IterationNumber = iterationNumber,
                StartedAt = DateTime.UtcNow,
                Status = IterationStatus.Running
            };

            // Register this iteration for event capture
            _currentIterations[sessionId] = iteration;

            // Fire iteration started event
            IterationProgress?.Invoke(this, new IterationProgressEventArgs(
                sessionId, iterationNumber, IterationProgressType.Started,
                message: $"Starting iteration {iterationNumber}"));

            try
            {
                // Build the prompt for this iteration
                var prompt = BuildIterationPrompt(task, iterationNumber);

                // Send to Copilot and wait for response
                var response = await _copilotService.SendMessageAsync(session, prompt, cancellationToken);

                // The SDK events will populate tool executions and reasoning
                // The response content is the final assistant message
                if (!string.IsNullOrEmpty(response.Content))
                {
                    iteration.Result = response.Content;
                    
                    // Try to extract action from the response
                    iteration.Action = ExtractActionFromResponse(response.Content);
                }

                if (response.IsError)
                {
                    iteration.Status = IterationStatus.Failed;
                    iteration.ErrorMessage = response.Content;
                }
                else
                {
                    iteration.Status = IterationStatus.Completed;
                }

                // Generate summary from tool executions
                iteration.Summary = GenerateIterationSummary(iteration);

                // Evaluate if task is complete based on response content
                var (isComplete, evaluation) = EvaluateCompletion(task, iteration, response.Content);
                iteration.IsComplete = isComplete;
                iteration.Evaluation = evaluation;
                iteration.CompletedAt = DateTime.UtcNow;

                task.State.Iterations.Add(iteration);
                task.State.CurrentIteration = iterationNumber;

                IterationCompleted?.Invoke(this, new IterationCompletedEventArgs(sessionId, iteration));

                if (isComplete)
                {
                    UpdateTaskStatus(sessionId, task, IterativeTaskStatus.Completed, "Task completed successfully");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                iteration.Status = IterationStatus.Cancelled;
                iteration.CompletedAt = DateTime.UtcNow;
                iteration.Summary = "Iteration cancelled by user";
                task.State.Iterations.Add(iteration);
                throw;
            }
            catch (Exception ex)
            {
                iteration.Status = IterationStatus.Failed;
                iteration.ErrorMessage = ex.Message;
                iteration.Result = $"Error: {ex.Message}";
                iteration.Evaluation = "Iteration failed due to error";
                iteration.CompletedAt = DateTime.UtcNow;
                iteration.Summary = $"Failed: {ex.Message}";
                task.State.Iterations.Add(iteration);
                
                _logger.LogError(ex, "Iteration {Iteration} failed for session {SessionId}", iterationNumber, sessionId);
                
                // Continue to next iteration unless it's a critical error
                IterationCompleted?.Invoke(this, new IterationCompletedEventArgs(sessionId, iteration));
            }
        }

        // Max iterations reached
        UpdateTaskStatus(sessionId, task, IterativeTaskStatus.MaxIterationsReached, 
            $"Reached maximum of {task.MaxIterations} iterations without completing");
    }

    /// <summary>
    /// Generates a human-readable summary of what was accomplished in the iteration
    /// </summary>
    private string GenerateIterationSummary(IterationResult iteration)
    {
        var toolCount = iteration.ToolExecutions.Count;
        
        if (toolCount == 0)
        {
            // No tools executed, use reasoning or a generic message
            if (iteration.AgentReasoning.Any())
            {
                var firstReasoning = iteration.AgentReasoning.First().Content;
                return firstReasoning.Length > 80 
                    ? firstReasoning[..80] + "..." 
                    : firstReasoning;
            }
            return "Analysis and planning";
        }

        var successCount = iteration.ToolExecutions.Count(t => t.Success);
        var failedCount = toolCount - successCount;
        
        // Get distinct tool names
        var toolNames = iteration.ToolExecutions
            .Select(t => t.DisplayName ?? ToolExecutionDetail.GetFriendlyToolName(t.ToolName))
            .Distinct()
            .Take(3)
            .ToList();

        var toolList = string.Join(", ", toolNames);
        if (iteration.ToolExecutions.Count > 3)
        {
            toolList += $" (+{iteration.ToolExecutions.Count - 3} more)";
        }

        var statusPart = failedCount > 0 
            ? $" ({successCount} succeeded, {failedCount} failed)" 
            : "";

        return $"Executed {toolCount} tools{statusPart}: {toolList}";
    }

    /// <summary>
    /// Extracts the main action description from the assistant response
    /// </summary>
    private string ExtractActionFromResponse(string response)
    {
        // Try to find action markers in the response
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("ACTION:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[7..].Trim();
            }
            if (trimmed.StartsWith("**ACTION:**", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[11..].Trim();
            }
        }

        // Default: use first sentence or first 100 chars
        var firstSentence = response.Split(new[] { '.', '!', '?' }, 2).FirstOrDefault() ?? response;
        return firstSentence.Length > 100 ? firstSentence[..100] + "..." : firstSentence;
    }

    private string BuildIterationPrompt(IterativeTaskConfig task, int iterationNumber)
    {
        var previousIterations = task.State.Iterations.Count > 0
            ? string.Join("\n", task.State.Iterations.Select(i => 
                $"- Iteration {i.IterationNumber}: {i.Summary ?? i.Action} -> {(i.IsComplete ? "Task criteria met" : "Continuing...")}"))
            : "None yet";

        return $@"You are working on an iterative task. Make progress toward the goal.

**Task Description:** {task.TaskDescription}

**Success Criteria:** {task.SuccessCriteria}

**Current Iteration:** {iterationNumber} of {task.MaxIterations}

**Previous Iterations:**
{previousIterations}

Please take the next step to accomplish the task. Use available tools as needed.
After your actions, briefly evaluate whether the success criteria has been met.

If the task is complete, clearly state ""TASK COMPLETE"" in your response.
If more work is needed, explain what you'll do next.";
    }

    /// <summary>
    /// Evaluates whether the task is complete based on the response
    /// </summary>
    private (bool isComplete, string evaluation) EvaluateCompletion(
        IterativeTaskConfig task, 
        IterationResult iteration, 
        string responseContent)
    {
        // Check for explicit completion markers
        var lowerContent = responseContent.ToLowerInvariant();
        
        if (lowerContent.Contains("task complete") || 
            lowerContent.Contains("task is complete") ||
            lowerContent.Contains("successfully completed") ||
            lowerContent.Contains("objective achieved") ||
            lowerContent.Contains("success criteria met"))
        {
            return (true, "Agent indicated task completion");
        }

        // Check for completion signals in tool results
        var hasSuccessfulCompletion = iteration.ToolExecutions
            .Any(t => t.ToolName.Contains("completion", StringComparison.OrdinalIgnoreCase) && t.Success);
        
        if (hasSuccessfulCompletion)
        {
            return (true, "Completion tool executed successfully");
        }

        // Check for error states
        var failedTools = iteration.ToolExecutions.Count(t => !t.Success);
        if (failedTools > 0)
        {
            return (false, $"{failedTools} tool(s) failed; continuing to next iteration");
        }

        return (false, "More work needed to meet success criteria");
    }

    private void UpdateTaskStatus(string sessionId, IterativeTaskConfig task, IterativeTaskStatus newStatus, string reason)
    {
        var oldStatus = task.State.Status;
        task.State.Status = newStatus;
        task.State.CompletionReason = reason;
        task.CompletedAt = DateTime.UtcNow;

        _logger.LogInformation("Task status changed for session {SessionId}: {OldStatus} -> {NewStatus} ({Reason})", 
            sessionId, oldStatus, newStatus, reason);

        TaskStatusChanged?.Invoke(this, new TaskStatusChangedEventArgs(sessionId, oldStatus, newStatus, reason));
    }

    public async Task StopTaskAsync(string sessionId)
    {
        _logger.LogInformation("Stop requested for task in session {SessionId}", sessionId);
        
        // Cancel the task
        if (_cancellationTokens.TryGetValue(sessionId, out var cts))
        {
            cts.Cancel();
        }

        // Also abort any in-flight SDK operation
        try
        {
            await _copilotService.AbortAsync(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error aborting SDK session for {SessionId}", sessionId);
        }

        _runningTasks[sessionId] = false;
    }

    public void ClearTask(string sessionId)
    {
        // Stop first if running
        if (IsTaskRunning(sessionId))
        {
            StopTaskAsync(sessionId).GetAwaiter().GetResult();
        }
        
        _tasks.TryRemove(sessionId, out _);
        _currentIterations.TryRemove(sessionId, out _);
        _runningTasks.TryRemove(sessionId, out _);
        
        _logger.LogInformation("Cleared task for session {SessionId}", sessionId);
    }

    public IReadOnlyDictionary<string, IterativeTaskConfig> GetAllTasks()
    {
        return _tasks;
    }
}