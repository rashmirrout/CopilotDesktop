using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CopilotAgent.Core.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Set of internal tools that should be auto-approved without user interaction.
/// These are typically low-risk read operations or internal SDK operations.
/// </summary>
internal static class AutoApprovedInternalTools
{
    private static readonly HashSet<string> _autoApprovedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        // Read-only operations
        "read_file",
        "view_file",
        "list_files",
        "list_directory",
        "search_files",
        "get_file_info",
        "find_files",
        "grep",
        
        // Code analysis (read-only)
        "get_symbols",
        "get_references",
        "get_definition",
        "get_hover",
        "get_diagnostics",
        
        // Context gathering
        "get_context",
        "get_workspace_info",
        "get_project_info",
        
        // Internal SDK operations
        "thinking",
        "plan",
        "summarize",
        
        // Documentation and help tools
        "fetch_copilot_cli_documentation",
        "get_help",
        "show_documentation",
        "get_documentation",
        "fetch_documentation",
        
        // MCP discovery/listing (read-only)
        "list_mcp_servers",
        "list_tools",
        "list_mcp_tools",
        "get_tools",
        "get_mcp_servers",
        
        // Copilot internal tools
        "report_intent",
        "glob",
        "view",
        "fetch",
        "find",
        "search",
        "lookup"
    };
    
    /// <summary>
    /// Checks if a tool is an internal/low-risk tool that can be auto-approved.
    /// </summary>
    public static bool IsAutoApproved(string toolName, object? toolArgs)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return false;
        
        // Check exact match first
        if (_autoApprovedTools.Contains(toolName))
            return true;
        
        // Check partial matches for common patterns
        var lowerName = toolName.ToLowerInvariant();
        
        // Auto-approve read operations
        if (lowerName.StartsWith("read_") || lowerName.StartsWith("get_") || 
            lowerName.StartsWith("list_") || lowerName.StartsWith("view_"))
            return true;
        
        // Auto-approve if it's an internal tool with no/empty args
        // (these are often internal SDK probe calls)
        if (IsEmptyOrInternalArgs(toolArgs))
        {
            // Common internal tool patterns
            if (lowerName.Contains("info") || lowerName.Contains("status") || 
                lowerName.Contains("check") || lowerName.Contains("validate"))
                return true;
        }
        
        return false;
    }
    
    private static bool IsEmptyOrInternalArgs(object? args)
    {
        if (args == null)
            return true;
        
        if (args is string s && string.IsNullOrWhiteSpace(s))
            return true;
        
        if (args is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Null || je.ValueKind == JsonValueKind.Undefined)
                return true;
            if (je.ValueKind == JsonValueKind.Object && je.EnumerateObject().Count() == 0)
                return true;
            if (je.ValueKind == JsonValueKind.Array && je.GetArrayLength() == 0)
                return true;
        }
        
        return false;
    }
}

/// <summary>
/// SDK-based implementation for interacting with GitHub Copilot.
/// 
/// This is the recommended implementation that uses the GitHub Copilot SDK
/// with JSON-RPC communication for better integration and tool approval support.
/// 
/// Key features:
/// - OnPreToolUse hook for tool approval gating
/// - Event streaming support
/// - Graceful abort/cancellation
/// - Session resume support
/// - Integration with autonomous mode settings
/// - Skills integration (SkillDirectories + DisabledSkills)
/// </summary>
public class CopilotSdkService : ICopilotService, IAsyncDisposable
{
    private readonly IToolApprovalService _approvalService;
    private readonly IMcpService _mcpService;
    private readonly ISkillsService _skillsService;
    private readonly IPersistenceService _persistenceService;
    private readonly ILogger<CopilotSdkService> _logger;
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    
    private CopilotClient? _client;
    private readonly ConcurrentDictionary<string, CopilotSession> _sdkSessions = new();
    private readonly ConcurrentDictionary<string, Session> _appSessions = new();
    
    /// <summary>
    /// Tracks streaming context per session for state-aware timeout management.
    /// Key = sessionId, Value = streaming context with state machine.
    /// </summary>
    private readonly ConcurrentDictionary<string, SessionStreamingContext> _streamingContexts = new();
    
    /// <summary>
    /// Tracks observed tools per session. Key = sessionId, Value = dictionary of toolName -> toolInfo
    /// Tools are observed through PreToolUse hooks during session execution.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ObservedToolInfo>> _observedToolsPerSession = new();
    
    /// <summary>
    /// Cached settings to avoid async loads during streaming.
    /// Refreshed periodically or when settings change.
    /// </summary>
    private StreamingTimeoutSettings _cachedTimeoutSettings = new();
    private DateTime _settingsLastLoadedUtc = DateTime.MinValue;
    private readonly TimeSpan _settingsCacheDuration = TimeSpan.FromMinutes(5);
    
    private bool _disposed;

    /// <summary>
    /// Event raised when a session event occurs (assistant message, tool execution, etc.)
    /// </summary>
    public event EventHandler<SdkSessionEventArgs>? SessionEventReceived;
    
    /// <summary>
    /// Event raised when streaming progress changes (tool started, completed, etc.)
    /// Used by UI to show progress indicators.
    /// </summary>
    public event EventHandler<StreamingProgressEventArgs>? StreamingProgressChanged;

    /// <summary>
    /// Internal class to track observed tools during session execution.
    /// </summary>
    private class ObservedToolInfo
    {
        public string Name { get; set; } = "";
        public string? McpServerName { get; set; }
        public object? LastArgs { get; set; }
        public int InvocationCount { get; set; }
        public DateTimeOffset LastUsed { get; set; }
    }

    public CopilotSdkService(
        IToolApprovalService approvalService,
        IMcpService mcpService,
        ISkillsService skillsService,
        IPersistenceService persistenceService,
        ILogger<CopilotSdkService> logger)
    {
        _approvalService = approvalService;
        _mcpService = mcpService;
        _skillsService = skillsService;
        _persistenceService = persistenceService;
        _logger = logger;
        _logger.LogInformation("CopilotSdkService initialized with configurable timeouts");
    }
    
