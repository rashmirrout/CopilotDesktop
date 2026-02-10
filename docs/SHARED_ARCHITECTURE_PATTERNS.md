# Shared Architecture Patterns — Agent Team & Agent Office

> **Version**: 1.0  
> **Status**: Living Document  
> **Date**: February 2026  
> **Scope**: Cross-cutting architectural patterns shared between `CopilotAgent.MultiAgent` (Agent Team) and `CopilotAgent.Office` (Agent Office)

---

## Table of Contents

1. [Purpose & Audience](#1-purpose--audience)
2. [Architecture Overview](#2-architecture-overview)
3. [Pattern 1: State Machine](#3-pattern-1-state-machine)
4. [Pattern 2: Manager-Worker Session Lifecycle](#4-pattern-2-manager-worker-session-lifecycle)
5. [Pattern 3: Settings Management & Dirty-State Tracking](#5-pattern-3-settings-management--dirty-state-tracking)
6. [Pattern 4: Event-Driven UI & Thread Marshalling](#6-pattern-4-event-driven-ui--thread-marshalling)
7. [Pattern 5: Clarification Flow with Correlation IDs](#7-pattern-5-clarification-flow-with-correlation-ids)
8. [Pattern 6: Session Health Polling](#8-pattern-6-session-health-polling)
9. [Pattern 7: Side Panel Architecture](#9-pattern-7-side-panel-architecture)
10. [Pattern 8: Tool Approval Pipeline](#10-pattern-8-tool-approval-pipeline)
11. [Cross-Cutting Concerns](#11-cross-cutting-concerns)
12. [Comparative Reference](#12-comparative-reference)
13. [Implementation Guidelines](#13-implementation-guidelines)
14. [Future Considerations](#14-future-considerations)

---

## 1. Purpose & Audience

This document captures the **shared architectural patterns** that emerged independently in both the Agent Team (`CopilotAgent.MultiAgent`) and Agent Office (`CopilotAgent.Office`) features. These patterns have proven robust in production and should be treated as the canonical approach for:

- Building new multi-agent features
- Understanding existing code structure
- Maintaining consistency across the codebase
- Onboarding new contributors

**Companion Documents**:
- [`MULTI_AGENT_ORCHESTRATOR_DESIGN.md`](MULTI_AGENT_ORCHESTRATOR_DESIGN.md) — Agent Team detailed design
- [`AGENT_OFFICE_DESIGN.md`](AGENT_OFFICE_DESIGN.md) — Agent Office detailed design
- [`PROJECT_STRUCTURE.md`](PROJECT_STRUCTURE.md) — Solution-level file organization

---

## 2. Architecture Overview

### 2.1 Shared Layered Architecture

Both features follow an identical three-layer architecture:

```
┌──────────────────────────────────────────────────────────────┐
│                    CopilotAgent.App (WPF)                     │
│   ┌────────────────────┐    ┌──────────────────────┐         │
│   │ AgentTeamViewModel │    │  OfficeViewModel     │         │
│   │ (MVVM, Dirty Track)│    │  (MVVM, Dirty Track) │         │
│   └────────┬───────────┘    └──────────┬───────────┘         │
│            │ DI / Events               │ DI / Events          │
├────────────┼───────────────────────────┼─────────────────────┤
│   ┌────────▼───────────┐    ┌──────────▼───────────┐         │
│   │ OrchestratorService│    │ OfficeManagerService  │         │
│   │ (State Machine)    │    │ (State Machine)       │         │
│   │ CopilotAgent.      │    │ CopilotAgent.         │         │
│   │   MultiAgent       │    │   Office              │         │
│   └────────┬───────────┘    └──────────┬───────────┘         │
│            │                           │                      │
├────────────┴───────────────────────────┴─────────────────────┤
│                      CopilotAgent.Core                        │
│   ┌──────────────┐  ┌──────────────┐  ┌───────────────────┐  │
│   │ICopilotService│  │ISessionManager│  │IToolApprovalSvc  │  │
│   └──────────────┘  └──────────────┘  └───────────────────┘  │
│   ┌──────────────────┐  ┌────────────────────────┐           │
│   │MultiAgentSettings│  │OfficeSettings          │           │
│   └──────────────────┘  └────────────────────────┘           │
└──────────────────────────────────────────────────────────────┘
```

### 2.2 Key Shared Principles

| Principle | Description |
|-----------|-------------|
| **MVVM Strict** | All UI state flows through ViewModels. Zero business logic in code-behind. |
| **Interface-First** | Every service has an interface. DI everywhere. |
| **Event-Driven** | All service-to-UI communication via typed events. No direct coupling. |
| **Settings as DTOs** | Lightweight settings models in `CopilotAgent.Core` to avoid cross-project references. |
| **Snapshot-Based Dirty Tracking** | Immutable records for comparing pending vs. persisted settings. |
| **Graceful Lifecycle** | Every session, timer, and task respects `CancellationToken`. |

### 2.3 Dependency Graph

```
CopilotAgent.App
  ├── CopilotAgent.MultiAgent   (Agent Team engine)
  ├── CopilotAgent.Office       (Agent Office engine)
  ├── CopilotAgent.Core         (shared models, services, interfaces)
  └── CopilotAgent.Persistence  (JSON file persistence)

CopilotAgent.MultiAgent ──► CopilotAgent.Core
CopilotAgent.Office     ──► CopilotAgent.Core
```

Both engine projects (`MultiAgent`, `Office`) depend only on `Core` — never on each other and never on `App`. This ensures clean testability and separation of concerns.

---

## 3. Pattern 1: State Machine

### 3.1 Pattern Description

Both services implement an **enum-based state machine** with explicit phase transitions, event emission on every transition, and guard clauses to prevent invalid transitions.

### 3.2 State Machine Comparison

**Agent Team** (`OrchestratorService`):

```
Idle → Clarifying → Planning → AwaitingApproval → Executing → Aggregating → Completed
                                                                              ↕
                                                                          Cancelled
```

**Agent Office** (`OfficeManagerService`):

```
Idle → Clarifying → Planning → AwaitingApproval → FetchingEvents → Scheduling → Executing → Aggregating → Resting → (loop)
                                                                                                                ↕
                                                                                                          Stopped / Error
```

### 3.3 Canonical Implementation

Both services use the same `TransitionTo()` pattern:

```csharp
// Phase enum (each feature defines its own)
public enum OrchestrationPhase  // Agent Team
{
    Idle, Clarifying, Planning, AwaitingApproval, 
    Executing, Aggregating, Completed, Cancelled
}

public enum ManagerPhase  // Agent Office
{
    Idle, Clarifying, Planning, AwaitingApproval, FetchingEvents,
    Scheduling, Executing, Aggregating, Resting, Error, Stopped
}

// TransitionTo() — shared pattern
private void TransitionTo(OrchestrationPhase newPhase, string? correlationId = null)
{
    var oldPhase = _currentPhase;
    _currentPhase = newPhase;

    _logger.LogInformation(
        "[Service] Phase transition: {From} → {To}", oldPhase, newPhase);

    RaiseEvent(new PhaseTransitionEvent
    {
        EventType = EventType.PhaseChanged,
        FromPhase = oldPhase,
        ToPhase = newPhase,
        CorrelationId = correlationId,
        Message = $"Phase changed: {oldPhase} → {newPhase}"
    });
}
```

### 3.4 Design Rules

1. **Phase transitions are always explicit** — no implicit state changes.
2. **Every transition emits an event** — the ViewModel subscribes and updates UI.
3. **Guard clauses** validate that transitions are legal (e.g., cannot approve a plan when not in `AwaitingApproval`).
4. **Correlation IDs** are threaded through transitions caused by user actions (see [Pattern 5](#7-pattern-5-clarification-flow-with-correlation-ids)).

### 3.5 When to Use

Any new feature that has a multi-step lifecycle (start → intermediate states → completion) should adopt this state machine pattern with:
- A dedicated `Phase` enum
- A `TransitionTo()` method that emits a typed event
- Public `CurrentPhase` property exposed on the service interface

---

## 4. Pattern 2: Manager-Worker Session Lifecycle

### 4.1 Pattern Description

Both features use a **Manager-Worker** pattern built on top of `ICopilotService`. A single **long-lived manager/orchestrator session** coordinates work, while **ephemeral worker/assistant sessions** are created per task unit, execute, report results, and are disposed.

### 4.2 Lifecycle Diagram

```
Manager/Orchestrator Session (LONG-LIVED)
├── Created once (per tab activation or StartAsync)
├── Persists across tasks (Team) or iterations (Office)
├── System prompt evolves with accumulated context
├── Only disposed on Reset/Stop or app shutdown
│
├── Task/Iteration 1
│   ├── Worker/Assistant Session #1 (EPHEMERAL) → create → prompt → collect → dispose
│   ├── Worker/Assistant Session #2 (EPHEMERAL) → create → prompt → collect → dispose
│   └── Worker/Assistant Session #3 (EPHEMERAL) → create → prompt → collect → dispose
│
├── [Agent Team: completed / Agent Office: REST PERIOD]
│
├── Task/Iteration 2
│   ├── Worker/Assistant Session #1 (EPHEMERAL) ...
│   └── Worker/Assistant Session #2 (EPHEMERAL) ...
│
└── ... (continues until stopped/reset)
```

### 4.3 Comparison

| Aspect | Agent Team | Agent Office |
|--------|------------|--------------|
| Manager session name | Orchestrator | Manager |
| Worker session name | WorkerAgent | AssistantAgent |
| Manager lifetime | Per tab, survives across tasks | Per `StartAsync()`, survives across iterations |
| Worker lifetime | Per `WorkChunk` | Per `AssistantTask` |
| Concurrency control | `SemaphoreSlim` in `AgentPool` | `SemaphoreSlim` in `AssistantPool` |
| Worker pool interface | `IAgentPool` | `IAssistantPool` |
| Worker dispatch | `DispatchBatchAsync()` | `ExecuteTasksAsync()` |
| Result type | `AgentResult` | `AssistantResult` |
| Session creation | `ICopilotService.GetOrCreateSdkSessionAsync()` | `ICopilotService.GetOrCreateSdkSessionAsync()` |
| Session disposal | `ICopilotService.TerminateSessionProcess()` | `ICopilotService.TerminateSessionProcess()` |

### 4.4 Session Creation Pattern

```csharp
// Shared pattern used by both features
private async Task<string> EnsureManagerSessionAsync(CancellationToken ct)
{
    if (!string.IsNullOrEmpty(_managerSessionId) 
        && _copilotService.HasActiveSession(_managerSessionId))
    {
        return _managerSessionId;
    }

    var session = new Session
    {
        SessionId = $"feature-manager-{Guid.NewGuid():N[..8]}",
        ModelId = _config.ManagerModelId,
        WorkingDirectory = _config.WorkingDirectory,
        EnabledMcpServers = _config.EnabledMcpServers,
        // ... other config
    };

    _managerSessionId = await _copilotService
        .GetOrCreateSdkSessionAsync(session, ct);
    return _managerSessionId;
}
```

### 4.5 Design Rules

1. **Manager sessions are reused** — avoid recreating unless the session is dead.
2. **Worker sessions are always disposed** after completion — no session leaks.
3. **Worker sessions are independent** — each gets its own system prompt, workspace, and cancellation token.
4. **Results flow upward** — workers return structured results; the manager aggregates.

---

## 5. Pattern 3: Settings Management & Dirty-State Tracking

### 5.1 Pattern Description

Both ViewModels implement a **snapshot-based dirty tracking** system for settings management. This is the most directly shared pattern, with near-identical implementations in `AgentTeamViewModel` and `OfficeViewModel`.

### 5.2 Architecture Diagram

```
┌──────────────────────────────────────────────────────────┐
│  ViewModel (e.g., AgentTeamViewModel)                    │
│                                                          │
│  ┌─── UI-Bound Properties (Pending Values) ───────────┐ │
│  │  SettingsMaxParallelWorkers = 5                     │ │
│  │  SelectedManagerModel = "gpt-4o"                    │ │
│  │  SettingsWorkerTimeoutMinutes = 15                  │ │
│  │  ...                                                │ │
│  └─────────────────┬──────────────────────────────────┘ │
│                    │ OnXxxChanged → RecalculateDirtyState│
│                    ▼                                     │
│  ┌─── Dirty Tracking Engine ──────────────────────────┐ │
│  │  CaptureCurrentSnapshot() → immutable record       │ │
│  │  Compare with _persistedSnapshot                    │ │
│  │  Count differing fields → PendingChangesCount       │ │
│  │  HasPendingChanges = count > 0                      │ │
│  └─────────────────┬──────────────────────────────────┘ │
│                    │                                     │
│  ┌─── Commands ───────────────────────────────────────┐ │
│  │  [Apply]  → write to AppSettings → persist to disk │ │
│  │           → _persistedSnapshot = CaptureSnapshot() │ │
│  │           → RecalculateDirtyState()                │ │
│  │           → if (IsRunning) SettingsRequireRestart   │ │
│  │                                                    │ │
│  │  [Discard] → LoadSettingsFromSnapshot(persisted)   │ │
│  │            → RecalculateDirtyState()               │ │
│  │                                                    │ │
│  │  [Restore Defaults] → set hardcoded defaults       │ │
│  └────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
```

### 5.3 Canonical Code Pattern

#### 5.3.1 Snapshot Record

Each ViewModel defines an immutable record for comparison:

```csharp
// Agent Team
private sealed record AgentTeamSettingsSnapshot(
    int MaxParallelWorkers,
    string WorkspaceStrategy,
    int WorkerTimeoutMinutes,
    int MaxRetries,
    int RetryDelaySeconds,
    bool AutoApproveReadOnly,
    string ManagerModel,
    string WorkerModel,
    string WorkingDirectory);

// Agent Office
private sealed record OfficeSettingsSnapshot(
    int CheckIntervalMinutes,
    int MaxAssistants,
    string ManagerModel,
    string AssistantModel,
    int AssistantTimeoutSeconds,
    int ManagerLlmTimeoutSeconds,
    int MaxRetries,
    int MaxQueueDepth,
    bool RequirePlanApproval,
    string CommentaryStreamingMode,
    string WorkspacePath);
```

#### 5.3.2 Property Change Hooks

Every settings property triggers dirty recalculation via `partial void OnXxxChanged`:

```csharp
[ObservableProperty]
private int _settingsMaxParallelWorkers = 3;
partial void OnSettingsMaxParallelWorkersChanged(int value) => RecalculateDirtyState();

[ObservableProperty]
private string _selectedManagerModel = "gpt-4";
partial void OnSelectedManagerModelChanged(string value) => RecalculateDirtyState();

// ... same pattern for every settings property
```

#### 5.3.3 Dirty State Calculation

```csharp
private void RecalculateDirtyState()
{
    if (_persistedSnapshot is null)
    {
        HasPendingChanges = false;
        PendingChangesCount = 0;
        return;
    }

    var current = CaptureCurrentSnapshot();
    var count = 0;

    if (current.MaxParallelWorkers != _persistedSnapshot.MaxParallelWorkers) count++;
    if (!string.Equals(current.WorkspaceStrategy, _persistedSnapshot.WorkspaceStrategy,
        StringComparison.Ordinal)) count++;
    // ... compare all fields
    
    PendingChangesCount = count;
    HasPendingChanges = count > 0;
}
```

#### 5.3.4 Apply & Discard

```csharp
[RelayCommand(CanExecute = nameof(CanApplySettings))]
private async Task ApplySettingsAsync()
{
    // 1. Write UI values to in-memory AppSettings
    var settings = _appSettings.MultiAgent; // or _appSettings.Office
    settings.MaxParallelSessions = SettingsMaxParallelWorkers;
    // ... all properties
    
    // 2. Persist to disk
    await _persistenceService.SaveSettingsAsync(_appSettings);
    
    // 3. Update snapshot — resets dirty state
    _persistedSnapshot = CaptureCurrentSnapshot();
    RecalculateDirtyState();
    
    // 4. Flag restart if running
    if (IsOrchestrating)
        SettingsRequireRestart = true;
}

[RelayCommand(CanExecute = nameof(CanDiscardSettings))]
private void DiscardSettings()
{
    if (_persistedSnapshot is not null)
        LoadSettingsFromSnapshot(_persistedSnapshot);
    RecalculateDirtyState();
}
```

#### 5.3.5 Loading from Persistence

```csharp
private void LoadSettingsFromPersistence()
{
    var settings = _appSettings.MultiAgent; // or _appSettings.Office
    // IMPORTANT: Use property setters (not backing fields)
    // so PropertyChanged fires and UI updates correctly
    SettingsMaxParallelWorkers = settings.MaxParallelSessions;
    SelectedManagerModel = settings.OrchestratorModelId ?? _appSettings.DefaultModel;
    // ... all properties
}
```

### 5.4 Settings DTO Models (in `CopilotAgent.Core`)

Settings are stored as lightweight DTOs in `Core` to avoid cross-project dependencies:

```csharp
// CopilotAgent.Core/Models/MultiAgentSettings.cs
public class MultiAgentSettings
{
    public int MaxParallelSessions { get; set; } = 3;
    public string WorkspaceStrategy { get; set; } = "InMemory";
    public int MaxRetriesPerChunk { get; set; } = 2;
    public int RetryDelaySeconds { get; set; } = 5;
    public string? OrchestratorModelId { get; set; }
    public string? WorkerModelId { get; set; }
    public string DefaultWorkingDirectory { get; set; } = string.Empty;
    public bool AutoApproveReadOnlyTools { get; set; } = true;
    public int WorkerTimeoutMinutes { get; set; } = 10;
}

// CopilotAgent.Core/Models/OfficeSettings.cs
public class OfficeSettings
{
    public int DefaultCheckIntervalMinutes { get; set; } = 5;
    public int DefaultMaxAssistants { get; set; } = 3;
    public string DefaultManagerModel { get; set; } = string.Empty;
    public string DefaultAssistantModel { get; set; } = string.Empty;
    public int DefaultAssistantTimeoutSeconds { get; set; } = 600;
    public int DefaultManagerLlmTimeoutSeconds { get; set; } = 120;
    public int DefaultMaxRetries { get; set; } = 1;
    public int DefaultMaxQueueDepth { get; set; } = 50;
    public bool DefaultRequirePlanApproval { get; set; } = true;
    public string DefaultCommentaryStreamingMode { get; set; } = "RealTime";
    public string DefaultWorkspacePath { get; set; } = string.Empty;
}
```

### 5.5 UI Binding Pattern

Both features display an **Apply/Discard** bar in the side panel when `HasPendingChanges` is true:

```xml
<!-- Apply/Discard bar — shown only when changes are pending -->
<Border Visibility="{Binding HasPendingChanges, Converter={StaticResource BoolToVisibilityConverter}}"
        Background="#1E3A1E" CornerRadius="6" Padding="8" Margin="0,8,0,0">
    <Grid>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Left">
            <TextBlock Text="⚠" Margin="0,0,6,0"/>
            <TextBlock>
                <Run Text="{Binding PendingChangesCount}"/>
                <Run Text=" unsaved change(s)"/>
            </TextBlock>
        </StackPanel>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="Apply" Command="{Binding ApplySettingsCommand}"/>
            <Button Content="Discard" Command="{Binding DiscardSettingsCommand}"/>
        </StackPanel>
    </Grid>
</Border>

<!-- Restart required badge — shown after Apply during active session -->
<Border Visibility="{Binding SettingsRequireRestart, Converter={StaticResource BoolToVisibilityConverter}}"
        Background="#4E3A00" CornerRadius="6" Padding="8" Margin="0,4,0,0">
    <TextBlock Text="⚠ Settings applied. Restart or reset to take effect." 
               Foreground="#FFD54F"/>
</Border>
```

### 5.6 Design Rules

1. **Never write to `AppSettings` without persisting** — always call `SaveSettingsAsync()` after mutation.
2. **Use property setters for loading** — ensures `PropertyChanged` fires and UI reflects the values.
3. **Snapshot after every Apply** — ensures dirty state resets correctly.
4. **`SettingsRequireRestart`** signals that the running session uses stale config; the user must reset/restart.
5. **`CanApplySettings` / `CanDiscardSettings`** are gated by `HasPendingChanges` via `NotifyCanExecuteChangedFor`.

---

## 6. Pattern 4: Event-Driven UI & Thread Marshalling

### 6.1 Pattern Description

Both services run their core logic on background threads (via `Task.Run` or `async`/`await`). All UI updates are marshalled to the WPF Dispatcher thread via `Dispatcher.InvokeAsync()`.

### 6.2 Event Flow

```
Background Thread (Service)
    │
    ├── TransitionTo(newPhase) → emits OrchestratorEvent / OfficeEvent
    ├── Worker completes     → emits WorkerProgressEvent / AssistantEvent
    ├── Countdown tick       → emits RestCountdownEvent
    │
    ▼
ViewModel subscribes to service.EventReceived / service.OnEvent
    │
    └── _dispatcher.InvokeAsync(() => {
            // Safe to update ObservableCollection and properties here
            CurrentPhaseDisplay = phase.ToString();
            Workers.Add(workerItem);
            EventLog.Insert(0, message);
        });
```

### 6.3 Canonical Implementation

```csharp
// In ViewModel constructor
_orchestrator.EventReceived += OnOrchestratorEvent;

// Event handler — ALWAYS marshals to Dispatcher
private void OnOrchestratorEvent(object? sender, OrchestratorEvent e)
{
    _dispatcher.InvokeAsync(() =>
    {
        // All UI-bound property mutations are safe here
        var phase = _orchestrator.CurrentPhase;
        CurrentPhaseDisplay = phase.ToString();
        CurrentPhaseColor = GetPhaseColor(phase);
        
        AddEvent($"[{e.TimestampUtc:HH:mm:ss}] {e.EventType}: {e.Message}");
        
        if (e is WorkerProgressEvent workerEvent)
        {
            UpdateWorkerStatus(workerEvent);
        }
    });
}
```

### 6.4 Critical Rules

| Rule | Rationale |
|------|-----------|
| **Never mutate `ObservableCollection` from a background thread** | WPF throws `InvalidOperationException` for cross-thread collection mutations |
| **Never set `[ObservableProperty]` from a background thread** | Bindings fire on the calling thread; must be the UI thread |
| **Use `InvokeAsync` not `Invoke`** | `InvokeAsync` is non-blocking; `Invoke` can deadlock if the UI thread is waiting |
| **Batch rapid events** | If events fire faster than UI can render, consider throttling or `BufferBlock<T>` |
| **Cap collection sizes** | Both features cap `EventLog` at 500 entries: `while (EventLog.Count > 500) EventLog.RemoveAt(EventLog.Count - 1)` |

### 6.5 Dispose Pattern

Both ViewModels unsubscribe from events on dispose to prevent leaks:

```csharp
public void Dispose()
{
    _pulseTimer.Stop();
    _pulseTimer.Tick -= OnPulseTimerTick;
    _sessionHealthTimer.Stop();
    _sessionHealthTimer.Tick -= OnSessionHealthTimerTick;
    _orchestrator.EventReceived -= OnOrchestratorEvent;
    _approvalQueue.PendingCountChanged -= OnPendingApprovalsChanged;
    _cts?.Cancel();
    _cts?.Dispose();
}
```

---

## 7. Pattern 5: Clarification Flow with Correlation IDs

### 7.1 Pattern Description

Both features support a **multi-turn clarification flow** where the LLM (via the manager/orchestrator session) asks the user questions before proceeding. A **correlation ID** mechanism prevents race conditions between the user's response and background phase transitions.

### 7.2 Flow Diagram

```
User submits task/prompt
    │
    ▼
Service sends to LLM: "Evaluate this task. Ask questions if needed."
    │
    ├── LLM responds: "I need clarification on X and Y"
    │   ├── Service: TransitionTo(Clarifying)
    │   ├── ViewModel: ShowClarification = true, display questions
    │   └── User types response → RespondToClarificationAsync(text)
    │       ├── Service generates correlationId
    │       ├── Emits ClarificationReceived event with correlationId
    │       ├── Sends response to LLM
    │       ├── LLM processes → may ask more questions or proceed
    │       ├── TransitionTo(Planning) with same correlationId
    │       └── ViewModel: recognizes correlated transition
    │           → does NOT dismiss clarification panel prematurely
    │
    └── LLM responds: "I have enough context, proceeding to plan"
        └── Service: TransitionTo(Planning) → skip clarification
```

### 7.3 Correlation ID Mechanism

```csharp
// In ViewModel — track the active correlation ID
private string? _activeClarificationCorrelationId;

// When a clarification response is sent, capture the correlation ID
if (e.EventType == OrchestratorEventType.ClarificationReceived 
    && !string.IsNullOrEmpty(e.CorrelationId))
{
    _activeClarificationCorrelationId = e.CorrelationId;
}

// When a phase transition arrives, check if it's correlated
var isCorrelatedTransition = e is PhaseTransitionEvent pte
    && !string.IsNullOrEmpty(pte.CorrelationId)
    && pte.CorrelationId == _activeClarificationCorrelationId;

// Correlated transitions don't auto-dismiss the clarification panel
if (!isCorrelatedTransition && ShowClarification 
    && phase != OrchestrationPhase.Clarifying)
{
    // External/unexpected transition — dismiss clarification panel
    ShowClarification = false;
}
```

### 7.4 Why This Matters

Without correlation IDs, a timing race occurs:
1. User clicks "Send" on clarification response.
2. Service processes the response and transitions to `Planning`.
3. The `PhaseChanged` event arrives at the ViewModel.
4. ViewModel sees phase is no longer `Clarifying` → dismisses the panel.
5. But the `HandleOrchestratorResponse` hasn't arrived yet → user sees a flash.

The correlation ID ensures that phase transitions **caused by the user's action** are recognized and handled gracefully.

---

## 8. Pattern 6: Session Health Polling

### 8.1 Pattern Description

Both features implement **periodic session health polling** as ground truth, because event-driven notifications alone can miss disconnections. A `DispatcherTimer` periodically calls `ICopilotService.HasActiveSession()` and updates a health indicator.

### 8.2 Implementation

```csharp
// Timer setup (configurable interval, clamped to [5, 60] seconds)
var healthIntervalSec = Math.Clamp(appSettings.SessionHealthCheckIntervalSeconds, 5, 60);
_sessionHealthTimer = new DispatcherTimer
{
    Interval = TimeSpan.FromSeconds(healthIntervalSec)
};
_sessionHealthTimer.Tick += OnSessionHealthTimerTick;
_sessionHealthTimer.Start();

// Health check logic
private void OnSessionHealthTimerTick(object? sender, EventArgs e)
{
    try
    {
        var sessionId = _service.SessionId;
        
        if (string.IsNullOrEmpty(sessionId))
        {
            ApplySessionHealth("WAITING", "#FFC107");  // Amber
            return;
        }

        var isAlive = _copilotService.HasActiveSession(sessionId);

        if (!isAlive)
        {
            ApplySessionHealth("DISCONNECTED", "#F44336");  // Red
            return;
        }

        if (_service.IsRunning)
            ApplySessionHealth("LIVE", "#4CAF50");     // Green
        else
            ApplySessionHealth("IDLE", "#9E9E9E");     // Grey
    }
    catch (Exception ex)
    {
        ApplySessionHealth("ERROR", "#F44336");         // Red
    }
}

// Apply only on state change (avoids redundant PropertyChanged)
private void ApplySessionHealth(string text, string color)
{
    if (SessionIndicatorText == text && SessionIndicatorColor == color)
        return;
    SessionIndicatorText = text;
    SessionIndicatorColor = color;
}
```

### 8.3 Visual States

| State | Color | Meaning |
|-------|-------|---------|
| `WAITING` | 🟡 Amber `#FFC107` | No session created yet |
| `LIVE` | 🟢 Green `#4CAF50` | Session active and service is running — indicator blinks |
| `IDLE` | ⚪ Grey `#9E9E9E` | Session exists but service is not actively running |
| `DISCONNECTED` | 🔴 Red `#F44336` | Session ID known but `HasActiveSession` returned false |
| `ERROR` | 🔴 Red `#F44336` | Health check threw an exception |

### 8.4 Blinking Animation

Both features use a unified pulse timer (500ms interval) that toggles opacity on the `LIVE` indicator:

```csharp
private void OnPulseTimerTick(object? sender, EventArgs e)
{
    _pulseToggle = !_pulseToggle;
    
    if (SessionIndicatorText == "LIVE")
        SessionIndicatorOpacity = _pulseToggle ? 1.0 : 0.4;
    else
        SessionIndicatorOpacity = 1.0;
}
```

---

## 9. Pattern 7: Side Panel Architecture

### 9.1 Pattern Description

Both features use a **fly-in/fly-out side panel** that overlays the main content area from the right edge. The panel contains settings controls, event logs, and live status information.

### 9.2 Layout Architecture

```
┌──────────────────────────────────────────────────────────────┐
│  Main Content Area (Chat / Status / Controls)                 │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                                                         │ │
│  │          Full-width primary content                     │ │
│  │                                                         │ │
│  │                    ┌──────── Side Panel (overlay) ────┐ │ │
│  │                    │                                  │ │ │
│  │                    │  ⚙️ Settings Section             │ │ │
│  │                    │  📊 Event Log Section            │ │ │
│  │                    │  📈 Statistics Section           │ │ │
│  │                    │  💭 Live Commentary (Office)     │ │ │
│  │                    │                                  │ │ │
│  │                    └──────────────────────────────────┘ │ │
│  │                                                         │ │
│  └─────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

### 9.3 Animation Pattern

Both views use `Storyboard`-driven `TranslateTransform` animations:

```xml
<!-- Side Panel Container -->
<Border x:Name="SidePanel"
        Width="380"
        HorizontalAlignment="Right"
        RenderTransformOrigin="1,0.5">
    <Border.RenderTransform>
        <TranslateTransform x:Name="SidePanelTranslate" X="380"/>
    </Border.RenderTransform>
</Border>

<!-- Slide In (300ms, EaseOut) -->
<Storyboard x:Key="SlideInStoryboard">
    <DoubleAnimation Storyboard.TargetName="SidePanelTranslate"
                     Storyboard.TargetProperty="X"
                     From="380" To="0" Duration="0:0:0.3">
        <DoubleAnimation.EasingFunction>
            <CubicEase EasingMode="EaseOut"/>
        </DoubleAnimation.EasingFunction>
    </DoubleAnimation>
</Storyboard>

<!-- Slide Out (250ms, EaseIn) -->
<Storyboard x:Key="SlideOutStoryboard">
    <DoubleAnimation Storyboard.TargetName="SidePanelTranslate"
                     Storyboard.TargetProperty="X"
                     From="0" To="380" Duration="0:0:0.25">
        <DoubleAnimation.EasingFunction>
            <CubicEase EasingMode="EaseIn"/>
        </DoubleAnimation.EasingFunction>
    </DoubleAnimation>
</Storyboard>
```

### 9.4 Side Panel Sections (Shared Structure)

| Section | Agent Team | Agent Office |
|---------|------------|--------------|
| **Settings** | Max workers, workspace strategy, models, timeout, retry, working directory | Check interval, pool size, models, timeout, retry, queue depth, commentary mode |
| **Apply/Discard Bar** | ✅ Identical pattern | ✅ Identical pattern |
| **Restart Badge** | ✅ Identical pattern | ✅ Identical pattern |
| **Event Log** | Reverse-chronological, 500 max | Reverse-chronological, 500 max |
| **Live Commentary** | N/A | ✅ Real-time streaming commentary |
| **Iteration Stats** | N/A | ✅ Completed iterations, success rate |
| **Worker Status** | In main area (pills) | Activity indicators in main area |

### 9.5 Toggle Mechanism

```csharp
// ViewModel
[ObservableProperty]
private bool _isSidePanelOpen;

[RelayCommand]
private void ToggleSidePanel()
{
    IsSidePanelOpen = !IsSidePanelOpen;
}

// Code-behind (minimal — only animation triggers)
private void OnSidePanelVisibilityChanged(object sender, ...)
{
    var vm = DataContext as ViewModel;
    if (vm.IsSidePanelOpen)
        (FindResource("SlideInStoryboard") as Storyboard)?.Begin();
    else
        (FindResource("SlideOutStoryboard") as Storyboard)?.Begin();
}
```

---

## 10. Pattern 8: Tool Approval Pipeline

### 10.1 Pattern Description

Both features share the existing `IToolApprovalService` infrastructure from `CopilotAgent.Core`. In multi-worker scenarios, a centralized `IApprovalQueue` serializes approval requests to prevent dialog storms.

### 10.2 Approval Flow

```
Worker/Assistant executes → LLM requests tool call
    │
    ├── Check IToolApprovalService.IsApproved(sessionId, toolName, args)
    │   ├── Yes → proceed without UI prompt
    │   └── No  → enqueue approval request
    │
    ▼
IApprovalQueue.EnqueueApprovalAsync(request)
    │
    ├── SemaphoreSlim(1,1) ensures one approval dialog at a time
    ├── IToolApprovalService.RequestApprovalAsync(request)
    │   └── UI shows approval dialog / inline approval
    ├── User approves/denies → result returned
    └── Semaphore released → next queued request proceeds
```

### 10.3 Approval Scopes

| Scope | Behavior |
|-------|----------|
| **Once** | Approved for this single invocation only |
| **Session** | Approved for all workers in this orchestration/office session |
| **Global** | Approved everywhere across all features |

### 10.4 Worker Integration

```csharp
// In WorkerAgent/AssistantAgent — tool approval hook
Hooks = new SessionHooks
{
    OnPreToolUse = async (toolCall) =>
    {
        if (_toolApprovalService.IsApproved(sessionId, toolCall.ToolName, toolCall.Arguments))
            return ToolApprovalResult.Approve;

        var request = new ToolApprovalRequest
        {
            SessionId = _parentSessionId,  // Use orchestrator/manager session ID
            ToolName = toolCall.ToolName,
            Arguments = toolCall.Arguments,
            Source = $"Worker [{_taskTitle}]"
        };

        var response = await _approvalQueue.EnqueueApprovalAsync(request);
        return response.Decision == ApprovalDecision.Approve
            ? ToolApprovalResult.Approve
            : ToolApprovalResult.Deny;
    }
};
```

---

## 11. Cross-Cutting Concerns

### 11.1 DI Registration

All services are registered in `App.xaml.cs`. The registration pattern:

```csharp
// Singletons — shared state across the app lifetime
services.AddSingleton<IOrchestratorService, OrchestratorService>();
services.AddSingleton<IOfficeManagerService, OfficeManagerService>();
services.AddSingleton<IApprovalQueue, ApprovalQueue>();

// Transient — new instance per resolution
services.AddTransient<AgentTeamViewModel>();
services.AddTransient<OfficeViewModel>();

// Strategy factories
services.AddSingleton<Func<WorkspaceStrategyType, IWorkspaceStrategy>>(...);
```

### 11.2 Error Handling Conventions

| Scenario | Both Features Handle With |
|----------|--------------------------|
| LLM timeout | `TimeoutException` catch → user-facing message + event log entry |
| Session disconnect | `InvalidOperationException` (connection check) → attempt reconnect or prompt reset |
| Worker/assistant failure | Structured result with `IsSuccess = false` + retry policy |
| Unparseable LLM response | Retry with clarification prompt, fall back to manual parse |
| User cancellation | `CancellationToken` respected at every async boundary |
| Fatal/unrecoverable | Transition to `Error`/`Cancelled` state, log, raise event |

### 11.3 Cancellation Token Hierarchy

```
_masterCts (root — cancelled on DisposeAsync/app shutdown)
├── _taskCts / _loopCts (per task/loop — cancelled on Cancel/Stop/Reset)
│   └── per-worker/assistant CancellationToken (linked to parent)
```

### 11.4 Logging Conventions

Both features use `ILogger<T>` with structured logging:

```csharp
_logger.LogInformation("[AgentTeamVM] SubmitTask: prompt='{Prompt}'", Truncate(prompt, 120));
_logger.LogDebug("[OfficeVM] Worker update: chunk={ChunkId}, status={Status}", chunkId, status);
_logger.LogWarning(ex, "[Service] Session health check failed.");
_logger.LogError(ex, "[Service] Unexpected error during execution.");
```

**Naming convention**: Log messages prefixed with `[ClassName]` for easy filtering.

### 11.5 Converter Reuse

Both features share the same set of WPF value converters from `CopilotAgent.App/Converters/`:

| Converter | Usage |
|-----------|-------|
| `BoolToVisibilityConverter` | Show/hide panels based on boolean state |
| `InverseBoolToVisibilityConverter` | Hide when true, show when false |
| `StringToVisibilityConverter` | Visible when string is non-empty |
| `StringToBrushConverter` | Hex color string → `SolidColorBrush` |
| `UtcToLocalTimeConverter` | UTC timestamps → local display time |
| `ZeroCountToVisibilityConverter` | Show when count > 0 |

---

## 12. Comparative Reference

### 12.1 Feature-Level Comparison

| Aspect | Agent Team | Agent Office |
|--------|------------|--------------|
| **Lifecycle** | One-shot: submit → plan → execute → done | Continuous loop: check → delegate → report → rest → repeat |
| **Phase enum** | `OrchestrationPhase` (8 values) | `ManagerPhase` (11 values) |
| **Manager session** | Created per task, long-lived within tab | Created on `StartAsync()`, long-lived across iterations |
| **Worker session** | Per `WorkChunk` | Per `AssistantTask` |
| **Scheduling** | DAG-based dependency scheduler, all chunks dispatched per stage | Queue-based: if tasks > pool size, pending tasks wait |
| **User interaction** | One-shot with optional follow-up | Continuous: inject instructions, change interval, pause/resume |
| **Rest period** | None | Configurable countdown with UI visualization |
| **Iteration** | Single execution | Periodic with configurable interval |
| **Task source** | User provides full task upfront | Manager discovers tasks from events/data sources |
| **Event type** | `OrchestratorEvent` hierarchy | `OfficeEvent` hierarchy |
| **Settings model** | `MultiAgentSettings` | `OfficeSettings` |
| **Service interface** | `IOrchestratorService` | `IOfficeManagerService` |
| **ViewModel** | `AgentTeamViewModel` | `OfficeViewModel` |
| **View** | `AgentTeamView.xaml` | `OfficeView.xaml` |

### 12.2 Identical Patterns

These patterns are implemented identically (or near-identically) in both features:

| Pattern | Files |
|---------|-------|
| Snapshot-based dirty tracking | `AgentTeamViewModel.cs`, `OfficeViewModel.cs` |
| Apply/Discard settings commands | Both ViewModels |
| `SettingsRequireRestart` flag | Both ViewModels |
| `Dispatcher.InvokeAsync` event marshalling | Both ViewModels |
| Session health polling timer | Both ViewModels |
| Side panel slide animation | `AgentTeamView.xaml`, `OfficeView.xaml` |
| Event log (reverse-chronological, 500 cap) | Both ViewModels |
| Model selection dropdown with cache | Both ViewModels |
| `TransitionTo()` with event emission | `OrchestratorService.cs`, `OfficeManagerService.cs` |
| Clarification flow with correlation IDs | Both services and ViewModels |
| `CancellationToken` threading | Both services |

### 12.3 Divergent Patterns

These patterns differ between features due to their inherent design differences:

| Aspect | Agent Team | Agent Office |
|--------|------------|--------------|
| Worker status display | Ephemeral pills (auto-show/hide) | Activity indicators with pulse animation |
| Plan visualization | Interactive plan card with stages | Inline in chat stream |
| Aggregation | Synthesis agent produces report | Manager LLM consolidates results |
| Instruction injection | During execution, routed to orchestrator | Between iterations, absorbed at boundary |
| Live commentary | N/A (events in log) | Dedicated side panel section with streaming |
| Iteration containers | N/A | Foldable iteration sections in chat |

---

## 13. Implementation Guidelines

### 13.1 Adding a New Multi-Agent Feature

When building a new feature that follows these patterns:

1. **Create a new project** (e.g., `CopilotAgent.NewFeature`) that depends only on `CopilotAgent.Core`.
2. **Define a phase enum** with all lifecycle states.
3. **Implement a service** with `TransitionTo()`, event emission, and `CancellationToken` support.
4. **Define events** as a typed hierarchy inheriting from a base event class.
5. **Add a settings DTO** in `CopilotAgent.Core/Models/`.
6. **Create a ViewModel** with the snapshot-based dirty tracking pattern.
7. **Create a View** with a side panel using the slide animation pattern.
8. **Register in DI** in `App.xaml.cs`.

### 13.2 Settings Checklist

When adding a new settings property:

- [ ] Add property to the `Core` settings DTO model
- [ ] Add `[ObservableProperty]` in the ViewModel
- [ ] Add `partial void OnXxxChanged(T value) => RecalculateDirtyState();`
- [ ] Add the field to the snapshot record
- [ ] Add comparison in `RecalculateDirtyState()`
- [ ] Add read in `LoadSettingsFromPersistence()`
- [ ] Add read in `LoadSettingsFromSnapshot()`
- [ ] Add write in `ApplySettingsAsync()`
- [ ] Add XAML binding in the side panel settings section
- [ ] Test: change value → verify `HasPendingChanges` becomes true
- [ ] Test: Apply → verify persisted and snapshot updated
- [ ] Test: Discard → verify reverted to last persisted value

### 13.3 Event Handling Checklist

When subscribing to service events from a ViewModel:

- [ ] Subscribe in constructor
- [ ] Unsubscribe in `Dispose()`
- [ ] Wrap ALL handler logic in `_dispatcher.InvokeAsync(() => { ... })`
- [ ] Never assume event ordering — check current phase from the service
- [ ] Cap collection sizes to prevent unbounded growth
- [ ] Log events at `Debug` level for traceability

### 13.4 Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Mutating `ObservableCollection` from background thread | Always wrap in `Dispatcher.InvokeAsync` |
| Settings property changes not detected | Ensure `partial void OnXxxChanged` calls `RecalculateDirtyState()` |
| Session leak on error path | Always dispose worker sessions in `finally` blocks |
| Phase transition race during clarification | Use correlation IDs (Pattern 5) |
| Stale UI after Reset | Clear all collections, reset all properties, call `UpdateTeamStatus()` |
| Event handler leak | Always unsubscribe in `Dispose()` |
| Loading settings with backing fields instead of setters | Use `SettingsXxx = value` (property setter), not `_settingsXxx = value` |

---

## 14. Future Considerations

### 14.1 Potential for Base Classes

Both ViewModels share significant duplicated code. A future refactoring could extract:

```csharp
// Hypothetical shared base
public abstract class MultiAgentViewModelBase<TSnapshot> : ViewModelBase, IDisposable
    where TSnapshot : class
{
    protected TSnapshot? PersistedSnapshot;
    
    // Shared infrastructure:
    // - RecalculateDirtyState()
    // - ApplySettingsAsync() / DiscardSettings()
    // - Session health polling
    // - Event log management (AddEvent, cap at 500)
    // - Pulse timer for animations
    // - Dispatcher marshalling helper
    
    protected abstract TSnapshot CaptureCurrentSnapshot();
    protected abstract void LoadSettingsFromSnapshot(TSnapshot snapshot);
    protected abstract void LoadSettingsFromPersistence();
    protected abstract Task WriteSettingsToAppSettings();
}
```

**Trade-offs**:
- ✅ Reduces duplication (~200 lines per ViewModel)
- ✅ Enforces consistency
- ⚠️ Adds coupling between features via shared base
- ⚠️ May over-constrain future features with different settings patterns

### 14.2 Shared Event Infrastructure

Both event hierarchies (`OrchestratorEvent`, `OfficeEvent`) have similar shapes. A shared base could be introduced:

```csharp
public abstract class AgentEvent
{
    public string EventId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Message { get; set; }
    public string? CorrelationId { get; set; }
}
```

### 14.3 Extension Points

The current architecture supports these future additions without structural changes:

- **New multi-agent features**: Follow the patterns in this doc
- **Cloud scalability**: Service interfaces abstract away local vs. remote execution (see [Appendix E in Agent Team design](MULTI_AGENT_ORCHESTRATOR_DESIGN.md))
- **Persistent event logs**: `IOfficeEventLog` / `ITaskLogStore` can be backed by database instead of in-memory
- **Settings sync**: Persistence layer can be extended to sync across machines
- **Feature composition**: A future "super-orchestrator" could compose Agent Team and Agent Office

---

*End of Shared Architecture Patterns Document*