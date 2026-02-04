using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CopilotAgent.Core.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Core.Services;

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
/// </summary>
public class CopilotSdkService : ICopilotService, IAsyncDisposable
{
    private readonly IToolApprovalService _approvalService;
    private readonly IMcpService _mcpService;
    private readonly ILogger<CopilotSdkService> _logger;
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    
    private CopilotClient? _client;
    private readonly ConcurrentDictionary<string, CopilotSession> _sdkSessions = new();
    private readonly ConcurrentDictionary<string, Session> _appSessions = new();
    private bool _disposed;

    /// <summary>
    /// Event raised when a session event occurs (assistant message, tool execution, etc.)
    /// </summary>
    public event EventHandler<SdkSessionEventArgs>? SessionEventReceived;

    public CopilotSdkService(
        IToolApprovalService approvalService,
        IMcpService mcpService,
        ILogger<CopilotSdkService> logger)
    {
        _approvalService = approvalService;
        _mcpService = mcpService;
        _logger = logger;
        _logger.LogInformation("CopilotSdkService initialized");
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

            _logger.LogInformation("Initializing Copilot SDK client");
            
            _client = new CopilotClient(new CopilotClientOptions
            {
                UseStdio = true,
                LogLevel = "info",
                AutoStart = true,
                AutoRestart = true
            });

            await _client.StartAsync(cancellationToken);
            _logger.LogInformation("Copilot SDK client connected");
            
            return _client;
        }
        finally
        {
            _clientLock.Release();
        }
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

        var contentBuilder = new System.Text.StringBuilder();
        var messageComplete = false;
        var errorOccurred = false;

        // Subscribe to streaming events
        using var subscription = sdkSession.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                    if (delta.Data?.DeltaContent != null)
                    {
                        contentBuilder.Append(delta.Data.DeltaContent);
                    }
                    break;

                case AssistantMessageEvent complete:
                    if (complete.Data?.Content != null)
                    {
                        contentBuilder.Clear();
                        contentBuilder.Append(complete.Data.Content);
                    }
                    messageComplete = true;
                    break;

                case SessionErrorEvent error:
                    _logger.LogError("SDK session error occurred");
                    errorOccurred = true;
                    break;