    /// <summary>
    /// Gets the current timeout settings, using cached values when possible.
    /// </summary>
    private async Task<StreamingTimeoutSettings> GetTimeoutSettingsAsync()
    {
        // Check if we need to refresh the cache
        if (DateTime.UtcNow - _settingsLastLoadedUtc > _settingsCacheDuration)
        {
            try
            {
                var settings = await _persistenceService.LoadSettingsAsync();
                _cachedTimeoutSettings = settings.StreamingTimeouts ?? new StreamingTimeoutSettings();
                _settingsLastLoadedUtc = DateTime.UtcNow;
                _logger.LogDebug("Refreshed timeout settings cache: Idle={Idle}s, Tool={Tool}s, Approval={Approval}s",
                    _cachedTimeoutSettings.IdleTimeoutSeconds,
                    _cachedTimeoutSettings.ToolExecutionTimeoutSeconds,
                    _cachedTimeoutSettings.ApprovalWaitTimeoutSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load timeout settings, using defaults");
                if (_cachedTimeoutSettings == null)
                {
                    _cachedTimeoutSettings = new StreamingTimeoutSettings();
                }
            }
        }
        
        return _cachedTimeoutSettings;
    }
    
    /// <summary>
    /// Gets the streaming context for a session, creating one if it doesn't exist.
    /// </summary>
    private SessionStreamingContext GetOrCreateStreamingContext(string sessionId)
    {
        return _streamingContexts.GetOrAdd(sessionId, id => new SessionStreamingContext(id));
    }
    
    /// <summary>
    /// Notifies the streaming context that we're waiting for user approval.
    /// Called from the PreToolUse hook when user approval is needed.
    /// </summary>
    internal void NotifyWaitingForApproval(string sessionId, string toolName)
    {
        if (_streamingContexts.TryGetValue(sessionId, out var context))
        {
            context.StartWaitingForApproval(toolName);
            RaiseProgressChanged(sessionId, context);
            _logger.LogDebug("Session {SessionId} waiting for approval: {Tool}", sessionId, toolName);
        }
    }
    
    /// <summary>
    /// Notifies the streaming context that approval has completed.
    /// Called from the PreToolUse hook after user makes a decision.
    /// </summary>
    internal void NotifyApprovalCompleted(string sessionId)
    {
        if (_streamingContexts.TryGetValue(sessionId, out var context))
        {
            context.CompleteApproval();
            RaiseProgressChanged(sessionId, context);
            _logger.LogDebug("Session {SessionId} approval completed", sessionId);
        }
    }
    
    /// <summary>
    /// Raises the StreamingProgressChanged event.
    /// </summary>
    private void RaiseProgressChanged(string sessionId, SessionStreamingContext context)
    {
        try
        {
            var settings = _cachedTimeoutSettings ?? new StreamingTimeoutSettings();
            var message = context.GetProgressMessage(settings);
            StreamingProgressChanged?.Invoke(this, new StreamingProgressEventArgs(sessionId, context.State, message, context));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error raising progress changed event");
        }
    }

    /// <summary>
    /// Ensures the SDK client is initialized and connected.
    /// </summary>
    private async Task<CopilotClient> EnsureClientAsync(CancellationToken cancellationToken = default)
    {
        if (_client != null)
            return _client;

        await _clientLock.WaitAsync(cancellationToken);
        try
        {
            if (_client != null)
                return _client;

            _logger.LogInformation("Initializing Copilot SDK client...");
            
            // Check if copilot CLI is available
            var cliPath = FindCopilotCli();
            if (cliPath != null)
            {
                _logger.LogInformation("Found Copilot CLI at: {Path}", cliPath);
            }
            else
            {
                _logger.LogWarning("Could not find Copilot CLI in PATH or common locations. SDK will attempt to locate it.");
            }
            
            var options = new CopilotClientOptions
            {
                UseStdio = true,
                LogLevel = "debug", // Use debug for more verbose logging
                AutoStart = true,
                AutoRestart = true
            };
            
            _logger.LogInformation("Creating CopilotClient with options: UseStdio={UseStdio}, LogLevel={LogLevel}, AutoStart={AutoStart}",
                options.UseStdio, options.LogLevel, options.AutoStart);
            
            _client = new CopilotClient(options);

            _logger.LogInformation("Starting CopilotClient...");
            await _client.StartAsync(cancellationToken);
            _logger.LogInformation("CopilotClient started successfully");
            
            // Verify connection with a ping
            try
            {
                _logger.LogInformation("Verifying connection with ping...");
                var pingResponse = await _client.PingAsync("init-check");
                _logger.LogInformation("Ping successful: {Response}", pingResponse);
            }
            catch (Exception pingEx)
            {
                _logger.LogWarning(pingEx, "Ping after startup failed - connection may be unstable");
            }
            
            return _client;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize CopilotClient: {Message}", ex.Message);
            _client = null;
            throw;
        }
        finally
        {
            _clientLock.Release();
        }
    }
    
    /// <summary>
    /// Attempts to find the Copilot CLI in common locations.
    /// </summary>
    private string? FindCopilotCli()
    {
        // Check PATH first
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathDirs = pathEnv.Split(Path.PathSeparator);
        
        var cliNames = new[] { "copilot", "copilot.exe", "github-copilot-cli", "github-copilot-cli.exe" };
        
        foreach (var dir in pathDirs)
        {
            foreach (var name in cliNames)
            {
                var fullPath = Path.Combine(dir, name);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }
        
        // Check common installation locations
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var commonPaths = new[]
        {
            Path.Combine(homeDir, ".copilot", "bin", "copilot.exe"),
            Path.Combine(homeDir, ".copilot", "bin", "copilot"),
            Path.Combine(homeDir, "AppData", "Local", "Programs", "copilot", "copilot.exe"),
            Path.Combine(homeDir, ".local", "bin", "copilot"),
            "/usr/local/bin/copilot",
            "/usr/bin/copilot"
        };
        
        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }
        
        return null;
    }

    public async Task<bool> IsCopilotAvailableAsync()
    {
        try
        {
            var client = await EnsureClientAsync();
            var response = await client.PingAsync("health-check");
            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check Copilot availability via SDK");
            return false;
        }
    }

    public async Task<List<string>> GetAvailableModelsAsync()
    {
        try
        {
            var client = await EnsureClientAsync();
            var models = await client.ListModelsAsync();
            return models.Select(m => m.Id).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get models from SDK, returning defaults");
            return new List<string>
            {
                "claude-sonnet-4.5",
                "claude-haiku-4.5",
                "gpt-5.2-codex",
                "gpt-5.1",
                "gpt-4.1"
            };
        }
    }

    public async IAsyncEnumerable<ChatMessage> SendMessageStreamingAsync(
        Session session,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending message via SDK for session {SessionId}: {Message}",
            session.SessionId, userMessage.Length > 50 ? userMessage[..50] + "..." : userMessage);

        // Load timeout settings at start of streaming
        var timeoutSettings = await GetTimeoutSettingsAsync();
        _logger.LogDebug("Using timeout settings: Idle={Idle}s, Tool={Tool}s, Approval={Approval}s, ProgressTracking={Progress}",
            timeoutSettings.IdleTimeoutSeconds, 
            timeoutSettings.ToolExecutionTimeoutSeconds,
            timeoutSettings.ApprovalWaitTimeoutSeconds,
            timeoutSettings.EnableProgressTracking);

        var message = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = string.Empty,
            IsStreaming = true,
            Timestamp = DateTime.UtcNow
        };

        CopilotSession? sdkSession = null;
        string? initError = null;
        
