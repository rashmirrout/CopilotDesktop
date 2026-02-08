using CopilotAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Consumes an accumulated LLM streaming response, extracts deltas,
/// and returns the final complete text.
///
/// <para>
/// <b>Key insight</b>: <see cref="ICopilotService.SendMessageStreamingAsync"/>
/// yields <see cref="ChatMessage"/> objects where <c>Content</c> is the
/// <b>accumulated</b> text so far (not a delta). This service tracks the
/// previous length and extracts only the new delta for each chunk,
/// invoking the optional <paramref name="onDelta"/> callback for live commentary.
/// </para>
/// </summary>
public sealed class ReasoningStream : IReasoningStream
{
    private readonly ILogger<ReasoningStream> _logger;

    public ReasoningStream(ILogger<ReasoningStream> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> StreamAsync(
        IAsyncEnumerable<ChatMessage> source,
        string agentName,
        int iterationNumber,
        Action<string>? onDelta,
        CancellationToken ct)
    {
        var lastContent = string.Empty;
        var previousLength = 0;

        await foreach (var chunk in source.WithCancellation(ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                lastContent = chunk.Content;

                // Extract only the new delta from the accumulated content
                if (onDelta is not null && chunk.Content.Length > previousLength)
                {
                    var delta = chunk.Content[previousLength..];
                    onDelta(delta);
                }

                previousLength = chunk.Content.Length;
            }
        }

        var finalText = lastContent.Trim();

        _logger.LogDebug(
            "ReasoningStream complete for {Agent}: {Length} chars",
            agentName, finalText.Length);

        return finalText;
    }
}