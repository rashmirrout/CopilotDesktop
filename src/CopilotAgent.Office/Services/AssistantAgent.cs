using System.Diagnostics;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.Office.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Ephemeral worker that creates a Copilot session, sends a task prompt, and collects the result.
/// Each agent is single-use per task execution.
/// </summary>
public sealed class AssistantAgent : IAssistantAgent
{
    private readonly ICopilotService _copilotService;
    private readonly ILogger _logger;

    /// <inheritdoc />
    public int AssistantIndex { get; }

    /// <inheritdoc />
    public event Action<string>? OnProgress;

    public AssistantAgent(ICopilotService copilotService, int assistantIndex, ILogger logger)
    {
        _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        AssistantIndex = assistantIndex;
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

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(config.AssistantTimeoutSeconds));

            var responseContent = new System.Text.StringBuilder();

            await foreach (var message in _copilotService.SendMessageStreamingAsync(session, task.Prompt, timeoutCts.Token))
            {
                if (message.Role == MessageRole.Assistant && !string.IsNullOrEmpty(message.Content))
                {
                    responseContent.Append(message.Content);

                    try
                    {
                        OnProgress?.Invoke(message.Content);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error in progress handler for task {TaskId}", task.Id);
                    }
                }
            }

            stopwatch.Stop();
            var content = responseContent.ToString();
            var summary = content.Length > 200 ? content[..200] + "..." : content;

            _logger.LogInformation(
                "Assistant[{Index}] completed task {TaskId} in {Duration}ms ({ContentLength} chars)",
                AssistantIndex, task.Id, stopwatch.ElapsedMilliseconds, content.Length);

            return new AssistantResult
            {
                TaskId = task.Id,
                AssistantIndex = AssistantIndex,
                Success = true,
                Content = content,
                Summary = summary,
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

    private static Session CreateSession(AssistantTask task, OfficeConfig config)
    {
        return new Session
        {
            SessionId = $"office-assistant-{task.Id}",
            DisplayName = $"Office Assistant: {task.Title}",
            ModelId = config.AssistantModel,
            WorkingDirectory = config.WorkspacePath,
            SystemPrompt = BuildAssistantSystemPrompt(task),
            AutonomousMode = new AutonomousModeSettings { AllowAll = true }
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