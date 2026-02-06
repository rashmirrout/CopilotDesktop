using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for the chat interface.
/// 
/// This ViewModel is designed to be ephemeral - it can be destroyed and recreated
/// when switching sessions. It relies on:
/// - Session.MessageHistory as the single source of truth for messages
/// - StreamingMessageManager for handling streaming operations
/// 
/// Key responsibilities:
/// - Binds Messages to UI for display
/// - Delegates streaming to StreamingMessageManager
/// - Subscribes to streaming events and updates UI
/// - Handles tool execution display
/// </summary>
public partial class ChatViewModel : ViewModelBase
{
    private readonly ICopilotService _copilotService;
    private readonly ISessionManager _sessionManager;
    private readonly IStreamingMessageManager _streamingManager;
    private readonly ILogger<ChatViewModel> _logger;
    
    /// <summary>
    /// Tracks active tool executions by toolCallId.
    /// This ensures proper tracking when multiple tools run concurrently
    /// and prevents UI from getting stuck if a complete event is missed.
    /// </summary>
    private readonly ConcurrentDictionary<string, ToolExecutionInfo> _activeTools = new();

    [ObservableProperty]
    private Session _session = null!;

    [ObservableProperty]
    private ObservableCollection<ChatMessage> _messages = new();

    [ObservableProperty]
    private string _messageInput = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _canSendMessage = true;

    [ObservableProperty]
    private bool _isToolExecuting;

    [ObservableProperty]
    private string _currentToolName = string.Empty;

    [ObservableProperty]
    private TerminalViewModel _terminalViewModel = null!;

    [ObservableProperty]
    private SkillsViewModel _skillsViewModel = null!;

    [ObservableProperty]
    private McpConfigViewModel _mcpConfigViewModel = null!;

    [ObservableProperty]
    private IterativeTaskViewModel _iterativeTaskViewModel = null!;

    [ObservableProperty]
    private SessionInfoViewModel _sessionInfoViewModel = null!;

    public ChatViewModel(
        ICopilotService copilotService,
        ISessionManager sessionManager,
        IStreamingMessageManager streamingManager,
        ILogger<ChatViewModel> logger,
        TerminalViewModel terminalViewModel,
        SkillsViewModel skillsViewModel,
        McpConfigViewModel mcpConfigViewModel,
        IterativeTaskViewModel iterativeTaskViewModel,
        SessionInfoViewModel sessionInfoViewModel)
    {
        _copilotService = copilotService;
        _sessionManager = sessionManager;
        _streamingManager = streamingManager;
        _logger = logger;
        _terminalViewModel = terminalViewModel;
        _skillsViewModel = skillsViewModel;
        _mcpConfigViewModel = mcpConfigViewModel;
        _iterativeTaskViewModel = iterativeTaskViewModel;
        _sessionInfoViewModel = sessionInfoViewModel;
        
        // Subscribe to terminal "Add to message" events
        _terminalViewModel.AddToMessageRequested += OnTerminalAddToMessage;
        
        // Subscribe to streaming manager events
        _streamingManager.StreamingUpdated += OnStreamingUpdated;
    }
    
