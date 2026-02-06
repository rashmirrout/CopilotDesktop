using System.Collections.Concurrent;
using CopilotAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Internal state tracking for a streaming operation.
/// </summary>
internal sealed class StreamingOperation
{
    public required string SessionId { get; init; }
    public required string MessageId { get; init; }
    public required ChatMessage Message { get; init; }
    public required CancellationTokenSource CancellationTokenSource { get; init; }
    public StreamingState State { get; set; } = StreamingState.Idle;
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Production-grade implementation of streaming message management.
/// 
/// This service manages streaming operations independently of the UI, ensuring
/// that streaming continues even when users switch between sessions. It maintains
/// Session.MessageHistory as the single source of truth.
/// 
/// Thread Safety:
/// - All state access is protected by ConcurrentDictionary or locks
/// - Event raising is done on the caller's thread (typically background)
/// - Subscribers (ViewModels) must marshal to UI thread as needed
/// 
/// Architecture:
/// - Session.MessageHistory is the authoritative data store
/// - Streaming messages are added to history immediately (with IsStreaming=true)
/// - Messages are updated in-place during streaming
/// - Subscribers receive events for UI updates
/// </summary>
public class StreamingMessageManager : IStreamingMessageManager
{
    private readonly ICopilotService _copilotService;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<StreamingMessageManager> _logger;
    
    /// <summary>
    /// Active streaming operations indexed by session ID.
    /// Only one streaming operation per session is allowed.
    /// </summary>
    private readonly ConcurrentDictionary<string, StreamingOperation> _activeOperations = new();
    
    /// <summary>
    /// Lock object for thread-safe operations on message history.
    /// </summary>
    private readonly object _historyLock = new();

    public event EventHandler<StreamingUpdateEventArgs>? StreamingUpdated;

    public StreamingMessageManager(
        ICopilotService copilotService,
        ISessionManager sessionManager,
        ILogger<StreamingMessageManager> logger)
    {
        _copilotService = copilotService;
        _sessionManager = sessionManager;
        _logger = logger;
        _logger.LogInformation("StreamingMessageManager initialized");
    }

    public async Task<string> StartStreamingAsync(
        Session session, 
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var sessionId = session.SessionId;
        
        // Check if already streaming in this session
        if (_activeOperations.TryGetValue(sessionId, out var existingOp))
        {
            if (existingOp.State == StreamingState.Streaming)
            {
                _logger.LogWarning(
                    "Session {SessionId} already has active streaming. Cancelling previous operation.",
                    sessionId);
                await StopStreamingAsync(sessionId);
            }
        }
        
        // Create the user message and add to history immediately
        var userChatMessage = new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            Role = MessageRole.User,
            Content = userMessage,
            Timestamp = DateTime.UtcNow,
            IsStreaming = false
        };
        
        // Create the assistant message placeholder
        var assistantMessage = new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            Role = MessageRole.Assistant,
            Content = string.Empty,
            Timestamp = DateTime.UtcNow,
            IsStreaming = true
        };
        
        // Add both messages to history immediately (thread-safe)
        lock (_historyLock)
        {
            session.MessageHistory.Add(userChatMessage);
            session.MessageHistory.Add(assistantMessage);
        }
        
        _logger.LogInformation(
            "Starting streaming in session {SessionId}. User message ID: {UserMsgId}, Assistant message ID: {AssistantMsgId}",
            sessionId, userChatMessage.Id, assistantMessage.Id);
        
        // Create cancellation token source that combines with the caller's token
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        // Create the streaming operation
        var operation = new StreamingOperation
        {
            SessionId = sessionId,
            MessageId = assistantMessage.Id,
            Message = assistantMessage,
            CancellationTokenSource = cts,
            State = StreamingState.Streaming
        };
        
        // Register the operation
        _activeOperations[sessionId] = operation;
        
        // Notify subscribers about the user message
        RaiseStreamingUpdated(sessionId, userChatMessage, isComplete: false, isError: false);
        
        // Notify subscribers about the initial assistant message (empty, streaming)
        RaiseStreamingUpdated(sessionId, assistantMessage, isComplete: false, isError: false);
        
