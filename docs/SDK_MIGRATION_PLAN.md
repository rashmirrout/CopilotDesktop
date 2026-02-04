# SDK Migration Plan - CopilotDesktop

> **Production-Grade Migration from CLI Process to GitHub Copilot SDK**

## Executive Summary

### Decision Matrix

| Aspect | Decision |
|--------|----------|
| **Migration Approach** | Path A: SDK Migration with Feature Flag |
| **SDK Availability** | NuGet public gallery (GitHub.Copilot.SDK) |
| **UI Preference** | Both modal dialogs + inline chat (user configurable) |
| **Migration Timeline** | Feature flag (SDK default, CLI fallback) |
| **Backwards Compatibility** | Keep autonomous mode UI - bypasses dialogs when enabled |

### Timeline

| Phase | Duration | Description |
|-------|----------|-------------|
| Phase 1 | Day 1 | Foundation - NuGet, models, interfaces |
| Phase 2 | Days 2-3 | SDK Service implementation |
| Phase 3 | Day 4 | Approval UI (modal + inline) |
| Phase 4 | Day 5 | Settings page |
| Phase 5 | Day 6 | Advanced features (streaming, abort) |
| Phase 6 | Day 7 | Testing & polish |

---

## SDK Capabilities Matrix

### Core Features

| Feature | Current CLI | SDK Mode | Benefit |
|---------|-------------|----------|---------|
| Message Send | Process stdout | `SendAsync()` / `SendAndWaitAsync()` | Type-safe, async |
| Event Streaming | Parse text | `session.On(evt => {})` | Real-time, typed events |
| Tool Approval | None (autonomous only) | `OnPreToolUse` hook | Pre-execution approval |
| Session Resume | Manual state | `ResumeSessionAsync()` | Built-in persistence |
| Abort/Cancel | Kill process | `AbortAsync()` | Graceful cancellation |
| Streaming | Not supported | `Streaming = true` | Token-by-token UI |
| Error Handling | Parse stderr | `SessionErrorEvent` | Structured errors |

### Event Types (10 Primary Events)

```csharp
// User Events
UserMessageEvent             // User sent a message

// Assistant Events
AssistantMessageEvent        // Complete assistant response
AssistantMessageDeltaEvent   // Streaming chunk (when Streaming=true)

// Tool Events
ToolExecutionStartEvent      // Tool execution began
ToolExecutionCompleteEvent   // Tool execution finished

// Session Events
SessionStartEvent            // Session created
SessionResumeEvent           // Session resumed
SessionIdleEvent             // Turn complete, ready for input
SessionErrorEvent            // Error occurred
AbortEvent                   // User aborted operation
```

### Hook System (6 Hooks)

| Hook | Trigger | Use Case |
|------|---------|----------|
| `OnPreToolUse` | Before tool execution | **Permission gating** |
| `OnPostToolUse` | After tool execution | Logging, result modification |
| `OnUserPromptSubmitted` | Before prompt sent | Prompt validation/modification |
| `OnSessionStart` | Session created | Custom initialization |
| `OnSessionEnd` | Session ended | Cleanup, summary |
| `OnErrorOccurred` | Error happens | Custom error handling |

---

## Architecture Design

