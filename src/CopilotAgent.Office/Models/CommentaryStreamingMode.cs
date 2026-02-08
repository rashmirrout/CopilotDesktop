namespace CopilotAgent.Office.Models;

/// <summary>
/// Controls how the Manager's LLM reasoning is streamed as live commentary.
/// </summary>
public enum CommentaryStreamingMode
{
    /// <summary>
    /// Default. Buffer the full LLM response, then emit it as a single
    /// commentary entry once the response is complete.
    /// Best for readability — each commentary is a coherent, complete thought.
    /// </summary>
    CompleteThought,

    /// <summary>
    /// Stream each token chunk as it arrives from the LLM, emitting
    /// incremental commentary updates in real-time (word-by-word).
    /// Best for visibility — the user sees the Manager "thinking" live.
    /// </summary>
    StreamingTokens
}