# Shared Architecture Patterns — Agent Team & Agent Office

> **Version**: 2.0  
> **Status**: Living Document — Enterprise Production Standard  
> **Date**: February 2026  
> **Scope**: Cross-cutting architectural patterns shared between `CopilotAgent.MultiAgent` (Agent Team) and `CopilotAgent.Office` (Agent Office)  
> **Audience**: Engineers building, maintaining, or extending CopilotDesktop for millions of users worldwide

---

## Table of Contents

**Part I — Proven Patterns (Production-Validated)**

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

**Part II — Enterprise Enhancement Patterns (Next-Generation)**

14. [Pattern 9: Domain-Expertise Agent Roles](#14-pattern-9-domain-expertise-agent-roles)
15. [Pattern 10: Enterprise Conversation Management](#15-pattern-10-enterprise-conversation-management)
16. [Pattern 11: Critique-Refine Quality Gates](#16-pattern-11-critique-refine-quality-gates)
17. [Pattern 12: Moderator-Based Dynamic Orchestration](#17-pattern-12-moderator-based-dynamic-orchestration)

**Part III — Production Readiness Standards**

18. [Scalability & Performance Guidelines](#18-scalability--performance-guidelines)
19. [Error Recovery Playbook](#19-error-recovery-playbook)
20. [Debugging & Observability Standards](#20-debugging--observability-standards)
21. [Cost & Resource Management](#21-cost--resource-management)
22. [Production Readiness Checklist](#22-production-readiness-checklist)
23. [Future Architecture Roadmap](#23-future-architecture-roadmap)

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

---

# Part II — Enterprise Enhancement Patterns

> The patterns in Part II represent **next-generation** architectural improvements derived from
> competitive analysis of multi-agent orchestration frameworks (Semantic Kernel AgentGroupChat,
> Microsoft.Agents.AI, Panel-of-Experts architectures). These patterns are designed to be
> **incrementally adopted** — each one is independently valuable and backward-compatible with
> the production patterns in Part I.

---

## 14. Pattern 9: Domain-Expertise Agent Roles

### 14.1 Pattern Description

Worker agents should be named and configured by **domain expertise**, not by abstract function.
Role names signal intent to both the LLM (via system prompts) and the user (via UI labels).
A user who sees "SecurityAnalyst" immediately understands value; "Generic" communicates nothing.

### 14.2 Role Taxonomy

```csharp
/// <summary>
/// Domain-expertise roles for worker agents. Each role carries a distinct system
/// prompt persona, tool access profile, and UI presentation.
///
/// DESIGN RULES:
///   1. Every role MUST have a unique system prompt template.
///   2. Every role MUST map to a user-visible display name and icon.
///   3. The TaskDecomposer assigns roles during planning based on chunk content.
///   4. GeneralPurpose is the fallback — never assign it when a specific role fits.
///   5. New roles require: enum value + prompt template + UI mapping + tests.
/// </summary>
public enum AgentRole
{
    // ── Architecture & Design ───────────────────────────────
    
    /// <summary>System design, API contracts, component decomposition, architecture decisions.</summary>
    Architect,
    
    /// <summary>Database schema design, query optimization, data modeling, migration strategy.</summary>
    DatabaseExpert,

    // ── Quality & Security ──────────────────────────────────
    
    /// <summary>Threat modeling, vulnerability analysis, authentication/authorization review, OWASP compliance.</summary>
    SecurityAnalyst,
    
    /// <summary>Code review, static analysis, best practices enforcement, pattern compliance.</summary>
    CodeReviewer,
    
    /// <summary>Test strategy, coverage analysis, test creation, edge case identification, regression testing.</summary>
    QualityAssurance,

    // ── Performance & Operations ────────────────────────────
    
    /// <summary>Profiling, bottleneck identification, algorithmic complexity, memory optimization, cache strategy.</summary>
    PerformanceEngineer,
    
    /// <summary>CI/CD pipelines, deployment strategy, infrastructure-as-code, monitoring setup.</summary>
    DevOpsSpecialist,

    // ── Implementation ──────────────────────────────────────
    
    /// <summary>Feature development, refactoring, bug fixes, code implementation.</summary>
    Developer,
    
    /// <summary>Research, data gathering, competitive analysis, documentation, technical writing.</summary>
    Researcher,

    // ── Meta Roles (Orchestration) ──────────────────────────
    
    /// <summary>
    /// Synthesizes results from multiple workers into a unified report.
    /// Used as the final aggregation step in multi-stage plans.
    /// </summary>
    Synthesizer,
    
    /// <summary>
    /// Evaluates work from other agents and provides adversarial review.
    /// See Pattern 11: Critique-Refine Quality Gates.
    /// </summary>
    Critic,

    // ── Fallback ────────────────────────────────────────────
    
    /// <summary>
    /// No domain specialization. Use ONLY when the task does not map to any specific role.
    /// The TaskDecomposer should minimize use of this role.
    /// </summary>
    GeneralPurpose
}
```

### 14.3 Role Configuration Contract

```csharp
/// <summary>
/// Configuration for a specific agent role. Loaded from settings or hardcoded defaults.
/// IMMUTABLE after construction — pass by value, never mutate in-place.
/// </summary>
public sealed record AgentRoleConfig
{
    /// <summary>The role this configuration applies to.</summary>
    public required AgentRole Role { get; init; }
    
    /// <summary>Human-readable display name shown in UI badges and pills.</summary>
    public required string DisplayName { get; init; }
    
    /// <summary>Emoji or icon identifier for UI rendering.</summary>
    public required string Icon { get; init; }
    
    /// <summary>
    /// System prompt template injected into the worker session.
    /// Supports placeholders: {TaskDescription}, {WorkspacePath}, {DependencyOutputs}.
    /// </summary>
    public required string SystemPromptTemplate { get; init; }
    
    /// <summary>
    /// Hex color for UI accent (pills, badges, status indicators).
    /// Must contrast against both light and dark backgrounds.
    /// </summary>
    public required string AccentColor { get; init; }
    
    /// <summary>
    /// Tool categories this role is authorized to use.
    /// Empty = all tools allowed. Populated = whitelist mode.
    /// </summary>
    public IReadOnlyList<string> AllowedToolCategories { get; init; } = [];
    
    /// <summary>
    /// Maximum time this role's worker may execute before forced termination.
    /// Null = use global default from MultiAgentSettings.WorkerTimeoutMinutes.
    /// </summary>
    public TimeSpan? TimeoutOverride { get; init; }
}
```

### 14.4 Default Role Registry

```csharp
/// <summary>
/// Provides default configurations for all agent roles.
/// Acts as single source of truth for role metadata across the application.
///
/// EXTENSION RULE: To add a new role:
///   1. Add enum value to AgentRole
///   2. Add entry to DefaultRoles dictionary below
///   3. Add system prompt template
///   4. Add UI mapping in AgentTeamView.xaml / OfficeView.xaml
///   5. Add unit test verifying the role is fully configured
/// </summary>
public static class AgentRoleDefaults
{
    public static IReadOnlyDictionary<AgentRole, AgentRoleConfig> DefaultRoles { get; } =
        new Dictionary<AgentRole, AgentRoleConfig>
        {
            [AgentRole.Architect] = new()
            {
                Role = AgentRole.Architect,
                DisplayName = "Architect",
                Icon = "🏗️",
                AccentColor = "#4FC3F7",
                SystemPromptTemplate = """
                    You are a senior software architect. Your expertise:
                    - System design and component decomposition
                    - API contract design and interface boundaries
                    - Design pattern selection and architectural trade-offs
                    - Scalability, maintainability, and extensibility analysis
                    Focus on structural decisions. Justify every choice with trade-offs.
                    """
            },
            [AgentRole.SecurityAnalyst] = new()
            {
                Role = AgentRole.SecurityAnalyst,
                DisplayName = "Security Analyst",
                Icon = "🛡️",
                AccentColor = "#EF5350",
                SystemPromptTemplate = """
                    You are a security analyst specializing in application security.
                    Your expertise:
                    - Threat modeling (STRIDE, DREAD)
                    - Authentication and authorization review
                    - Input validation and injection prevention
                    - OWASP Top 10 compliance assessment
                    - Secrets management and data protection
                    Be adversarial. Assume every input is hostile. Flag every risk.
                    """
            },
            [AgentRole.PerformanceEngineer] = new()
            {
                Role = AgentRole.PerformanceEngineer,
                DisplayName = "Performance Engineer",
                Icon = "⚡",
                AccentColor = "#FFB74D",
                SystemPromptTemplate = """
                    You are a performance engineer. Your expertise:
                    - Algorithmic complexity analysis (time and space)
                    - Memory allocation patterns and GC pressure
                    - Cache strategy and data locality
                    - Concurrency bottlenecks and contention
                    - Profiling methodology and benchmark design
                    Quantify everything. No vague claims — provide Big-O, measurements, or estimates.
                    """
            },
            [AgentRole.QualityAssurance] = new()
            {
                Role = AgentRole.QualityAssurance,
                DisplayName = "QA Engineer",
                Icon = "🧪",
                AccentColor = "#81C784",
                SystemPromptTemplate = """
                    You are a QA engineer. Your expertise:
                    - Test strategy (unit, integration, e2e, property-based)
                    - Edge case identification and boundary analysis
                    - Regression test design
                    - Code coverage analysis and gap identification
                    - Test data generation and fixture management
                    Every claim must be testable. Every path must be covered.
                    """
            },
            [AgentRole.CodeReviewer] = new()
            {
                Role = AgentRole.CodeReviewer,
                DisplayName = "Code Reviewer",
                Icon = "🔍",
                AccentColor = "#CE93D8",
                SystemPromptTemplate = """
                    You are a senior code reviewer. Your expertise:
                    - Code quality, readability, and maintainability
                    - Design pattern compliance and anti-pattern detection
                    - Error handling completeness and edge cases
                    - Naming conventions and API ergonomics
                    - SOLID principles and clean code practices
                    Be constructive but thorough. Every suggestion must include the 'why'.
                    """
            },
            [AgentRole.Developer] = new()
            {
                Role = AgentRole.Developer,
                DisplayName = "Developer",
                Icon = "💻",
                AccentColor = "#90CAF9",
                SystemPromptTemplate = """
                    You are a senior software developer. Your expertise:
                    - Feature implementation and code generation
                    - Refactoring and technical debt reduction
                    - Bug diagnosis and fix implementation
                    - API integration and data transformation
                    Write clean, tested, production-ready code. No shortcuts.
                    """
            },
            [AgentRole.DatabaseExpert] = new()
            {
                Role = AgentRole.DatabaseExpert,
                DisplayName = "Database Expert",
                Icon = "🗄️",
                AccentColor = "#A1887F",
                SystemPromptTemplate = """
                    You are a database expert. Your expertise:
                    - Schema design and normalization
                    - Query optimization and index strategy
                    - Migration planning and backward compatibility
                    - Data integrity constraints and transaction design
                    Every schema change must consider migration cost and query performance.
                    """
            },
            [AgentRole.DevOpsSpecialist] = new()
            {
                Role = AgentRole.DevOpsSpecialist,
                DisplayName = "DevOps Specialist",
                Icon = "🚀",
                AccentColor = "#80DEEA",
                SystemPromptTemplate = """
                    You are a DevOps specialist. Your expertise:
                    - CI/CD pipeline design and optimization
                    - Infrastructure-as-code and environment management
                    - Deployment strategies (blue-green, canary, rolling)
                    - Monitoring, alerting, and incident response
                    Automate everything. Manual steps are bugs.
                    """
            },
            [AgentRole.Researcher] = new()
            {
                Role = AgentRole.Researcher,
                DisplayName = "Researcher",
                Icon = "📚",
                AccentColor = "#FFF176",
                SystemPromptTemplate = """
                    You are a technical researcher. Your expertise:
                    - Technology evaluation and competitive analysis
                    - Documentation review and knowledge synthesis
                    - Best practice research across industry sources
                    - Data gathering, analysis, and structured reporting
                    Cite sources. Distinguish facts from opinions. Provide actionable recommendations.
                    """
            },
            [AgentRole.Synthesizer] = new()
            {
                Role = AgentRole.Synthesizer,
                DisplayName = "Synthesizer",
                Icon = "📊",
                AccentColor = "#B0BEC5",
                SystemPromptTemplate = """
                    You are a results synthesizer. Your job:
                    - Combine outputs from multiple agents into a unified, coherent report
                    - Resolve contradictions between agent findings
                    - Highlight consensus and disagreements
                    - Produce actionable executive summary
                    Never just concatenate. Always synthesize, prioritize, and conclude.
                    """
            },
            [AgentRole.Critic] = new()
            {
                Role = AgentRole.Critic,
                DisplayName = "Critic",
                Icon = "⚖️",
                AccentColor = "#FFAB91",
                SystemPromptTemplate = """
                    You are an adversarial reviewer. Your job:
                    - Challenge every assumption in the presented work
                    - Identify logical gaps, missing edge cases, and unverified claims
                    - Grade the work (PASS / NEEDS_REVISION / FAIL) with specific reasons
                    - Suggest concrete improvements for each issue found
                    Be rigorous. Do not rubber-stamp. Quality depends on your honesty.
                    """
            },
            [AgentRole.GeneralPurpose] = new()
            {
                Role = AgentRole.GeneralPurpose,
                DisplayName = "Agent",
                Icon = "🤖",
                AccentColor = "#E0E0E0",
                SystemPromptTemplate = """
                    You are a capable AI assistant. Complete the assigned task thoroughly
                    and accurately. If you encounter ambiguity, state your assumptions explicitly.
                    """
            }
        };
}
```

### 14.5 Role Selection Decision Tree

The task decomposer uses this logic when assigning roles to work chunks:

```
Task Content Analysis
    │
    ├── Contains "security", "auth", "vulnerability", "OWASP" → SecurityAnalyst
    ├── Contains "performance", "optimize", "profile", "bottleneck" → PerformanceEngineer
    ├── Contains "test", "coverage", "QA", "regression" → QualityAssurance
    ├── Contains "review", "code quality", "refactor" → CodeReviewer
    ├── Contains "design", "architecture", "API contract" → Architect
    ├── Contains "database", "schema", "SQL", "migration" → DatabaseExpert
    ├── Contains "deploy", "CI/CD", "pipeline", "infrastructure" → DevOpsSpecialist
    ├── Contains "research", "compare", "analyze", "document" → Researcher
    ├── Contains "implement", "build", "fix", "develop" → Developer
    ├── Contains "synthesize", "summarize", "aggregate" → Synthesizer
    ├── Contains "critique", "review adversarially", "validate" → Critic
    └── No match → GeneralPurpose (log warning: role assignment missed)
```

**NOTE**: This is a **heuristic fallback**. The primary role assignment is done by the LLM during
task decomposition — the LLM includes a `role` field in each chunk's JSON. The heuristic only
applies when the LLM omits or provides an unrecognized role.

### 14.6 Migration Guide (from Current Enum)

| Old Value | New Value | Notes |
|-----------|-----------|-------|
| `Generic` | `GeneralPurpose` | Renamed for clarity |
| `Planning` | `Architect` | Planning is an activity, not an expertise |
| `CodeAnalysis` | `CodeReviewer` | More specific, human-recognizable |
| `MemoryDiagnostics` | `PerformanceEngineer` | Broadened scope (memory is one aspect of performance) |
| `Performance` | `PerformanceEngineer` | More professional title |
| `Testing` | `QualityAssurance` | Industry-standard terminology |
| `Implementation` | `Developer` | Universally understood |
| `Synthesis` | `Synthesizer` | Consistent noun form |
| — | `SecurityAnalyst` | **NEW** — Critical gap filled |
| — | `DatabaseExpert` | **NEW** — Specialized data expertise |
| — | `DevOpsSpecialist` | **NEW** — Infrastructure expertise |
| — | `Researcher` | **NEW** — Non-code task support |
| — | `Critic` | **NEW** — Enables Critique-Refine pattern |

### 14.7 Design Rules

1. **Role names are domain expertise nouns**, not activity verbs. "SecurityAnalyst" not "AnalyzingSecurity".
2. **Every role has a unique system prompt** — never reuse prompts across roles.
3. **The LLM assigns roles during planning** — the enum exists for type safety and UI mapping.
4. **GeneralPurpose is the fallback of last resort** — log a warning when it's used.
5. **New roles require the full checklist**: enum value → config → prompt → UI → test.
6. **AccentColors must be WCAG AA compliant** against both `#1E1E1E` (dark) and `#FFFFFF` (light) backgrounds.

---

## 15. Pattern 10: Enterprise Conversation Management

### 15.1 Pattern Description

Conversation history is a **first-class concern** in multi-agent orchestration. Every message
exchanged between the orchestrator LLM and the system must be tracked, queryable, exportable,
and bounded in memory. This pattern extracts conversation management from service internals
into a dedicated, testable, thread-safe abstraction.

### 15.2 Interface Contract

```csharp
/// <summary>
/// Thread-safe conversation history manager for multi-agent orchestration.
///
/// THREAD SAFETY: All methods are safe for concurrent access from multiple workers.
/// MEMORY MANAGEMENT: Auto-trims when message count exceeds configured maximum.
/// PERSISTENCE: Supports export/import for session recovery and audit trails.
///
/// CRITICAL: Implementations MUST:
///   1. Never expose internal mutable collections.
///   2. Return defensive copies from all Get* methods.
///   3. Log all mutations at Debug level with ConversationId prefix.
///   4. Respect CancellationToken at every async boundary.
/// </summary>
public interface IAgentConversation : IDisposable
{
    /// <summary>Unique identifier for correlation and logging.</summary>
    string ConversationId { get; }

    /// <summary>
    /// Adds a message. Thread-safe via internal SemaphoreSlim.
    /// Auto-trims oldest messages when MaxMessages threshold is exceeded.
    /// </summary>
    /// <exception cref="ArgumentNullException">If message is null.</exception>
    /// <exception cref="ObjectDisposedException">If conversation is disposed.</exception>
    Task AddMessageAsync(ChatMessage message, CancellationToken ct = default);

    /// <summary>
    /// Returns complete history as an immutable snapshot.
    /// Never returns null — returns empty list if no messages exist.
    /// Performance: O(n) — creates defensive copy.
    /// </summary>
    IReadOnlyList<ChatMessage> GetHistory();

    /// <summary>
    /// Returns the last N messages. Useful for context window management.
    /// If lastN > MessageCount, returns all messages.
    /// If lastN &lt;= 0, returns empty list.
    /// </summary>
    IReadOnlyList<ChatMessage> GetHistory(int lastN);

    /// <summary>
    /// Filters by role. Returns immutable filtered snapshot.
    /// </summary>
    IReadOnlyList<ChatMessage> GetHistoryByRole(MessageRole role);

    /// <summary>
    /// Clears all messages. Irreversible. Emits ConversationChanged event.
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Exports conversation to JSON file including all metadata.
    /// </summary>
    /// <exception cref="IOException">On file write failure.</exception>
    Task ExportAsync(string filePath, CancellationToken ct = default);

    /// <summary>Current message count. Thread-safe read.</summary>
    int MessageCount { get; }

    /// <summary>
    /// Raised when conversation is modified (add, clear, trim).
    /// Handlers MUST NOT block — offload heavy work to background threads.
    /// </summary>
    event EventHandler<ConversationChangedEventArgs>? ConversationChanged;
}

/// <summary>
/// Event args for conversation modifications.
/// </summary>
public sealed class ConversationChangedEventArgs : EventArgs
{
    public required string ConversationId { get; init; }
    public required ConversationChangeType ChangeType { get; init; }
    public int MessageCount { get; init; }
    public int TrimmedCount { get; init; }
}

public enum ConversationChangeType
{
    MessageAdded,
    HistoryCleared,
    HistoryTrimmed,
    HistoryExported
}
```

### 15.3 Canonical Implementation Pattern

```csharp
/// <summary>
/// Production implementation of IAgentConversation.
///
/// Memory Management:
///   - Default MaxMessages = 1000 (configurable via constructor).
///   - When threshold exceeded, trims oldest 20% of messages.
///   - Trimming preserves the first message (system prompt) if present.
///
/// Thread Safety:
///   - SemaphoreSlim(1,1) guards all list mutations.
///   - Read operations return defensive copies (safe without lock).
///
/// Disposal:
///   - Releases semaphore, clears internal list, nulls event handlers.
///   - Post-disposal access throws ObjectDisposedException.
/// </summary>
public sealed class ConversationManager : IAgentConversation
{
    private readonly List<ChatMessage> _messages = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _maxMessages;
    private readonly ILogger<ConversationManager> _logger;
    private bool _disposed;

    public string ConversationId { get; }
    public int MessageCount => _messages.Count;

    public event EventHandler<ConversationChangedEventArgs>? ConversationChanged;

    public ConversationManager(
        string conversationId,
        ILogger<ConversationManager> logger,
        int maxMessages = 1000)
    {
        ConversationId = conversationId;
        _logger = logger;
        _maxMessages = Math.Max(10, maxMessages); // Floor at 10
    }

    public async Task AddMessageAsync(ChatMessage message, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(message);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _messages.Add(message);
            _logger.LogDebug("[{ConvId}] Message added: Role={Role}, Length={Length}",
                ConversationId, message.Role, message.Content?.Length ?? 0);

            // Auto-trim when threshold exceeded
            var trimmed = 0;
            if (_messages.Count > _maxMessages)
            {
                var trimCount = _maxMessages / 5; // Remove oldest 20%
                // Preserve first message (system prompt) if it's a System role
                var startIndex = _messages.Count > 0 
                    && _messages[0].Role == MessageRole.System ? 1 : 0;
                _messages.RemoveRange(startIndex, Math.Min(trimCount, _messages.Count - startIndex));
                trimmed = trimCount;

                _logger.LogInformation(
                    "[{ConvId}] Auto-trimmed {Count} messages (threshold: {Max})",
                    ConversationId, trimmed, _maxMessages);
            }

            RaiseChanged(trimmed > 0
                ? ConversationChangeType.HistoryTrimmed
                : ConversationChangeType.MessageAdded, trimmed);
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<ChatMessage> GetHistory()
    {
        // Defensive copy — caller cannot mutate our internal list
        return _messages.ToList().AsReadOnly();
    }

    public IReadOnlyList<ChatMessage> GetHistory(int lastN)
    {
        if (lastN <= 0) return [];
        var skip = Math.Max(0, _messages.Count - lastN);
        return _messages.Skip(skip).ToList().AsReadOnly();
    }

    public IReadOnlyList<ChatMessage> GetHistoryByRole(MessageRole role)
    {
        return _messages.Where(m => m.Role == role).ToList().AsReadOnly();
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var count = _messages.Count;
            _messages.Clear();
            _logger.LogInformation("[{ConvId}] Conversation cleared ({Count} messages removed)",
                ConversationId, count);
            RaiseChanged(ConversationChangeType.HistoryCleared);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ExportAsync(string filePath, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var snapshot = GetHistory(); // Defensive copy
        var json = JsonSerializer.Serialize(new
        {
            ConversationId,
            ExportedAtUtc = DateTime.UtcNow,
            MessageCount = snapshot.Count,
            Messages = snapshot
        }, new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
        _logger.LogInformation("[{ConvId}] Exported {Count} messages to {Path}",
            ConversationId, snapshot.Count, filePath);
        RaiseChanged(ConversationChangeType.HistoryExported);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
        _messages.Clear();
        ConversationChanged = null;
    }

    private void RaiseChanged(ConversationChangeType type, int trimmed = 0)
    {
        try
        {
            ConversationChanged?.Invoke(this, new ConversationChangedEventArgs
            {
                ConversationId = ConversationId,
                ChangeType = type,
                MessageCount = _messages.Count,
                TrimmedCount = trimmed
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{ConvId}] Error in ConversationChanged handler", ConversationId);
        }
    }
}
```

### 15.4 Integration with Existing Services

```csharp
// In IOrchestratorService — expose conversation as read-only
public interface IOrchestratorService
{
    // ... existing methods ...

    /// <summary>
    /// Returns a read-only view of the orchestrator's conversation history.
    /// Useful for debugging, export, and context carryover between tasks.
    /// </summary>
    IReadOnlyList<ChatMessage> GetConversationHistory();

    /// <summary>Returns the last N messages from conversation history.</summary>
    IReadOnlyList<ChatMessage> GetConversationHistory(int lastNMessages);
}

// In OrchestratorService — delegate to IAgentConversation
public IReadOnlyList<ChatMessage> GetConversationHistory()
    => _conversation.GetHistory();

public IReadOnlyList<ChatMessage> GetConversationHistory(int lastNMessages)
    => _conversation.GetHistory(lastNMessages);
```

### 15.5 Failure Modes & Recovery

| Failure | Detection | Recovery | User Impact |
|---------|-----------|----------|-------------|
| Memory pressure (>10K messages) | Auto-trim threshold | Trim oldest 20%, preserve system prompt | None (transparent) |
| Export file locked by another process | `IOException` during `ExportAsync` | Retry with timestamped filename suffix | Toast notification |
| Concurrent mutation contention | `SemaphoreSlim` wait timeout | Queue via internal Channel | None (serialized internally) |
| Post-disposal access | `ObjectDisposedException` | Re-create conversation, log warning | Session reset prompt |
| JSON serialization failure | `JsonException` during export | Fall back to plain-text export | Degraded export format |

### 15.6 Performance Characteristics

| Operation | Time Complexity | Memory | Notes |
|-----------|----------------|--------|-------|
| `AddMessageAsync` | O(1) amortized | ~1KB per message | O(n) during trim cycles |
| `GetHistory()` | O(n) | Creates full copy | Use `GetHistory(int)` for large conversations |
| `GetHistory(n)` | O(n) | Copy of last n | Preferred for context window management |
| `GetHistoryByRole` | O(n) | Filtered copy | Consider caching if called frequently |
| `ExportAsync` | O(n) | JSON string + file I/O | Async file write, non-blocking |
| `ClearAsync` | O(1) | Releases references | GC reclaims on next collection |

### 15.7 Testing Requirements

```csharp
[Fact]
public async Task AddMessageAsync_ConcurrentAccess_AllMessagesPreserved()
{
    var conversation = CreateTestConversation(maxMessages: 10_000);
    var tasks = Enumerable.Range(0, 100)
        .Select(i => conversation.AddMessageAsync(
            new ChatMessage { Role = MessageRole.User, Content = $"Msg {i}" }));

    await Task.WhenAll(tasks);

    Assert.Equal(100, conversation.MessageCount);
}

[Fact]
public async Task AddMessageAsync_ExceedsMax_TrimsOldestPreservesSystemPrompt()
{
    var conversation = CreateTestConversation(maxMessages: 10);

    // Add system prompt first
    await conversation.AddMessageAsync(
        new ChatMessage { Role = MessageRole.System, Content = "System" });

    for (var i = 0; i < 15; i++)
        await conversation.AddMessageAsync(
            new ChatMessage { Role = MessageRole.User, Content = $"Msg {i}" });

    Assert.True(conversation.MessageCount <= 10);
    Assert.Equal(MessageRole.System, conversation.GetHistory().First().Role);
}

[Fact]
public async Task ExportAsync_ProducesValidJson_CanBeDeserialized()
{
    var conversation = CreateTestConversation();
    await conversation.AddMessageAsync(
        new ChatMessage { Role = MessageRole.User, Content = "Hello" });

    var path = Path.GetTempFileName();
    await conversation.ExportAsync(path);

    var json = await File.ReadAllTextAsync(path);
    var doc = JsonDocument.Parse(json); // Should not throw
    Assert.Equal(1, doc.RootElement.GetProperty("MessageCount").GetInt32());

    File.Delete(path);
}
```

### 15.8 Design Rules

1. **Never expose mutable collections** — all `Get*` methods return defensive copies.
2. **Always trim, never crash** — memory pressure is handled gracefully via auto-trim.
3. **System prompt is sacred** — trimming preserves the first System-role message.
4. **Export is non-destructive** — exporting does not modify the conversation.
5. **Disposal is final** — post-disposal access throws; callers must handle gracefully.
6. **Events are fire-and-forget** — handlers must not block the calling thread.

---

## 16. Pattern 11: Critique-Refine Quality Gates

### 16.1 Pattern Description

In complex multi-agent workflows, the output of one agent should be **adversarially reviewed**
by another before the orchestration proceeds. This pattern formalizes the relationship between
"proposer" agents and "critic" agents as a typed dependency in the execution plan.

This is **not** a simple sequential dependency ("B needs data from A"). It's a structured
quality gate: the critic's output determines whether the proposer's work is accepted, revised,
or escalated.

### 16.2 Dependency Type Taxonomy

```csharp
/// <summary>
/// Classifies the relationship between dependent work chunks.
/// Used by the DependencyScheduler to determine execution strategy.
/// </summary>
public enum ChunkDependencyType
{
    /// <summary>
    /// Standard data flow: Chunk B needs the output from Chunk A as input context.
    /// Scheduler: B executes after A completes. B receives A's output in its prompt.
    /// </summary>
    DataFlow,

    /// <summary>
    /// Quality gate: Chunk B reviews/critiques the output of Chunk A.
    /// Scheduler: B executes after A. B's prompt includes A's output + critique instructions.
    /// If B returns FAIL, the orchestrator may re-plan or abort.
    /// </summary>
    CritiqueGate,

    /// <summary>
    /// Refinement loop: Chunk C refines Chunk A's work based on Chunk B's critique.
    /// Scheduler: C executes after both A and B. C receives both outputs.
    /// This creates a Propose → Critique → Refine chain.
    /// </summary>
    RefinementFromCritique
}
```

### 16.3 Extended WorkChunk Model

```csharp
/// <summary>
/// Extended work chunk with critique-refine support.
/// Backward-compatible: existing chunks without critique fields work unchanged.
/// </summary>
public class WorkChunk
{
    // ── Existing fields (unchanged) ────────────────────────
    public string ChunkId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public AgentRole Role { get; set; } = AgentRole.GeneralPurpose;
    public List<string> DependsOnChunkIds { get; set; } = [];

    // ── NEW: Critique-Refine fields ────────────────────────

    /// <summary>
    /// Maps each dependency to its type. Keys must match DependsOnChunkIds entries.
    /// If a dependency is not in this dictionary, it defaults to DataFlow.
    /// </summary>
    public Dictionary<string, ChunkDependencyType> DependencyTypes { get; set; } = [];

    /// <summary>
    /// When true, this chunk acts as a quality gate. Its output MUST contain
    /// a structured verdict (PASS / NEEDS_REVISION / FAIL) parseable by the scheduler.
    /// </summary>
    public bool IsCritiqueChunk { get; set; }

    /// <summary>
    /// Optional: Override prompt template for critique chunks.
    /// If null, the default critique prompt for the assigned Role is used.
    /// </summary>
    public string? CritiquePromptOverride { get; set; }

    /// <summary>
    /// Maximum number of critique-refine iterations before force-accepting.
    /// Default 1 (one critique pass). Set to 0 to disable critique for this chunk.
    /// </summary>
    public int MaxCritiqueRounds { get; set; } = 1;

    // ── Helper methods ─────────────────────────────────────

    /// <summary>Returns the dependency type for a given chunk ID, defaulting to DataFlow.</summary>
    public ChunkDependencyType GetDependencyType(string dependencyChunkId)
        => DependencyTypes.TryGetValue(dependencyChunkId, out var type) ? type : ChunkDependencyType.DataFlow;
}
```

### 16.4 Critique-Refine Execution Flow

```
┌─────────────────────────────────────────────────────────────────┐
│ Stage 1: Proposal                                                │
│ ┌───────────────────────────────────┐                           │
│ │ Chunk: "design-api"               │                           │
│ │ Role: Architect                   │                           │
│ │ Output: API specification         │──────────┐                │
│ └───────────────────────────────────┘          │                │
│                                                 ▼                │
│ Stage 2: Critique                     ┌────────────────────────┐│
│ ┌───────────────────────────────────┐ │ Critique Prompt:       ││
│ │ Chunk: "critique-api-security"    │ │ "Review the API design ││
│ │ Role: SecurityAnalyst             │◄│  for auth, injection,  ││
│ │ IsCritiqueChunk: true             │ │  data validation gaps" ││
│ │ DependsOn: ["design-api"]         │ └────────────────────────┘│
│ │ DependencyType: CritiqueGate      │                           │
│ │                                   │                           │
│ │ Output: { "verdict": "NEEDS_REVISION",                        │
│ │           "issues": [...],                                     │
│ │           "severity": "HIGH" }    │──────────┐                │
│ └───────────────────────────────────┘          │                │
│                                                 ▼                │
│ Stage 3: Refinement                   ┌────────────────────────┐│
│ ┌───────────────────────────────────┐ │ Context injected:      ││
│ │ Chunk: "refine-api"               │ │ - Original design      ││
│ │ Role: Architect                   │◄│ - Critique findings    ││
│ │ DependsOn: ["design-api",         │ │ - Specific issues      ││
│ │             "critique-api-sec"]   │ └────────────────────────┘│
│ │ DependencyTypes:                  │                           │
│ │   design-api: DataFlow            │                           │
│ │   critique-api-sec: Refinement    │                           │
│ │                                   │                           │
│ │ Output: Revised API specification │                           │
│ └───────────────────────────────────┘                           │
└─────────────────────────────────────────────────────────────────┘
```

### 16.5 Critique Verdict Contract

```csharp
/// <summary>
/// Structured verdict from a critique chunk. The DependencyScheduler parses
/// the critic agent's output to extract this verdict.
///
/// The critique agent's output MUST contain a JSON block with these fields.
/// If parsing fails, the scheduler treats the critique as PASS (fail-open).
/// </summary>
public sealed record CritiqueVerdict
{
    /// <summary>Overall verdict: PASS, NEEDS_REVISION, or FAIL.</summary>
    public required CritiqueDecision Decision { get; init; }

    /// <summary>Specific issues found, ordered by severity.</summary>
    public IReadOnlyList<CritiqueIssue> Issues { get; init; } = [];

    /// <summary>Free-form rationale for the verdict.</summary>
    public string? Rationale { get; init; }

    /// <summary>
    /// Confidence score (0.0 to 1.0) in the verdict.
    /// Below 0.5 = low confidence → may warrant human review.
    /// </summary>
    public double Confidence { get; init; } = 1.0;
}

public enum CritiqueDecision
{
    /// <summary>Work meets quality bar. Proceed without changes.</summary>
    Pass,

    /// <summary>Work has issues but is salvageable. Refinement chunk should address them.</summary>
    NeedsRevision,

    /// <summary>Work is fundamentally flawed. Re-planning may be necessary.</summary>
    Fail
}

public sealed record CritiqueIssue
{
    public required string Description { get; init; }
    public required IssueSeverity Severity { get; init; }
    public string? SuggestedFix { get; init; }
}

public enum IssueSeverity { Low, Medium, High, Critical }
```

### 16.6 Scheduler Integration

```csharp
// In DependencyScheduler — enhanced stage building
public List<ExecutionStage> BuildSchedule(OrchestrationPlan plan)
{
    var stages = BuildTopologicalStages(plan); // Existing DAG sort

    // Post-process: inject critique context into dependent chunks
    foreach (var stage in stages)
    {
        foreach (var chunk in stage.Chunks)
        {
            foreach (var depId in chunk.DependsOnChunkIds)
            {
                var depType = chunk.GetDependencyType(depId);

                if (depType == ChunkDependencyType.CritiqueGate && chunk.IsCritiqueChunk)
                {
                    // Inject critique-specific prompt wrapper
                    chunk.Prompt = WrapWithCritiqueInstructions(chunk.Prompt, chunk.Role);
                }
                else if (depType == ChunkDependencyType.RefinementFromCritique)
                {
                    // Inject both original work AND critique findings
                    // (actual injection happens in InjectDependencyOutputs)
                }
            }
        }
    }

    return stages;
}

private static string WrapWithCritiqueInstructions(string originalPrompt, AgentRole role)
{
    return $"""
        {originalPrompt}

        ## Critique Output Format (REQUIRED)
        You MUST include a JSON block in your response with this structure:
        ```json
        {{
          "verdict": "PASS" | "NEEDS_REVISION" | "FAIL",
          "confidence": 0.0-1.0,
          "issues": [
            {{ "description": "...", "severity": "Low|Medium|High|Critical", "suggestedFix": "..." }}
          ],
          "rationale": "Brief explanation of your overall assessment"
        }}
        ```
        Be thorough but fair. Only mark FAIL for fundamental flaws.
        """;
}
```

### 16.7 Validation Rules

```csharp
/// <summary>
/// Validates critique-refine relationships in a plan. Called before execution.
/// </summary>
public static class CritiqueValidation
{
    public static ValidationResult Validate(OrchestrationPlan plan)
    {
        var errors = new List<string>();

        foreach (var chunk in plan.Chunks)
        {
            // Rule 1: Critique chunks must have at least one CritiqueGate dependency
            if (chunk.IsCritiqueChunk && !chunk.DependencyTypes.ContainsValue(ChunkDependencyType.CritiqueGate))
            {
                errors.Add($"Chunk '{chunk.ChunkId}' is marked as critique but has no CritiqueGate dependency.");
            }

            // Rule 2: CritiqueGate dependencies must target an existing chunk
            foreach (var (depId, depType) in chunk.DependencyTypes)
            {
                if (!plan.Chunks.Any(c => c.ChunkId == depId))
                {
                    errors.Add($"Chunk '{chunk.ChunkId}' has dependency on non-existent chunk '{depId}'.");
                }
            }

            // Rule 3: No circular critique chains (A critiques B which critiques A)
            if (chunk.IsCritiqueChunk)
            {
                var critiqued = chunk.DependsOnChunkIds;
                foreach (var targetId in critiqued)
                {
                    var target = plan.Chunks.FirstOrDefault(c => c.ChunkId == targetId);
                    if (target?.IsCritiqueChunk == true && target.DependsOnChunkIds.Contains(chunk.ChunkId))
                    {
                        errors.Add($"Circular critique: '{chunk.ChunkId}' ↔ '{targetId}'.");
                    }
                }
            }

            // Rule 4: MaxCritiqueRounds must be bounded
            if (chunk.MaxCritiqueRounds > 3)
            {
                errors.Add($"Chunk '{chunk.ChunkId}' has MaxCritiqueRounds={chunk.MaxCritiqueRounds} (max allowed: 3).");
            }
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }
}
```

### 16.8 Example Plan with Critique-Refine

```json
{
  "planId": "plan-secure-api-001",
  "planSummary": "Design secure REST API with adversarial security review",
  "chunks": [
    {
      "chunkId": "design-api",
      "title": "Design REST API",
      "role": "Architect",
      "prompt": "Design a REST API for user management with CRUD operations...",
      "dependsOnChunkIds": [],
      "isCritiqueChunk": false
    },
    {
      "chunkId": "security-review",
      "title": "Security Review",
      "role": "SecurityAnalyst",
      "prompt": "Review the API design for security vulnerabilities...",
      "dependsOnChunkIds": ["design-api"],
      "dependencyTypes": { "design-api": "CritiqueGate" },
      "isCritiqueChunk": true,
      "maxCritiqueRounds": 1
    },
    {
      "chunkId": "refine-api",
      "title": "Refine API Design",
      "role": "Architect",
      "prompt": "Address all security findings and produce revised API design...",
      "dependsOnChunkIds": ["design-api", "security-review"],
      "dependencyTypes": {
        "design-api": "DataFlow",
        "security-review": "RefinementFromCritique"
      },
      "isCritiqueChunk": false
    },
    {
      "chunkId": "implement-api",
      "title": "Implement API",
      "role": "Developer",
      "prompt": "Implement the refined API design...",
      "dependsOnChunkIds": ["refine-api"],
      "dependencyTypes": { "refine-api": "DataFlow" },
      "isCritiqueChunk": false
    }
  ]
}
```

### 16.9 Design Rules

1. **Critique is opt-in** — existing plans without critique fields work unchanged (backward compatible).
2. **Fail-open on parse failure** — if critique output can't be parsed, treat as PASS and log warning.
3. **Max 3 critique rounds** — prevents infinite loops. After max rounds, force-accept with warning.
4. **No circular critiques** — validation rejects mutual critique relationships.
5. **Critic role is distinct from reviewer** — Critics are adversarial by design; CodeReviewers are constructive.
6. **Verdicts are structured** — JSON format enables automated processing, not just human reading.
7. **Low-confidence critiques flag for human review** — confidence < 0.5 triggers optional user notification.

---

## 17. Pattern 12: Moderator-Based Dynamic Orchestration

### 17.1 Pattern Description

The default execution strategy (DAG-based dependency scheduling) is **static**: the full execution
order is determined at planning time. For exploratory or open-ended tasks, a **dynamic** strategy
allows the orchestrator LLM to select the next work unit based on accumulated results.

This pattern introduces a **pluggable work selection strategy** that enables both approaches.

### 17.2 Strategy Interface

```csharp
/// <summary>
/// Selects the next work chunk(s) to execute from the remaining plan.
///
/// Two implementations:
///   1. DependencyBasedSelector — static DAG (default, deterministic, fast)
///   2. ModeratorBasedSelector — LLM-driven (adaptive, exploratory, slower)
///
/// SELECTION RULES:
///   - Must never return an already-completed chunk.
///   - Must respect dependency constraints (no chunk before its dependencies).
///   - Must return empty list when all chunks are complete.
///   - Must be cancellation-aware.
/// </summary>
public interface IWorkChunkSelector
{
    /// <summary>
    /// Returns the next batch of chunks eligible for parallel execution.
    /// Empty list = all work complete (or no eligible chunks remain).
    /// </summary>
    Task<IReadOnlyList<WorkChunk>> SelectNextBatchAsync(
        OrchestrationPlan plan,
        IReadOnlyList<AgentResult> completedResults,
        CancellationToken ct = default);

    /// <summary>Human-readable strategy name for logging and UI display.</summary>
    string StrategyName { get; }
}
```

### 17.3 Static Strategy (Current — Default)

```csharp
/// <summary>
/// Deterministic DAG-based scheduling. Builds all execution stages upfront
/// via topological sort and dispatches stage-by-stage.
///
/// Properties:
///   - Deterministic: same plan always produces same execution order.
///   - Fast: O(V+E) topological sort, no LLM calls during execution.
///   - Predictable: user can see full execution order before starting.
///
/// When to use:
///   - Well-defined tasks with clear dependencies.
///   - Cost-sensitive scenarios (no extra LLM calls).
///   - When user wants full execution plan visibility upfront.
/// </summary>
public sealed class DependencyBasedSelector : IWorkChunkSelector
{
    private readonly IDependencyScheduler _scheduler;
    private List<ExecutionStage>? _stages;
    private int _currentStageIndex;

    public string StrategyName => "Static (DAG)";

    public Task<IReadOnlyList<WorkChunk>> SelectNextBatchAsync(
        OrchestrationPlan plan,
        IReadOnlyList<AgentResult> completedResults,
        CancellationToken ct = default)
    {
        // Build stages once, then advance through them
        _stages ??= _scheduler.BuildSchedule(plan);

        if (_currentStageIndex >= _stages.Count)
            return Task.FromResult<IReadOnlyList<WorkChunk>>([]);

        var stage = _stages[_currentStageIndex++];
        return Task.FromResult<IReadOnlyList<WorkChunk>>(stage.Chunks);
    }
}
```

### 17.4 Dynamic Strategy (Moderator-Driven)

```csharp
/// <summary>
/// LLM-driven dynamic work selection. After each batch completes, asks the
/// orchestrator LLM which chunks to execute next based on accumulated results.
///
/// Properties:
///   - Adaptive: can skip low-value work, reprioritize based on findings.
///   - Exploratory: ideal for research, investigation, or loosely defined tasks.
///   - Slower: adds one LLM call per selection round.
///   - Less predictable: execution order depends on LLM decisions.
///
/// When to use:
///   - Research or investigation tasks where findings drive next steps.
///   - Tasks where early results may invalidate later planned work.
///   - When adaptability is more valuable than predictability.
///
/// When NOT to use:
///   - Well-defined implementation tasks with clear dependencies.
///   - Cost-sensitive scenarios (adds ~1 LLM call per execution round).
///   - When the user needs full execution plan visibility upfront.
/// </summary>
public sealed class ModeratorBasedSelector : IWorkChunkSelector
{
    private readonly Func<string, CancellationToken, Task<string>> _sendToLlm;
    private readonly ILogger<ModeratorBasedSelector> _logger;

    public string StrategyName => "Dynamic (Moderator)";

    public async Task<IReadOnlyList<WorkChunk>> SelectNextBatchAsync(
        OrchestrationPlan plan,
        IReadOnlyList<AgentResult> completedResults,
        CancellationToken ct = default)
    {
        var completedIds = new HashSet<string>(completedResults.Select(r => r.ChunkId));
        var remaining = plan.Chunks.Where(c => !completedIds.Contains(c.ChunkId)).ToList();

        if (remaining.Count == 0)
            return [];

        // Filter to dependency-satisfied chunks only
        var eligible = remaining.Where(c =>
            c.DependsOnChunkIds.All(d => completedIds.Contains(d))).ToList();

        if (eligible.Count == 0)
        {
            _logger.LogWarning("No eligible chunks despite {Remaining} remaining — possible dead dependency",
                remaining.Count);
            return [];
        }

        // If only one eligible, no need to ask LLM
        if (eligible.Count == 1)
            return eligible;

        // Ask orchestrator LLM which chunks to execute next
        var prompt = BuildSelectionPrompt(eligible, completedResults);
        var response = await _sendToLlm(prompt, ct).ConfigureAwait(false);
        var selectedIds = ParseSelectionResponse(response, eligible);

        // Fallback: if LLM response is unparseable, execute all eligible
        if (selectedIds.Count == 0)
        {
            _logger.LogWarning("Moderator selection returned no valid chunks — executing all eligible");
            return eligible;
        }

        return eligible.Where(c => selectedIds.Contains(c.ChunkId)).ToList();
    }

    private static string BuildSelectionPrompt(
        IReadOnlyList<WorkChunk> eligible, IReadOnlyList<AgentResult> completed)
    {
        var completedSummary = string.Join("\n", completed.Select(r =>
            $"- [{r.ChunkId}] ({(r.IsSuccess ? "✅" : "❌")}): {Truncate(r.Response ?? "", 100)}"));

        var eligibleList = string.Join("\n", eligible.Select(c =>
            $"- [{c.ChunkId}] Role: {c.Role}, Title: {c.Title}"));

        return $"""
            You are the orchestration moderator. Based on completed work, select which
            chunks should execute next for maximum value.

            ## Completed Work
            {completedSummary}

            ## Eligible Chunks (dependencies satisfied)
            {eligibleList}

            ## Instructions
            Select 1-{eligible.Count} chunks to execute in the next parallel batch.
            Consider: which chunks provide the most value given current findings?
            Skip chunks that completed work has made unnecessary.

            Respond with a JSON array of chunk IDs:
            ["chunk-id-1", "chunk-id-2"]
            """;
    }

    private List<string> ParseSelectionResponse(string response, IReadOnlyList<WorkChunk> eligible)
    {
        try
        {
            var json = ExtractJsonArray(response);
            if (json is null) return [];

            var ids = JsonSerializer.Deserialize<List<string>>(json);
            var validIds = new HashSet<string>(eligible.Select(c => c.ChunkId));

            return ids?.Where(id => validIds.Contains(id)).ToList() ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse moderator selection response");
            return [];
        }
    }

    private static string? ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}
```

### 17.5 Strategy Selection Setting

```csharp
/// <summary>
/// Controls how the orchestrator selects work chunks during execution.
/// </summary>
public enum WorkflowStrategy
{
    /// <summary>
    /// Static DAG-based scheduling. Default. Deterministic, fast, predictable.
    /// Best for: well-defined tasks, cost-sensitive users, implementation work.
    /// </summary>
    Static,

    /// <summary>
    /// LLM-driven dynamic selection. Adaptive, exploratory, adds LLM call overhead.
    /// Best for: research tasks, investigation, loosely defined objectives.
    /// </summary>
    Dynamic
}

// In MultiAgentSettings
public class MultiAgentSettings
{
    // ... existing properties ...

    /// <summary>
    /// Workflow execution strategy. Default: Static (DAG-based).
    /// </summary>
    public string WorkflowStrategy { get; set; } = "Static";
}
```

### 17.6 Decision Matrix

| Criterion | Static (DAG) | Dynamic (Moderator) |
|-----------|-------------|---------------------|
| **Determinism** | ✅ Same plan → same order | ⚠️ Order depends on LLM decisions |
| **Speed** | ✅ No LLM overhead | ⚠️ +1 LLM call per round |
| **Cost** | ✅ Zero extra LLM calls | ⚠️ N additional LLM calls (N = execution rounds) |
| **Adaptability** | ❌ Fixed execution order | ✅ Can skip/reprioritize based on findings |
| **Visibility** | ✅ Full plan visible upfront | ⚠️ Next batch revealed one round at a time |
| **Best for** | Implementation, well-defined tasks | Research, investigation, exploration |
| **Failure mode** | Predictable (dependency errors) | LLM may hallucinate chunk IDs (handled via fallback) |

### 17.7 Design Rules

1. **Static is the default** — dynamic requires explicit user opt-in via settings.
2. **Dynamic has a fallback** — if LLM selection fails, execute all eligible chunks (never deadlock).
3. **Both strategies respect dependencies** — dynamic cannot execute chunks with unsatisfied dependencies.
4. **Strategy is set per task** — cannot change mid-execution (would invalidate state).
5. **Moderator prompts are lean** — minimize token usage to control cost overhead.
6. **Log every selection decision** — for debugging and cost tracking.

---

# Part III — Production Readiness Standards

> These standards apply to **every feature** built on the patterns above.
> They are non-negotiable for an application serving millions of users worldwide.

---

## 18. Scalability & Performance Guidelines

### 18.1 Memory Management Rules

| Rule | Implementation | Rationale |
|------|---------------|-----------|
| **Cap all collections** | `ObservableCollection` capped at 500 items; conversation history at 1000 | Prevents unbounded growth in long-running sessions |
| **Dispose worker sessions in `finally`** | `try { ... } finally { TerminateSessionProcess(id); }` | Prevents session leaks on error paths |
| **Use `IDisposable` on ViewModels** | Unsubscribe events, stop timers, cancel tokens | Prevents event handler leaks across tab switches |
| **Avoid closure captures in hot paths** | Pre-allocate delegates for frequently-invoked callbacks | Reduces GC pressure from captured closures |
| **Return `IReadOnlyList<T>` not `List<T>`** | All public APIs return immutable views | Prevents consumer mutation of internal state |

### 18.2 Async Best Practices

```csharp
// ✅ CORRECT: ConfigureAwait(false) in library/service code
var result = await _copilotService.SendMessageAsync(session, prompt, ct)
    .ConfigureAwait(false);

// ✅ CORRECT: Linked cancellation tokens for timeout + user cancellation
using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
timeoutCts.CancelAfter(timeout);

// ✅ CORRECT: ThrowIfCancellationRequested at every async boundary
ct.ThrowIfCancellationRequested();
var response = await _llm.SendAsync(prompt, ct).ConfigureAwait(false);
ct.ThrowIfCancellationRequested();

// ❌ WRONG: Fire-and-forget without error observation
Task.Run(() => DoWorkAsync()); // Exception silently swallowed!

// ✅ CORRECT: Observe faults
var task = Task.Run(() => DoWorkAsync(ct));
task.ContinueWith(t => _logger.LogError(t.Exception, "Background task failed"),
    TaskContinuationOptions.OnlyOnFaulted);
```

### 18.3 Event Throttling Strategy

When events fire faster than the UI can render (e.g., streaming LLM tokens):

```csharp
// Throttle rapid events to avoid UI thread starvation
private DateTime _lastEventRender = DateTime.MinValue;
private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMilliseconds(50); // 20 FPS max

private void OnHighFrequencyEvent(object? sender, StreamingEvent e)
{
    var now = DateTime.UtcNow;
    if (now - _lastEventRender < ThrottleInterval)
        return; // Skip this frame
    
    _lastEventRender = now;
    _dispatcher.InvokeAsync(() => UpdateStreamingDisplay(e.Delta));
}
```

### 18.4 Concurrency Limits

| Resource | Limit | Rationale |
|----------|-------|-----------|
| Parallel worker sessions | Configurable, default 3, max 10 | LLM API rate limits + memory |
| Approval queue concurrency | 1 (SemaphoreSlim) | Prevent dialog storms |
| Settings file access | 1 (SemaphoreSlim) | Prevent concurrent write corruption |
| Event log entries | 500 | UI rendering performance |
| Conversation history | 1000 messages | Memory + context window limits |

---

## 19. Error Recovery Playbook

### 19.1 Error Classification & Recovery Matrix

| Error Class | Examples | Detection | Recovery | User Communication |
|-------------|----------|-----------|----------|-------------------|
| **Transient** | Network timeout, rate limit, 503 | Specific exception types | Retry with exponential backoff (max 3 attempts) | "Retrying... (attempt 2/3)" |
| **Connection Lost** | Session disconnect, pipe broken | `IsConnectionLossException()` | Recreate session, replay last prompt | "Reconnecting to Copilot..." |
| **LLM Parse Failure** | Unparseable JSON, missing fields | `JsonException`, null checks | Retry with clarification prompt; fallback to raw text | Transparent (handled internally) |
| **Worker Failure** | Worker timeout, crash, bad output | `AgentResult.IsSuccess == false` | Retry per `RetryPolicy`; if exhausted, continue with partial results | "Worker [name] failed — continuing with partial results" |
| **Fatal** | Out of memory, disk full, app crash | Unhandled exception at top level | Transition to Error state, log full stack, persist state for recovery | "An unexpected error occurred. Please reset and try again." |
| **User Cancellation** | Cancel button, tab close | `OperationCanceledException` | Clean shutdown: cancel tokens, dispose sessions, preserve state | "Cancelled. Your progress has been saved." |

### 19.2 Retry Policy

```csharp
/// <summary>
/// Retry policy applied to worker execution and LLM calls.
/// Uses exponential backoff with jitter to prevent thundering herd.
/// </summary>
public sealed record RetryPolicy
{
    /// <summary>Maximum number of retry attempts. 0 = no retries.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Base delay between retries. Actual delay = base * 2^attempt + jitter.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum delay cap to prevent excessively long waits.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Calculates delay for a given attempt number.
    /// Includes random jitter (±25%) to prevent synchronized retries.
    /// </summary>
    public TimeSpan GetDelay(int attempt)
    {
        var exponential = BaseDelay * Math.Pow(2, attempt);
        var capped = TimeSpan.FromTicks(Math.Min(exponential.Ticks, MaxDelay.Ticks));
        var jitter = capped * (0.75 + Random.Shared.NextDouble() * 0.5); // ±25%
        return jitter;
    }
}
```

### 19.3 Circuit Breaker for LLM Calls

```
State: CLOSED (normal operation)
    │
    ├── Success → stay CLOSED, reset failure counter
    ├── Failure → increment counter
    │   └── Counter >= 5 → transition to OPEN
    │
State: OPEN (reject all calls immediately)
    │
    ├── Wait 60 seconds → transition to HALF-OPEN
    │
State: HALF-OPEN (allow one probe call)
    │
    ├── Success → transition to CLOSED, reset counter
    └── Failure → transition to OPEN, restart timer
```

**Implementation Note**: The circuit breaker is implemented per-service (orchestrator, office manager),
not globally. A failure in Agent Office does not affect Agent Team.

---

## 20. Debugging & Observability Standards

### 20.1 Structured Logging Requirements

Every log message MUST include:

```csharp
// ✅ Required fields for production debugging
_logger.LogInformation(
    "[{Component}] {Action}: {Detail}. " +
    "SessionId={SessionId}, Phase={Phase}, CorrelationId={CorrelationId}",
    "OrchestratorService", "PhaseTransition", "Planning → Executing",
    _sessionId, _currentPhase, correlationId);

// ❌ NEVER log like this — unstructured, unfilterable
_logger.LogInformation("Phase changed to executing");
```

### 20.2 Log Level Guidelines

| Level | Usage | Examples |
|-------|-------|---------|
| `Trace` | Internal state dumps (disabled in production) | Full LLM prompt text, raw JSON responses |
| `Debug` | Detailed operation flow | "Sending evaluation prompt", "Worker created" |
| `Information` | Significant lifecycle events | Phase transitions, task submission, completion |
| `Warning` | Recoverable issues | Parse failures, retry attempts, timeout reached |
| `Error` | Unrecoverable issues requiring attention | Session lost, disk full, unhandled exception |
| `Critical` | Application-wide failures | Iteration loop crash, DI failure, startup error |

### 20.3 Correlation ID Propagation

```
User Action (Submit Task)
    │ correlationId = "task-{guid}"
    │
    ├── OrchestratorService.SubmitTaskAsync     [correlationId logged]
    │   ├── SendToOrchestratorLlmAsync          [correlationId logged]
    │   ├── PlanTaskAsync                       [correlationId logged]
    │   │   └── TaskDecomposer.DecomposeAsync   [correlationId logged]
    │   └── ExecutePlanAsync                    [correlationId logged]
    │       ├── AgentPool.DispatchBatchAsync     [correlationId logged]
    │       │   ├── Worker #1                   [correlationId in context]
    │       │   ├── Worker #2                   [correlationId in context]
    │       │   └── Worker #3                   [correlationId in context]
    │       └── ResultAggregator.AggregateAsync [correlationId logged]
    │
    └── All events emitted carry same correlationId
        → UI can trace entire operation from one ID
```

### 20.4 Telemetry Points

| Point | Metric | Purpose |
|-------|--------|---------|
| `task.submitted` | Counter | Track usage volume |
| `task.completed` | Counter + Duration | Track success rate and latency |
| `worker.dispatched` | Counter | Track worker pool utilization |
| `worker.duration` | Histogram | Identify slow workers |
| `llm.call.duration` | Histogram | Track LLM response times |
| `llm.call.tokens` | Counter | Track cost (tokens consumed) |
| `session.created` | Counter | Track session lifecycle |
| `session.terminated` | Counter | Detect session leaks (created - terminated ≠ 0) |
| `error.count` | Counter by error class | Alert on error rate spikes |
| `approval.queue.depth` | Gauge | Detect approval bottlenecks |

---

## 21. Cost & Resource Management

### 21.1 LLM Call Cost Awareness

Every LLM call has a cost. The architecture must track and minimize unnecessary calls.

| Call Type | Frequency | Cost Impact | Optimization |
|-----------|-----------|-------------|-------------|
| Evaluation (clarify/proceed) | 1 per task | Low | Cache evaluation for identical prompts |
| Plan decomposition | 1 per task | Medium | Reuse plan on re-execute |
| Worker execution | N per task | **High** | Minimize prompt size, use cheaper models for simple chunks |
| Aggregation | 1 per task | Medium | Limit result context to summaries, not full outputs |
| Moderator selection (dynamic) | N per execution round | Medium | Use only when Static strategy is insufficient |
| Critique | 1 per critique chunk | Medium | Gate critique behind user opt-in |

### 21.2 Model Tier Strategy

```
┌─────────────────────────────────────────────────────────────┐
│ Tier 1: Premium Model (e.g., gpt-4o, claude-3-opus)         │
│ Used for: Orchestrator, Architect role, Synthesizer role     │
│ Rationale: These roles require deep reasoning                │
├─────────────────────────────────────────────────────────────┤
│ Tier 2: Standard Model (e.g., gpt-4o-mini, claude-3-sonnet)│
│ Used for: Developer, QA, Researcher, most worker roles       │
│ Rationale: Good quality at lower cost for execution tasks    │
├─────────────────────────────────────────────────────────────┤
│ Tier 3: Fast Model (e.g., gpt-4o-mini)                     │
│ Used for: Moderator selection, simple parsing, classification│
│ Rationale: Speed and cost matter more than deep reasoning    │
└─────────────────────────────────────────────────────────────┘
```

### 21.3 Resource Budget Rules

1. **Worker sessions are bounded** — never exceed `MaxParallelSessions` (default 3).
2. **Conversation history is bounded** — auto-trim at 1000 messages.
3. **Event logs are bounded** — cap at 500 entries in UI, archive overflow to disk.
4. **LLM prompts are bounded** — truncate context to fit model's context window.
5. **Critique rounds are bounded** — max 3 rounds per chunk, then force-accept.
6. **REST periods are mandatory** (Office) — prevents runaway iteration loops.

---

## 22. Production Readiness Checklist

### 22.1 Before Shipping a New Feature

| # | Check | Owner | Status |
|---|-------|-------|--------|
| 1 | State machine enum defined with all lifecycle phases | Engineer | ☐ |
| 2 | `TransitionTo()` emits typed event on every transition | Engineer | ☐ |
| 3 | Guard clauses prevent invalid phase transitions | Engineer | ☐ |
| 4 | All async methods accept and respect `CancellationToken` | Engineer | ☐ |
| 5 | Worker sessions disposed in `finally` blocks | Engineer | ☐ |
| 6 | Manager session reuse with `HasActiveSession()` health check | Engineer | ☐ |
| 7 | Settings snapshot record defined with all fields | Engineer | ☐ |
| 8 | Dirty tracking wired: `OnXxxChanged → RecalculateDirtyState` | Engineer | ☐ |
| 9 | Apply/Discard commands gated by `HasPendingChanges` | Engineer | ☐ |
| 10 | `SettingsRequireRestart` flag set when applying during active session | Engineer | ☐ |
| 11 | All event handlers wrapped in `Dispatcher.InvokeAsync` | Engineer | ☐ |
| 12 | Event subscriptions unsubscribed in `Dispose()` | Engineer | ☐ |
| 13 | Collections capped (`EventLog` ≤ 500, `History` ≤ 1000) | Engineer | ☐ |
| 14 | Structured logging with `[ClassName]` prefix on all messages | Engineer | ☐ |
| 15 | Error recovery: retry policy + graceful degradation | Engineer | ☐ |
| 16 | LLM response parsing: JSON extraction with fallback | Engineer | ☐ |
| 17 | Side panel with slide animation (300ms in, 250ms out) | Engineer | ☐ |
| 18 | Session health polling timer (configurable interval) | Engineer | ☐ |
| 19 | Tool approval integration via `IApprovalQueue` | Engineer | ☐ |
| 20 | Agent roles use domain-expertise names from `AgentRole` enum | Engineer | ☐ |

### 22.2 Before Shipping to Production

| # | Check | Owner | Status |
|---|-------|-------|--------|
| 1 | Memory profile: create/destroy 50 sessions → flat memory graph | QA | ☐ |
| 2 | Stress test: 10 concurrent worker sessions for 30 minutes | QA | ☐ |
| 3 | Network disconnect: verify reconnection and state recovery | QA | ☐ |
| 4 | Tab switching: verify no event handler leaks across tab changes | QA | ☐ |
| 5 | Long-running session: 8-hour continuous operation (Office) | QA | ☐ |
| 6 | Cancellation: verify all tokens respected, no orphaned tasks | QA | ☐ |
| 7 | UI responsiveness: 60 FPS during worker execution | QA | ☐ |
| 8 | Settings persistence: verify round-trip (save → close → open → load) | QA | ☐ |
| 9 | Error messages: no stack traces shown to users | QA | ☐ |
| 10 | Logging: verify correlation IDs propagate through full operation | QA | ☐ |

---

## 23. Future Architecture Roadmap

### 23.1 Potential Base Class Extraction

Both ViewModels share significant duplicated code. A future refactoring could extract:

```csharp
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

### 23.2 Shared Event Infrastructure

Both event hierarchies (`OrchestratorEvent`, `OfficeEvent`) have similar shapes. A shared base:

```csharp
public abstract class AgentEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Message { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
}
```

### 23.3 Extension Points

The current architecture supports these future additions without structural changes:

- **New multi-agent features**: Follow the patterns in Parts I and II
- **Cloud scalability**: Service interfaces abstract away local vs. remote execution
- **Persistent event logs**: `IOfficeEventLog` / `ITaskLogStore` backed by database
- **Settings sync**: Persistence layer extended to sync across machines
- **Feature composition**: A "super-orchestrator" composing Agent Team and Agent Office
- **Agent marketplace**: User-defined role configurations loaded from shared repository
- **Conversation replay**: Load exported conversations to replay or analyze past sessions
- **A/B testing**: Strategy selection (Static vs Dynamic) measured by success rate metrics
- **Multi-model routing**: Per-role model assignment (premium for Architect, standard for Developer)

### 23.4 Technology Watch

| Technology | Relevance | Adoption Criteria |
|------------|-----------|-------------------|
| **Semantic Kernel** | AI orchestration framework | Adopt if we need: memory management, plugin ecosystem, multi-provider support |
| **System.Reactive (Rx.NET)** | Reactive event streams | Adopt if event volume exceeds Dispatcher.InvokeAsync capacity |
| **Stateless library** | Formal state machine | Adopt if state machines grow beyond 15 states with complex guard conditions |
| **gRPC / SignalR** | Remote agent execution | Adopt when cloud-hosted agent workers are needed |
| **SQLite / LiteDB** | Local persistence | Adopt when JSON file persistence hits performance limits (>100MB settings) |

---

*End of Shared Architecture Patterns Document — Version 2.0*

*This document is the canonical architectural reference for CopilotDesktop.*  
*Every pattern documented here has been designed for an application serving millions of users.*  
*No shortcuts. No quick fixes. Clean, extensible, debuggable, maintainable.*