    /// <summary>
    /// Handles streaming updates from the StreamingMessageManager.
    /// Updates the UI to reflect message changes.
    /// </summary>
    private void OnStreamingUpdated(object? sender, StreamingUpdateEventArgs args)
    {
        // Only handle events for the current session
        if (Session == null || args.SessionId != Session.SessionId)
            return;
        
        // Marshal to UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Find or add the message in our Messages collection
            var existingIndex = -1;
            for (int i = 0; i < Messages.Count; i++)
            {
                if (Messages[i].Id == args.Message.Id)
                {
                    existingIndex = i;
                    break;
                }
            }
            
            if (existingIndex >= 0)
            {
                // Update existing message by replacing it (triggers UI update)
                Messages[existingIndex] = new ChatMessage
                {
                    Id = args.Message.Id,
                    Role = args.Message.Role,
                    Content = args.Message.Content,
                    Timestamp = args.Message.Timestamp,
                    IsStreaming = args.Message.IsStreaming,
                    IsError = args.Message.IsError,
                    ToolCall = args.Message.ToolCall,
                    ToolResult = args.Message.ToolResult,
                    Metadata = args.Message.Metadata
                };
            }
            else
            {
                // Add new message
                Messages.Add(args.Message);
            }
            
            // Update busy state based on streaming
            if (args.IsComplete)
            {
                UpdateBusyState(false);
                ClearAllActiveTools();
                
                if (args.IsError)
                {
                    StatusMessage = "Error occurred";
                }
                else
                {
                    StatusMessage = string.Empty;
                }
            }
        });
    }

    private void OnTerminalAddToMessage(object? sender, string terminalOutput)
    {
        if (string.IsNullOrWhiteSpace(terminalOutput))
            return;
        
        // Format the terminal output as a code block and append to message input
        var formattedOutput = $"\n```terminal\n{terminalOutput}\n```\n";
        
        if (string.IsNullOrWhiteSpace(MessageInput))
        {
            MessageInput = formattedOutput;
        }
        else
        {
            MessageInput += formattedOutput;
        }
        
        _logger.LogInformation("Added terminal output to message input ({Length} chars)", terminalOutput.Length);
    }

    partial void OnMessageInputChanged(string value)
    {
        CanSendMessage = !string.IsNullOrWhiteSpace(value) && !IsBusy;
    }

    private void UpdateBusyState(bool busy)
    {
        IsBusy = busy;
        CanSendMessage = !string.IsNullOrWhiteSpace(MessageInput) && !busy;
        StatusMessage = busy ? "Thinking..." : string.Empty;
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _logger.LogInformation("Stop requested for session {SessionId}", Session?.SessionId ?? "none");
        StatusMessage = "Stopping...";
        
        // Stop via the streaming manager - this will cancel the operation and notify via events
        if (Session != null)
        {
            try
            {
                await _streamingManager.StopStreamingAsync(Session.SessionId);
                _logger.LogInformation("Stop request sent for session {SessionId}", Session.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop streaming for session {SessionId}", Session.SessionId);
            }
        }
    }

    /// <summary>
    /// Subscribes to SDK events if the service supports them.
    /// Called during initialization to receive tool execution progress.
    /// </summary>
    private void SubscribeToSdkEvents()
    {
        if (_copilotService is CopilotSdkService sdkService)
        {
            sdkService.SessionEventReceived += OnSdkSessionEvent;
            _logger.LogDebug("Subscribed to SDK session events");
        }
    }

    /// <summary>
    /// Unsubscribes from SDK events.
    /// </summary>
    private void UnsubscribeFromSdkEvents()
    {
        if (_copilotService is CopilotSdkService sdkService)
        {
            sdkService.SessionEventReceived -= OnSdkSessionEvent;
        }
    }

    /// <summary>
    /// Handles SDK session events for tool progress display.
    /// Uses toolCallId tracking to properly handle concurrent tool executions
    /// and prevent UI from getting stuck.
    /// </summary>
    private void OnSdkSessionEvent(object? sender, SdkSessionEventArgs args)
    {
        // Only handle events for the current session
        if (Session == null || args.SessionId != Session.SessionId)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (args.Event)
            {
                case ToolExecutionStartEvent toolStart:
                    HandleToolStart(toolStart);
                    break;

                case ToolExecutionCompleteEvent toolComplete:
                    HandleToolComplete(toolComplete);
                    break;

                case SessionIdleEvent:
                    // Session is idle - clear ALL active tools as a safety net
                    // This ensures we never get stuck even if complete events are missed
                    if (_activeTools.Count > 0)
                    {
                        _logger.LogInformation("SessionIdleEvent - clearing {Count} active tool(s) as safety net", 
                            _activeTools.Count);
                        _activeTools.Clear();
                    }
                    UpdateToolExecutionState();
                    _logger.LogDebug("Session idle - tool tracking cleared");
                    break;

                case AbortEvent:
                    // Abort clears all tool tracking
                    _activeTools.Clear();
                    UpdateToolExecutionState();
                    StatusMessage = "Aborted";
                    _logger.LogInformation("Session aborted - tool tracking cleared");
                    break;
            }
        });
    }
    
    /// <summary>
    /// Handles tool execution start by tracking the tool by its toolCallId.
    /// </summary>
    private void HandleToolStart(ToolExecutionStartEvent toolStart)
    {
        var toolCallId = toolStart.Data?.ToolCallId;
        var toolName = toolStart.Data?.ToolName ?? "Unknown tool";
        
        if (string.IsNullOrEmpty(toolCallId))
        {
            // Fallback: generate a temporary ID if none provided
            toolCallId = $"temp_{Guid.NewGuid():N}";
            _logger.LogWarning("ToolExecutionStartEvent missing toolCallId, using temp: {TempId}", toolCallId);
        }
        
        var toolInfo = new ToolExecutionInfo
        {
            ToolCallId = toolCallId,
            ToolName = toolName,
            StartTime = DateTime.UtcNow
        };
        
        _activeTools[toolCallId] = toolInfo;
        _logger.LogDebug("Tool execution started: {ToolName} (id: {ToolCallId}, active: {ActiveCount})", 
            toolName, toolCallId, _activeTools.Count);
        
        UpdateToolExecutionState();
    }
    
    /// <summary>
    /// Handles tool execution complete by removing the tool from tracking.
    /// </summary>
    private void HandleToolComplete(ToolExecutionCompleteEvent toolComplete)
    {
        var toolCallId = toolComplete.Data?.ToolCallId;
        
        if (string.IsNullOrEmpty(toolCallId))
        {
            _logger.LogWarning("ToolExecutionCompleteEvent missing toolCallId - cannot track completion");
            // Don't blindly clear - wait for SessionIdleEvent
            return;
        }
        
        if (_activeTools.TryRemove(toolCallId, out var removedTool))
        {
            var duration = DateTime.UtcNow - removedTool.StartTime;
            _logger.LogDebug("Tool execution completed: {ToolName} (id: {ToolCallId}, duration: {Duration}ms, remaining: {ActiveCount})", 
                removedTool.ToolName, toolCallId, duration.TotalMilliseconds, _activeTools.Count);
        }
        else
        {
            _logger.LogDebug("ToolExecutionCompleteEvent for unknown toolCallId: {ToolCallId} (may have been cleared by SessionIdleEvent)", 
                toolCallId);
        }
        
        UpdateToolExecutionState();
    }
    
    /// <summary>
    /// Updates the tool execution UI state based on active tools.
    /// </summary>
    private void UpdateToolExecutionState()
    {
        var hasActiveTools = _activeTools.Count > 0;
        IsToolExecuting = hasActiveTools;
        
        if (hasActiveTools)
        {
            // Show the most recently started tool (or first if multiple)
            var latestTool = _activeTools.Values
                .OrderByDescending(t => t.StartTime)
                .FirstOrDefault();
            
            CurrentToolName = latestTool?.ToolName ?? "Unknown tool";
            StatusMessage = _activeTools.Count > 1 
                ? $"Executing: {CurrentToolName} (+{_activeTools.Count - 1} more)"
                : $"Executing: {CurrentToolName}";
        }
        else
        {
            CurrentToolName = string.Empty;
            // Only clear status if we're still busy (streaming response)
            // Otherwise leave it alone
            if (IsBusy)
            {
                StatusMessage = "Thinking...";
            }
        }
    }
    
    /// <summary>
    /// Clears all active tool tracking. Called when message handling completes
    /// to ensure UI never gets stuck, even if SessionIdleEvent is not received
    /// (e.g., on timeout or error).
    /// </summary>
    private void ClearAllActiveTools()
    {
        if (_activeTools.Count > 0)
        {
            _logger.LogInformation("Clearing {Count} active tool(s) on message completion", _activeTools.Count);
            _activeTools.Clear();
        }
        UpdateToolExecutionState();
    }

    public Task InitializeAsync(Session session)
    {
        // Unsubscribe from previous session events
        UnsubscribeFromSdkEvents();
        
        Session = session;
        
        // Load existing messages from the single source of truth (Session.MessageHistory)
        RefreshMessagesFromHistory();
        
        // Check if this session has active streaming
        // If so, set busy state and ensure UI reflects streaming
        if (_streamingManager.IsStreaming(session.SessionId))
        {
            _logger.LogInformation("Session {SessionId} has active streaming - restoring busy state", 
                session.SessionId);
            UpdateBusyState(true);
        }
        else
        {
            // Ensure busy state is cleared when switching to idle session
            UpdateBusyState(false);
        }
        
        // Set terminal working directory
        if (!string.IsNullOrEmpty(session.WorkingDirectory))
        {
            TerminalViewModel.SetWorkingDirectory(session.WorkingDirectory);
        }
        else if (session.GitWorktreeInfo != null)
        {
            TerminalViewModel.SetWorkingDirectory(session.GitWorktreeInfo.WorktreePath);
        }

        // Initialize iterative task view model with session
        IterativeTaskViewModel.SetSession(session.SessionId);
        
        // Initialize session info view model with session
        SessionInfoViewModel.SetSession(session);
        
        // Subscribe to SDK events for tool progress
        SubscribeToSdkEvents();
        
        _logger.LogInformation("Chat initialized for session {SessionId} with {Count} messages (streaming: {IsStreaming})", 
            session.SessionId, session.MessageHistory.Count, _streamingManager.IsStreaming(session.SessionId));
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Refreshes the Messages collection from Session.MessageHistory.
    /// This is the authoritative source of message data.
    /// </summary>
    private void RefreshMessagesFromHistory()
    {
        Messages.Clear();
        foreach (var message in Session.MessageHistory)
        {
            Messages.Add(message);
        }
        _logger.LogDebug("Refreshed {Count} messages from session history", Messages.Count);
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(MessageInput) || IsBusy)
            return;

        var userMessage = MessageInput;
        MessageInput = string.Empty;
        UpdateBusyState(true);

        try
        {
            _logger.LogInformation("Sending message via StreamingMessageManager for session {SessionId}", 
                Session.SessionId);
            
            // Delegate to StreamingMessageManager - it will:
            // 1. Add user message to Session.MessageHistory
            // 2. Create assistant placeholder in Session.MessageHistory  
            // 3. Stream response and update history
            // 4. Notify us via StreamingUpdated events
            // 5. Save session when complete
            var messageId = await _streamingManager.StartStreamingAsync(Session, userMessage);
            
            _logger.LogInformation("Streaming started with message ID: {MessageId}", messageId);
            
            // Note: UpdateBusyState(false) will be called by OnStreamingUpdated when complete
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start streaming message");
            
            // Show error in UI
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = $"Error: {ex.Message}",
                Timestamp = DateTime.UtcNow
            });
            
            // Reset busy state on error
            UpdateBusyState(false);
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var result = MessageBox.Show(
            "Clear all chat history for this session?",
            "Confirm Clear",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _logger.LogInformation("Clearing chat history for session {SessionId}", Session.SessionId);
            Session.MessageHistory.Clear();
            Messages.Clear();
            await _sessionManager.SaveActiveSessionAsync();
        }
    }

    /// <summary>
    /// Copies message content to clipboard and provides visual feedback
    /// </summary>
    public void CopyToClipboard(string? content, Action? onCopied = null)
    {
        if (string.IsNullOrEmpty(content))
            return;

        try
        {
            System.Windows.Clipboard.SetText(content);
            _logger.LogDebug("Copied message content to clipboard ({Length} chars)", content.Length);
            onCopied?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy to clipboard");
        }
    }

    [RelayCommand]
    private void CopyMessage(string? content)
    {
        CopyToClipboard(content);
    }

    [RelayCommand]
    private async Task SaveSessionAsync()
    {
        try
        {
            await _sessionManager.SaveActiveSessionAsync();
            StatusMessage = "Session saved";
            _logger.LogInformation("Session {SessionId} saved", Session.SessionId);
            
            await Task.Delay(2000);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save session");
            MessageBox.Show(
                $"Failed to save session: {ex.Message}",
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

/// <summary>
/// Tracks information about a tool execution in progress.
/// </summary>
internal sealed class ToolExecutionInfo
{
    /// <summary>
    /// Unique identifier for this tool call (from SDK).
    /// </summary>
    public required string ToolCallId { get; init; }
    
    /// <summary>
    /// Name of the tool being executed.
    /// </summary>
    public required string ToolName { get; init; }
    
    /// <summary>
    /// When the tool execution started.
    /// </summary>
    public required DateTime StartTime { get; init; }
}