### System Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    WPF Application Layer                     │
│  ┌──────────────────┐  ┌───────────────┐  ┌──────────────┐ │
│  │ ChatView         │  │ SessionInfo   │  │ SettingsView │ │
│  │ (Streaming UI)   │  │ (Autonomous)  │  │ (Feature Cfg)│ │
│  └────────┬─────────┘  └───────┬───────┘  └──────┬───────┘ │
│           │                    │                  │          │
├───────────┼────────────────────┼──────────────────┼──────────┤
│           │       ViewModels   │                  │          │
│  ┌────────▼────────┐  ┌────────▼────────┐  ┌─────▼───────┐ │
│  │ ChatViewModel   │  │ SessionInfo    │  │ Settings    │ │
│  │ • Events        │  │ ViewModel      │  │ ViewModel   │ │
│  │ • Approvals     │  │ • AutonomousMode│ │ • UseSdkMode│ │
│  └────────┬────────┘  └────────┬────────┘  └─────┬───────┘ │
│           │                    │                  │          │
├───────────┼────────────────────┼──────────────────┼──────────┤
│           │       Services     │                  │          │
│  ┌────────▼────────────────────▼──────────────────▼────────┐ │
│  │              ICopilotService (Interface)                │ │
│  │  ┌─────────────────────┐  ┌───────────────────────────┐ │ │
│  │  │  CopilotSdkService  │  │  CopilotCliService        │ │ │
│  │  │  (SDK Mode - Default)│  │  (Legacy CLI Mode)       │ │ │
│  │  │  ✓ OnPreToolUse     │  │  • Process.Start()       │ │ │
│  │  │  ✓ Event Streaming   │  │  • Parse stdout/stderr   │ │ │
│  │  └──────────┬──────────┘  └────────────┬──────────────┘ │ │
│  └─────────────┼───────────────────────────┼───────────────┘ │
│                │                           │                  │
├────────────────┼───────────────────────────┼──────────────────┤
│  ┌─────────────▼───────────┐  ┌────────────▼───────────────┐ │
│  │ IToolApprovalService    │  │ AutonomousModeSettings     │ │
│  │ • RequestApprovalAsync()│  │ • AllowAll                 │ │
│  │ • IsGloballyApproved()  │  │ • AllowAllTools            │ │
│  │ • RecordDecision()      │  │ • AllowAllPaths            │ │
│  │ • GetSavedApprovals()   │  │ • AllowAllUrls             │ │
│  └─────────────────────────┘  └────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                    │                           │
          ┌─────────▼─────────┐       ┌─────────▼─────────┐
          │  GitHub Copilot   │       │  Copilot CLI      │
          │  SDK (JSON-RPC)   │       │  Process (Legacy) │
          │  • CopilotClient  │       │  -p flag          │
          │  • CopilotSession │       │  --allow-* flags  │
          └───────────────────┘       └───────────────────┘
```

### Feature Flag Strategy

**Goal**: Elegant abstraction that can be completely removed in future

```csharp
// AppSettings.cs
public class AppSettings
{
    /// <summary>
    /// Use SDK mode (recommended). Set to false for legacy CLI mode.
    /// Default: true
    /// </summary>
    public bool UseSdkMode { get; set; } = true;
}

// App.xaml.cs - DI Registration
services.AddSingleton<CopilotSdkService>();
services.AddSingleton<CopilotCliService>();
services.AddSingleton<ICopilotService>(sp =>
{
    var settings = sp.GetRequiredService<AppSettings>();
    return settings.UseSdkMode
        ? sp.GetRequiredService<CopilotSdkService>()
        : sp.GetRequiredService<CopilotCliService>();
});
```

**Removal Path** (Future):
1. Delete `CopilotCliService.cs`
2. Remove `UseSdkMode` from `AppSettings`
3. Register `CopilotSdkService` directly as `ICopilotService`
4. Remove toggle from Settings UI
5. ~50 lines of code removed, zero impact on architecture

---

## Phase 1: Foundation (Day 1)

### 1.1 Add NuGet Package

```xml
<!-- src/CopilotAgent.Core/CopilotAgent.Core.csproj -->
<PackageReference Include="GitHub.Copilot.SDK" Version="*" />
```

### 1.2 Create Tool Approval Models

**File**: `src/CopilotAgent.Core/Models/ToolApprovalModels.cs`

```csharp
namespace CopilotAgent.Core.Models;

/// <summary>
/// Scope of a tool approval decision.
/// </summary>
public enum ApprovalScope
{
    /// <summary>Approve only this specific invocation.</summary>
    Once,
    
    /// <summary>Approve for the current session only.</summary>
    Session,
    
    /// <summary>Approve globally for all sessions.</summary>
    Global
}

/// <summary>
/// Risk level of a tool operation.
/// </summary>
public enum ToolRiskLevel
{
    Low,      // read operations
    Medium,   // write operations
    High,     // shell commands, network
    Critical  // system modifications
}