        try
        {
            sdkSession = await GetOrCreateSdkSessionAsync(session, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create SDK session for {SessionId}", session.SessionId);
            initError = ex.Message;
        }

        // Handle initialization error outside of try-catch to allow yield
        if (sdkSession == null)
        {
            message.Content = $"Error: Failed to create SDK session: {initError ?? "Unknown error"}";
            message.IsStreaming = false;
            message.IsError = true;
            yield return message;
            yield break;
        }

        // Create streaming context for state-aware timeout management
        var streamingContext = GetOrCreateStreamingContext(session.SessionId);
        streamingContext.TransitionTo(SessionStreamingState.Idle);
        
        var contentBuilder = new System.Text.StringBuilder();
        var messageComplete = false;
        var errorOccurred = false;
        string? lastWarningMessage = null;

        // Track current turn ID for grouping agent work
        string? currentTurnId = null;
        
        // Subscribe to streaming events with state-aware handling
        using var subscription = sdkSession.On(evt =>
        {
            // Record event and update activity time
            streamingContext.RecordEvent();
            
            // Log events for debugging
            var eventTypeName = evt.GetType().Name;
            _logger.LogInformation("[EVENT #{Count}] {EventType} received for session {SessionId} (state: {State})",
                streamingContext.EventCount, eventTypeName, session.SessionId, streamingContext.State);
            
            switch (evt)
            {
                // === TURN LIFECYCLE EVENTS ===
                case AssistantTurnStartEvent turnStart:
                    currentTurnId = turnStart.Data?.TurnId;
                    _logger.LogInformation("Assistant turn started: {TurnId}", currentTurnId);
                    break;

                case AssistantTurnEndEvent turnEnd:
                    _logger.LogInformation("Assistant turn ended: {TurnId}", turnEnd.Data?.TurnId);
                    // The turn is complete - UI can now collapse agent work
                    break;

                // === REASONING/THINKING EVENTS (Agent Commentary) ===
                case AssistantReasoningDeltaEvent reasoningDelta:
                    // Transition to streaming state when receiving reasoning
                    if (streamingContext.State != SessionStreamingState.Streaming &&
                        streamingContext.State != SessionStreamingState.ToolExecuting)
                    {
                        streamingContext.TransitionTo(SessionStreamingState.Streaming);
                        RaiseProgressChanged(session.SessionId, streamingContext);
                    }
                    
                    _logger.LogDebug("Reasoning delta: +{Length} chars (reasoningId: {Id})", 
                        reasoningDelta.Data?.DeltaContent?.Length ?? 0,
                        reasoningDelta.Data?.ReasoningId);
                    
                    // Forward to UI via event - ChatViewModel will handle display
                    // The event includes: reasoningId and deltaContent
                    break;

                case AssistantReasoningEvent reasoning:
                    // Complete reasoning content (may fire instead of or after deltas)
                    _logger.LogInformation("Reasoning complete: {Length} chars (reasoningId: {Id})",
                        reasoning.Data?.Content?.Length ?? 0,
                        reasoning.Data?.ReasoningId);
                    break;

                // === ASSISTANT MESSAGE EVENTS ===
                case AssistantMessageDeltaEvent delta:
                    // Transition to streaming state when receiving content
                    if (streamingContext.State != SessionStreamingState.Streaming &&
                        streamingContext.State != SessionStreamingState.ToolExecuting)
                    {
                        streamingContext.TransitionTo(SessionStreamingState.Streaming);
                        RaiseProgressChanged(session.SessionId, streamingContext);
                    }
                    
                    if (delta.Data?.DeltaContent != null)
                    {
                        contentBuilder.Append(delta.Data.DeltaContent);
                        _logger.LogDebug("Delta content: +{Length} chars", delta.Data.DeltaContent.Length);
                    }
                    break;

                case AssistantMessageEvent complete:
                    // NOTE: Do NOT mark complete here - tools may still be executing!
                    // Wait for SessionIdleEvent to signal true completion.
                    _logger.LogInformation("AssistantMessageEvent received (NOT marking complete - waiting for SessionIdleEvent)");
                    if (complete.Data?.Content != null)
                    {
                        contentBuilder.Clear();
                        contentBuilder.Append(complete.Data.Content);
                    }
                    break;

                // === TOOL EXECUTION EVENTS ===
                case ToolExecutionStartEvent toolStart:
                    // Tool is starting - use longer timeout
                    var toolName = toolStart.Data?.ToolName ?? "unknown";
                    streamingContext.StartToolExecution(toolName);
                    RaiseProgressChanged(session.SessionId, streamingContext);
                    _logger.LogInformation("Tool execution started: {Tool} (timeout: {Timeout}s)", 
                        toolName, timeoutSettings.ToolExecutionTimeoutSeconds);
                    break;

                case ToolExecutionCompleteEvent toolComplete:
                    // Tool completed - return to idle timeout
                    // Note: We use the tracked tool name from context since ToolExecutionCompleteData
                    // may not have a ToolName property (depends on SDK version)
                    var completedToolName = streamingContext.CurrentToolName ?? "unknown";
                    streamingContext.CompleteToolExecution();
                    RaiseProgressChanged(session.SessionId, streamingContext);
                    _logger.LogInformation("Tool execution completed: {Tool} (total tools: {Count})", 
                        completedToolName, streamingContext.ToolsExecutedCount);
                    break;

                // === SESSION LIFECYCLE EVENTS ===
                case SessionErrorEvent error:
                    _logger.LogError("SDK session error occurred - marking error");
                    streamingContext.TransitionTo(SessionStreamingState.Error);
                    errorOccurred = true;
                    break;

                case SessionIdleEvent:
                    // Session is idle - THIS is the true completion signal
                    // Only fires after all tools have completed
                    _logger.LogInformation("SessionIdleEvent - marking complete (all tools finished)");
                    streamingContext.TransitionTo(SessionStreamingState.Completed);
                    messageComplete = true;
                    break;

                default:
                    // Log other event types for debugging
                    _logger.LogDebug("Unhandled event type: {EventType}", eventTypeName);
                    break;
            }

            // Dispatch to UI - includes turnId context for grouping
            SessionEventReceived?.Invoke(this, new SdkSessionEventArgs(session.SessionId, evt, currentTurnId));
        });

        try
        {
            // Send the message
            _logger.LogInformation("Sending message to SDK session...");
            await sdkSession.SendAsync(new MessageOptions { Prompt = userMessage }, cancellationToken);
            _logger.LogInformation("Message sent, waiting for response...");
            
            // Raise initial progress event
            RaiseProgressChanged(session.SessionId, streamingContext);

            // Stream the response with state-aware timeout management
            while (!messageComplete && !errorOccurred && !cancellationToken.IsCancellationRequested)
            {
                message.Content = contentBuilder.ToString();
                yield return message;
                
                // Check for timeout based on current state
                if (streamingContext.IsTimedOut(timeoutSettings))
                {
                    // Get detailed diagnostics BEFORE generating the message
                    var diagnostics = streamingContext.GetTimeoutDiagnostics(timeoutSettings);
                    var timeoutMessage = streamingContext.GetTimeoutMessage(timeoutSettings);
                    
                    // Log comprehensive timeout information for debugging
                    _logger.LogWarning(
                        "Session {SessionId} TIMEOUT TRIGGERED:\n" +
                        "  Diagnostics: {Diagnostics}\n" +
                        "  Explanation: {Explanation}\n" +
                        "  Message: {Message}",
                        session.SessionId, 
                        diagnostics.ToString(),
                        diagnostics.GetTimeoutExplanation(),
                        timeoutMessage);
                    
                    // If we have content, append timeout message; otherwise set as content
                    if (contentBuilder.Length == 0)
                    {
                        contentBuilder.Append(timeoutMessage);
                    }
                    else
                    {
                        contentBuilder.AppendLine();
                        contentBuilder.Append(timeoutMessage);
                    }
                    
                    // Raise timeout progress event
                    StreamingProgressChanged?.Invoke(this, new StreamingProgressEventArgs(
                        session.SessionId, streamingContext.State, 
                        timeoutMessage, streamingContext, isWarning: true, warningMessage: timeoutMessage));
                    
                    break;
                }
                
                // Check for warning threshold
                if (streamingContext.ShouldShowWarning(timeoutSettings))
                {
                    var warningMessage = streamingContext.GetTimeoutWarningMessage(timeoutSettings);
                    
                    // Only log/display if this is a new warning
                    if (warningMessage != lastWarningMessage)
                    {
                        lastWarningMessage = warningMessage;
                        
                        // Log detailed context for the warning
                        var diagnostics = streamingContext.GetTimeoutDiagnostics(timeoutSettings);
                        _logger.LogWarning(
                            "Session {SessionId} TIMEOUT WARNING:\n" +
                            "  Warning: {Warning}\n" +
                            "  Diagnostics: {Diagnostics}\n" +
                            "  Protection Status: {Explanation}",
                            session.SessionId, 
                            warningMessage,
                            diagnostics.ToString(),
                            diagnostics.GetTimeoutExplanation());
                        
                        // Raise warning progress event
                        StreamingProgressChanged?.Invoke(this, new StreamingProgressEventArgs(
                            session.SessionId, streamingContext.State, 
                            warningMessage, streamingContext, isWarning: true, warningMessage: warningMessage));
                        
                        // Increment warning count (for max warnings limit)
                        streamingContext.IncrementWarningCount(timeoutSettings.MaxConsecutiveWarnings);
                    }
                }
                
                // Update progress display if enabled and enough time has passed
                if (streamingContext.ShouldUpdateProgress(timeoutSettings))
                {
                    streamingContext.MarkProgressUpdated();
                    RaiseProgressChanged(session.SessionId, streamingContext);
                }
                
                await Task.Delay(50, cancellationToken); // Small delay for streaming updates
            }

            // Final message
            message.Content = contentBuilder.ToString();
            message.IsStreaming = false;
            message.IsError = errorOccurred;
            yield return message;

            _logger.LogInformation("Response complete for session {SessionId} (events: {EventCount}, tools: {ToolCount}, error: {Error}, cancelled: {Cancelled}, complete: {Complete})", 
                session.SessionId, streamingContext.EventCount, streamingContext.ToolsExecutedCount, 
                errorOccurred, cancellationToken.IsCancellationRequested, messageComplete);
        }
        finally
        {
            // Clean up streaming context (but keep it for potential future reference)
            _logger.LogDebug("Streaming context final state: {Context}", streamingContext);
        }
    }