        // Start the streaming operation on a background task
        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteStreamingAsync(session, operation, userMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in streaming background task for session {SessionId}", sessionId);
            }
        }, CancellationToken.None); // Don't cancel the background task itself
        
        return assistantMessage.Id;
    }

    /// <summary>
    /// Executes the actual streaming operation on a background thread.
    /// </summary>
    private async Task ExecuteStreamingAsync(
        Session session, 
        StreamingOperation operation,
        string userMessage)
    {
        var sessionId = session.SessionId;
        var ct = operation.CancellationTokenSource.Token;
        
        try
        {
            _logger.LogDebug("ExecuteStreamingAsync started for session {SessionId}", sessionId);
            
            await foreach (var chunk in _copilotService.SendMessageStreamingAsync(session, userMessage, ct))
            {
                // Check for cancellation
                if (ct.IsCancellationRequested)
                {
                    _logger.LogInformation("Streaming cancelled for session {SessionId}", sessionId);
                    operation.State = StreamingState.Cancelled;
                    
                    // Update the message to reflect cancellation
                    UpdateMessageInHistory(session, operation.MessageId, msg =>
                    {
                        msg.IsStreaming = false;
                        if (string.IsNullOrEmpty(msg.Content))
                        {
                            msg.Content = "[Cancelled]";
                        }
                    });
                    
                    RaiseStreamingUpdated(sessionId, operation.Message, isComplete: true, isError: false);
                    return;
                }
                
                // Update the message content in history
                UpdateMessageInHistory(session, operation.MessageId, msg =>
                {
                    msg.Content = chunk.Content;
                    msg.IsStreaming = chunk.IsStreaming;
                    msg.IsError = chunk.IsError;
                });
                
                // Notify subscribers of the update
                RaiseStreamingUpdated(sessionId, operation.Message, 
                    isComplete: !chunk.IsStreaming,
                    isError: chunk.IsError);
                
                // If streaming is complete, update state
                if (!chunk.IsStreaming)
                {
                    operation.State = chunk.IsError ? StreamingState.Error : StreamingState.Completed;
                    if (chunk.IsError)
                    {
                        operation.ErrorMessage = chunk.Content;
                    }
                    
                    _logger.LogInformation(
                        "Streaming completed for session {SessionId}. State: {State}, Content length: {Length}",
                        sessionId, operation.State, operation.Message.Content.Length);
                }
            }
            
            // Ensure final state is set
            if (operation.State == StreamingState.Streaming)
            {
                operation.State = StreamingState.Completed;
                operation.Message.IsStreaming = false;
                
                UpdateMessageInHistory(session, operation.MessageId, msg =>
                {
                    msg.IsStreaming = false;
                });
                
                RaiseStreamingUpdated(sessionId, operation.Message, isComplete: true, isError: false);
            }
            
            // Save the session after streaming completes
            await _sessionManager.SaveActiveSessionAsync();
            _logger.LogDebug("Session saved after streaming completed for {SessionId}", sessionId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Streaming operation cancelled for session {SessionId}", sessionId);
            operation.State = StreamingState.Cancelled;
            
            UpdateMessageInHistory(session, operation.MessageId, msg =>
            {
                msg.IsStreaming = false;
                if (string.IsNullOrEmpty(msg.Content))
                {
                    msg.Content = "[Cancelled]";
                }
            });
            
            RaiseStreamingUpdated(sessionId, operation.Message, isComplete: true, isError: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during streaming for session {SessionId}", sessionId);
            operation.State = StreamingState.Error;
            operation.ErrorMessage = ex.Message;
            
            UpdateMessageInHistory(session, operation.MessageId, msg =>
            {
                msg.IsStreaming = false;
                msg.IsError = true;
                msg.Content = string.IsNullOrEmpty(msg.Content) 
                    ? $"Error: {ex.Message}" 
                    : msg.Content + $"\n\n[Error: {ex.Message}]";
            });
            
            RaiseStreamingUpdated(sessionId, operation.Message, isComplete: true, isError: true, errorMessage: ex.Message);
        }
        finally
        {
            // Cleanup - but don't remove immediately to allow state queries
            // The operation will be cleaned up when a new streaming starts
            operation.CancellationTokenSource.Dispose();
            _logger.LogDebug("Streaming operation finalized for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Updates a message in the session's history by ID.
    /// </summary>
    private void UpdateMessageInHistory(Session session, string messageId, Action<ChatMessage> updateAction)
    {
        lock (_historyLock)
        {
            var message = session.MessageHistory.FirstOrDefault(m => m.Id == messageId);
            if (message != null)
            {
                updateAction(message);
            }
            else
            {
                _logger.LogWarning("Message {MessageId} not found in session {SessionId} history",
                    messageId, session.SessionId);
            }
        }
    }

    public async Task StopStreamingAsync(string sessionId)
    {
        if (_activeOperations.TryGetValue(sessionId, out var operation))
        {
            if (operation.State == StreamingState.Streaming)
            {
                _logger.LogInformation("Stopping streaming for session {SessionId}", sessionId);
                
                try
                {
                    operation.CancellationTokenSource.Cancel();
                    
                    // Also call AbortAsync on the copilot service
                    await _copilotService.AbortAsync(sessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping streaming for session {SessionId}", sessionId);
                }
            }
        }
    }

    public bool IsStreaming(string sessionId)
    {
        return _activeOperations.TryGetValue(sessionId, out var op) && 
               op.State == StreamingState.Streaming;
    }

    public StreamingState GetStreamingState(string sessionId)
    {
        if (_activeOperations.TryGetValue(sessionId, out var operation))
        {
            return operation.State;
        }
        return StreamingState.Idle;
    }

    public ChatMessage? GetCurrentStreamingMessage(string sessionId)
    {
        if (_activeOperations.TryGetValue(sessionId, out var operation))
        {
            return operation.Message;
        }
        return null;
    }

    public string? GetCurrentStreamingMessageId(string sessionId)
    {
        if (_activeOperations.TryGetValue(sessionId, out var operation))
        {
            return operation.MessageId;
        }
        return null;
    }

    /// <summary>
    /// Raises the StreamingUpdated event.
    /// </summary>
    private void RaiseStreamingUpdated(
        string sessionId, 
        ChatMessage message, 
        bool isComplete, 
        bool isError,
        string? errorMessage = null)
    {
        try
        {
            StreamingUpdated?.Invoke(this, new StreamingUpdateEventArgs
            {
                SessionId = sessionId,
                Message = message,
                IsComplete = isComplete,
                IsError = isError,
                ErrorMessage = errorMessage
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error raising StreamingUpdated event for session {SessionId}", sessionId);
        }
    }
}