using System.Diagnostics;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.Office.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Ephemeral worker that creates a Copilot session, sends a task prompt, and collects the result.
/// Each agent is single-use per task execution.
///
/// Bug fixes applied:
/// - Streaming: SDK message.Content is ACCUMULATED (not delta). We track previousLength
///   and only append/emit the new delta portion each time.
/// - Reasoning: Subscribe to CopilotSdkService.SessionEventReceived for reasoning deltas
///   and surface them via OnReasoningDelta for live commentary.
/// - Result: Build a concise summary instead of echoing the entire response verbatim.
/// </summary>
public sealed class AssistantAgent : IAssistantAgent
{
    private readonly ICopilotService _copilotService;
    private readonly CopilotSdkService? _sdkService;
    private readonly ILogger _logger;

    /// <inheritdoc />
    public int AssistantIndex { get; }

    /// <inheritdoc />
    public event Action<string>? OnProgress;

    /// <inheritdoc />
    public event Action<string>? OnReasoningDelta;

    /// <inheritdoc />
    public event Action<string, string>? OnToolCallStarted;

    /// <inheritdoc />
    public event Action<string>? OnToolCallCompleted;

    public AssistantAgent(ICopilotService copilotService, int assistantIndex, ILogger logger)
    {
        _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        AssistantIndex = assistantIndex;

        // Cache SDK service reference for reasoning event subscription
        _sdkService = copilotService as CopilotSdkService;
    }