    public async Task<ChatMessage> SendMessageAsync(
        Session session,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        await foreach (var msg in SendMessageStreamingAsync(session, userMessage, cancellationToken))
        {
            if (!msg.IsStreaming)
            {
                return msg;
            }
        }

        return new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = "No response received",
            IsError = true
        };
    }

    public Task<ToolResult> ExecuteCommandAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        // SDK mode doesn't directly execute commands - this is handled by the SDK's tool system
        // Return a not-supported result
        _logger.LogWarning("ExecuteCommandAsync called in SDK mode - not directly supported");
        return Task.FromResult(new ToolResult
        {
            Success = false,
            Error = "Direct command execution not supported in SDK mode. Commands are executed via Copilot tools."
        });
    }

    public void TerminateSessionProcess(string sessionId)
    {
        _sdkSessions.TryRemove(sessionId, out _);
        _appSessions.TryRemove(sessionId, out _);
        _logger.LogDebug("Cleared SDK session tracking for {SessionId}", sessionId);
    }

    public void TerminateAllProcesses()
    {
        _sdkSessions.Clear();
        _appSessions.Clear();
        _logger.LogDebug("Cleared all SDK session tracking");
    }

    public async Task AbortAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sdkSessions.TryGetValue(sessionId, out var sdkSession))
        {
            try
            {
                await sdkSession.AbortAsync(cancellationToken);
                _logger.LogInformation("Aborted SDK session {SessionId}", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to abort SDK session {SessionId}", sessionId);
            }
        }
    }

    /// <summary>
    /// Checks if a session has an active SDK session.
    /// </summary>
    public bool HasActiveSession(string sessionId)
    {
        return _sdkSessions.ContainsKey(sessionId);
    }

    /// <summary>
    /// Tracks an observed tool for MCP server discovery.
    /// Called from PreToolUse hook to build up knowledge of available tools.
    /// </summary>
    private void TrackObservedTool(string sessionId, string toolName, object? toolArgs)
    {
        try
        {
            // Get or create the tools dictionary for this session
            var sessionTools = _observedToolsPerSession.GetOrAdd(
                sessionId, 
                _ => new ConcurrentDictionary<string, ObservedToolInfo>());
            
            // Parse MCP server name from tool name (format: "serverName/toolName" or just "toolName")
            string? mcpServerName = null;
            var actualToolName = toolName;
            var slashIndex = toolName.IndexOf('/');
            if (slashIndex > 0)
            {
                mcpServerName = toolName.Substring(0, slashIndex);
                actualToolName = toolName.Substring(slashIndex + 1);
            }
            
            // Update or add tool info
            sessionTools.AddOrUpdate(
                toolName,
                _ => new ObservedToolInfo
                {
                    Name = actualToolName,
                    McpServerName = mcpServerName,
                    LastArgs = toolArgs,
                    InvocationCount = 1,
                    LastUsed = DateTimeOffset.UtcNow
                },
                (_, existing) =>
                {
                    existing.LastArgs = toolArgs;
                    existing.InvocationCount++;
                    existing.LastUsed = DateTimeOffset.UtcNow;
                    return existing;
                });
            
            _logger.LogInformation("[MCP_TRACK] Tracked tool: {ToolName} (server: {Server}, total tools for session: {Count})",
                toolName, mcpServerName ?? "builtin", sessionTools.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MCP_TRACK] Failed to track tool {ToolName}", toolName);
        }
    }

    /// <summary>
    /// Gets live MCP server information from the active SDK session.
    /// This combines:
    /// 1. Configured MCP servers that were passed to the SDK
    /// 2. Observed tools from PreToolUse hooks
    /// 3. Reflection inspection of the SDK session object
    /// </summary>
    public Task<List<LiveMcpServerInfo>> GetLiveMcpServersAsync(
        string sessionId, 
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetLiveMcpServersInternal(sessionId));
    }
    
    /// <summary>
    /// Internal synchronous implementation of GetLiveMcpServersAsync.
    /// </summary>
    private List<LiveMcpServerInfo> GetLiveMcpServersInternal(string sessionId)
    {
        var result = new List<LiveMcpServerInfo>();
        
        _logger.LogInformation("[MCP_DEBUG] GetLiveMcpServersAsync called for session {SessionId}", sessionId);
        
        // Check if we have an active SDK session
        if (!_sdkSessions.TryGetValue(sessionId, out var sdkSession))
        {
            _logger.LogInformation("[MCP_DEBUG] No active SDK session for {SessionId}, returning empty MCP list", sessionId);
            return result;
        }
        
        _logger.LogInformation("[MCP_DEBUG] Found active SDK session for {SessionId}", sessionId);
        
#if DEBUG
        // Log reflection info about the SDK session (development builds only)
        LogSdkSessionReflectionInfo(sdkSession, sessionId);
#endif
        
        // Get the app session to find which MCP servers were configured
        if (!_appSessions.TryGetValue(sessionId, out var appSession))
        {
            _logger.LogWarning("[MCP_DEBUG] No app session found for {SessionId}", sessionId);
            return result;
        }
        
        _logger.LogInformation("[MCP_DEBUG] Getting live MCP servers for session {SessionId}", sessionId);
        _logger.LogInformation("[MCP_DEBUG] App session EnabledMcpServers: {Servers}", 
            appSession.EnabledMcpServers != null 
                ? string.Join(", ", appSession.EnabledMcpServers) 
                : "null (all enabled)");
        
        try
        {
            // Get observed tools from our tracking
            var observedToolsByServer = new Dictionary<string, List<ObservedToolInfo>>();
            var observedBuiltinTools = new List<ObservedToolInfo>();
            
            if (_observedToolsPerSession.TryGetValue(sessionId, out var sessionTools))
            {
                _logger.LogInformation("[MCP_DEBUG] Found {Count} observed tools for session", sessionTools.Count);
                
                foreach (var kvp in sessionTools)
                {
                    var tool = kvp.Value;
                    if (!string.IsNullOrEmpty(tool.McpServerName))
                    {
                        if (!observedToolsByServer.ContainsKey(tool.McpServerName))
                            observedToolsByServer[tool.McpServerName] = new List<ObservedToolInfo>();
                        observedToolsByServer[tool.McpServerName].Add(tool);
                    }
                    else
                    {
                        observedBuiltinTools.Add(tool);
                    }
                }
                
                _logger.LogInformation("[MCP_DEBUG] Observed tools by server: {Servers}", 
                    string.Join(", ", observedToolsByServer.Select(kvp => $"{kvp.Key}({kvp.Value.Count} tools)")));
            }
            else
            {
                _logger.LogInformation("[MCP_DEBUG] No observed tools for session yet (tools appear when used)");
            }
            
            // Build the result from the session's CONFIGURED MCP servers
            // Since these were passed to the SDK at session creation, they should be "running"
            List<McpServerConfig> configuredServers;
            
            if (appSession.EnabledMcpServers == null)
            {
                // null = all enabled servers from config
                configuredServers = _mcpService.GetServers()
                    .Where(s => s.Enabled)
                    .ToList();
                _logger.LogInformation("[MCP_DEBUG] EnabledMcpServers is null, using ALL enabled servers: {Count}", 
                    configuredServers.Count);
            }
            else if (appSession.EnabledMcpServers.Count == 0)
            {
                // Empty list = no servers
                configuredServers = new List<McpServerConfig>();
                _logger.LogInformation("[MCP_DEBUG] EnabledMcpServers is empty list, no servers");
            }
            else
            {
                // Specific servers
                configuredServers = _mcpService.GetServers()
                    .Where(s => s.Enabled && appSession.EnabledMcpServers.Contains(s.Name))
                    .ToList();
                _logger.LogInformation("[MCP_DEBUG] Using specific enabled servers: {Names}", 
                    string.Join(", ", configuredServers.Select(s => s.Name)));
            }
            
            _logger.LogInformation("[MCP_DEBUG] Building result for {Count} configured servers", configuredServers.Count);
            
            foreach (var serverConfig in configuredServers)
            {
                _logger.LogInformation("[MCP_DEBUG] Processing server: {Name}", serverConfig.Name);
                
                var serverInfo = new LiveMcpServerInfo
                {
                    Name = serverConfig.Name,
                    Transport = serverConfig.Transport.ToString().ToLower(),
                    ConnectionInfo = serverConfig.Transport == McpTransport.Stdio 
                        ? $"{serverConfig.Command} {string.Join(" ", serverConfig.Args ?? new())}"
                        : serverConfig.Url ?? "",
                };
                
                // Since we passed this server to the SDK at session creation, assume it's running
                // The SDK doesn't expose a direct way to check server status, so we mark as "running"
                // if the session was created successfully with this server in the config
                serverInfo.IsActive = true;
                serverInfo.Status = "running";
                
                // Add observed tools for this server (if any tools have been used)
                if (observedToolsByServer.TryGetValue(serverConfig.Name, out var observedTools))
                {
                    serverInfo.Tools = observedTools.Select(t => new LiveMcpToolInfo
                    {
                        Name = t.Name,
                        Description = $"Invoked {t.InvocationCount} time(s), last used: {t.LastUsed:HH:mm:ss}"
                    }).ToList();
                    
                    _logger.LogInformation("[MCP_DEBUG] Server {Name} has {Count} observed tools", 
                        serverConfig.Name, serverInfo.Tools.Count);
                }
                else
                {
                    // No tools observed yet, but server is configured/running
                    serverInfo.Tools = new List<LiveMcpToolInfo>();
                    _logger.LogInformation("[MCP_DEBUG] Server {Name} has no observed tools yet (tools appear when used)", 
                        serverConfig.Name);
                }
                
                result.Add(serverInfo);
            }
            
            // Add builtin tools if any were observed
            if (observedBuiltinTools.Any())
            {
                result.Insert(0, new LiveMcpServerInfo
                {
                    Name = "copilot-builtin",
                    IsActive = true,
                    Status = "running",
                    Transport = "internal",
                    ConnectionInfo = "Built-in Copilot tools",
                    Tools = observedBuiltinTools.Select(t => new LiveMcpToolInfo
                    {
                        Name = t.Name,
                        Description = $"Invoked {t.InvocationCount} time(s)"
                    }).ToList()
                });
            }
            
            _logger.LogInformation("[MCP_DEBUG] Final result: {Count} servers, {ToolCount} total observed tools",
                result.Count, result.Sum(s => s.Tools.Count));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MCP_DEBUG] Failed to get live MCP servers for session {SessionId}", sessionId);
        }
        
        return result;
    }

    /// <summary>
    /// Uses reflection to log available properties and methods on the CopilotSession object.
    /// This helps discover what APIs the SDK exposes for querying MCP servers/tools.
    /// </summary>
    private void LogSdkSessionReflectionInfo(CopilotSession sdkSession, string sessionId)
    {
        try
        {
            var sessionType = sdkSession.GetType();
            _logger.LogInformation("[SDK_REFLECT] CopilotSession type: {TypeName}", sessionType.FullName);
            
            // Log public properties
            var properties = sessionType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            _logger.LogInformation("[SDK_REFLECT] Public properties ({Count}):", properties.Length);
            foreach (var prop in properties)
            {
                try
                {
                    var value = prop.GetValue(sdkSession);
                    var valueStr = value?.ToString() ?? "null";
                    if (valueStr.Length > 100) valueStr = valueStr.Substring(0, 100) + "...";
                    _logger.LogInformation("[SDK_REFLECT]   - {PropName} ({PropType}): {Value}", 
                        prop.Name, prop.PropertyType.Name, valueStr);
                }
                catch (Exception propEx)
                {
                    _logger.LogDebug("[SDK_REFLECT]   - {PropName} ({PropType}): <error reading: {Error}>", 
                        prop.Name, prop.PropertyType.Name, propEx.Message);
                }
            }
            
            // Log public methods (excluding inherited Object methods)
            var methods = sessionType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(m => m.DeclaringType == sessionType || m.DeclaringType?.FullName?.StartsWith("GitHub.Copilot") == true)
                .Where(m => !m.IsSpecialName) // Exclude property getters/setters
                .ToList();
            _logger.LogInformation("[SDK_REFLECT] Public methods ({Count}):", methods.Count);
            foreach (var method in methods)
            {
                var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                _logger.LogInformation("[SDK_REFLECT]   - {MethodName}({Params}) -> {ReturnType}", 
                    method.Name, parameters, method.ReturnType.Name);
            }
            
            // Look for MCP-related members specifically
            var mcpMembers = properties.Where(p => p.Name.ToLower().Contains("mcp") || p.Name.ToLower().Contains("server") || p.Name.ToLower().Contains("tool")).ToList();
            if (mcpMembers.Any())
            {
                _logger.LogInformation("[SDK_REFLECT] MCP-related properties found:");
                foreach (var prop in mcpMembers)
                {
                    try
                    {
                        var value = prop.GetValue(sdkSession);
                        _logger.LogInformation("[SDK_REFLECT]   *** {PropName}: {Value}", prop.Name, 
                            value != null ? JsonSerializer.Serialize(value) : "null");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("[SDK_REFLECT]   *** {PropName}: <error: {Error}>", prop.Name, ex.Message);
                    }
                }
            }
            
            var mcpMethods = methods.Where(m => m.Name.ToLower().Contains("mcp") || m.Name.ToLower().Contains("server") || m.Name.ToLower().Contains("tool")).ToList();
            if (mcpMethods.Any())
            {
                _logger.LogInformation("[SDK_REFLECT] MCP-related methods found:");
                foreach (var method in mcpMethods)
                {
                    var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    _logger.LogInformation("[SDK_REFLECT]   *** {MethodName}({Params}) -> {ReturnType}", 
                        method.Name, parameters, method.ReturnType.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SDK_REFLECT] Failed to reflect on CopilotSession");
        }
    }

    /// <summary>
    /// Gets all available tools from the SDK session.
    /// Since the SDK may not expose a direct ListTools API, we track tools that have been
    /// observed through the pre-tool-use hooks and from the configured MCP servers.
    /// </summary>
    private Task<List<LiveMcpToolInfo>> GetSessionToolsAsync(
        CopilotSession sdkSession, 
        CancellationToken cancellationToken)
    {
        var tools = new List<LiveMcpToolInfo>();
        
        // The SDK doesn't expose a direct ListTools API
        // Tools are discovered through pre-tool-use hooks during session use
        // For now, we return an empty list - tools will be populated from MCP server configs
        // and tracked during session execution
        
        _logger.LogDebug("GetSessionToolsAsync: SDK doesn't expose direct tool listing, returning empty list");
        
        return Task.FromResult(tools);
    }

    /// <summary>
    /// Converts SDK tool schema to our LiveMcpToolParameter format.
    /// </summary>
    private Dictionary<string, LiveMcpToolParameter>? ConvertToolParameters(object? inputSchema)
    {
        if (inputSchema == null)
            return null;
        
        var result = new Dictionary<string, LiveMcpToolParameter>();
        
        try
        {
            // inputSchema is typically a JsonElement or similar structure
            if (inputSchema is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                if (je.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
                {
                    var requiredProps = new HashSet<string>();
                    if (je.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var req in required.EnumerateArray())
                        {
                            if (req.ValueKind == JsonValueKind.String)
                            {
                                requiredProps.Add(req.GetString() ?? "");
                            }
                        }
                    }
                    
                    foreach (var prop in properties.EnumerateObject())
                    {
                        var paramName = prop.Name;
                        var param = new LiveMcpToolParameter
                        {
                            Required = requiredProps.Contains(paramName)
                        };
                        
                        if (prop.Value.TryGetProperty("type", out var typeEl))
                        {
                            param.Type = typeEl.GetString() ?? "string";
                        }
                        
                        if (prop.Value.TryGetProperty("description", out var descEl))
                        {
                            param.Description = descEl.GetString();
                        }
                        
                        result[paramName] = param;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert tool parameters from schema");
        }
        
        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Recreates the SDK session with new configuration.
    /// This disposes the old SDK session and updates the app Session object
    /// so the next SendMessageStreamingAsync will create a fresh SDK session.
    /// Message history in the app Session is preserved.
    /// </summary>
    public async Task RecreateSessionAsync(
        Session session,
        SessionRecreateOptions options,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Recreating session {SessionId} with options: Model={Model}, WorkDir={WorkDir}",
            session.SessionId, options.NewModel ?? "(unchanged)", options.NewWorkingDirectory ?? "(unchanged)");

        // 1. Dispose old SDK session if it exists
        if (_sdkSessions.TryRemove(session.SessionId, out var oldSdkSession))
        {
            try
            {
                _logger.LogDebug("Disposing old SDK session for {SessionId}", session.SessionId);
                await oldSdkSession.DisposeAsync();
                _logger.LogInformation("Disposed old SDK session for {SessionId}", session.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing old SDK session for {SessionId}", session.SessionId);
            }
        }

        // Also clean up the app session reference
        _appSessions.TryRemove(session.SessionId, out _);

        // 2. Update app Session object with new values
        if (!string.IsNullOrEmpty(options.NewModel))
        {
            _logger.LogDebug("Updating model from {Old} to {New}", session.ModelId, options.NewModel);
            session.ModelId = options.NewModel;
        }

        if (!string.IsNullOrEmpty(options.NewWorkingDirectory))
        {
            _logger.LogDebug("Updating working directory from {Old} to {New}", 
                session.WorkingDirectory, options.NewWorkingDirectory);
            session.WorkingDirectory = options.NewWorkingDirectory;
        }

        // 3. Clear the CopilotSessionId to force new session creation
        // The next SendMessageStreamingAsync will create a new SDK session
        session.CopilotSessionId = null;

        _logger.LogInformation(
            "Session {SessionId} prepared for recreation. New config: Model={Model}, WorkDir={WorkDir}. " +
            "New SDK session will be created on next message.",
            session.SessionId, session.ModelId, session.WorkingDirectory);
    }

    /// <summary>
    /// Gets or creates an SDK session for the given app session.
    /// </summary>
    private async Task<CopilotSession> GetOrCreateSdkSessionAsync(
        Session session,
        CancellationToken cancellationToken)
    {
        // Check if we already have an SDK session
        if (_sdkSessions.TryGetValue(session.SessionId, out var existingSession))
        {
            return existingSession;
        }

        var client = await EnsureClientAsync(cancellationToken);
        
        // Store the app session for hook access
        _appSessions[session.SessionId] = session;
        
        // Build MCP server configuration from session-enabled servers
        var mcpServersConfig = BuildMcpServersConfig(session);
        if (mcpServersConfig != null)
        {
            _logger.LogInformation("Passing {Count} MCP servers to SDK session: {Servers}",
                mcpServersConfig.Count, string.Join(", ", mcpServersConfig.Keys));
        }
        else
        {
            _logger.LogDebug("No MCP servers enabled for session {SessionId}", session.SessionId);
        }

        // Check if we should resume an existing Copilot session
        if (!string.IsNullOrEmpty(session.CopilotSessionId))
        {
            try
            {
                _logger.LogInformation("Resuming Copilot session {CopilotSessionId} for {SessionId}",
                    session.CopilotSessionId, session.SessionId);

                var resumeConfig = new ResumeSessionConfig
                {
                    WorkingDirectory = session.WorkingDirectory,
                    Streaming = true,
                    Hooks = CreateSessionHooks(session),
                    McpServers = mcpServersConfig
                };

                var resumedSession = await client.ResumeSessionAsync(
                    session.CopilotSessionId, resumeConfig, cancellationToken);

                _sdkSessions[session.SessionId] = resumedSession;
                return resumedSession;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resume session {CopilotSessionId}, creating new",
                    session.CopilotSessionId);
            }
        }

        // Build skill configuration
        var skillDirectories = BuildSkillDirectories(session);
        var disabledSkills = BuildDisabledSkills(session);
        
        if (skillDirectories.Any())
        {
            _logger.LogInformation("Passing {Count} skill directories to SDK session: {Dirs}",
                skillDirectories.Count, string.Join(", ", skillDirectories));
        }
        
        if (disabledSkills.Any())
        {
            _logger.LogInformation("Passing {Count} disabled skills to SDK session: {Skills}",
                disabledSkills.Count, string.Join(", ", disabledSkills.Take(5)) + 
                (disabledSkills.Count > 5 ? $"... (+{disabledSkills.Count - 5} more)" : ""));
        }

        // Create a new SDK session
        _logger.LogInformation("Creating new SDK session for {SessionId} with model {Model} in {WorkDir}",
            session.SessionId, session.ModelId, session.WorkingDirectory);

        var config = new SessionConfig
        {
            Model = session.ModelId,
            WorkingDirectory = session.WorkingDirectory,
            Streaming = true,
            Hooks = CreateSessionHooks(session),
            McpServers = mcpServersConfig,
            SkillDirectories = skillDirectories.Any() ? skillDirectories : null,
            DisabledSkills = disabledSkills.Any() ? disabledSkills : null
        };

        _logger.LogDebug("Session config: Model={Model}, WorkingDir={WorkDir}, Streaming={Streaming}, SkillDirs={SkillDirs}, DisabledSkills={DisabledSkills}",
            config.Model, config.WorkingDirectory, config.Streaming, 
            skillDirectories.Count, disabledSkills.Count);

        CopilotSession sdkSession;
        try
        {
            sdkSession = await client.CreateSessionAsync(config, cancellationToken);
        }
        catch (Exception createEx)
        {
            _logger.LogError(createEx, "CreateSessionAsync failed for {SessionId}. Error type: {ErrorType}, Message: {Message}",
                session.SessionId, createEx.GetType().FullName, createEx.Message);
            
            // Check for inner exceptions
            var inner = createEx.InnerException;
            while (inner != null)
            {
                _logger.LogError("Inner exception: {Type} - {Message}", inner.GetType().FullName, inner.Message);
                inner = inner.InnerException;
            }
            
            throw;
        }
        
        // Store the Copilot session ID for future resume
        session.CopilotSessionId = sdkSession.SessionId;
        
        _sdkSessions[session.SessionId] = sdkSession;
        return sdkSession;
    }

    /// <summary>
    /// Creates session hooks for tool approval and event handling.
    /// </summary>
    private SessionHooks CreateSessionHooks(Session session)
    {
        return new SessionHooks
        {
            OnPreToolUse = async (input, invocation) =>
                await HandlePreToolUseAsync(session, input, invocation),
            OnPostToolUse = async (input, invocation) =>
                await HandlePostToolUseAsync(session, input, invocation),
            OnErrorOccurred = async (input, invocation) =>
                await HandleErrorAsync(session, input, invocation)
        };
    }

    /// <summary>
    /// Handles pre-tool-use hook for permission gating.
    /// This is the core integration point for tool approval.
    /// Also tracks tools for MCP server discovery.
    /// </summary>
    private async Task<PreToolUseHookOutput?> HandlePreToolUseAsync(
        Session session,
        PreToolUseHookInput input,
        HookInvocation invocation)
    {
        // TOP-LEVEL try-catch to ensure no exceptions escape the hook
        try
        {
            _logger.LogInformation(">>> PreToolUse hook START: {Tool} (args: {ArgsType}) in session {Session}",
                input.ToolName, input.ToolArgs?.GetType().Name ?? "null", session.SessionId);
            
            // TRACK TOOL FOR MCP DISCOVERY
            TrackObservedTool(session.SessionId, input.ToolName, input.ToolArgs);
            
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 1. Check for auto-approved internal/low-risk tools (reduces noise)
            _logger.LogInformation("[STEP 1] Checking auto-approved list for {Tool}...", input.ToolName);
            if (AutoApprovedInternalTools.IsAutoApproved(input.ToolName, input.ToolArgs))
            {
                stopwatch.Stop();
                _logger.LogInformation("<<< Tool {Tool} auto-approved as internal/low-risk tool (elapsed: {Elapsed}ms)", 
                    input.ToolName, stopwatch.ElapsedMilliseconds);
                return new PreToolUseHookOutput { PermissionDecision = "allow" };
            }
            _logger.LogInformation("[STEP 1] Tool {Tool} NOT in auto-approved list", input.ToolName);

            // 2. Check autonomous mode settings
            _logger.LogInformation("[STEP 2] Checking autonomous mode for {Tool}...", input.ToolName);
            if (IsAutonomouslyApproved(session, input.ToolName))
            {
                stopwatch.Stop();
                _logger.LogInformation("<<< Tool {Tool} auto-approved via autonomous mode (elapsed: {Elapsed}ms)", 
                    input.ToolName, stopwatch.ElapsedMilliseconds);
                return new PreToolUseHookOutput { PermissionDecision = "allow" };
            }
            _logger.LogInformation("[STEP 2] Tool {Tool} NOT approved via autonomous mode", input.ToolName);

            // 3. Check saved approvals (this uses synchronous check but rules should be loaded by now)
            _logger.LogInformation("[STEP 3] Checking saved rules for {Tool}...", input.ToolName);
            bool isAlreadyApproved;
            try
            {
                isAlreadyApproved = _approvalService.IsApproved(session.SessionId, input.ToolName, input.ToolArgs);
                _logger.LogInformation("[STEP 3] IsApproved returned: {Result} for {Tool}", isAlreadyApproved, input.ToolName);
            }
            catch (Exception isApprovedEx)
            {
                _logger.LogError(isApprovedEx, "[STEP 3] Exception in IsApproved for {Tool}", input.ToolName);
                isAlreadyApproved = false;
            }
            
            if (isAlreadyApproved)
            {
                stopwatch.Stop();
                _logger.LogInformation("<<< Tool {Tool} approved via saved rule (elapsed: {Elapsed}ms)", 
                    input.ToolName, stopwatch.ElapsedMilliseconds);
                return new PreToolUseHookOutput { PermissionDecision = "allow" };
            }
            _logger.LogInformation("[STEP 3] Tool {Tool} has no saved approval rule, proceeding to user prompt", input.ToolName);

            // 4. Request user approval
            _logger.LogInformation("[STEP 4] Building ToolApprovalRequest for {Tool}...", input.ToolName);
            ToolApprovalRequest request;
            try
            {
                // Log individual property assignments to identify crash point
                _logger.LogInformation("[STEP 4a] Setting SessionId...");
                var sessionId = session.SessionId;
                
                _logger.LogInformation("[STEP 4b] Setting ToolName...");
                var toolName = input.ToolName;
                
                _logger.LogInformation("[STEP 4c] Converting ToolArgs to safe format...");
                // Convert ToolArgs from JsonElement to a safe object representation
                // This handles the case where ToolArgs is a JsonElement of type String
                object? safeToolArgs = NormalizeToolArgs(input.ToolArgs);
                _logger.LogInformation("[STEP 4c] ToolArgs normalized: {ArgsType}", 
                    safeToolArgs?.GetType().Name ?? "null");
                
                _logger.LogInformation("[STEP 4d] Setting WorkingDirectory...");
                var workingDir = input.Cwd;
                
                _logger.LogInformation("[STEP 4e] Setting Timestamp from {RawTimestamp}...", input.Timestamp);
                DateTimeOffset timestamp;
                try
                {
                    timestamp = DateTimeOffset.FromUnixTimeMilliseconds(input.Timestamp);
                }
                catch (Exception tsEx)
                {
                    _logger.LogWarning(tsEx, "[STEP 4e] Failed to parse timestamp {Timestamp}, using UtcNow", input.Timestamp);
                    timestamp = DateTimeOffset.UtcNow;
                }
                
                _logger.LogInformation("[STEP 4f] Getting RiskLevel...");
                var riskLevel = _approvalService.GetToolRiskLevel(input.ToolName);
                
                _logger.LogInformation("[STEP 4g] Getting Description...");
                var description = GetToolDescription(input.ToolName, safeToolArgs);
                
                _logger.LogInformation("[STEP 4h] Creating ToolApprovalRequest object...");
                request = new ToolApprovalRequest
                {
                    SessionId = sessionId,
                    ToolName = toolName,
                    ToolArgs = safeToolArgs,
                    WorkingDirectory = workingDir,
                    Timestamp = timestamp,
                    RiskLevel = riskLevel,
                    Description = description
                };
                _logger.LogInformation("[STEP 4] ToolApprovalRequest built successfully for {Tool}", input.ToolName);
            }
            catch (Exception reqEx)
            {
                _logger.LogError(reqEx, "[STEP 4] FAILED to build ToolApprovalRequest for {Tool}: {Message}", 
                    input.ToolName, reqEx.Message);
                stopwatch.Stop();
                return new PreToolUseHookOutput
                {
                    PermissionDecision = "deny",
                    PermissionDecisionReason = $"Failed to build approval request: {reqEx.Message}"
                };
            }

            try
            {
                // RequestApprovalAsync handles the UI interaction and rule checking
                // Note: The ToolApprovalUIService will call RecordDecision for us
                // when the user makes a choice with Session/Global scope
                _logger.LogInformation("[STEP 5] Requesting user approval for tool {Tool}... (waiting for UI)", input.ToolName);
                var response = await _approvalService.RequestApprovalAsync(request);
                stopwatch.Stop();
                _logger.LogInformation("[STEP 5] Got response for {Tool}: Approved={Approved}, Scope={Scope}", 
                    input.ToolName, response.Approved, response.Scope);

                if (response.Approved)
                {
                    _logger.LogInformation("<<< Tool {Tool} approved by user with scope {Scope} (elapsed: {Elapsed}ms)",
                        input.ToolName, response.Scope, stopwatch.ElapsedMilliseconds);
                    return new PreToolUseHookOutput
                    {
                        PermissionDecision = "allow",
                        PermissionDecisionReason = response.Reason
                    };
                }

                _logger.LogInformation("<<< Tool {Tool} denied by user: {Reason} (elapsed: {Elapsed}ms)",
                    input.ToolName, response.Reason, stopwatch.ElapsedMilliseconds);
                return new PreToolUseHookOutput
                {
                    PermissionDecision = "deny",
                    PermissionDecisionReason = response.Reason ?? "User denied"
                };
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                _logger.LogInformation("<<< Tool approval cancelled for {Tool} (elapsed: {Elapsed}ms)", 
                    input.ToolName, stopwatch.ElapsedMilliseconds);
                return new PreToolUseHookOutput
                {
                    PermissionDecision = "deny",
                    PermissionDecisionReason = "Operation cancelled"
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "<<< Error during tool approval for {Tool} (elapsed: {Elapsed}ms)", 
                    input.ToolName, stopwatch.ElapsedMilliseconds);
                return new PreToolUseHookOutput
                {
                    PermissionDecision = "deny",
                    PermissionDecisionReason = $"Approval error: {ex.Message}"
                };
            }
        }
        catch (Exception fatalEx)
        {
            // This should NEVER happen, but if it does, log it prominently
            _logger.LogCritical(fatalEx, "!!! FATAL ERROR in PreToolUse hook for {Tool}: {Message}", 
                input?.ToolName ?? "unknown", fatalEx.Message);
            return new PreToolUseHookOutput
            {
                PermissionDecision = "deny",
                PermissionDecisionReason = $"Fatal hook error: {fatalEx.Message}"
            };
        }
    }

    /// <summary>
    /// Generates a human-readable description for a tool operation.
    /// </summary>
    private string GetToolDescription(string toolName, object? toolArgs)
    {
        var lowerName = toolName.ToLowerInvariant();
        
        // Extract command from args if it's a shell/command tool
        var command = ExtractCommandFromArgs(toolArgs);
        
        if (lowerName.Contains("shell") || lowerName.Contains("exec") || 
            lowerName.Contains("run") || lowerName.Contains("command") ||
            lowerName.Contains("powershell") || lowerName.Contains("bash"))
        {
            if (!string.IsNullOrWhiteSpace(command))
                return $"Execute command: {command}";
            return $"Tool '{toolName}' wants to execute a shell command";
        }
        
        if (lowerName.Contains("write") || lowerName.Contains("edit") || lowerName.Contains("create"))
            return $"Tool '{toolName}' wants to modify/create files";
        
        if (lowerName.Contains("delete") || lowerName.Contains("remove"))
            return $"Tool '{toolName}' wants to delete files/data";
        
        if (lowerName.Contains("http") || lowerName.Contains("fetch") || lowerName.Contains("curl"))
            return $"Tool '{toolName}' wants to make a network request";
        
        return $"Tool '{toolName}' requests execution";
    }
    
    /// <summary>
    /// Normalizes tool arguments from SDK format (JsonElement) to a safe object representation.
    /// This handles the case where ToolArgs is a JsonElement with various ValueKind types.
    /// </summary>
    private static object? NormalizeToolArgs(object? args)
    {
        if (args == null)
            return null;
        
        // If it's already a simple type, return as-is
        if (args is string)
            return args;
        
        if (args is JsonElement je)
        {
            // Convert JsonElement to a safe representation based on its ValueKind
            return ConvertJsonElementToObject(je);
        }
        
        // For other types (Dictionary, List, etc.), try to serialize and re-deserialize
        // to ensure it's in a standard format
        try
        {
            var json = JsonSerializer.Serialize(args);
            using var doc = JsonDocument.Parse(json);
            return ConvertJsonElementToObject(doc.RootElement);
        }
        catch
        {
            // If serialization fails, return the original object
            return args;
        }
    }
    
    /// <summary>
    /// Attempts to extract the command string from tool arguments.
    /// </summary>
    private static string? ExtractCommandFromArgs(object? args)
    {
        if (args == null)
            return null;
        
        if (args is string s)
            return s;
        
        if (args is JsonElement je)
        {
            // Try common argument names for commands
            foreach (var propName in new[] { "command", "cmd", "script", "args", "input" })
            {
                if (je.TryGetProperty(propName, out var cmdElement) && 
                    cmdElement.ValueKind == JsonValueKind.String)
                {
                    var cmd = cmdElement.GetString();
                    if (!string.IsNullOrWhiteSpace(cmd))
                        return cmd.Length > 100 ? cmd[..100] + "..." : cmd;
                }
            }
        }
        
        // Try to serialize and look for common patterns
        try
        {
            var json = args is string str ? str : JsonSerializer.Serialize(args);
            if (json.Length < 200)
                return json;
            return json[..200] + "...";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Handles post-tool-use hook for logging and result modification.
    /// </summary>
    private Task<PostToolUseHookOutput?> HandlePostToolUseAsync(
        Session session,
        PostToolUseHookInput input,
        HookInvocation invocation)
    {
        _logger.LogDebug("PostToolUse hook: {Tool} completed in session {Session}",
            input.ToolName, session.SessionId);
        
        // Currently no post-processing needed
        return Task.FromResult<PostToolUseHookOutput?>(null);
    }

    /// <summary>
    /// Handles error occurred hook.
    /// </summary>
    private Task<ErrorOccurredHookOutput?> HandleErrorAsync(
        Session session,
        ErrorOccurredHookInput input,
        HookInvocation invocation)
    {
        _logger.LogWarning("Error in session {Session}: {Error} (Context: {Context}, Recoverable: {Recoverable})",
            session.SessionId, input.Error, input.ErrorContext, input.Recoverable);
        
        // Let the SDK handle error recovery
        return Task.FromResult<ErrorOccurredHookOutput?>(null);
    }

    /// <summary>
    /// Builds the MCP server configuration dictionary for the SDK.
    /// Converts the app's McpServerConfig to the SDK's expected Dictionary format.
    /// </summary>
    /// <param name="session">The session containing enabled MCP server names</param>
    /// <returns>Dictionary of MCP server configurations, or null if none enabled</returns>
    /// <remarks>
    /// EnabledMcpServers behavior:
    /// - null: Load ALL servers from mcp-config.json (default for new sessions)
    /// - Empty list: Explicitly no servers (user choice)
    /// - Non-empty list: Only these specific servers are enabled
    /// </remarks>
    private Dictionary<string, object>? BuildMcpServersConfig(Session session)
    {
        List<McpServerConfig> serversToLoad;
        
        if (session.EnabledMcpServers == null)
        {
            // null = load ALL servers from mcp-config.json (default behavior for new sessions)
            serversToLoad = _mcpService.GetServers()
                .Where(s => s.Enabled) // Only servers with Enabled=true in config
                .ToList();
            
            _logger.LogInformation("Session {SessionId}: EnabledMcpServers is null, loading ALL enabled servers from config ({Count} servers)",
                session.SessionId, serversToLoad.Count);
        }
        else if (session.EnabledMcpServers.Count == 0)
        {
            // Empty list = user explicitly wants NO servers
            _logger.LogInformation("Session {SessionId}: EnabledMcpServers is empty list, no servers will be loaded",
                session.SessionId);
            return null;
        }
        else
        {
            // Non-empty list = load specific servers
            serversToLoad = _mcpService.GetServers()
                .Where(s => s.Enabled && session.EnabledMcpServers.Contains(s.Name))
                .ToList();
            
            _logger.LogInformation("Session {SessionId}: Loading {Count} specific servers: {Names}",
                session.SessionId, serversToLoad.Count, string.Join(", ", serversToLoad.Select(s => s.Name)));
        }

        if (!serversToLoad.Any())
        {
            _logger.LogDebug("No MCP servers to load for session {SessionId}",
                session.SessionId);
            return null;
        }
        
        var enabledServers = serversToLoad;

        var mcpServers = new Dictionary<string, object>();

        foreach (var server in enabledServers)
        {
            var config = new Dictionary<string, object>();

            if (server.Transport == McpTransport.Stdio)
            {
                // Local/stdio server configuration
                config["type"] = "local";
                config["command"] = server.Command ?? "";
                config["args"] = server.Args ?? new List<string>();
                config["tools"] = new List<string> { "*" }; // Allow all tools from this server

                if (server.Env != null && server.Env.Any())
                    config["env"] = server.Env;

                if (server.TimeoutSeconds > 0)
                    config["timeout"] = server.TimeoutSeconds * 1000; // Convert to milliseconds

                _logger.LogDebug("Added stdio MCP server '{Name}': command={Command}, args={Args}",
                    server.Name, server.Command, string.Join(" ", server.Args ?? new List<string>()));
            }
            else if (server.Transport == McpTransport.Http)
            {
                // HTTP server configuration
                config["type"] = "http";
                config["url"] = server.Url ?? "";
                config["tools"] = new List<string> { "*" }; // Allow all tools from this server

                if (server.Headers != null && server.Headers.Any())
                    config["headers"] = server.Headers;

                if (server.TimeoutSeconds > 0)
                    config["timeout"] = server.TimeoutSeconds * 1000; // Convert to milliseconds

                _logger.LogDebug("Added HTTP MCP server '{Name}': url={Url}",
                    server.Name, server.Url);
            }
            else
            {
                _logger.LogWarning("Unknown transport type for MCP server '{Name}': {Transport}",
                    server.Name, server.Transport);
                continue;
            }

            mcpServers[server.Name] = config;
        }

        if (!mcpServers.Any())
        {
            _logger.LogDebug("No MCP servers configured after filtering");
            return null;
        }

        _logger.LogInformation("Built MCP config with {Count} servers: {Names}",
            mcpServers.Count, string.Join(", ", mcpServers.Keys));

        return mcpServers;
    }

    /// <summary>
    /// Builds the list of skill directories to pass to the SDK.
    /// </summary>
    /// <param name="session">The session (may have custom skill directories)</param>
    /// <returns>List of skill directory paths</returns>
    private List<string> BuildSkillDirectories(Session session)
    {
        // If session has custom skill directories, use those
        if (session.SkillDirectories != null && session.SkillDirectories.Any())
        {
            return session.SkillDirectories.ToList();
        }
        
        // Otherwise, use the default directories from skills service
        return _skillsService.GetSkillDirectories();
    }

    /// <summary>
    /// Builds the list of disabled skill names to pass to the SDK.
    /// By default, all skills are disabled until explicitly enabled by the user.
    /// </summary>
    /// <param name="session">The session containing disabled skills list</param>
    /// <returns>List of skill names to disable</returns>
    private List<string> BuildDisabledSkills(Session session)
    {
        return _skillsService.GetDisabledSkillNames(session);
    }

    /// <summary>
    /// Checks if a tool is autonomously approved based on session settings.
    /// </summary>
    private static bool IsAutonomouslyApproved(Session session, string toolName)
    {
        var auto = session.AutonomousMode;
        
        // Full autonomous mode - approve everything
        if (auto.AllowAll)
            return true;
        
        // All tools allowed
        if (auto.AllowAllTools)
            return true;

        // Tool-specific checks based on tool name patterns
        var lowerToolName = toolName.ToLowerInvariant();
        
        // Path-related tools
        if (auto.AllowAllPaths)
        {
            if (lowerToolName.Contains("read") ||
                lowerToolName.Contains("write") ||
                lowerToolName.Contains("file") ||
                lowerToolName.Contains("directory") ||
                lowerToolName.Contains("path"))
            {
                return true;
            }
        }

        // URL-related tools
        if (auto.AllowAllUrls)
        {
            if (lowerToolName.Contains("url") ||
                lowerToolName.Contains("fetch") ||
                lowerToolName.Contains("http") ||
                lowerToolName.Contains("web") ||
                lowerToolName.Contains("download"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads MCP server configuration from the Copilot CLI's config file.
    /// The CLI stores MCP config at ~/.copilot/mcp-config.json
    /// 
    /// Uses Dictionary{string, object} with nested dictionaries and arrays to
    /// represent the JSON structure. This should serialize naturally through
    /// the SDK's JSON serializer.
    /// </summary>
    private async Task<Dictionary<string, object>?> ReadMcpConfigAsJsonElementsAsync()
    {
        try
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configPath = Path.Combine(homeDir, ".copilot", "mcp-config.json");
            
            if (!File.Exists(configPath))
            {
                _logger.LogDebug("No MCP config file found at {Path}", configPath);
                return null;
            }
            
            var json = await File.ReadAllTextAsync(configPath);
            
            // Parse using JsonDocument to get JsonElements
            using var document = JsonDocument.Parse(json);
            
            if (!document.RootElement.TryGetProperty("mcpServers", out var mcpServersElement))
            {
                _logger.LogDebug("No mcpServers property in MCP config");
                return null;
            }
            
            if (mcpServersElement.ValueKind != JsonValueKind.Object)
            {
                _logger.LogDebug("mcpServers is not an object in MCP config");
                return null;
            }
            
            var result = new Dictionary<string, object>();
            
            foreach (var serverProp in mcpServersElement.EnumerateObject())
            {
                var serverName = serverProp.Name;
                
                try
                {
                    // Convert JsonElement to nested dictionaries/arrays
                    // This creates a structure that should serialize naturally
                    var serverDict = ConvertJsonElementToObject(serverProp.Value);
                    if (serverDict != null)
                    {
                        result[serverName] = serverDict;
                        
                        // Log the command for debugging
                        var command = serverProp.Value.TryGetProperty("command", out var cmdElement) 
                            ? cmdElement.GetString() ?? "unknown" 
                            : "unknown";
                        _logger.LogDebug("Loaded MCP server '{ServerName}' (command: {Command})", 
                            serverName, command);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to convert MCP server '{ServerName}', skipping", serverName);
                }
            }
            
            _logger.LogInformation("Loaded {Count} MCP servers from CLI config at {Path}", result.Count, configPath);
            return result.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read MCP config from CLI config file");
            return null;
        }
    }

    /// <summary>
    /// Converts a JsonElement to a native .NET object (Dictionary, List, or primitive).
    /// This creates objects that should serialize naturally through the SDK's JSON serializer.
    /// </summary>
    private static object? ConvertJsonElementToObject(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object>();
                foreach (var prop in element.EnumerateObject())
                {
                    var value = ConvertJsonElementToObject(prop.Value);
                    if (value != null)
                    {
                        dict[prop.Name] = value;
                    }
                }
                return dict;
            
            case JsonValueKind.Array:
                var list = new List<object>();
                foreach (var item in element.EnumerateArray())
                {
                    var value = ConvertJsonElementToObject(item);
                    if (value != null)
                    {
                        list.Add(value);
                    }
                }
                return list;
            
            case JsonValueKind.String:
                return element.GetString();
            
            case JsonValueKind.Number:
                // Try to preserve the number type
                if (element.TryGetInt32(out var intValue))
                    return intValue;
                if (element.TryGetInt64(out var longValue))
                    return longValue;
                return element.GetDouble();
            
            case JsonValueKind.True:
                return true;
            
            case JsonValueKind.False:
                return false;
            
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Dispose all SDK sessions
        foreach (var session in _sdkSessions.Values)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing SDK session");
            }
        }
        _sdkSessions.Clear();
        _appSessions.Clear();

        // Dispose the client
        if (_client != null)
        {
            try
            {
                await _client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing SDK client");
            }
            _client = null;
        }

        _clientLock.Dispose();
        _logger.LogInformation("CopilotSdkService disposed");
    }
}

/// <summary>
/// Event args for SDK session events.
/// Includes context information like TurnId for grouping agent work.
/// </summary>
public class SdkSessionEventArgs : EventArgs
{
    /// <summary>Session ID</summary>
    public string SessionId { get; }
    
    /// <summary>The SDK session event</summary>
    public SessionEvent Event { get; }
    
    /// <summary>
    /// Current assistant turn ID for grouping agent work.
    /// Null if no turn is active or for events outside of a turn.
    /// </summary>
    public string? TurnId { get; }

    public SdkSessionEventArgs(string sessionId, SessionEvent evt)
        : this(sessionId, evt, null)
    {
    }

    public SdkSessionEventArgs(string sessionId, SessionEvent evt, string? turnId)
    {
        SessionId = sessionId;
        Event = evt;
        TurnId = turnId;
    }
}

/// <summary>
/// Event args for streaming progress changes.
/// Provides detailed information about current streaming state.
/// </summary>
public class StreamingProgressEventArgs : EventArgs
{
    /// <summary>Session ID</summary>
    public string SessionId { get; }
    
    /// <summary>Current streaming state</summary>
    public SessionStreamingState State { get; }
    
    /// <summary>Human-readable progress message</summary>
    public string ProgressMessage { get; }
    
    /// <summary>Current tool name (if tool is executing)</summary>
    public string? CurrentToolName { get; }
    
    /// <summary>Number of tools completed so far</summary>
    public int ToolsCompletedCount { get; }
    
    /// <summary>Elapsed time in the current state</summary>
    public TimeSpan ElapsedTime { get; }
    
    /// <summary>Total events received so far</summary>
    public int EventCount { get; }
    
    /// <summary>True if a warning threshold has been reached</summary>
    public bool IsWarning { get; }
    
    /// <summary>Warning message (if any)</summary>
    public string? WarningMessage { get; }

    public StreamingProgressEventArgs(
        string sessionId, 
        SessionStreamingState state, 
        string progressMessage,
        SessionStreamingContext context,
        bool isWarning = false,
        string? warningMessage = null)
    {
        SessionId = sessionId;
        State = state;
        ProgressMessage = progressMessage;
        CurrentToolName = context.CurrentToolName;
        ToolsCompletedCount = context.ToolsExecutedCount;
        ElapsedTime = context.GetStateElapsedTime();
        EventCount = context.EventCount;
        IsWarning = isWarning;
        WarningMessage = warningMessage;
    }
}
