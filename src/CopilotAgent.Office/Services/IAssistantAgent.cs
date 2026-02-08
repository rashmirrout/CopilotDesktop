using CopilotAgent.Office.Models;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Ephemeral worker that creates a Copilot session, sends a prompt, and collects the result.
/// </summary>
public interface IAssistantAgent
{
    /// <summary>Index of this assistant in the pool (0-based).</summary>
    int AssistantIndex { get; }

    /// <summary>Execute a single task and return the result.</summary>
    /// <param name="task">The task to execute.</param>
    /// <param name="config">Office configuration for model and timeout settings.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The assistant's result.</returns>
    Task<AssistantResult> ExecuteAsync(AssistantTask task, OfficeConfig config, CancellationToken ct = default);

    /// <summary>Raised when the assistant makes progress (streaming chunks).</summary>
    event Action<string>? OnProgress;

    /// <summary>Raised when SDK emits reasoning/thinking deltas for live commentary.</summary>
    event Action<string>? OnReasoningDelta;

    /// <summary>Raised when an assistant starts executing a tool (toolCallId, toolName).</summary>
    event Action<string, string>? OnToolCallStarted;

    /// <summary>Raised when an assistant completes a tool execution (toolCallId).</summary>
    event Action<string>? OnToolCallCompleted;
}
