using CopilotAgent.Core.Models;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Consumes an accumulated LLM streaming response, extracts deltas,
/// and returns the final complete text.
///
/// Single responsibility: bridge between the accumulated-content streaming API
/// (<see cref="ICopilotService.SendMessageStreamingAsync"/>) and a clean
/// final-text result. Optionally emits deltas to a callback for live commentary.
/// </summary>
public interface IReasoningStream
{
    /// <summary>
    /// Consumes an accumulated streaming response, extracts the final complete
    /// response text, and returns it. Optionally invokes <paramref name="onDelta"/>
    /// with each new text delta as it arrives (for live commentary streaming).
    /// </summary>
    /// <param name="source">
    /// Accumulated streaming response from <see cref="ICopilotService.SendMessageStreamingAsync"/>.
    /// Each yielded <see cref="ChatMessage.Content"/> contains the full text so far (not a delta).
    /// </param>
    /// <param name="agentName">Display name of the agent (for logging only).</param>
    /// <param name="iterationNumber">Current iteration number (for logging only).</param>
    /// <param name="onDelta">
    /// Optional callback invoked with each new text delta extracted from the accumulated stream.
    /// Pass null to disable delta emission.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The final complete response text (trimmed).</returns>
    Task<string> StreamAsync(
        IAsyncEnumerable<ChatMessage> source,
        string agentName,
        int iterationNumber,
        Action<string>? onDelta,
        CancellationToken ct);
}