                case SessionIdleEvent:
                    messageComplete = true;
                    break;
            }

            // Dispatch to UI
            SessionEventReceived?.Invoke(this, new SdkSessionEventArgs(session.SessionId, evt));
        });

        try
        {
            // Send the message
            await sdkSession.SendAsync(new MessageOptions { Prompt = userMessage }, cancellationToken);

            // Stream the response
            while (!messageComplete && !errorOccurred && !cancellationToken.IsCancellationRequested)
            {
                message.Content = contentBuilder.ToString();
                yield return message;
                await Task.Delay(50, cancellationToken); // Small delay for streaming updates
            }

            // Final message
            message.Content = contentBuilder.ToString();
            message.IsStreaming = false;
            message.IsError = errorOccurred;
            yield return message;

            _logger.LogInformation("Response complete for session {SessionId}", session.SessionId);
        }
        finally
        {
            // Subscription is disposed automatically
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
        
        // TODO: MCP config passing currently causes serialization issues - needs investigation
        // For now, disabled to restore basic chat functionality
        // The SDK should inherit MCP config from CLI automatically, but if not, this needs to be fixed
        Dictionary<string, object>? mcpServers = null;
        _logger.LogDebug("MCP server loading temporarily disabled - using SDK defaults");

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
                    McpServers = mcpServers
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

        // Create a new SDK session
        _logger.LogInformation("Creating new SDK session for {SessionId}", session.SessionId);

        // Pass MCP servers from CLI config to enable MCP tool invocation
        // The copilot-ui reference implementation shows that mcpServers MUST be passed
        // to the SDK for MCP tools to work - the SDK does NOT inherit CLI config automatically

        var config = new SessionConfig
        {
            Model = session.ModelId,
            WorkingDirectory = session.WorkingDirectory,
            Streaming = true,
            Hooks = CreateSessionHooks(session),
            McpServers = mcpServers
        };

        var sdkSession = await client.CreateSessionAsync(config, cancellationToken);
        
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
    /// </summary>
    private async Task<PreToolUseHookOutput?> HandlePreToolUseAsync(
        Session session,
        PreToolUseHookInput input,
        HookInvocation invocation)
    {
        _logger.LogDebug("PreToolUse hook: {Tool} in session {Session}",
            input.ToolName, session.SessionId);

        // Check autonomous mode settings FIRST
        if (IsAutonomouslyApproved(session, input.ToolName))
        {
            _logger.LogInformation("Tool {Tool} auto-approved via autonomous mode", input.ToolName);
            return new PreToolUseHookOutput { PermissionDecision = "allow" };
        }

        // Check saved approvals
        if (_approvalService.IsApproved(session.SessionId, input.ToolName, input.ToolArgs))
        {
            _logger.LogInformation("Tool {Tool} approved via saved rule", input.ToolName);
            return new PreToolUseHookOutput { PermissionDecision = "allow" };
        }

        // Request user approval
        var request = new ToolApprovalRequest
        {
            SessionId = session.SessionId,
            ToolName = input.ToolName,
            ToolArgs = input.ToolArgs,
            WorkingDirectory = input.Cwd,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(input.Timestamp),
            RiskLevel = _approvalService.GetToolRiskLevel(input.ToolName),
            Description = $"Tool '{input.ToolName}' wants to execute"
        };

        try
        {
            var response = await _approvalService.RequestApprovalAsync(request);

            if (response.Approved)
            {
                _approvalService.RecordDecision(request, response);
                _logger.LogInformation("Tool {Tool} approved by user with scope {Scope}",
                    input.ToolName, response.Scope);
                return new PreToolUseHookOutput
                {
                    PermissionDecision = "allow",
                    PermissionDecisionReason = response.Reason
                };
            }

            _logger.LogInformation("Tool {Tool} denied by user: {Reason}",
                input.ToolName, response.Reason);
            return new PreToolUseHookOutput
            {
                PermissionDecision = "deny",
                PermissionDecisionReason = response.Reason ?? "User denied"
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Tool approval cancelled for {Tool}", input.ToolName);
            return new PreToolUseHookOutput
            {
                PermissionDecision = "deny",
                PermissionDecisionReason = "Operation cancelled"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during tool approval for {Tool}", input.ToolName);
            return new PreToolUseHookOutput
            {
                PermissionDecision = "deny",
                PermissionDecisionReason = $"Approval error: {ex.Message}"
            };
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
    /// Returns a dictionary that can be passed to SessionConfig.McpServers
    /// Uses the SDK's McpLocalServerConfig type for proper serialization.
    /// </summary>
    private async Task<Dictionary<string, object>?> ReadCliMcpConfigAsync()
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
            using var doc = JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("mcpServers", out var mcpServersElement))
            {
                _logger.LogDebug("No mcpServers property in MCP config");
                return null;
            }
            
            var result = new Dictionary<string, object>();
            
            foreach (var serverProp in mcpServersElement.EnumerateObject())
            {
                var serverName = serverProp.Name;
                var serverConfig = serverProp.Value;
                
                try
                {
                    // Use the SDK's McpLocalServerConfig type for proper serialization
                    var mcpServer = new McpLocalServerConfig
                    {
                        // Default to allowing all tools from this server
                        Tools = new List<string> { "*" }
                    };
                    
                    // Read command (for stdio/local servers)
                    if (serverConfig.TryGetProperty("command", out var commandEl))
                    {
                        mcpServer.Command = commandEl.GetString() ?? string.Empty;
                    }
                    
                    // Read args
                    if (serverConfig.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Array)
                    {
                        mcpServer.Args = argsEl.EnumerateArray()
                            .Select(a => a.GetString() ?? string.Empty)
                            .ToList();
                    }
                    
                    // Read env
                    if (serverConfig.TryGetProperty("env", out var envEl) && envEl.ValueKind == JsonValueKind.Object)
                    {
                        mcpServer.Env = new Dictionary<string, string>();
                        foreach (var envProp in envEl.EnumerateObject())
                        {
                            mcpServer.Env[envProp.Name] = envProp.Value.GetString() ?? string.Empty;
                        }
                    }
                    
                    // Read cwd if present
                    if (serverConfig.TryGetProperty("cwd", out var cwdEl))
                    {
                        mcpServer.Cwd = cwdEl.GetString();
                    }
                    
                    // Read type if present (defaults to "local" in SDK)
                    if (serverConfig.TryGetProperty("type", out var typeEl))
                    {
                        mcpServer.Type = typeEl.GetString();
                    }
                    
                    // Read timeout if present
                    if (serverConfig.TryGetProperty("timeout", out var timeoutEl) && timeoutEl.TryGetInt32(out var timeout))
                    {
                        mcpServer.Timeout = timeout;
                    }
                    
                    result[serverName] = mcpServer;
                    _logger.LogDebug("Loaded MCP server '{ServerName}' from CLI config (command: {Command})", 
                        serverName, mcpServer.Command);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse MCP server '{ServerName}' from config", serverName);
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
/// </summary>
public class SdkSessionEventArgs : EventArgs
{
    public string SessionId { get; }
    public SessionEvent Event { get; }

    public SdkSessionEventArgs(string sessionId, SessionEvent evt)
    {
        SessionId = sessionId;
        Event = evt;
    }
}