/// <summary>
/// Request for tool approval from the user.
/// </summary>
public class ToolApprovalRequest
{
    public required string SessionId { get; init; }
    public required string ToolName { get; init; }
    public object? ToolArgs { get; init; }
    public string? WorkingDirectory { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public ToolRiskLevel RiskLevel { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// User's decision on a tool approval request.
/// </summary>
public class ToolApprovalResponse
{
    public bool Approved { get; init; }
    public ApprovalScope Scope { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Persisted approval rule.
/// </summary>
public class ToolApprovalRule
{
    public required string ToolName { get; init; }
    public string? ToolArgsPattern { get; init; }
    public bool Approved { get; init; }
    public ApprovalScope Scope { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? SessionId { get; init; } // null for global
}
```

### 1.3 Create IToolApprovalService Interface

**File**: `src/CopilotAgent.Core/Services/IToolApprovalService.cs`

```csharp
namespace CopilotAgent.Core.Services;

public interface IToolApprovalService
{
    /// <summary>
    /// Request user approval for a tool invocation.
    /// Shows dialog if not auto-approved.
    /// </summary>
    Task<ToolApprovalResponse> RequestApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if a tool is already approved (no dialog needed).
    /// </summary>
    bool IsApproved(string sessionId, string toolName, object? args);
    
    /// <summary>
    /// Record an approval decision for future reference.
    /// </summary>
    void RecordDecision(ToolApprovalRequest request, ToolApprovalResponse response);
    
    /// <summary>
    /// Get all saved approval rules.
    /// </summary>
    IReadOnlyList<ToolApprovalRule> GetSavedRules();
    
    /// <summary>
    /// Remove an approval rule.
    /// </summary>
    void RemoveRule(ToolApprovalRule rule);
    
    /// <summary>
    /// Clear all session-scoped approvals for a session.
    /// </summary>
    void ClearSessionApprovals(string sessionId);
    
    /// <summary>
    /// Get the risk level for a tool.
    /// </summary>
    ToolRiskLevel GetToolRiskLevel(string toolName);
}
```

### 1.4 Update AppSettings

**File**: `src/CopilotAgent.Core/Models/AppSettings.cs` (update)

```csharp
public class AppSettings
{
    // Existing properties...
    
    /// <summary>
    /// Use SDK mode (recommended). Set to false for legacy CLI mode.
    /// </summary>
    public bool UseSdkMode { get; set; } = true;
    
    /// <summary>
    /// How to display tool approval requests.
    /// </summary>
    public ApprovalUIMode ApprovalUIMode { get; set; } = ApprovalUIMode.Both;
}

public enum ApprovalUIMode
{
    /// <summary>Show modal dialog only.</summary>
    Modal,
    
    /// <summary>Show inline in chat only.</summary>
    Inline,
    
    /// <summary>Ask user each time (default).</summary>
    Both
}
```

### 1.5 Files to Create - Phase 1

| File | Purpose |
|------|---------|
| `src/CopilotAgent.Core/Models/ToolApprovalModels.cs` | Request/Response/Rule models |
| `src/CopilotAgent.Core/Services/IToolApprovalService.cs` | Service interface |
| `src/CopilotAgent.Core/Services/ToolApprovalService.cs` | In-memory implementation |

---

## Phase 2: SDK Service Implementation (Days 2-3)

### 2.1 CopilotSdkService

**File**: `src/CopilotAgent.Core/Services/CopilotSdkService.cs`

```csharp
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Core.Services;

public class CopilotSdkService : ICopilotService, IAsyncDisposable
{
    private readonly IToolApprovalService _approvalService;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<CopilotSdkService> _logger;
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    
    private CopilotClient? _client;
    private readonly ConcurrentDictionary<string, CopilotSession> _sdkSessions = new();
    
    public event EventHandler<SessionEventArgs>? SessionEvent;
    public event EventHandler<ToolApprovalRequestEventArgs>? ToolApprovalRequested;
    
    public CopilotSdkService(
        IToolApprovalService approvalService,
        ISessionManager sessionManager,
        ILogger<CopilotSdkService> logger)
    {
        _approvalService = approvalService;
        _sessionManager = sessionManager;
        _logger = logger;
    }
    
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _clientLock.WaitAsync(cancellationToken);
        try
        {
            if (_client != null) return;
            
            _client = new CopilotClient(new CopilotClientOptions
            {
                UseStdio = true,
                LogLevel = "info",
                AutoStart = true,
                AutoRestart = true
            });
            
            await _client.StartAsync(cancellationToken);
            _logger.LogInformation("Copilot SDK client initialized");
        }
        finally
        {
            _clientLock.Release();
        }
    }
    
    public async Task<string> CreateSessionAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        
        var hooks = new SessionHooks
        {
            OnPreToolUse = (input, invocation) => 
                HandlePreToolUseAsync(session, input, invocation),
            OnPostToolUse = (input, invocation) => 
                HandlePostToolUseAsync(session, input, invocation),
            OnErrorOccurred = (input, invocation) => 
                HandleErrorAsync(session, input, invocation)
        };
        
        var config = new SessionConfig
        {
            Model = session.Model,
            WorkingDirectory = session.WorkingDirectory,
            Hooks = hooks,
            Streaming = true,
            McpServers = BuildMcpConfig(session)
        };
        
        var sdkSession = await _client!.CreateSessionAsync(config, cancellationToken);
        
        // Subscribe to events
        sdkSession.On(evt => DispatchEvent(session.Id, evt));
        
        _sdkSessions[session.Id] = sdkSession;
        
        _logger.LogInformation("SDK session {SessionId} created with SDK session {SdkSessionId}",
            session.Id, sdkSession.SessionId);
        
        return sdkSession.SessionId;
    }
    
    private async Task<PreToolUseHookOutput?> HandlePreToolUseAsync(
        Session session,
        PreToolUseHookInput input,
        HookInvocation invocation)
    {
        _logger.LogDebug("PreToolUse hook: {Tool} in session {Session}",
            input.ToolName, invocation.SessionId);
        
        // Check autonomous mode settings FIRST
        if (IsAutonomouslyApproved(session, input.ToolName))
        {
            _logger.LogInformation("Tool {Tool} auto-approved via autonomous mode",
                input.ToolName);
            return new PreToolUseHookOutput { PermissionDecision = "allow" };
        }
        
        // Check saved approvals
        if (_approvalService.IsApproved(session.Id, input.ToolName, input.ToolArgs))
        {
            _logger.LogInformation("Tool {Tool} approved via saved rule",
                input.ToolName);
            return new PreToolUseHookOutput { PermissionDecision = "allow" };
        }
        
        // Request user approval
        var request = new ToolApprovalRequest
        {
            SessionId = session.Id,
            ToolName = input.ToolName,
            ToolArgs = input.ToolArgs,
            WorkingDirectory = input.Cwd,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(input.Timestamp),
            RiskLevel = _approvalService.GetToolRiskLevel(input.ToolName)
        };
        
        var response = await _approvalService.RequestApprovalAsync(request);
        
        if (response.Approved)
        {
            _approvalService.RecordDecision(request, response);
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
    
    private bool IsAutonomouslyApproved(Session session, string toolName)
    {
        var auto = session.AutonomousMode;
        if (auto.AllowAll) return true;
        if (auto.AllowAllTools) return true;
        
        // Tool-specific checks
        if (toolName.StartsWith("read") && auto.AllowAllPaths) return true;
        if (toolName.StartsWith("write") && auto.AllowAllPaths) return true;
        if (toolName.Contains("url") && auto.AllowAllUrls) return true;
        
        return false;
    }
    
    public async Task SendMessageAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (!_sdkSessions.TryGetValue(sessionId, out var sdkSession))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }
        
        await sdkSession.SendAsync(new MessageOptions { Prompt = message }, cancellationToken);
    }
    
    public async Task<string?> SendAndWaitAsync(
        string sessionId,
        string message,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!_sdkSessions.TryGetValue(sessionId, out var sdkSession))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }
        
        var result = await sdkSession.SendAndWaitAsync(
            new MessageOptions { Prompt = message },
            timeout ?? TimeSpan.FromMinutes(5),
            cancellationToken);
        
        return result?.Data.Content;
    }
    
    public async Task AbortAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sdkSessions.TryGetValue(sessionId, out var sdkSession))
        {
            await sdkSession.AbortAsync(cancellationToken);
        }
    }
    
