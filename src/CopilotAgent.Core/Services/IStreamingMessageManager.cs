using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Event args for streaming message updates.
/// </summary>
public class StreamingUpdateEventArgs : EventArgs
{
    /// <summary>
    /// The session ID where streaming is occurring.
    /// </summary>
    public required string SessionId { get; init; }
    
    /// <summary>
    /// The current message being streamed (may be partial).
    /// </summary>
    public required ChatMessage Message { get; init; }
    
    /// <summary>
    /// Whether this update indicates streaming is complete.
    /// </summary>
    public bool IsComplete { get; init; }
    
    /// <summary>
    /// Whether an error occurred during streaming.
    /// </summary>
    public bool IsError { get; init; }
    
    /// <summary>
    /// Error message if IsError is true.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Represents the state of a streaming operation.
/// </summary>
public enum StreamingState
{
    /// <summary>
    /// No active streaming.
    /// </summary>
    Idle,
    
    /// <summary>
    /// Streaming is in progress.
    /// </summary>
    Streaming,
    
    /// <summary>
    /// Streaming completed successfully.
    /// </summary>
    Completed,
    
    /// <summary>
    /// Streaming was cancelled by user.
    /// </summary>
    Cancelled,
    
    /// <summary>
    /// Streaming failed with error.
    /// </summary>
    Error
}

/// <summary>
/// Manages streaming message state per session, independent of UI.
/// 
/// This service is the authoritative source for streaming operations and ensures
/// that streaming continues even when the user switches between sessions.
/// 
/// Key responsibilities:
/// - Manages streaming lifecycle per session
/// - Updates Session.MessageHistory (the single source of truth)
/// - Notifies subscribers of streaming updates via events
/// - Provides thread-safe access to streaming state
/// </summary>
public interface IStreamingMessageManager
{
    /// <summary>
    /// Event raised when streaming message content is updated.
    /// Subscribers should use this to update their UI.
    /// </summary>
    event EventHandler<StreamingUpdateEventArgs>? StreamingUpdated;
    
    /// <summary>
    /// Starts streaming a message for a session.
    /// The user message is added to history immediately.
    /// A placeholder assistant message is created and updated during streaming.
    /// Streaming continues in background even if UI switches away.
    /// </summary>
    /// <param name="session">The session to stream in</param>
    /// <param name="userMessage">The user's message prompt</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>The message ID of the streaming assistant message</returns>
    Task<string> StartStreamingAsync(
        Session session, 
        string userMessage, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stops streaming for a session if active.
    /// </summary>
    /// <param name="sessionId">The session to stop streaming for</param>
    Task StopStreamingAsync(string sessionId);
    
    /// <summary>
    /// Checks if a session has an active streaming operation.
    /// </summary>
    /// <param name="sessionId">The session ID to check</param>
    /// <returns>True if streaming is in progress</returns>
    bool IsStreaming(string sessionId);
    
    /// <summary>
    /// Gets the current streaming state for a session.
    /// </summary>
    /// <param name="sessionId">The session ID to check</param>
    /// <returns>The current streaming state</returns>
    StreamingState GetStreamingState(string sessionId);
    
    /// <summary>
    /// Gets the current streaming message for a session, if any.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <returns>The current streaming message, or null if not streaming</returns>
    ChatMessage? GetCurrentStreamingMessage(string sessionId);
    
    /// <summary>
    /// Gets the message ID of the current streaming message for a session.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <returns>The message ID, or null if not streaming</returns>
    string? GetCurrentStreamingMessageId(string sessionId);
}