    /// <inheritdoc />
    public async Task<AssistantResult> ExecuteAsync(AssistantTask task, OfficeConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(config);

        var stopwatch = Stopwatch.StartNew();
        var session = CreateSession(task, config);

        _logger.LogInformation(
            "Assistant[{Index}] starting task {TaskId}: {Title}",
            AssistantIndex, task.Id, task.Title);

        // Subscribe to SDK reasoning events for live commentary
        EventHandler<SdkSessionEventArgs>? reasoningHandler = null;
        if (_sdkService is not null)
        {
            reasoningHandler = (_, args) => OnSdkSessionEvent(args, session.SessionId);
            _sdkService.SessionEventReceived += reasoningHandler;
            _logger.LogDebug("Assistant[{Index}] subscribed to SDK reasoning events for session {SessionId}",
                AssistantIndex, session.SessionId);
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(config.AssistantTimeoutSeconds));

            // Wire up tool execution tracking
            using var collector = new AgentEventCollector(_copilotService, _logger);
            collector.Start(session.SessionId);

            // Track accumulated content length to extract only deltas
            var previousLength = 0;
            string finalContent = "";

            await foreach (var message in _copilotService.SendMessageStreamingAsync(session, task.Prompt, timeoutCts.Token))
            {
                if (message.Role == MessageRole.Assistant && !string.IsNullOrEmpty(message.Content))
                {
                    // SDK message.Content is ACCUMULATED text, not a delta.
                    // Only extract the new portion since last chunk.
                    if (message.Content.Length > previousLength)
                    {
                        var delta = message.Content[previousLength..];
                        previousLength = message.Content.Length;

                        try
                        {
                            OnProgress?.Invoke(delta);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error in progress handler for task {TaskId}", task.Id);
                        }
                    }

                    // Always keep the latest accumulated content as the final response
                    finalContent = message.Content;
                }
            }

            stopwatch.Stop();
            var toolExecutions = collector.Complete();

            _logger.LogInformation(
                "Assistant[{Index}] completed task {TaskId} in {Duration}ms ({ContentLength} chars)",
                AssistantIndex, task.Id, stopwatch.ElapsedMilliseconds, finalContent.Length);

            // Build concise result: task title + summary (not the entire verbose response)
            var conciseSummary = BuildConciseSummary(task, finalContent, toolExecutions);

            return new AssistantResult
            {
                TaskId = task.Id,
                AssistantIndex = AssistantIndex,
                Success = true,
                Content = conciseSummary,
                Summary = conciseSummary,
                ToolExecutions = toolExecutions,
                Duration = stopwatch.Elapsed,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning("Assistant[{Index}] task {TaskId} was cancelled", AssistantIndex, task.Id);

            return new AssistantResult
            {
                TaskId = task.Id,
                AssistantIndex = AssistantIndex,
                Success = false,
                ErrorMessage = "Task was cancelled",
                Duration = stopwatch.Elapsed,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "Assistant[{Index}] task {TaskId} timed out after {Timeout}s",
                AssistantIndex, task.Id, config.AssistantTimeoutSeconds);

            return new AssistantResult
            {
                TaskId = task.Id,
                AssistantIndex = AssistantIndex,
                Success = false,
                ErrorMessage = $"Task timed out after {config.AssistantTimeoutSeconds}s",
                Duration = stopwatch.Elapsed,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Assistant[{Index}] task {TaskId} failed", AssistantIndex, task.Id);

            return new AssistantResult
            {
                TaskId = task.Id,
                AssistantIndex = AssistantIndex,
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
        finally
        {
            // Unsubscribe from SDK reasoning events
            if (_sdkService is not null && reasoningHandler is not null)
            {
                _sdkService.SessionEventReceived -= reasoningHandler;
            }

            // Terminate the ephemeral session process
            try
            {
                _copilotService.TerminateSessionProcess(session.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to terminate session process for task {TaskId}", task.Id);
            }
        }
    }

    /// <summary>
    /// Handles SDK session events, filtering for reasoning deltas on our session
    /// and forwarding them via <see cref="OnReasoningDelta"/>.
    /// Uses reflection to extract DeltaContent to avoid requiring a direct SDK package
    /// reference in the Office project.
    /// </summary>
    private void OnSdkSessionEvent(SdkSessionEventArgs args, string expectedSessionId)
    {
        if (args.SessionId != expectedSessionId)
            return;

        try
        {
            var eventTypeName = args.Event.GetType().Name;

            switch (eventTypeName)
            {
                case "AssistantReasoningDeltaEvent":
                {
                    // Extract Data.DeltaContent via reflection (SDK type not directly referenced)
                    var data = args.Event.GetType().GetProperty("Data")?.GetValue(args.Event);
                    if (data is not null)
                    {
                        var deltaContent = data.GetType().GetProperty("DeltaContent")?.GetValue(data) as string;
                        if (!string.IsNullOrEmpty(deltaContent))
                        {
                            OnReasoningDelta?.Invoke(deltaContent);
                        }
                    }
                    break;
                }

                case "ToolExecutionStartEvent":
                {
                    var data = args.Event.GetType().GetProperty("Data")?.GetValue(args.Event);
                    if (data is not null)
                    {
                        var toolCallId = data.GetType().GetProperty("ToolCallId")?.GetValue(data) as string ?? "unknown";
                        var toolName = data.GetType().GetProperty("ToolName")?.GetValue(data) as string ?? "unknown";
                        OnToolCallStarted?.Invoke(toolCallId, toolName);
                    }
                    break;
                }

                case "ToolExecutionCompleteEvent":
                {
                    var data = args.Event.GetType().GetProperty("Data")?.GetValue(args.Event);
                    if (data is not null)
                    {
                        var toolCallId = data.GetType().GetProperty("ToolCallId")?.GetValue(data) as string ?? "unknown";
                        OnToolCallCompleted?.Invoke(toolCallId);
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error processing SDK session event for reasoning");
        }
    }

    /// <summary>
    /// Builds a concise summary for the assistant result that includes the task title,
    /// tools called, and a truncated response — not the entire verbose output.
    /// </summary>
    private static string BuildConciseSummary(
        AssistantTask task,
        string fullResponse,
        IReadOnlyList<ToolExecution> toolExecutions)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"**Task**: {task.Title}");

        if (toolExecutions.Count > 0)
        {
            var toolNames = string.Join(", ", toolExecutions.Select(t => t.ToolName).Distinct());
            sb.AppendLine($"**Tools**: {toolNames}");
        }

        // Truncate the response to a reasonable summary length
        const int maxSummaryLength = 500;
        if (fullResponse.Length > maxSummaryLength)
        {
            sb.AppendLine($"**Summary**: {fullResponse[..maxSummaryLength]}…");
        }
        else
        {
            sb.AppendLine($"**Summary**: {fullResponse}");
        }

        return sb.ToString().TrimEnd();
    }

    private static Session CreateSession(AssistantTask task, OfficeConfig config)
    {
        return new Session
        {
            SessionId = $"office-assistant-{task.Id}",
            DisplayName = $"Office Assistant: {task.Title}",
            ModelId = config.AssistantModel,
            WorkingDirectory = config.WorkspacePath,
            SystemPrompt = BuildAssistantSystemPrompt(task),
            AutonomousMode = new AutonomousModeSettings { AllowAll = true },

            // Issue #5/#6: Propagate MCP servers and skills from OfficeConfig
            // so assistant sessions inherit the same tool/skill configuration
            // that the user configured on the parent session.
            EnabledMcpServers = config.EnabledMcpServers,
            DisabledSkills = config.DisabledSkills,
            SkillDirectories = config.SkillDirectories
        };
    }

    private static string BuildAssistantSystemPrompt(AssistantTask task)
    {
        return $"""
            You are an AI assistant executing a specific task as part of a larger automated workflow.
            
            ## Your Task
            **{task.Title}**
            
            ## Guidelines
            - Focus exclusively on the task described in the user message.
            - Be thorough but concise in your response.
            - If you encounter errors, describe them clearly.
            - Do not ask for clarification — work with the information provided.
            - Provide actionable results that can be aggregated with other task results.
            """;
    }
}