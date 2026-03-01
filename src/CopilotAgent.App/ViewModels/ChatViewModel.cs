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
    
    /// <summary>
    /// Tracks active reasoning messages by reasoningId.
    /// Used to update streaming reasoning content as deltas arrive.
    /// </summary>
    private readonly ConcurrentDictionary<string, ChatMessage> _activeReasoningMessages = new();
    
    /// <summary>
    /// Tracks active tool commentary messages by toolCallId.
    /// </summary>
    private readonly ConcurrentDictionary<string, ChatMessage> _activeToolMessages = new();
    
    /// <summary>
    /// Current turn ID for grouping agent work items.
    /// </summary>
    private string? _currentTurnId;
    
    /// <summary>
    /// Tracks the message index where the current turn's agent work starts.
    /// Used for collapsing agent work after turn completion.
    /// </summary>
    private int _turnAgentWorkStartIndex = -1;

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
        
        // 1. Immediately reset UI state — do NOT wait for backend
        UpdateBusyState(false);
        ClearAllActiveTools();
        _activeReasoningMessages.Clear();
        _activeToolMessages.Clear();
        _currentTurnId = null;
        _turnAgentWorkStartIndex = -1;
        
        // 2. Mark any still-streaming messages as stopped
        MarkStreamingMessagesAsStopped();
        
        StatusMessage = "Stopped";
        
        // 3. Fire-and-forget: tell backend to abort (UI is already idle)
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
        
        // Brief status then clear
        await Task.Delay(1500);
        if (!IsBusy)
        {
            StatusMessage = string.Empty;
        }
    }
    
    /// <summary>
    /// Finds any messages still marked as streaming and marks them as stopped.
    /// This ensures the UI doesn't show perpetual "streaming" indicators after Stop.
    /// </summary>
    private void MarkStreamingMessagesAsStopped()
    {
        for (int i = 0; i < Messages.Count; i++)
        {
            var msg = Messages[i];
            if (msg.IsStreaming)
            {
                var clone = CloneMessageWithUpdate(msg);
                clone.IsStreaming = false;
                Messages[i] = clone;
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
    /// Handles SDK session events for tool progress display and agent commentary.
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
                // === TURN LIFECYCLE EVENTS ===
                case AssistantTurnStartEvent turnStart:
                    HandleTurnStart(turnStart, args.TurnId);
                    break;

                case AssistantTurnEndEvent turnEnd:
                    HandleTurnEnd(turnEnd);
                    break;

                // === REASONING/COMMENTARY EVENTS ===
                case AssistantReasoningDeltaEvent reasoningDelta:
                    HandleReasoningDelta(reasoningDelta, args.TurnId);
                    break;

                case AssistantReasoningEvent reasoning:
                    HandleReasoningComplete(reasoning, args.TurnId);
                    break;

                // === TOOL EXECUTION EVENTS ===
                case ToolExecutionStartEvent toolStart:
                    HandleToolStart(toolStart);
                    break;

                case ToolExecutionCompleteEvent toolComplete:
                    HandleToolComplete(toolComplete);
                    break;

                // === SESSION LIFECYCLE EVENTS ===
                case SessionIdleEvent:
                    // Session is idle - clear ALL active tools as a safety net
                    // This ensures we never get stuck even if complete events are missed
                    if (_activeTools.Count > 0)
                    {
                        _logger.LogInformation("SessionIdleEvent - clearing {Count} active tool(s) as safety net", 
                            _activeTools.Count);
                        _activeTools.Clear();
                    }
                    
                    // Also clear reasoning tracking
                    _activeReasoningMessages.Clear();
                    
                    UpdateToolExecutionState();
                    _logger.LogDebug("Session idle - tool and reasoning tracking cleared");
                    break;

                case AbortEvent:
                    // Abort clears all tracking AND resets busy state
                    _activeTools.Clear();
                    _activeReasoningMessages.Clear();
                    _activeToolMessages.Clear();
                    _currentTurnId = null;
                    _turnAgentWorkStartIndex = -1;
                    UpdateToolExecutionState();
                    MarkStreamingMessagesAsStopped();
                    UpdateBusyState(false);
                    StatusMessage = "Aborted";
                    _logger.LogInformation("Session aborted - all tracking and busy state cleared");
                    break;
            }
        });
    }
    
    #region Turn Lifecycle Handlers
    
    /// <summary>
    /// Handles assistant turn start - marks the beginning of agent work.
    /// </summary>
    private void HandleTurnStart(AssistantTurnStartEvent turnStart, string? turnId)
    {
        _currentTurnId = turnId ?? turnStart.Data?.TurnId;
        _turnAgentWorkStartIndex = Messages.Count; // Mark where agent work starts
        
        _logger.LogDebug("Turn started: {TurnId}, agent work starts at index {Index}", 
            _currentTurnId, _turnAgentWorkStartIndex);
    }
    
    /// <summary>
    /// Handles assistant turn end - collapses agent work into a summary bar.
    /// This happens automatically before the response is fully rendered.
    /// </summary>
    private void HandleTurnEnd(AssistantTurnEndEvent turnEnd)
    {
        var turnId = turnEnd.Data?.TurnId;
        
        _logger.LogDebug("Turn ended: {TurnId}, reasoning messages: {Count}", 
            turnId, _activeReasoningMessages.Count);
        
        // Collapse all reasoning messages from this turn into a summary
        CollapseAgentWorkToSummary();
        
        // Clear tracking
        _activeReasoningMessages.Clear();
        _currentTurnId = null;
        _turnAgentWorkStartIndex = -1;
    }
    
    /// <summary>
    /// Collapses all reasoning/agent work messages from the current turn into a single summary bar.
    /// This provides a clean, modern UX like VS Code Copilot.
    /// </summary>
    private void CollapseAgentWorkToSummary()
    {
        // Collect all agent work messages (reasoning + tool commentary)
        var agentWorkMessages = new List<ChatMessage>();
        var indicesToRemove = new List<int>();
        var reasoningCount = 0;
        var toolCount = 0;
        
        for (int i = 0; i < Messages.Count; i++)
        {
            var msg = Messages[i];
            if (msg.Role == MessageRole.Reasoning && msg.IsAgentWork)
            {
                // Check if this message belongs to current turn
                var isFromThisTurn = msg.TurnId == _currentTurnId || 
                    _activeReasoningMessages.ContainsKey(msg.ReasoningId ?? "") ||
                    _activeToolMessages.Values.Any(t => t.Id == msg.Id);
                
                if (isFromThisTurn)
                {
                    agentWorkMessages.Add(msg);
                    indicesToRemove.Add(i);
                    
                    // Count reasoning vs tool messages by content prefix
                    if (msg.Content?.StartsWith("🔧") == true || msg.Content?.StartsWith("✅") == true)
                        toolCount++;
                    else
                        reasoningCount++;
                }
            }
        }
        
        if (agentWorkMessages.Count == 0)
        {
            _logger.LogDebug("No agent work messages to collapse");
            return;
        }
        
        // Generate summary text based on what was done
        var summaryText = GenerateSummaryText(agentWorkMessages, reasoningCount, toolCount);
        var summaryMessage = new ChatMessage
        {
            Id = $"summary_{_currentTurnId ?? Guid.NewGuid().ToString()}",
            Role = MessageRole.AgentWorkSummary,
            Content = string.Empty,
            Timestamp = DateTime.UtcNow,
            IsStreaming = false,
            IsAgentWork = true,
            TurnId = _currentTurnId,
            SummaryText = summaryText,
            ReasoningCount = reasoningCount,
            ToolCount = toolCount,
            CollapsedMessages = agentWorkMessages,
            IsExpanded = false
        };
        
        // Find insertion point (where first message was)
        var insertIndex = indicesToRemove.Count > 0 ? indicesToRemove[0] : Messages.Count;
        
        // Remove agent work messages in reverse order to preserve indices
        for (int i = indicesToRemove.Count - 1; i >= 0; i--)
        {
            Messages.RemoveAt(indicesToRemove[i]);
        }
        
        // Adjust insert index if items were removed before it
        var removedBefore = indicesToRemove.Count(idx => idx < insertIndex);
        insertIndex -= removedBefore;
        
        // Insert summary message
        if (insertIndex >= 0 && insertIndex <= Messages.Count)
        {
            Messages.Insert(insertIndex, summaryMessage);
        }
        else
        {
            Messages.Add(summaryMessage);
        }
        
        // Clear tool message tracking
        _activeToolMessages.Clear();
        
        _logger.LogInformation("Collapsed {Total} agent work ({Reasoning} reasoning, {Tools} tools) into summary at index {Index}", 
            agentWorkMessages.Count, reasoningCount, toolCount, insertIndex);
    }
    
    /// <summary>
    /// Generates a brief summary text from the agent work messages.
    /// </summary>
    private static string GenerateSummaryText(List<ChatMessage> messages, int reasoningCount, int toolCount)
    {
        if (messages.Count == 0)
            return "Agent work completed";
        
        // Generate contextual summary based on what was done
        if (reasoningCount > 0 && toolCount > 0)
            return $"Agent analyzed and executed {toolCount} tool{(toolCount > 1 ? "s" : "")}";
        else if (toolCount > 0)
            return $"Agent executed {toolCount} tool{(toolCount > 1 ? "s" : "")}";
        else if (reasoningCount > 0)
        {
            // Take first few words from the first reasoning message as a preview
            var firstContent = messages.FirstOrDefault(m => 
                !m.Content.StartsWith("🔧") && !m.Content.StartsWith("✅"))?.Content;
            if (string.IsNullOrWhiteSpace(firstContent))
                return "Agent analyzed the request";
            
            // Truncate to ~50 chars for the summary
            var preview = firstContent.Length > 50 
                ? firstContent.Substring(0, 47) + "..." 
                : firstContent;
            
            // Clean up newlines
            preview = preview.Replace("\n", " ").Replace("\r", "").Trim();
            return preview;
        }
        
        return "Agent work completed";
    }
    
    #endregion
    
    #region Reasoning Event Handlers
    
    /// <summary>
    /// Handles streaming reasoning delta events.
    /// Creates or updates a reasoning message in the UI.
    /// IMPORTANT: Reasoning messages are inserted BEFORE the streaming assistant message
    /// to ensure correct visual order (commentary first, then response).
    /// </summary>
    private void HandleReasoningDelta(AssistantReasoningDeltaEvent reasoningDelta, string? turnId)
    {
        var reasoningId = reasoningDelta.Data?.ReasoningId;
        var deltaContent = reasoningDelta.Data?.DeltaContent;
        
        if (string.IsNullOrEmpty(reasoningId))
        {
            _logger.LogWarning("ReasoningDeltaEvent missing reasoningId");
            return;
        }
        
        // Find or create the reasoning message
        if (_activeReasoningMessages.TryGetValue(reasoningId, out var existingMessage))
        {
            // Update existing message with delta content
            existingMessage.Content += deltaContent ?? "";
            existingMessage.IsStreaming = true;
            
            // Find and update in Messages collection
            var index = FindMessageIndex(existingMessage.Id);
            if (index >= 0)
            {
                // Replace to trigger UI update
                Messages[index] = CloneMessageWithUpdate(existingMessage);
            }
            
            _logger.LogDebug("Updated reasoning message {ReasoningId}: +{Chars} chars", 
                reasoningId, deltaContent?.Length ?? 0);
        }
        else
        {
            // Create new reasoning message
            var message = new ChatMessage
            {
                Id = $"reasoning_{reasoningId}",
                Role = MessageRole.Reasoning,
                Content = deltaContent ?? "",
                Timestamp = DateTime.UtcNow,
                IsStreaming = true,
                IsAgentWork = true,
                ReasoningId = reasoningId,
                TurnId = turnId ?? _currentTurnId
            };
            
            _activeReasoningMessages[reasoningId] = message;
            
            // Insert BEFORE the streaming assistant message to maintain correct order:
            // User message -> Reasoning/Commentary -> Assistant response
            var insertIndex = FindStreamingAssistantMessageIndex();
            if (insertIndex >= 0)
            {
                Messages.Insert(insertIndex, message);
                _logger.LogDebug("Inserted reasoning message {ReasoningId} at index {Index} (before assistant)", 
                    reasoningId, insertIndex);
            }
            else
            {
                // Fallback: append if no streaming assistant found
                Messages.Add(message);
                _logger.LogDebug("Appended reasoning message {ReasoningId} (no streaming assistant found)", 
                    reasoningId);
            }
        }
    }
    
    /// <summary>
    /// Finds the index of the current streaming assistant message.
    /// Returns -1 if not found.
    /// </summary>
    private int FindStreamingAssistantMessageIndex()
    {
        for (int i = Messages.Count - 1; i >= 0; i--)
        {
            if (Messages[i].Role == MessageRole.Assistant && Messages[i].IsStreaming)
            {
                return i;
            }
        }
        return -1;
    }
    
    /// <summary>
    /// Handles complete reasoning event.
    /// This may fire instead of or after deltas.
    /// </summary>
    private void HandleReasoningComplete(AssistantReasoningEvent reasoning, string? turnId)
    {
        var reasoningId = reasoning.Data?.ReasoningId;
        var content = reasoning.Data?.Content;
        
        if (string.IsNullOrEmpty(reasoningId))
        {
            _logger.LogWarning("ReasoningEvent missing reasoningId");
            return;
        }
        
        if (_activeReasoningMessages.TryGetValue(reasoningId, out var existingMessage))
        {
            // Update existing message with complete content
            existingMessage.Content = content ?? existingMessage.Content;
            existingMessage.IsStreaming = false;
            
            // Find and update in Messages collection
            var index = FindMessageIndex(existingMessage.Id);
            if (index >= 0)
            {
                Messages[index] = CloneMessageWithUpdate(existingMessage);
            }
        }
        else
        {
            // Create complete reasoning message (no deltas were received)
            var message = new ChatMessage
            {
                Id = $"reasoning_{reasoningId}",
                Role = MessageRole.Reasoning,
                Content = content ?? "",
                Timestamp = DateTime.UtcNow,
                IsStreaming = false,
                IsAgentWork = true,
                ReasoningId = reasoningId,
                TurnId = turnId ?? _currentTurnId
            };
            
            _activeReasoningMessages[reasoningId] = message;
            Messages.Add(message);
        }
        
        _logger.LogDebug("Reasoning complete: {ReasoningId}, {Length} chars", 
            reasoningId, content?.Length ?? 0);
    }
    
    /// <summary>
    /// Finds the index of a message by ID in the Messages collection.
    /// </summary>
    private int FindMessageIndex(string messageId)
    {
        for (int i = 0; i < Messages.Count; i++)
        {
            if (Messages[i].Id == messageId)
                return i;
        }
        return -1;
    }
    
    /// <summary>
    /// Creates a clone of a message with updated properties.
    /// This is needed to trigger UI updates in WPF.
    /// </summary>
    private static ChatMessage CloneMessageWithUpdate(ChatMessage source)
    {
        return new ChatMessage
        {
            Id = source.Id,
            Role = source.Role,
            Content = source.Content,
            Timestamp = source.Timestamp,
            IsStreaming = source.IsStreaming,
            IsError = source.IsError,
            ToolCall = source.ToolCall,
            ToolResult = source.ToolResult,
            Metadata = source.Metadata,
            ReasoningId = source.ReasoningId,
            TurnId = source.TurnId,
            IsAgentWork = source.IsAgentWork,
            CollapsedMessages = source.CollapsedMessages,
            SummaryText = source.SummaryText,
            IsExpanded = source.IsExpanded,
            ToolCount = source.ToolCount,
            ReasoningCount = source.ReasoningCount
        };
    }
    
    #endregion
    
    /// <summary>
    /// Handles tool execution start by tracking the tool and creating commentary.
    /// </summary>
    private void HandleToolStart(ToolExecutionStartEvent toolStart)
    {
        var toolCallId = toolStart.Data?.ToolCallId;
        var toolName = toolStart.Data?.ToolName ?? "Unknown tool";
        
        if (string.IsNullOrEmpty(toolCallId))
        {
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
        
        // Create commentary message for tool execution
        var toolMessage = new ChatMessage
        {
            Id = $"tool_{toolCallId}",
            Role = MessageRole.Reasoning,
            Content = $"🔧 Executing: {toolName}",
            Timestamp = DateTime.UtcNow,
            IsStreaming = true,
            IsAgentWork = true,
            TurnId = _currentTurnId
        };
        
        _activeToolMessages[toolCallId] = toolMessage;
        
        // Insert BEFORE streaming assistant message
        var insertIndex = FindStreamingAssistantMessageIndex();
        if (insertIndex >= 0)
        {
            Messages.Insert(insertIndex, toolMessage);
            _logger.LogDebug("Inserted tool commentary at index {Index}: {ToolName}", insertIndex, toolName);
        }
        else
        {
            Messages.Add(toolMessage);
        }
        
        _logger.LogDebug("Tool execution started: {ToolName} (id: {ToolCallId}, active: {ActiveCount})", 
            toolName, toolCallId, _activeTools.Count);
        
        UpdateToolExecutionState();
    }
    
    /// <summary>
    /// Handles tool execution complete by updating commentary and removing from tracking.
    /// </summary>
    private void HandleToolComplete(ToolExecutionCompleteEvent toolComplete)
    {
        var toolCallId = toolComplete.Data?.ToolCallId;
        
        if (string.IsNullOrEmpty(toolCallId))
        {
            _logger.LogWarning("ToolExecutionCompleteEvent missing toolCallId - cannot track completion");
            return;
        }
        
        // Update tool commentary message
        if (_activeToolMessages.TryGetValue(toolCallId, out var toolMessage))
        {
            var duration = _activeTools.TryGetValue(toolCallId, out var info) 
                ? (int)(DateTime.UtcNow - info.StartTime).TotalMilliseconds 
                : 0;
            var toolName = info?.ToolName ?? "Unknown tool";
            
            toolMessage.Content = $"✅ Completed: {toolName} ({duration}ms)";
            toolMessage.IsStreaming = false;
            
            // Update in Messages collection
            var index = FindMessageIndex(toolMessage.Id);
            if (index >= 0)
            {
                Messages[index] = CloneMessageWithUpdate(toolMessage);
            }
            
            _logger.LogDebug("Updated tool commentary: {ToolName} completed in {Duration}ms", toolName, duration);
        }
        
        if (_activeTools.TryRemove(toolCallId, out var removedTool))
        {
            var duration = DateTime.UtcNow - removedTool.StartTime;
            _logger.LogDebug("Tool execution completed: {ToolName} (id: {ToolCallId}, duration: {Duration}ms, remaining: {ActiveCount})", 
                removedTool.ToolName, toolCallId, duration.TotalMilliseconds, _activeTools.Count);
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