    private void DispatchEvent(string sessionId, SessionEvent evt)
    {
        SessionEvent?.Invoke(this, new SessionEventArgs(sessionId, evt));
    }
    
    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sdkSessions.Values)
        {
            await session.DisposeAsync();
        }
        _sdkSessions.Clear();
        
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }
}
```

### 2.2 Update ICopilotService Interface

**File**: `src/CopilotAgent.Core/Services/ICopilotService.cs` (update)

Add new methods to support SDK features:
- `Task AbortAsync(string sessionId)`
- `event EventHandler<SessionEventArgs> SessionEvent`
- `Task<IReadOnlyList<SessionEvent>> GetMessagesAsync(string sessionId)`

---

## Phase 3: Approval UI (Day 4)

### 3.1 Modal Dialog

**File**: `src/CopilotAgent.App/Views/ToolApprovalDialog.xaml`

Features:
- Tool name and arguments display
- Risk level indicator (color-coded)
- Working directory context
- Approve Once / Session / Always buttons
- Deny button
- "Don't ask again for this tool" checkbox

### 3.2 Inline Chat Approval

**File**: `src/CopilotAgent.App/Views/InlineApprovalView.xaml`

Embedded in chat stream:
```
┌────────────────────────────────────────────┐
│ 🔧 Tool Request: write_file                │
│ ┌────────────────────────────────────────┐ │
│ │ Path: /src/example.ts                  │ │
│ │ Content: (148 characters)              │ │
│ │ Risk: ⚠️ Medium                         │ │
│ └────────────────────────────────────────┘ │
│                                            │
│ [Allow Once] [Allow Session] [Allow Always]│
│ [Deny] [View Details]                      │
└────────────────────────────────────────────┘
```

---

## Phase 4: Settings Page (Day 5)

### 4.1 Settings Dialog

**File**: `src/CopilotAgent.App/Views/SettingsDialog.xaml`

```
┌─ Settings ───────────────────────────────────────────┐
│                                                       │
│ ⚙️ General                                            │
│ ┌───────────────────────────────────────────────────┐ │
│ │ Backend Mode                                      │ │
│ │   ◉ SDK Mode (recommended)                        │ │
│ │   ○ CLI Mode (legacy)                             │ │
│ └───────────────────────────────────────────────────┘ │
│                                                       │
│ 🔔 Tool Approval                                      │
│ ┌───────────────────────────────────────────────────┐ │
│ │ Approval UI Style                                 │ │
│ │   ○ Modal dialogs                                 │ │
│ │   ○ Inline in chat                                │ │
│ │   ◉ Ask each time                                 │ │
│ │                                                   │ │
│ │ ☐ Auto-approve low-risk operations               │ │
│ └───────────────────────────────────────────────────┘ │
│                                                       │
│ 🛡️ Default Permissions (New Sessions)                │
│ ┌───────────────────────────────────────────────────┐ │
│ │ ☐ Allow All (YOLO mode)                          │ │
│ │ ☐ Allow All Tools                                │ │
│ │ ☐ Allow All Paths                                │ │
│ │ ☐ Allow All URLs                                 │ │
│ └───────────────────────────────────────────────────┘ │
│                                                       │
│ 📋 Saved Approvals                                    │
│ ┌───────────────────────────────────────────────────┐ │
│ │ [Manage Global Approvals...]                      │ │
│ │ 3 global rules, 12 session rules                  │ │
│ └───────────────────────────────────────────────────┘ │
│                                                       │
│                        [Save] [Cancel]               │
└───────────────────────────────────────────────────────┘
```

### 4.2 Settings Icon Integration

Update `MainWindow.xaml` to make the settings icon functional:
- Click handler opens SettingsDialog
- Gear icon in top-right corner (already exists, just unimplemented)

---

## Phase 5: Advanced Features (Day 6)

### 5.1 Streaming Support

Enable real-time token streaming in ChatView:

```csharp
_copilotService.SessionEvent += (sender, args) =>
{
    Dispatcher.Invoke(() =>
    {
        switch (args.Event)
        {
            case AssistantMessageDeltaEvent delta:
                // Append to current message
                AppendToChatMessage(args.SessionId, delta.Data.DeltaContent);
                break;
                
            case AssistantMessageEvent complete:
                // Finalize message
                FinalizeChatMessage(args.SessionId, complete.Data.Content);
                break;
                
            case ToolExecutionStartEvent toolStart:
                ShowToolProgress(toolStart.Data.ToolName);
                break;
                
            case ToolExecutionCompleteEvent toolComplete:
                HideToolProgress();
                break;
                
            case SessionIdleEvent:
                EnableInput();
                break;
        }
    });
};
```

### 5.2 Abort/Cancel Support

Add cancel button during processing:
- Show "Cancel" button when session is processing
- Call `AbortAsync()` on click
- Show abort confirmation in chat

### 5.3 Session Resume

Enable resuming sessions:
- Store SDK session IDs in persistence
- Call `ResumeSessionAsync()` when loading session
- Handle `SessionResumeEvent`

---

## Phase 6: Testing & Polish (Day 7)

### 6.1 Unit Tests

| Test | Description |
|------|-------------|
| `ToolApprovalService_ShouldApproveGlobalRule` | Global rules work |
| `ToolApprovalService_ShouldApproveSessionRule` | Session rules work |
| `CopilotSdkService_ShouldUseAutonomousMode` | Auto-approve when enabled |
| `CopilotSdkService_ShouldShowDialog` | Show dialog when not auto-approved |

### 6.2 Integration Tests

| Test | Description |
|------|-------------|
| `E2E_SendMessage_ReceivesResponse` | Basic message flow |
| `E2E_ToolApproval_ModalDialog` | Modal approval works |
| `E2E_ToolApproval_InlineChat` | Inline approval works |
| `E2E_AutonomousMode_SkipsDialog` | No dialog in autonomous |

### 6.3 Documentation Updates

- Update README.md with SDK mode information
- Update RELEASE_NOTES.md with new features
- Create docs/SETTINGS_GUIDE.md

---

## Implementation Checklist

### Phase 1: Foundation ✅ COMPLETED
- [x] Add GitHub.Copilot.SDK NuGet package to Core project
- [x] Create `Models/ToolApprovalModels.cs`
- [x] Create `Services/IToolApprovalService.cs`
- [x] Create `Services/ToolApprovalService.cs`
- [x] Update `Models/AppSettings.cs` with UseSdkMode and ApprovalUIMode
- [x] Update `IPersistenceService` for approval rules

### Phase 2: SDK Service ✅ COMPLETED
- [x] Rename existing `CopilotService.cs` to `CopilotCliService.cs`
- [x] Create `Services/CopilotSdkService.cs`
- [x] Implement `OnPreToolUse` hook with autonomous mode check
- [x] Implement event streaming dispatch
- [x] Update DI registration with feature flag
- [x] Add `AbortAsync` method to ICopilotService interface
- [x] Add structured logging throughout

### Phase 3: Approval UI ✅ COMPLETED
- [x] Create `Views/ToolApprovalDialog.xaml`
- [x] Create `ViewModels/ToolApprovalDialogViewModel.cs`
- [x] Create `Views/ToolApprovalDialog.xaml.cs`
- [x] Create `ViewModels/InlineApprovalViewModel.cs`
- [x] Create `Views/InlineApprovalView.xaml` (user control)
- [x] Create `Views/InlineApprovalView.xaml.cs`
- [x] Create `Converters/BoolToExpandTextConverter.cs`
- [x] Create `Services/ToolApprovalUIService.cs`
- [x] Register ToolApprovalUIService in DI
- [x] Add risk level indicators (color-coded)
- [x] Add approval scope buttons (Once, Session, Always)

### Phase 4: Settings Page ✅ COMPLETED
- [x] Create `Views/SettingsDialog.xaml`
- [x] Create `ViewModels/SettingsDialogViewModel.cs`
- [x] Create `Views/SettingsDialog.xaml.cs`
- [x] Create `Views/ManageApprovalsDialog.xaml`
- [x] Create `Views/ManageApprovalsDialog.xaml.cs`
- [x] Wire settings icon in MainWindow
- [x] Persist settings to JSON (via existing IPersistenceService)
- [x] Add default permissions for new sessions

### Phase 5: Advanced Features ✅ COMPLETED
- [x] Enable streaming in SessionConfig
- [x] Handle AssistantMessageDeltaEvent in ChatView
- [x] Add cancel button during processing
- [x] Implement AbortAsync() call
- [x] Add session resume support
- [x] Add progress indicators for tool execution

### Phase 6: Testing & Polish
- [ ] Write unit tests for ToolApprovalService
- [ ] Write unit tests for CopilotSdkService
- [ ] Write integration tests
- [ ] Performance testing
- [ ] Update documentation
- [ ] Create migration guide

---

## Removal Path (Future)

When ready to remove CLI mode completely:

1. **Delete files**:
   - `CopilotCliService.cs`

2. **Update AppSettings**:
   - Remove `UseSdkMode` property

3. **Update DI registration**:
```csharp
// From:
services.AddSingleton<ICopilotService>(sp =>
    sp.GetRequiredService<AppSettings>().UseSdkMode
        ? sp.GetRequiredService<CopilotSdkService>()
        : sp.GetRequiredService<CopilotCliService>());

// To:
services.AddSingleton<ICopilotService, CopilotSdkService>();
```

4. **Update Settings UI**:
   - Remove "Backend Mode" section

**Total code removed**: ~50 lines
**Impact on architecture**: None

---

## References

- [GitHub Copilot SDK Documentation](https://github.com/github/copilot-sdk)
- [CopilotDesktop Tool Approval Approaches](./TOOL_APPROVAL_APPROACHES.md)
- [MCP Tool Approval Implementation](./MCP_TOOL_APPROVAL_IMPLEMENTATION.md)

---

*Last Updated: February 2026*