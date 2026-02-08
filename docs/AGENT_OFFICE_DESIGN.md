# Agent Office — Comprehensive Design Document

> **Version**: 1.1  
> **Status**: Draft  
> **Project**: CopilotAgent.Office  
> **Date**: February 2026

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Architecture Overview](#2-architecture-overview)
3. [Use Cases & Examples](#3-use-cases--examples)
4. [High-Level Design (HLD)](#4-high-level-design-hld)
5. [Low-Level Design (LLD)](#5-low-level-design-lld)
6. [Technical Design](#6-technical-design)
7. [UI Design](#7-ui-design)
8. [Code Flow](#8-code-flow)
9. [Plan of Action — Phased Implementation](#9-plan-of-action--phased-implementation)
10. [Appendix](#10-appendix)

---

## 1. Executive Summary

### 1.1 Vision

Agent Office introduces a **long-running, autonomous Manager-Assistant pattern** to CopilotDesktop. Unlike the existing Agent Teams (one-shot task decomposition), Agent Office models a **perpetual office** where:

- A **Manager** sits in a continuous loop, periodically checking for events/incidents.
- When events arrive, the Manager decomposes work and **delegates to a finite pool of Assistants**.
- Each Assistant **independently completes** its task, **reports back**, and is **disposed**.
- The Manager **aggregates results**, produces a **comprehensive report**, takes a **configurable rest**, and repeats.

This is the "always-on operations center" pattern — ideal for incident management, monitoring, scheduled audits, and any workflow requiring periodic autonomous action.

### 1.2 Key Differentiators from Agent Teams

| Aspect | Agent Teams | Agent Office |
|--------|-------------|--------------|
| Lifecycle | One-shot: submit → plan → execute → done | Continuous loop: check → delegate → report → rest → repeat |
| Manager Session | Created per task, disposed after | Long-running, persists across iterations |
| Iteration | Single execution | Periodic with configurable interval |
| Task Source | User provides full task upfront | Manager discovers tasks from events/data sources |
| Assistant Lifecycle | Workers live for batch duration | Assistants are truly ephemeral: spawn → work → report → dispose |
| Scheduling | All chunks dispatched at once (parallel) | Queue-based: if tasks > pool size, pending tasks wait |
| User Interaction | Approval before execution | Ongoing: change prompt, interval, pause/resume mid-run |
| Rest Period | None | Configurable countdown with UI visualization |

### 1.3 Design Principles

1. **Production-Grade**: Clean, extensible, debuggable code. No shortcuts.
2. **MVVM Strict**: All UI state flows through ViewModels. Zero code-behind logic.
3. **Interface-First**: Every service has an interface. DI everywhere.
4. **Event-Driven**: All communication via typed events. No direct coupling.
5. **Graceful Lifecycle**: Every session, timer, and task respects CancellationToken.
6. **Observability**: Every Manager decision is logged. Every phase transition is an event.
7. **Reuse Over Rewrite**: Leverage existing `ICopilotService`, `Session` model, event patterns.

---

## 2. Architecture Overview

### 2.1 Project Structure

```
src/
├── CopilotAgent.Office/                    # NEW PROJECT
│   ├── CopilotAgent.Office.csproj
│   ├── Models/
│   │   ├── OfficeConfig.cs                 # Configuration for the office
│   │   ├── ManagerPhase.cs                 # State machine phases
│   │   ├── ManagerContext.cs               # Manager's accumulated context
│   │   ├── IterationReport.cs             # Per-iteration aggregated report
│   │   ├── AssistantTask.cs               # Unit of work for an assistant
│   │   ├── AssistantResult.cs             # Completion result from assistant
│   │   ├── AssistantStatus.cs             # Enum: Idle, Working, Completed, Failed
│   │   ├── SchedulingDecision.cs          # Log entry for scheduling decisions
│   │   ├── OfficeChatMessage.cs           # Chat message for Office UI
│   │   └── OfficeColorScheme.cs           # Color coding for Manager vs Assistants
│   ├── Events/
│   │   ├── OfficeEvent.cs                 # Base event + typed hierarchy
│   │   └── OfficeEventType.cs             # Enum of all event types
│   └── Services/
│       ├── IOfficeManagerService.cs        # Core manager interface
│       ├── OfficeManagerService.cs         # Manager state machine + loop
│       ├── IAssistantPool.cs              # Pool management interface
│       ├── AssistantPool.cs               # Finite pool with queue
│       ├── IAssistantAgent.cs             # Single assistant interface
│       ├── AssistantAgent.cs              # Ephemeral worker implementation
│       ├── IIterationScheduler.cs         # Rest period + timer management
│       ├── IterationScheduler.cs          # Countdown + next-run scheduling
│       ├── IOfficeEventLog.cs             # Structured event log interface
│       └── OfficeEventLog.cs              # In-memory + persistence log
│
├── CopilotAgent.Core/
│   └── Models/
│       └── OfficeSettings.cs              # NEW: Settings model for Office tab
│
├── CopilotAgent.App/
│   ├── ViewModels/
│   │   ├── OfficeViewModel.cs             # NEW: Main ViewModel for Office tab
│   │   ├── OfficeConfigDialogViewModel.cs # NEW: Configuration dialog VM
│   │   └── MainWindowViewModel.cs         # MODIFIED: Add ShowOffice toggle
│   ├── Views/
│   │   ├── OfficeView.xaml                # NEW: Main Office tab view
│   │   ├── OfficeView.xaml.cs
│   │   ├── OfficeConfigDialog.xaml        # NEW: Configuration dialog
│   │   └── OfficeConfigDialog.xaml.cs
│   ├── MainWindow.xaml                    # MODIFIED: Add Office tab button + view
│   └── App.xaml.cs                        # MODIFIED: Register Office services
```

### 2.2 Dependency Graph

```
CopilotAgent.App
    ├── CopilotAgent.Office        (NEW)
    ├── CopilotAgent.Core          (existing)
    ├── CopilotAgent.MultiAgent    (existing, no changes)
    └── CopilotAgent.Persistence   (existing)

CopilotAgent.Office
    └── CopilotAgent.Core          (models, ICopilotService, Session)
```

### 2.3 Component Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                         CopilotAgent.App                            │
│                                                                     │
│  ┌──────────────┐    ┌──────────────────┐    ┌───────────────────┐ │
│  │ MainWindow   │    │ OfficeView.xaml   │    │ OfficeConfigDialog│ │
│  │ (Office Tab) │───▶│ (Rich Chat UI)   │    │ (Settings)        │ │
│  └──────────────┘    └────────┬─────────┘    └───────────────────┘ │
│                               │                                     │
│                    ┌──────────▼──────────┐                         │
│                    │  OfficeViewModel    │                          │
│                    │  (MVVM Binding Hub) │                          │
│                    └──────────┬──────────┘                         │
└───────────────────────────────┼─────────────────────────────────────┘
                                │
┌───────────────────────────────┼─────────────────────────────────────┐
│                    CopilotAgent.Office                               │
│                               │                                     │
│  ┌────────────────────────────▼────────────────────────────────┐   │
│  │              IOfficeManagerService                           │   │
│  │  ┌─────────────────────────────────────────────────────┐    │   │
│  │  │            OfficeManagerService                      │    │   │
│  │  │                                                      │    │   │
│  │  │  ┌──────────────┐  ┌──────────────┐  ┌───────────┐ │    │   │
│  │  │  │ Manager      │  │ Iteration    │  │ Office    │ │    │   │
│  │  │  │ State Machine│  │ Scheduler    │  │ Event Log │ │    │   │
│  │  │  └──────┬───────┘  └──────────────┘  └───────────┘ │    │   │
│  │  │         │                                            │    │   │
│  │  │  ┌──────▼───────────────────────────────────────┐   │    │   │
│  │  │  │           IAssistantPool                      │   │    │   │
│  │  │  │  ┌─────────┐ ┌─────────┐ ┌─────────┐       │   │    │   │
│  │  │  │  │Asst #1  │ │Asst #2  │ │Asst #3  │ ...   │   │    │   │
│  │  │  │  │(Session)│ │(Session)│ │(Session)│       │   │    │   │
│  │  │  │  └─────────┘ └─────────┘ └─────────┘       │   │    │   │
│  │  │  │  ┌──────────────────────────────────┐       │   │    │   │
│  │  │  │  │  Task Queue (overflow)            │       │   │    │   │
│  │  │  │  └──────────────────────────────────┘       │   │    │   │
│  │  │  └──────────────────────────────────────────────┘   │    │   │
│  │  └──────────────────────────────────────────────────────┘    │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                     │
└───────────────────────────────┼─────────────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────────────┐
│                    CopilotAgent.Core                                 │
│                                                                     │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────────┐  │
│  │ICopilotService│  │Session Model │  │ MCP / Skills / Playbooks│  │
│  └──────────────┘  └──────────────┘  └──────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 3. Use Cases & Examples

### 3.1 Primary Use Case: Incident Management

**User Prompt**:
> "Analyze open incidents belonging to Team Alpha every 5 minutes. For each P1/P2 incident, check the runbook, attempt automated remediation via MCP tools, and escalate if unresolved. For P3/P4, add a triage note."

**Manager Behavior**:
1. **Clarification Phase**: Manager asks: "Which incident source should I query? (ServiceNow MCP, Azure DevOps MCP, or custom API?) What constitutes 'resolved' — status change or acknowledgment?"
2. **Planning Phase**: Manager builds plan:
   - Event source: `servicenow-mcp` → `list_incidents` tool
   - Filter: `team=Alpha, status=open, priority IN (P1,P2,P3,P4)`
   - P1/P2 strategy: fetch runbook → execute steps → verify → escalate if failed
   - P3/P4 strategy: add triage note via `update_incident` tool
3. **Approval**: User reviews and approves plan
4. **Execution Loop** (repeats every 5 minutes):
   - Manager calls `list_incidents` → gets 7 incidents
   - Pool size = 3 assistants
   - Dispatches 3 immediately, queues 4
   - As each assistant completes, next queued task is dispatched
   - All 7 complete → Manager aggregates report
   - Report shows: 4 remediated, 2 escalated, 1 triage note added
5. **Rest**: 5-minute countdown displayed in UI
6. **Next Iteration**: Manager checks again, finds 2 new incidents, dispatches

### 3.2 Use Case: Periodic Code Quality Audit

**User Prompt**:
> "Every 30 minutes, scan the working directory for TODO comments, check for failing tests, and review recent git commits for security patterns. Produce a summary report."

**Manager Behavior**:
- Event source: local filesystem + git commands
- 3 parallel assistants: TODO scanner, test runner, security reviewer
- Each produces findings → Manager consolidates into unified report
- If no issues found, report says "All clear" with metrics

### 3.3 Use Case: Multi-Repository Monitoring

**User Prompt**:
> "Monitor these 5 GitHub repositories for new PRs every 10 minutes. For each new PR, run a code review using the review skill and post feedback."

**Manager Behavior**:
- Uses `github-mcp` to poll PRs across repos
- Batches PR reviews to assistants (1 PR per assistant)
- Handles rate limiting by queuing when pool is full
- Aggregates review feedback into per-repo summaries

### 3.4 Use Case: Dynamic Prompt Change Mid-Run

**User**: (while Manager is running) "Also check for stale branches older than 30 days and report them."

**Manager Behavior**:
- Receives injected instruction
- On next iteration, incorporates new requirement into planning
- Adds "stale branch check" as additional task category
- Continues with expanded scope without restart

### 3.5 Use Case: User-Initiated Pause

**User**: "Take a break for 2 hours."

**Manager Behavior**:
- If assistants are active: waits for current batch to complete
- Enters extended rest with 2-hour countdown
- UI shows "Paused until HH:MM" with countdown
- Resumes automatically after 2 hours

---

## 4. High-Level Design (HLD)

### 4.1 Manager Lifecycle State Machine

```
                    ┌─────────────────┐
                    │                 │
           ┌───────▶│     IDLE        │◀──── ResetSession()
           │        │                 │
           │        └────────┬────────┘
           │                 │ StartAsync(prompt)
           │                 ▼
           │        ┌─────────────────┐
           │        │                 │
           │        │   CLARIFYING    │◀──── Manager asks user questions
           │        │                 │──── User responds
           │        └────────┬────────┘
           │                 │ All questions answered
           │                 ▼
           │        ┌─────────────────┐
           │        │                 │
           │        │    PLANNING     │◀──── Manager builds execution plan
           │        │                 │      using tools, skills, playbooks
           │        └────────┬────────┘
           │                 │ Plan ready
           │                 ▼
           │        ┌─────────────────┐
           │        │                 │
           │        │ AWAITING_APPROVAL│──── User reviews plan
           │        │                 │
           │        └────────┬────────┘
           │                 │ User approves
           │                 ▼
     ┌─────┴─────────────────────────────────────────────────────┐
     │              EXECUTION LOOP (repeats)                      │
     │                                                            │
     │  ┌─────────────────┐                                      │
     │  │                 │                                      │
     │  │  FETCHING_EVENTS│◀──── Manager queries event sources   │
     │  │                 │                                      │
     │  └────────┬────────┘                                      │
     │           │ Events found (or none)                         │
     │           ▼                                                │
     │  ┌─────────────────┐                                      │
     │  │                 │                                      │
     │  │  SCHEDULING     │◀──── Decompose events → tasks       │
     │  │                 │      Assign to pool, queue overflow   │
     │  └────────┬────────┘                                      │
     │           │ All tasks assigned/queued                      │
     │           ▼                                                │
     │  ┌─────────────────┐                                      │
     │  │                 │                                      │
     │  │   EXECUTING     │◀──── Assistants working              │
     │  │                 │      Queue drains as slots free       │
     │  └────────┬────────┘                                      │
     │           │ All tasks complete                             │
     │           ▼                                                │
     │  ┌─────────────────┐                                      │
     │  │                 │                                      │
     │  │  AGGREGATING    │◀──── Manager consolidates results    │
     │  │                 │      Produces iteration report        │
     │  └────────┬────────┘                                      │
     │           │ Report ready                                   │
     │           ▼                                                │
     │  ┌─────────────────┐                                      │
     │  │                 │                                      │
     │  │    RESTING      │◀──── Countdown timer active          │
     │  │                 │      UI shows next check time         │
     │  └────────┬────────┘                                      │
     │           │ Timer elapsed                                  │
     │           └───── Loop back to FETCHING_EVENTS ────────────┘
     │                                                            │
     └────────────────────────────────────────────────────────────┘

     At ANY point during the loop:
       ├── User injects new instruction → absorbed next iteration
       ├── User changes interval → scheduler updated immediately
       ├── User says "pause for X" → enters RESTING with custom duration
       └── User says "reset" → CancelAll → dispose all → return to IDLE
```

### 4.2 Data Flow Per Iteration

```
┌──────────┐     prompt +     ┌──────────┐    query tools    ┌──────────┐
│          │     context      │          │    (MCP/Skills)   │          │
│   User   │────────────────▶│ Manager  │──────────────────▶│ Event    │
│          │                  │ Session  │                    │ Sources  │
│          │◀────────────────│          │◀──────────────────│          │
│          │    report +      │          │    events/data     │          │
│          │    summary       │          │                    │          │
└──────────┘                  └────┬─────┘                    └──────────┘
                                   │
                          decompose into tasks
                                   │
                    ┌──────────────▼──────────────┐
                    │       Assistant Pool         │
                    │                              │
                    │  Active: [A1] [A2] [A3]     │
                    │  Queue:  [T4] [T5] [T6] [T7]│
                    │                              │
                    │  A1 completes → T4 starts    │
                    │  A2 completes → T5 starts    │
                    │  ...                         │
                    └──────────────┬───────────────┘
                                   │
                          all results collected
                                   │
                    ┌──────────────▼──────────────┐
                    │    Manager Aggregation       │
                    │                              │
                    │  Per-task results             │
                    │  Success/failure counts       │
                    │  Detailed narrative           │
                    │  Summary with recommendations │
                    └──────────────────────────────┘
```

### 4.3 Session Lifecycle

```
Manager Session (LONG-LIVED)
├── Created once on StartAsync()
├── Persists across all iterations
├── Receives injected instructions between iterations
├── System prompt evolves with accumulated context
├── Only disposed on ResetSession() or app shutdown
│
├── Iteration 1
│   ├── Assistant Session A1 (EPHEMERAL) → create → work → report → dispose
│   ├── Assistant Session A2 (EPHEMERAL) → create → work → report → dispose
│   └── Assistant Session A3 (EPHEMERAL) → create → work → report → dispose
│
├── [REST PERIOD - 5 min]
│
├── Iteration 2
│   ├── Assistant Session A1 (EPHEMERAL) → create → work → report → dispose
│   └── Assistant Session A2 (EPHEMERAL) → create → work → report → dispose
│
├── [REST PERIOD - 5 min]
│
└── ... (continues until stopped)
```

---

## 5. Low-Level Design (LLD)

### 5.1 Models

#### 5.1.1 `ManagerPhase` (Enum)

```csharp
namespace CopilotAgent.Office.Models;

/// <summary>
/// Represents the current phase of the Office Manager's state machine.
/// </summary>
public enum ManagerPhase
{
    /// <summary>Manager is idle, waiting for user to start.</summary>
    Idle,

    /// <summary>Manager is asking the user clarifying questions.</summary>
    Clarifying,

    /// <summary>Manager is building the execution plan using tools/skills.</summary>
    Planning,

    /// <summary>Manager has a plan and is waiting for user approval.</summary>
    AwaitingApproval,

    /// <summary>Manager is querying event sources for new work.</summary>
    FetchingEvents,

    /// <summary>Manager is decomposing events into tasks and assigning to pool.</summary>
    Scheduling,

    /// <summary>Assistants are actively executing tasks.</summary>
    Executing,

    /// <summary>Manager is consolidating assistant results into a report.</summary>
    Aggregating,

    /// <summary>Manager is in rest period between iterations.</summary>
    Resting,

    /// <summary>Manager encountered a fatal error.</summary>
    Error,

    /// <summary>Manager has been stopped by the user.</summary>
    Stopped
}
```

#### 5.1.2 `OfficeConfig`

```csharp
namespace CopilotAgent.Office.Models;

/// <summary>
/// Configuration for an Agent Office session.
/// </summary>
public class OfficeConfig
{
    /// <summary>Unique identifier for this office configuration.</summary>
    public string ConfigId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Display name for the office session.</summary>
    public string DisplayName { get; set; } = "Agent Office";

    /// <summary>The user's master prompt that drives the Manager.</summary>
    public string MasterPrompt { get; set; } = string.Empty;

    /// <summary>Model ID for the Manager session.</summary>
    public string ManagerModelId { get; set; } = string.Empty;

    /// <summary>Model ID for Assistant sessions.</summary>
    public string AssistantModelId { get; set; } = string.Empty;

    /// <summary>Maximum number of concurrent assistant sessions.</summary>
    public int MaxAssistants { get; set; } = 3;

    /// <summary>Interval between iterations in minutes.</summary>
    public int CheckIntervalMinutes { get; set; } = 5;

    /// <summary>Working directory for all sessions.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>MCP servers available to Manager and Assistants.</summary>
    public List<string> EnabledMcpServers { get; set; } = new();

    /// <summary>Skills disabled for this office.</summary>
    public List<string> DisabledSkills { get; set; } = new();

    /// <summary>Skill directories available to all agents.</summary>
    public List<string> SkillDirectories { get; set; } = new();

    /// <summary>Timeout for individual assistant tasks in minutes.</summary>
    public int AssistantTimeoutMinutes { get; set; } = 10;

    /// <summary>Maximum retries per assistant task on failure.</summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>Whether Manager should auto-approve plan (skip approval step).</summary>
    public bool AutoApprovePlan { get; set; } = false;

    /// <summary>Maximum queue depth for pending tasks (0 = unlimited).</summary>
    public int MaxQueueDepth { get; set; } = 50;

    /// <summary>Manager LLM timeout in seconds.</summary>
    public int ManagerLlmTimeoutSeconds { get; set; } = 120;
}
```

#### 5.1.3 `ManagerContext`

```csharp
namespace CopilotAgent.Office.Models;

/// <summary>
/// Accumulated context the Manager carries across iterations.
/// </summary>
public class ManagerContext
{
    /// <summary>The original user prompt.</summary>
    public string OriginalPrompt { get; set; } = string.Empty;

    /// <summary>Effective prompt (original + any injected modifications).</summary>
    public string EffectivePrompt { get; set; } = string.Empty;

    /// <summary>Injected instructions accumulated between iterations.</summary>
    public List<string> InjectedInstructions { get; set; } = new();

    /// <summary>The approved execution plan description.</summary>
    public string ApprovedPlan { get; set; } = string.Empty;

    /// <summary>Number of completed iterations.</summary>
    public int CompletedIterations { get; set; }

    /// <summary>Timestamp of the last iteration start.</summary>
    public DateTime? LastIterationStartUtc { get; set; }

    /// <summary>Timestamp of the next scheduled iteration.</summary>
    public DateTime? NextIterationUtc { get; set; }

    /// <summary>Clarification Q&A history.</summary>
    public List<ClarificationExchange> ClarificationHistory { get; set; } = new();

    /// <summary>Accumulated learnings across iterations (Manager can remember patterns).</summary>
    public List<string> Learnings { get; set; } = new();

    /// <summary>Summary of previous iteration results (for context continuity).</summary>
    public string PreviousIterationSummary { get; set; } = string.Empty;
}

public class ClarificationExchange
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
```

#### 5.1.4 `AssistantTask`

```csharp
namespace CopilotAgent.Office.Models;

/// <summary>
/// A discrete unit of work assigned to an assistant.
/// </summary>
public class AssistantTask
{
    /// <summary>Unique task identifier.</summary>
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Human-readable title for the task.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Full prompt to send to the assistant.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Priority (lower = higher priority). Used for queue ordering.</summary>
    public int Priority { get; set; } = 5;

    /// <summary>Source event/incident that spawned this task.</summary>
    public string SourceEventId { get; set; } = string.Empty;

    /// <summary>Category label for reporting (e.g., "P1 Incident", "PR Review").</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Additional context data (JSON-safe dictionary).</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>Iteration number this task belongs to.</summary>
    public int IterationNumber { get; set; }

    /// <summary>When this task was created.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Current status.</summary>
    public AssistantTaskStatus Status { get; set; } = AssistantTaskStatus.Pending;

    /// <summary>Retry count for this task.</summary>
    public int RetryCount { get; set; }
}

public enum AssistantTaskStatus
{
    Pending,
    Queued,
    Assigned,
    InProgress,
    Completed,
    Failed,
    Cancelled
}
```

#### 5.1.5 `AssistantResult`

```csharp
namespace CopilotAgent.Office.Models;

/// <summary>
/// Result produced by an assistant after completing a task.
/// </summary>
public class AssistantResult
{
    /// <summary>The task that was executed.</summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>Task title for display.</summary>
    public string TaskTitle { get; set; } = string.Empty;

    /// <summary>Whether the task succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>The assistant's full response text.</summary>
    public string Response { get; set; } = string.Empty;

    /// <summary>Summary of what was accomplished (extracted by assistant).</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Error message if failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Which assistant index handled this task.</summary>
    public int AssistantIndex { get; set; }

    /// <summary>Duration of task execution.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Timestamp of completion.</summary>
    public DateTime CompletedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Category from the original task.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Actions taken (structured list for reporting).</summary>
    public List<string> ActionsTaken { get; set; } = new();
}
```

#### 5.1.6 `IterationReport`

```csharp
namespace CopilotAgent.Office.Models;

/// <summary>
/// Comprehensive report produced by the Manager after each iteration.
/// </summary>
public class IterationReport
{
    /// <summary>Iteration number.</summary>
    public int IterationNumber { get; set; }

    /// <summary>When this iteration started.</summary>
    public DateTime StartedUtc { get; set; }

    /// <summary>When this iteration completed.</summary>
    public DateTime CompletedUtc { get; set; }

    /// <summary>Total events/incidents discovered.</summary>
    public int EventsDiscovered { get; set; }

    /// <summary>Total tasks created from events.</summary>
    public int TasksCreated { get; set; }

    /// <summary>Tasks that completed successfully.</summary>
    public int TasksSucceeded { get; set; }

    /// <summary>Tasks that failed.</summary>
    public int TasksFailed { get; set; }

    /// <summary>Tasks that were cancelled.</summary>
    public int TasksCancelled { get; set; }

    /// <summary>Per-task detailed results.</summary>
    public List<AssistantResult> DetailedResults { get; set; } = new();

    /// <summary>Manager-generated narrative summary.</summary>
    public string NarrativeSummary { get; set; } = string.Empty;

    /// <summary>Manager-generated recommendations for next iteration.</summary>
    public string Recommendations { get; set; } = string.Empty;

    /// <summary>Scheduling decisions made during this iteration.</summary>
    public List<SchedulingDecision> SchedulingLog { get; set; } = new();

    /// <summary>Total wall-clock duration of the iteration.</summary>
    public TimeSpan Duration => CompletedUtc - StartedUtc;
}
```

#### 5.1.7 `SchedulingDecision`

```csharp
namespace CopilotAgent.Office.Models;

/// <summary>
/// Records a scheduling decision made by the Manager for observability.
/// </summary>
public class SchedulingDecision
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string TaskId { get; set; } = string.Empty;
    public string TaskTitle { get; set; } = string.Empty;
    public SchedulingAction Action { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int? AssignedAssistantIndex { get; set; }
    public int QueuePositionAtTime { get; set; }
    public int AvailableAssistantsAtTime { get; set; }
}

public enum SchedulingAction
{
    AssignedImmediate,
    QueuedPending,
    DequeuedAndAssigned,
    Retried,
    Cancelled,
    SkippedDuplicate
}
```

#### 5.1.8 `OfficeChatMessage`

```csharp
namespace CopilotAgent.Office.Models;

/// <summary>
/// Chat message displayed in the Office conversation view.
/// Supports both regular messages and iteration container sections.
/// </summary>
public class OfficeChatMessage
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public OfficeChatRole Role { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    /// <summary>If true, content is Markdown and should be rendered richly.</summary>
    public bool IsMarkdown { get; set; } = true;

    /// <summary>If true, this message can be collapsed in the UI.</summary>
    public bool IsCollapsible { get; set; }

    /// <summary>If true, this message is initially collapsed.</summary>
    public bool IsCollapsed { get; set; }

    /// <summary>
    /// If true, this message acts as an iteration container header.
    /// All child messages for this iteration are grouped under it.
    /// Completed iterations auto-collapse; active iteration stays expanded.
    /// </summary>
    public bool IsIterationContainer { get; set; }

    /// <summary>
    /// Tracks the expanded/collapsed state of an iteration container.
    /// When an iteration completes, this is set to false (collapsed).
    /// User can toggle manually via click.
    /// </summary>
    public bool ContainerExpanded { get; set; } = true;

    /// <summary>Color coding key for the sender.</summary>
    public string ColorKey { get; set; } = string.Empty;

    /// <summary>Phase when this message was generated.</summary>
    public ManagerPhase Phase { get; set; }

    /// <summary>Iteration number (0 if pre-loop).</summary>
    public int IterationNumber { get; set; }
}

public enum OfficeChatRole
{
    User,
    Manager,
    Assistant,
    System
}
```

#### 5.1.10 `LiveCommentary`

```csharp
namespace CopilotAgent.Office.Models;

/// <summary>
/// A real-time commentary entry from Manager or an Assistant,
/// displayed in the side panel's Live Commentary stream.
/// Similar to modern AI "thinking" indicators — shows what agents
/// are doing in natural language as it happens.
/// </summary>
public class LiveCommentary
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Source agent name: "Manager", "Assistant #1", etc.</summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>Human-readable progress message: "Fetching runbook...", "Querying ServiceNow..."</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Type determines the emoji/color indicator in the UI.</summary>
    public CommentaryType Type { get; set; }

    /// <summary>Color key for agent attribution styling.</summary>
    public string ColorKey { get; set; } = string.Empty;
}

/// <summary>
/// Categories for live commentary entries, each mapped to a visual indicator.
/// </summary>
public enum CommentaryType
{
    /// <summary>🔵 Manager planning, strategizing.</summary>
    Planning,

    /// <summary>🟢 Discovering events, fetching data.</summary>
    Discovery,

    /// <summary>🟡 Scheduling tasks, assigning to pool.</summary>
    Scheduling,

    /// <summary>🟠 Assistant actively working on a task.</summary>
    Working,

    /// <summary>✅ Task or phase completed successfully.</summary>
    Success,

    /// <summary>⚠️ Non-fatal warning or degraded result.</summary>
    Warning,

    /// <summary>❌ Error or failure.</summary>
    Error
}
```

#### 5.1.9 `OfficeColorScheme`

```csharp
namespace CopilotAgent.Office.Models;

public static class OfficeColorScheme
{
    public static string ManagerColor => "#2196F3";      // Blue
    public static string UserColor => "#4CAF50";          // Green
    public static string SystemColor => "#9E9E9E";        // Grey
    public static string ErrorColor => "#F44336";          // Red

    // Assistant colors cycle through these
    private static readonly string[] AssistantColors = new[]
    {
        "#FF9800", // Orange
        "#9C27B0", // Purple
        "#00BCD4", // Cyan
        "#E91E63", // Pink
        "#795548", // Brown
        "#607D8B", // Blue Grey
        "#CDDC39", // Lime
        "#3F51B5", // Indigo
    };

    public static string GetAssistantColor(int index)
        => AssistantColors[index % AssistantColors.Length];
}
```

### 5.2 Events

#### 5.2.1 `OfficeEventType` (Enum)

```csharp
namespace CopilotAgent.Office.Events;

public enum OfficeEventType
{
    // Lifecycle
    ManagerStarted,
    ManagerStopped,
    ManagerReset,
    ManagerError,

    // Phase transitions
    PhaseChanged,

    // Clarification
    ClarificationRequested,
    ClarificationReceived,

    // Planning
    PlanGenerated,
    PlanApproved,
    PlanRejected,

    // Iteration lifecycle
    IterationStarted,
    IterationCompleted,

    // Event fetching
    EventsFetched,
    NoEventsFound,

    // Scheduling
    TaskCreated,
    TaskAssigned,
    TaskQueued,
    TaskDequeued,
    TaskCancelled,

    // Assistant lifecycle
    AssistantSpawned,
    AssistantProgress,
    AssistantCompleted,
    AssistantFailed,
    AssistantDisposed,

    // Aggregation
    AggregationStarted,
    ReportGenerated,

    // Rest period
    RestStarted,
    RestCountdownTick,
    RestCompleted,

    // User interactions
    InstructionInjected,
    IntervalChanged,
    PauseRequested,
    ResumeRequested,

    // Chat
    ChatMessageAdded,

    // Live Commentary
    Commentary
}
```

#### 5.2.2 `OfficeEvent` (Base + Derived)

```csharp
namespace CopilotAgent.Office.Events;

/// <summary>
/// Base event for all Office Manager events.
/// </summary>
public class OfficeEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public OfficeEventType EventType { get; set; }
    public string Message { get; set; } = string.Empty;
    public ManagerPhase CurrentPhase { get; set; }
    public int IterationNumber { get; set; }
}

/// <summary>Phase transition event with from/to.</summary>
public class PhaseChangedEvent : OfficeEvent
{
    public ManagerPhase FromPhase { get; set; }
    public ManagerPhase ToPhase { get; set; }
}

/// <summary>Assistant lifecycle event.</summary>
public class AssistantEvent : OfficeEvent
{
    public string TaskId { get; set; } = string.Empty;
    public string TaskTitle { get; set; } = string.Empty;
    public int AssistantIndex { get; set; }
    public AssistantTaskStatus TaskStatus { get; set; }
    public string? ProgressMessage { get; set; }
}

/// <summary>Scheduling decision event.</summary>
public class SchedulingEvent : OfficeEvent
{
    public SchedulingDecision Decision { get; set; } = new();
    public int CurrentQueueDepth { get; set; }
    public int AvailableAssistants { get; set; }
}

/// <summary>Iteration completed with report.</summary>
public class IterationCompletedEvent : OfficeEvent
{
    public IterationReport Report { get; set; } = new();
}

/// <summary>Rest period tick event for countdown UI.</summary>
public class RestCountdownEvent : OfficeEvent
{
    public TimeSpan Remaining { get; set; }
    public DateTime NextIterationUtc { get; set; }
}

/// <summary>Chat message event for UI binding.</summary>
public class ChatMessageEvent : OfficeEvent
{
    public OfficeChatMessage ChatMessage { get; set; } = new();
}

/// <summary>Clarification request from Manager to User.</summary>
public class ClarificationEvent : OfficeEvent
{
    public string Question { get; set; } = string.Empty;
    public List<string>? SuggestedOptions { get; set; }
}

/// <summary>
/// Real-time commentary from Manager or Assistant for the Live Commentary stream.
/// Raised throughout execution to provide natural-language progress updates.
/// </summary>
public class CommentaryEvent : OfficeEvent
{
    public LiveCommentary Commentary { get; set; } = new();
}
```

### 5.3 Service Interfaces

#### 5.3.1 `IOfficeManagerService`

```csharp
namespace CopilotAgent.Office.Services;

/// <summary>
/// Core service interface for the Agent Office Manager.
/// Manages the entire lifecycle: start → clarify → plan → approve → loop → stop.
/// </summary>
public interface IOfficeManagerService
{
    /// <summary>Current phase of the Manager.</summary>
    ManagerPhase CurrentPhase { get; }

    /// <summary>Whether the Manager is actively running (not Idle/Stopped/Error).</summary>
    bool IsRunning { get; }

    /// <summary>The Manager's accumulated context.</summary>
    ManagerContext Context { get; }

    /// <summary>Configuration for this office session.</summary>
    OfficeConfig Config { get; }

    /// <summary>Current iteration number (0 if not started).</summary>
    int CurrentIteration { get; }

    /// <summary>Event stream for UI binding.</summary>
    event EventHandler<OfficeEvent>? OnEvent;

    /// <summary>
    /// Starts the Manager with the given configuration.
    /// Transitions: Idle → Clarifying (or Planning if no clarification needed).
    /// </summary>
    Task StartAsync(OfficeConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Responds to a clarification question from the Manager.
    /// Transitions: Clarifying → Planning (or Clarifying again if more questions).
    /// </summary>
    Task RespondToClarificationAsync(string response, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves the Manager's plan, entering the execution loop.
    /// Transitions: AwaitingApproval → FetchingEvents.
    /// </summary>
    Task ApprovePlanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects the plan with feedback, sending Manager back to planning.
    /// Transitions: AwaitingApproval → Planning.
    /// </summary>
    Task RejectPlanAsync(string feedback, CancellationToken cancellationToken = default);

    /// <summary>
    /// Injects a new instruction that the Manager absorbs on the next iteration.
    /// Can be called during any phase.
    /// </summary>
    Task InjectInstructionAsync(string instruction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the check interval. Takes effect on the next rest period.
    /// </summary>
    void UpdateCheckInterval(int newIntervalMinutes);

    /// <summary>
    /// Pauses the Manager for a specified duration. Current tasks complete first.
    /// </summary>
    Task PauseAsync(TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a paused Manager immediately (skips remaining rest).
    /// </summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully stops the Manager. Waits for active assistants to finish.
    /// Transitions: Any → Stopped.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard reset: cancels everything, disposes all sessions, returns to Idle.
    /// Transitions: Any → Idle.
    /// </summary>
    Task ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the history of iteration reports.
    /// </summary>
    IReadOnlyList<IterationReport> GetIterationHistory();
}
```

#### 5.3.2 `IAssistantPool`

```csharp
namespace CopilotAgent.Office.Services;

/// <summary>
/// Manages a finite pool of assistant agents with queue-based overflow.
/// </summary>
public interface IAssistantPool : IAsyncDisposable
{
    /// <summary>Maximum concurrent assistants.</summary>
    int MaxConcurrency { get; }

    /// <summary>Number of currently active assistants.</summary>
    int ActiveCount { get; }

    /// <summary>Number of tasks waiting in the queue.</summary>
    int QueueDepth { get; }

    /// <summary>Number of available assistant slots.</summary>
    int AvailableSlots { get; }

    /// <summary>
    /// Submits a batch of tasks. Tasks up to MaxConcurrency start immediately;
    /// the rest are queued and start as slots free up.
    /// Returns when ALL tasks (including queued) are complete.
    /// </summary>
    Task<List<AssistantResult>> ExecuteTasksAsync(
        List<AssistantTask> tasks,
        OfficeConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>Event raised for each assistant lifecycle change.</summary>
    event EventHandler<AssistantEvent>? OnAssistantEvent;

    /// <summary>Event raised for each scheduling decision.</summary>
    event EventHandler<SchedulingEvent>? OnSchedulingEvent;

    /// <summary>Cancels all active and queued tasks.</summary>
    Task CancelAllAsync();
}
```

#### 5.3.3 `IAssistantAgent`

```csharp
namespace CopilotAgent.Office.Services;

/// <summary>
/// A single ephemeral assistant that executes one task and is disposed.
/// </summary>
public interface IAssistantAgent : IAsyncDisposable
{
    /// <summary>Unique index within the current pool batch.</summary>
    int AssistantIndex { get; }

    /// <summary>Whether this assistant is currently executing.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Executes the given task: creates a Copilot session, sends prompt,
    /// collects response, and returns result.
    /// </summary>
    Task<AssistantResult> ExecuteAsync(
        AssistantTask task,
        OfficeConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>Progress event for streaming updates.</summary>
    event EventHandler<string>? OnProgress;
}
```

#### 5.3.4 `IIterationScheduler`

```csharp
namespace CopilotAgent.Office.Services;

/// <summary>
/// Manages the rest period between iterations with countdown support.
/// </summary>
public interface IIterationScheduler : IDisposable
{
    /// <summary>Whether the scheduler is currently in a rest period.</summary>
    bool IsResting { get; }

    /// <summary>Time remaining in the current rest period.</summary>
    TimeSpan Remaining { get; }

    /// <summary>When the next iteration is scheduled.</summary>
    DateTime? NextIterationUtc { get; }

    /// <summary>
    /// Waits for the configured interval, raising tick events for countdown.
    /// </summary>
    Task WaitForNextIterationAsync(
        TimeSpan interval,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Overrides the current rest with a custom duration (for pause requests).
    /// </summary>
    Task OverrideRestDurationAsync(
        TimeSpan newDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the current rest period, causing WaitForNextIterationAsync to return.
    /// </summary>
    void CancelRest();

    /// <summary>Tick event raised every second during rest for countdown UI.</summary>
    event EventHandler<RestCountdownEvent>? OnCountdownTick;
}
```

#### 5.3.5 `IOfficeEventLog`

```csharp
namespace CopilotAgent.Office.Services;

/// <summary>
/// Structured event log for Manager scheduling decisions and lifecycle events.
/// Supports both in-memory querying and optional persistence.
/// </summary>
public interface IOfficeEventLog
{
    /// <summary>Appends an event to the log.</summary>
    void Log(OfficeEvent officeEvent);

    /// <summary>Gets all events.</summary>
    IReadOnlyList<OfficeEvent> GetAll();

    /// <summary>Gets events for a specific iteration.</summary>
    IReadOnlyList<OfficeEvent> GetByIteration(int iterationNumber);

    /// <summary>Gets events of a specific type.</summary>
    IReadOnlyList<OfficeEvent> GetByType(OfficeEventType eventType);

    /// <summary>Gets scheduling decisions only.</summary>
    IReadOnlyList<SchedulingDecision> GetSchedulingLog();

    /// <summary>Clears all events.</summary>
    void Clear();
}
```

### 5.4 Service Implementations — Key Design Decisions

#### 5.4.1 `OfficeManagerService` — Core Manager Logic

**Responsibilities**:
- Owns the state machine (phase transitions)
- Owns the Manager's Copilot session (long-lived)
- Orchestrates the iteration loop
- Delegates to `IAssistantPool` for task execution
- Uses `IIterationScheduler` for rest periods
- Logs all decisions to `IOfficeEventLog`
- Raises `OfficeEvent` for UI binding

**Key Design**:

```
OfficeManagerService
├── _copilotService: ICopilotService          # Creates/manages sessions
├── _assistantPool: IAssistantPool            # Dispatches tasks to assistants
├── _scheduler: IIterationScheduler           # Manages rest periods
├── _eventLog: IOfficeEventLog                # Structured logging
├── _managerSession: Session?                 # Long-lived manager session
├── _context: ManagerContext                   # Accumulated context
├── _config: OfficeConfig                     # Configuration
├── _phase: ManagerPhase                      # Current state
├── _loopCts: CancellationTokenSource?        # Controls the main loop
│
├── StartAsync()
│   ├── Validate config
│   ├── Create manager session (EnsureManagerSessionAsync)
│   ├── Build system prompt with tools/skills/playbooks context
│   ├── Send initial prompt to LLM
│   ├── Parse response: does it contain questions? → Clarifying
│   │                   does it contain a plan? → AwaitingApproval
│   └── Raise ManagerStarted event
│
├── RunIterationLoopAsync()    # Private, runs on background Task
│   ├── while (!cancelled)
│   │   ├── TransitionTo(FetchingEvents)
│   │   ├── FetchEventsAsync()        # Send "check for events" prompt to Manager LLM
│   │   ├── ParseEventsResponse()     # Extract task list from LLM response
│   │   ├── if (no events) → log "No events" → skip to rest
│   │   ├── TransitionTo(Scheduling)
│   │   ├── CreateAssistantTasks()    # Build AssistantTask list with priorities
│   │   ├── Log scheduling decisions
│   │   ├── TransitionTo(Executing)
│   │   ├── results = await _assistantPool.ExecuteTasksAsync(tasks)
│   │   ├── TransitionTo(Aggregating)
│   │   ├── report = await AggregateResultsAsync(results)
│   │   ├── Raise IterationCompleted event
│   │   ├── TransitionTo(Resting)
│   │   ├── AbsorbInjectedInstructions()  # Apply any mid-run changes
│   │   └── await _scheduler.WaitForNextIterationAsync(interval)
│   └── end while
│
├── InjectInstructionAsync()
│   ├── Add to _context.InjectedInstructions
│   ├── Raise InstructionInjected event
│   └── (Will be absorbed at start of next iteration)
│
├── AggregateResultsAsync()    # Private
│   ├── Build aggregation prompt with all AssistantResults
│   ├── Send to Manager LLM: "Summarize these results..."
│   ├── Parse narrative summary + recommendations
│   └── Return IterationReport
│
└── ResetAsync()
    ├── _loopCts.Cancel()
    ├── await _assistantPool.CancelAllAsync()
    ├── Dispose manager session
    ├── Clear context
    ├── TransitionTo(Idle)
    └── Raise ManagerReset event
```

#### 5.4.2 `AssistantPool` — Queue-Based Concurrency

**Key Design**: Uses a `SemaphoreSlim` for concurrency gating and a `ConcurrentQueue<AssistantTask>` for overflow.

```
AssistantPool
├── _semaphore: SemaphoreSlim(maxConcurrency)
├── _activeAssistants: ConcurrentDictionary<string, IAssistantAgent>
├── _taskQueue: ConcurrentQueue<AssistantTask>
├── _copilotService: ICopilotService
│
├── ExecuteTasksAsync(tasks)
│   ├── Sort tasks by Priority
│   ├── Create Channel<AssistantResult> for result collection
│   ├── For each task:
│   │   ├── await _semaphore.WaitAsync()   # Blocks if pool full
│   │   ├── Log SchedulingDecision (AssignedImmediate or DequeuedAndAssigned)
│   │   ├── Spawn async: ExecuteSingleTaskAsync(task)
│   │   │   ├── Create AssistantAgent(index)
│   │   │   ├── result = await agent.ExecuteAsync(task, config)
│   │   │   ├── await agent.DisposeAsync()
│   │   │   ├── _semaphore.Release()        # Free slot for next queued task
│   │   │   ├── Raise AssistantCompleted/Failed event
│   │   │   └── Write result to channel
│   │   └── end spawn
│   ├── await Task.WhenAll(allSpawnedTasks)
│   └── Return collected results
│
└── CancelAllAsync()
    ├── Cancel all active assistant CancellationTokenSources
    ├── Clear queue
    └── Dispose all active assistants
```

**Queue Behavior**:
- If `MaxAssistants = 3` and 7 tasks arrive:
  - Tasks 1-3 start immediately (semaphore acquired)
  - Tasks 4-7 block on `_semaphore.WaitAsync()` — they naturally queue
  - As each of 1-3 finishes and releases the semaphore, the next waiting task proceeds
- This is elegant: `SemaphoreSlim` IS the queue mechanism. No separate queue data structure needed for basic flow.
- The `SchedulingDecision` log tracks whether each task was immediate or waited.

#### 5.4.3 `AssistantAgent` — Ephemeral Worker

```
AssistantAgent
├── _copilotService: ICopilotService
├── _assistantIndex: int
│
├── ExecuteAsync(task, config)
│   ├── Create Session model:
│   │   ├── SessionId = $"office-asst-{task.TaskId}"
│   │   ├── ModelId = config.AssistantModelId
│   │   ├── WorkingDirectory = config.WorkingDirectory
│   │   ├── EnabledMcpServers = config.EnabledMcpServers
│   │   ├── SystemPrompt = BuildAssistantSystemPrompt(task)
│   │   └── SkillDirectories = config.SkillDirectories
│   ├── Send task.Prompt via _copilotService.SendMessageAsync()
│   ├── Collect response
│   ├── Build AssistantResult
│   │   ├── Success = !response contains error indicators
│   │   ├── Response = full text
│   │   ├── Summary = extract first paragraph or structured summary
│   │   └── ActionsTaken = parse bullet points from response
│   └── Return result
│
├── BuildAssistantSystemPrompt(task)
│   └── "You are Assistant #{index} in an Agent Office.
│         Your task: {task.Title}
│         Category: {task.Category}
│         Context: {task.Metadata}
│         Complete this task thoroughly and report:
│         1. What you found
│         2. What actions you took
│         3. The outcome
│         4. Any recommendations
│         Be concise but complete."
│
└── DisposeAsync()
    └── _copilotService.TerminateSessionProcess(sessionId)
```

#### 5.4.4 `IterationScheduler` — Rest Period with Countdown

```
IterationScheduler
├── _restTcs: TaskCompletionSource?     # Signaled to cancel rest early
├── _timer: PeriodicTimer?
├── _remaining: TimeSpan
│
├── WaitForNextIterationAsync(interval)
│   ├── _remaining = interval
│   ├── _nextIterationUtc = DateTime.UtcNow + interval
│   ├── Start PeriodicTimer(1 second)
│   ├── Each tick:
│   │   ├── _remaining -= 1 second
│   │   ├── Raise OnCountdownTick(remaining, nextIterationUtc)
│   │   └── if _remaining <= 0 → return
│   ├── Also await _restTcs.Task (for early cancellation)
│   └── Return when either timer completes or cancelled
│
├── OverrideRestDurationAsync(newDuration)
│   ├── Cancel current timer
│   ├── Start new timer with newDuration
│   └── Update _nextIterationUtc
│
└── CancelRest()
    └── _restTcs.TrySetResult()   # Causes WaitForNextIterationAsync to return
```

---

## 6. Technical Design

### 6.1 Manager System Prompt Design

The Manager's system prompt is critical. It must instruct the LLM to:

1. **Understand its role** as a long-running office manager
2. **Use tools** (MCP servers, skills) to fetch events
3. **Return structured JSON** for task decomposition
4. **Aggregate results** into readable reports

**Manager System Prompt Template**:

```
You are the Manager of an Agent Office — a long-running autonomous operations center.

## Your Role
You manage a team of {MaxAssistants} assistants. You periodically check for events/work,
decompose it into discrete tasks, and delegate to assistants.

## Your Capabilities
- MCP Servers: {list of enabled MCP servers with descriptions}
- Skills: {list of available skills}
- Playbooks: Available in working directory {WorkingDirectory}

## User's Objective
{MasterPrompt}

## Accumulated Context
{PreviousIterationSummary}
{Learnings}
{InjectedInstructions}

## Response Format
When asked to CHECK FOR EVENTS, respond with JSON:
```json
{
  "events_found": true/false,
  "events": [
    {
      "event_id": "...",
      "title": "...",
      "description": "...",
      "priority": 1-5,
      "category": "...",
      "metadata": {}
    }
  ],
  "commentary": "Brief explanation of what you found"
}
```

When asked to AGGREGATE RESULTS, respond with Markdown:
- Per-task summary with status
- Overall statistics
- Recommendations for next iteration
- Any patterns or learnings observed

## Rules
1. Always use available tools to fetch real data. Do not fabricate events.
2. Be specific in task descriptions — each assistant works independently.
3. Include enough context in each task for the assistant to work without asking questions.
4. Track patterns across iterations and note them in your commentary.
```

### 6.2 LLM Interaction Protocol

**Clarification Phase**:
```
Manager LLM receives: System prompt + "BEGIN CLARIFICATION. Ask any questions needed to fully understand the user's objective. If no questions, respond with: READY_TO_PLAN"
Manager LLM responds: Either questions (parsed) or "READY_TO_PLAN"
```

**Planning Phase**:
```
Manager LLM receives: "CREATE EXECUTION PLAN. Based on the user's objective and your tools, describe step-by-step how each iteration will work."
Manager LLM responds: Plan in Markdown format
```

**Event Fetching Phase**:
```
Manager LLM receives: "CHECK FOR EVENTS. Use your tools to query for new work. Respond in the required JSON format."
Manager LLM responds: JSON with events list
```

**Aggregation Phase**:
```
Manager LLM receives: "AGGREGATE RESULTS for Iteration #{n}. Here are the assistant results: {JSON array of AssistantResult}. Produce a comprehensive report."
Manager LLM responds: Markdown report
```

### 6.3 Error Handling Strategy

| Scenario | Handling |
|----------|----------|
| Manager session disconnects | Reconnect with context replay (same as OrchestratorService pattern) |
| Assistant task timeout | Mark as Failed, log, continue with remaining tasks |
| Assistant task error | Retry up to `MaxRetries`, then mark Failed |
| All assistants fail | Manager enters error state, raises event, waits for user |
| LLM returns unparseable JSON | Retry with clarification prompt, fall back to manual parse |
| User resets during execution | `_loopCts.Cancel()`, `CancelAllAsync()`, dispose all |
| Queue overflow (>MaxQueueDepth) | Drop lowest priority tasks, log warning |

### 6.4 Thread Safety & UI Thread Marshalling

- `OfficeManagerService` main loop runs on a single `Task.Run` background thread
- `AssistantPool` uses `SemaphoreSlim` (thread-safe) for concurrency
- `ManagerContext` mutations are confined to the manager loop (no concurrent writes)
- `InjectedInstructions` is a `ConcurrentBag<string>` drained at iteration boundary
- `IterationScheduler` countdown timer fires on timer thread; events are thread-safe

**Event-to-UI Marshalling Flow**:

All events originate on background threads. The `OfficeViewModel` subscribes to `IOfficeManagerService.OnEvent` and marshals every UI-bound mutation to the WPF Dispatcher:

```
Background Thread (Manager/AssistantPool/AssistantAgent)
    │
    ├── Raises OfficeEvent (any type)
    │   └── ChatMessageEvent, CommentaryEvent, PhaseChangedEvent, etc.
    │
    ▼
OfficeViewModel.HandleEvent(OfficeEvent e)
    │
    ├── switch (e.EventType)
    │   ├── ChatMessageAdded:
    │   │   └── Dispatcher.Invoke(() => Messages.Add(chatMsg))
    │   │
    │   ├── Commentary:
    │   │   └── Dispatcher.Invoke(() => {
    │   │       LiveCommentaries.Add(commentary);
    │   │       if (AutoScrollCommentary) ScrollToBottom();
    │   │   })
    │   │
    │   ├── PhaseChanged:
    │   │   └── Dispatcher.Invoke(() => CurrentPhase = newPhase)
    │   │
    │   ├── RestCountdownTick:
    │   │   └── Dispatcher.Invoke(() => {
    │   │       RestCountdownText = format(remaining);
    │   │       RestProgressPercent = calculatePercent();
    │   │   })
    │   │
    │   └── ... (all other events follow same pattern)
    │
    └── All ObservableCollection<T> updates (Messages, LiveCommentaries, EventLog)
        are ALWAYS wrapped in Dispatcher.Invoke to prevent cross-thread exceptions.
```

**Commentary Event Sources**:

```
OfficeManagerService
├── Phase transitions      → CommentaryEvent(Planning, "Building execution plan...")
├── Event fetching         → CommentaryEvent(Discovery, "Querying ServiceNow for incidents...")
├── Scheduling decisions   → CommentaryEvent(Scheduling, "Assigning 3 tasks, queuing 4...")
└── Aggregation            → CommentaryEvent(Planning, "Consolidating 7 results into report...")

AssistantPool
├── Task assignment        → CommentaryEvent(Scheduling, "Assistant #1 starting INC001...")
├── Task dequeue           → CommentaryEvent(Scheduling, "Slot freed, dequeuing INC004...")
└── Pool exhaustion        → CommentaryEvent(Warning, "All slots busy, 4 tasks queued")

AssistantAgent
├── Session creation       → CommentaryEvent(Working, "Creating session for INC001...")
├── Tool invocation        → CommentaryEvent(Working, "Calling runbook-executor tool...")
├── Completion             → CommentaryEvent(Success, "INC001 remediated successfully")
└── Failure                → CommentaryEvent(Error, "INC002 remediation failed: timeout")
```

### 6.5 Cancellation Strategy

```
_masterCts (root)
├── _loopCts (controls iteration loop, cancelled on Stop/Reset)
│   ├── _iterationCts (per-iteration, cancelled on user pause)
│   │   ├── _executionCts (per-execution batch, cancelled on timeout)
│   │   │   └── per-assistant CancellationToken (linked to _executionCts)
│   │   └── _restCts (per-rest period, cancelled on Resume)
│   └── ...
└── Cancelled on DisposeAsync (app shutdown)
```

### 6.6 DI Registration

```csharp
// In App.xaml.cs ConfigureServices()

// Office services
services.AddSingleton<IOfficeManagerService, OfficeManagerService>();
services.AddTransient<IAssistantPool, AssistantPool>();
services.AddTransient<IAssistantAgent, AssistantAgent>();
services.AddSingleton<IIterationScheduler, IterationScheduler>();
services.AddSingleton<IOfficeEventLog, OfficeEventLog>();

// ViewModels
services.AddTransient<OfficeViewModel>();

// Settings model
services.AddSingleton<OfficeSettings>();
```

---

## 7. UI Design

The Office UI uses a **full-width scrollable chat plane** as the primary surface. All interactions — clarification, planning, iteration output, reports, and user interruptions — flow chronologically in a single chat stream. A **fly-in/fly-out animated side panel** overlays the chat from the right edge, providing live commentary, configuration, event logs, and statistics without navigating away from the conversation.

### 7.1 Layout Architecture

**Two-layer design**:
1. **Base layer**: Full-width chat plane + status bar + input area (always visible)
2. **Overlay layer**: Side panel that slides in from the right, dimming the chat beneath

```
┌────────────────────────────────────────────────────────────────────────┐
│ [Sessions] [+ New] [+ Worktree] [🏢 Office] [👥 Team] [⚙ Settings]  │
├────────────────────────────────────────────────────────────────────────┤
│                                                                        │
│  ┌─── Status Bar ────────────────────────────────────── [📊] ────┐   │
│  │ 🟢 EXECUTING  │ Iteration #3 │ Tasks: 4/7 │ Queue: 3 │ 00:42 │   │
│  └───────────────────────────────────────────────────────────────┘   │
│                                                                        │
│  ┌─── Full-Width Scrollable Chat Plane ──────────────────────────┐   │
│  │                                                                │   │
│  │  [USER] 10:30 AM                                              │   │
│  │  Analyze open incidents for Team Alpha every 5 minutes...     │   │
│  │                                                                │   │
│  │  [MANAGER] 10:30 AM                                   [fold]  │   │
│  │  I have a few questions before we begin:                      │   │
│  │  1. Which incident source — ServiceNow or Azure DevOps?       │   │
│  │                                                                │   │
│  │  [USER] 10:31 AM                                              │   │
│  │  ServiceNow. Resolved means status changed to "Resolved".     │   │
│  │                                                                │   │
│  │  [MANAGER] 10:31 AM — PLAN                           [fold]  │   │
│  │  ## Execution Plan                                             │   │
│  │  1. Query ServiceNow every 5 min...                           │   │
│  │  [✅ Approve] [❌ Reject]                                      │   │
│  │                                                                │   │
│  │  ━━ Iteration #1 ━━━━━━━━━━━━━━━━━━━━━━━ 10:32 AM ━━ [▾]    │   │
│  │  │                                                             │   │
│  │  │  [MANAGER] SCHEDULING                                      │   │
│  │  │  Found 5 incidents. Assigning 3 immediately, queuing 2.    │   │
│  │  │                                                             │   │
│  │  │  ▸ [ASSISTANT #1] INC001: P1 Database pool exhausted       │   │
│  │  │    ✅ Remediated: Restarted connection pool via runbook.   │   │
│  │  │                                                             │   │
│  │  │  ▸ [ASSISTANT #2] INC002: P2 High CPU on web tier          │   │
│  │  │    ⚠️ Escalated: Auto-scaling attempted, CPU still >90%.  │   │
│  │  │                                                             │   │
│  │  │  ▸ [ASSISTANT #3] INC003: P3 Certificate expiry            │   │
│  │  │    ✅ Triage note added.                                   │   │
│  │  │                                                             │   │
│  │  │  ▸ [ASSISTANT #1] INC004: P4 Log rotation       [fold]    │   │
│  │  │  ▸ [ASSISTANT #2] INC005: P2 Memory leak        [fold]    │   │
│  │  │                                                             │   │
│  │  │  [MANAGER] REPORT                                 [fold]   │   │
│  │  │  ## Iteration #1 Summary                                   │   │
│  │  │  - **5 incidents** processed                               │   │
│  │  │  - **3 remediated**, 1 escalated, 1 triaged               │   │
│  │  │                                                             │   │
│  │  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━   │   │
│  │                                                                │   │
│  │  [USER] 10:35 AM                                              │   │
│  │  Also check for stale branches older than 30 days.            │   │
│  │                                                                │   │
│  │  [SYSTEM] 📝 Instruction queued for next iteration            │   │
│  │                                                                │   │
│  │  ━━ Iteration #2 ━━━━━━━━━━━━━━━━━━━━━━━ 10:37 AM ━━ [▾]    │   │
│  │  │                                                             │   │
│  │  │  [MANAGER] SCHEDULING                                      │   │
│  │  │  Found 3 incidents + 12 stale branches. Assigning...       │   │
│  │  │  ... (active, auto-scrolling)                              │   │
│  │  │                                                             │   │
│  │                                                                │   │
│  └────────────────────────────────────────────────────────────────┘   │
│                                                                        │
│  ┌─── Input Area (bottom-pinned) ────────────────────────────────┐   │
│  │ Type a message or instruction...                    [Send] 📎 │   │
│  └────────────────────────────────────────────────────────────────┘   │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

### 7.2 Foldable Iteration Containers

Each iteration is wrapped in a **foldable container** with a separator header:

```
━━ Iteration #N ━━━━━━━━━━━━━━━━━━━━━ HH:MM AM ━━ [▾/▸]
```

**Behavior**:
- **Active iteration**: `ContainerExpanded = true`, auto-scrolls as messages arrive
- **Completed iteration**: Automatically collapses (`ContainerExpanded = false`) when `IterationCompleted` event fires
- **User toggle**: Clicking `[▾]` collapses to `[▸]` (and vice versa) — toggles `ContainerExpanded`
- **Collapsed view**: Shows only the separator line with summary: `━━ Iteration #1 ━━ 5 tasks, 3 ✅ 1 ⚠️ 1 📝 ━━ [▸]`
- **Reports within iterations**: Also foldable (using `IsCollapsible`/`IsCollapsed` on `OfficeChatMessage`)

**Chronological ordering**: User messages injected mid-iteration appear **inline** in the chat between iteration containers, maintaining true chronological order. This means:
- Iteration #1 container
- User interruption message (between iterations)
- System acknowledgment
- Iteration #2 container (now incorporates the instruction)

### 7.3 Status Bar

A slim bar at the top showing real-time Manager state, with a `[📊]` button on the right edge to toggle the side panel:

| Component | Binding | Visual |
|-----------|---------|--------|
| Phase indicator | `CurrentPhase` → color-coded pill | 🔵 PLANNING, 🟢 EXECUTING, 🟡 SCHEDULING, 🟠 AGGREGATING, ⏳ RESTING, 🔴 ERROR |
| Iteration counter | `CurrentIteration` | "Iteration #3" |
| Task progress | `CompletedTasks/TotalTasks` | "Tasks: 4/7" with inline progress bar |
| Queue depth | `QueueDepth` | "Queue: 3" (hidden when 0) |
| Timer | `Remaining` or elapsed | "00:42" countdown or elapsed time |
| Side panel toggle | `IsSidePanelOpen` | `[📊]` button, highlighted when panel is open |

**Rest period countdown** is rendered inline in the chat as a system message with a progress bar:

```xaml
<!-- Countdown bar rendered as a system message in the chat stream -->
<Grid Visibility="{Binding IsResting, Converter={StaticResource BoolToVisibilityConverter}}">
    <ProgressBar Value="{Binding RestProgressPercent}" 
                 Maximum="100" Height="24" />
    <TextBlock HorizontalAlignment="Center" VerticalAlignment="Center">
        <Run Text="⏳ Next check in " />
        <Run Text="{Binding RestCountdownText}" FontWeight="Bold" />
    </TextBlock>
</Grid>
```

### 7.4 Chat Message Rendering

- **User messages**: Right-aligned, green accent border
- **Manager messages**: Left-aligned, blue accent border, Markdown rendered
- **Assistant messages**: Left-aligned, indented under iteration container, per-assistant color accent, collapsible
- **System messages**: Center-aligned, grey, smaller font (instruction acknowledgments, phase changes)
- **Reports**: Full-width card within iteration container, bordered, collapsible, Markdown rendered
- **Clarification Q&A**: Manager question appears as a message; when `IsWaitingForClarification` is true, the input area shows a highlighted border indicating the Manager is waiting for a response

**Markdown Rendering**: Use existing `ChatView.xaml` approach — render Markdown to `FlowDocument` or use `RichTextBox` with a Markdown-to-XAML converter. For phase 1, plain text with code formatting. For phase 2, integrate a Markdown rendering library (e.g., `Markdig` + custom WPF renderer or `MdXaml`).

### 7.5 Fly-In / Fly-Out Animated Side Panel

The side panel is an **overlay** that slides in from the right edge of the window, sitting on top of the chat plane. It does NOT resize the chat — it overlays with a dimmed backdrop.

**Trigger**: Click `[📊]` in the status bar, or programmatically via `IsSidePanelOpen`.

**Close**: Click `[✕]` close button, click the dimmed backdrop area, or press `Escape`.

**Animation Spec**:

```xaml
<!-- Side panel container with slide-in/slide-out animation -->
<Border x:Name="SidePanel"
        Width="400"
        HorizontalAlignment="Right"
        Background="{StaticResource PanelBackground}"
        RenderTransformOrigin="1,0.5">
    <Border.RenderTransform>
        <TranslateTransform x:Name="SidePanelTranslate" X="400" />
    </Border.RenderTransform>
</Border>

<!-- Dimmed backdrop (click to close) -->
<Border x:Name="Backdrop"
        Background="#40000000"
        Visibility="{Binding IsSidePanelOpen, Converter={StaticResource BoolToVisibilityConverter}}"
        MouseDown="Backdrop_MouseDown" />

<!-- Slide-in animation (300ms, EaseOutQuart) -->
<Storyboard x:Key="SlideIn">
    <DoubleAnimation
        Storyboard.TargetName="SidePanelTranslate"
        Storyboard.TargetProperty="X"
        From="400" To="0"
        Duration="0:0:0.3">
        <DoubleAnimation.EasingFunction>
            <QuarticEase EasingMode="EaseOut" />
        </DoubleAnimation.EasingFunction>
    </DoubleAnimation>
</Storyboard>

<!-- Slide-out animation (250ms, EaseIn) -->
<Storyboard x:Key="SlideOut">
    <DoubleAnimation
        Storyboard.TargetName="SidePanelTranslate"
        Storyboard.TargetProperty="X"
        From="0" To="400"
        Duration="0:0:0.25">
        <DoubleAnimation.EasingFunction>
            <QuarticEase EasingMode="EaseIn" />
        </DoubleAnimation.EasingFunction>
    </DoubleAnimation>
</Storyboard>
```

**Panel width**: 400px fixed. On windows narrower than 800px, panel takes 50% width.

### 7.6 Side Panel Sections

The side panel contains 4 vertically stacked sections, each collapsible:

```
┌─── Side Panel ─────────────────────────── [✕] ──┐
│                                                   │
│  ┌─ 💭 Live Commentary ─────────────────── [▾] ┐ │
│  │                                              │ │
│  │  🔵 [MANAGER] Building execution plan...     │ │
│  │  🟢 [MANAGER] Querying ServiceNow API...     │ │
│  │  🟡 [MANAGER] 5 events found, scheduling...  │ │
│  │  🟠 [ASST #1] Creating session for INC001... │ │
│  │  🟠 [ASST #2] Fetching runbook for INC002... │ │
│  │  🟠 [ASST #3] Adding triage note to INC003.. │ │
│  │  ✅ [ASST #3] INC003 triage note added       │ │
│  │  🟠 [ASST #1] Calling restart-pool tool...   │ │
│  │  ✅ [ASST #1] INC001 remediated successfully  │ │
│  │  ❌ [ASST #2] INC002 remediation failed       │ │
│  │  🟡 [MANAGER] Slot freed, dequeuing INC004   │ │
│  │  🟠 [ASST #3] Starting INC004...             │ │
│  │  ▼ (auto-scrolling)                          │ │
│  │                                              │ │
│  └──────────────────────────────────────────────┘ │
│                                                   │
│  ┌─ ⚙️ Configuration ──────────────────── [▾] ┐ │
│  │  Interval: [5___] min                        │ │
│  │  Pool size: [3___] agents                    │ │
│  │  Model: [gpt-4o_________▾]                   │ │
│  │  [⏸ Pause] [⏹ Stop] [🔄 Reset Session]      │ │
│  └──────────────────────────────────────────────┘ │
│                                                   │
│  ┌─ 📊 Event Log ──────────────────────── [▾] ┐ │
│  │  10:34:12 TaskAssigned    INC004 → A1       │ │
│  │  10:34:10 TaskDequeued    INC004            │ │
│  │  10:33:58 AssistantCompleted A3             │ │
│  │  10:33:45 TaskQueued      INC005            │ │
│  │  10:33:45 TaskQueued      INC004            │ │
│  │  10:33:44 TaskAssigned    INC003 → A3       │ │
│  │  10:33:44 TaskAssigned    INC002 → A2       │ │
│  │  10:33:44 TaskAssigned    INC001 → A1       │ │
│  │  10:33:42 EventsFetched   count=5           │ │
│  │  ...                                        │ │
│  └──────────────────────────────────────────────┘ │
│                                                   │
│  ┌─ 📈 Iteration Stats ────────────────── [▾] ┐ │
│  │  Completed Iterations: 3                     │ │
│  │  Total Tasks Done: 14                        │ │
│  │  Success Rate: 92%                           │ │
│  │  Avg Task Duration: 38s                      │ │
│  └──────────────────────────────────────────────┘ │
│                                                   │
└───────────────────────────────────────────────────┘
```

#### 7.6.1 💭 Live Commentary Section

The Live Commentary section provides a **real-time, auto-scrolling stream** of natural-language progress updates from the Manager and Assistants — similar to modern AI "thinking" indicators that show what agents are doing as it happens.

**Visual design**:
- Each entry: `[emoji] [AGENT_NAME] message...`
- Emoji determined by `CommentaryType`: 🔵 Planning, 🟢 Discovery, 🟡 Scheduling, 🟠 Working, ✅ Success, ⚠️ Warning, ❌ Error
- Agent name color-coded: Manager = blue, Assistants = per-index color from `OfficeColorScheme`
- Auto-scrolls to bottom as new entries arrive (controlled by `AutoScrollCommentary`)
- Max visible entries: 200 (older entries trimmed for performance)
- Monospace font, compact line height for information density

**Data flow**: `CommentaryEvent` → `OfficeViewModel.HandleEvent` → `Dispatcher.Invoke(() => LiveCommentaries.Add(commentary))`

#### 7.6.2 ⚙️ Configuration Section

Runtime-editable controls for the active office session:
- **Interval**: Number input, calls `UpdateCheckInterval()` on change
- **Pool size**: Number input (only effective on next iteration if assistants are active)
- **Model selector**: Dropdown for Manager model
- **Action buttons**: Pause (with duration input), Stop, Reset Session

#### 7.6.3 📊 Event Log Section

Structured event log (same as `IOfficeEventLog`), displayed in reverse-chronological order:
- Timestamp + EventType + detail
- Filterable by event type (optional toggle)
- Scrollable, max 500 entries visible

#### 7.6.4 📈 Iteration Stats Section

Aggregate statistics computed from iteration history:
- Completed iterations count
- Total tasks completed
- Success rate percentage
- Average task duration

### 7.7 ViewModel Design — `OfficeViewModel`

```csharp
public class OfficeViewModel : ViewModelBase
{
    // === Chat Plane ===
    public ObservableCollection<OfficeChatMessage> Messages { get; }

    // === Status Bar ===
    public ManagerPhase CurrentPhase { get; set; }
    public int CurrentIteration { get; set; }
    public int CompletedTasks { get; set; }
    public int TotalTasks { get; set; }
    public int QueueDepth { get; set; }
    public bool IsResting { get; set; }
    public double RestProgressPercent { get; set; }
    public string RestCountdownText { get; set; }
    public bool IsRunning { get; set; }

    // === Side Panel ===
    public bool IsSidePanelOpen { get; set; }
    public ICommand ToggleSidePanelCommand { get; }

    // === Live Commentary (side panel) ===
    public ObservableCollection<LiveCommentary> LiveCommentaries { get; }
    public bool AutoScrollCommentary { get; set; } = true;

    // === Event Log (side panel) ===
    public ObservableCollection<OfficeEvent> EventLog { get; }

    // === Configuration (side panel) ===
    public int CheckIntervalMinutes { get; set; }
    public int MaxAssistants { get; set; }

    // === Iteration Stats (side panel) ===
    public int TotalIterations { get; set; }
    public int TotalTasksCompleted { get; set; }
    public double SuccessRate { get; set; }
    public string AverageDuration { get; set; }

    // === Clarification State ===
    public bool IsPlanAwaitingApproval { get; set; }
    public bool IsClarificationPending { get; set; }
    public bool IsWaitingForClarification { get; set; }

    // === Commands ===
    public ICommand StartCommand { get; }
    public ICommand SendMessageCommand { get; }
    public ICommand ApprovePlanCommand { get; }
    public ICommand RejectPlanCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand UpdateIntervalCommand { get; }
    public ICommand ToggleCollapsedCommand { get; }          // For folding iteration containers
    public ICommand ToggleIterationContainerCommand { get; } // For folding iteration sections
}
```

### 7.8 MainWindow Integration

Add an "Office" tab button and view, following the same pattern as Agent Teams:

```csharp
// MainWindowViewModel.cs
public bool ShowOffice { get; set; }   // Toggle Office view visibility

// MainWindow.xaml — add button next to Team button
// <Button Content="🏢 Office" Command="{Binding ToggleOfficeCommand}" />

// MainWindow.xaml — add OfficeView with DataTrigger on ShowOffice
```

---

## 8. Code Flow

### 8.1 Flow: User Starts Office Session

```
User clicks "Start" with prompt
    │
    ▼
OfficeViewModel.StartCommand.Execute()
    │ Creates OfficeConfig from UI fields
    │
    ▼
IOfficeManagerService.StartAsync(config)
    │
    ├── Validate config (non-empty prompt, valid interval, etc.)
    ├── EnsureManagerSessionAsync()
    │   ├── Create Session model (manager-{configId})
    │   ├── Set system prompt (see §6.1)
    │   └── Call ICopilotService to create session
    ├── TransitionTo(Clarifying)
    ├── Send initial prompt: "User objective: {prompt}. BEGIN CLARIFICATION."
    ├── Receive response from LLM
    ├── ParseClarificationResponse()
    │   ├── If contains questions → Raise ClarificationRequested event
    │   │   → Stay in Clarifying
    │   │   → Add questions to chat as Manager message
    │   │   → UI shows input field for user response
    │   │
    │   └── If "READY_TO_PLAN" → TransitionTo(Planning)
    │       → PlanTaskAsync()
    │       → Parse plan from LLM
    │       → TransitionTo(AwaitingApproval)
    │       → Add plan to chat as Manager message with Approve/Reject buttons
    │
    └── Raise ManagerStarted event
```

### 8.2 Flow: Iteration Loop (Single Iteration)

```
RunIterationLoopAsync() — iteration N
    │
    ├── AbsorbInjectedInstructions()
    │   ├── Drain _context.InjectedInstructions
    │   ├── Append to _context.EffectivePrompt
    │   └── Update manager session system prompt if needed
    │
    ├── TransitionTo(FetchingEvents)
    │   ├── Send to Manager LLM: "CHECK FOR EVENTS. Iteration #{N}. {EffectivePrompt}"
    │   ├── Receive JSON response
    │   ├── Parse events list
    │   └── If no events → Add "No events found" chat message → skip to rest
    │
    ├── TransitionTo(Scheduling)
    │   ├── For each event, create AssistantTask:
    │   │   ├── Title = event.title
    │   │   ├── Prompt = build detailed prompt with event context
    │   │   ├── Priority = event.priority
    │   │   ├── Category = event.category
    │   │   └── Metadata = event.metadata
    │   ├── Log SchedulingDecision for each task
    │   └── Add scheduling summary to chat as Manager message
    │
    ├── TransitionTo(Executing)
    │   ├── results = await _assistantPool.ExecuteTasksAsync(tasks, config)
    │   │
    │   │   Inside AssistantPool:
    │   │   ├── For each task (sorted by priority):
    │   │   │   ├── await semaphore.WaitAsync()  [blocks if pool full]
    │   │   │   ├── Create AssistantAgent
    │   │   │   ├── Raise AssistantSpawned event
    │   │   │   ├── result = await agent.ExecuteAsync(task, config)
    │   │   │   │   ├── Create ephemeral Session
    │   │   │   │   ├── Send task prompt
    │   │   │   │   ├── Collect response
    │   │   │   │   ├── Parse into AssistantResult
    │   │   │   │   └── Return result
    │   │   │   ├── Raise AssistantCompleted/Failed event
    │   │   │   ├── Add assistant result to chat (collapsible)
    │   │   │   ├── await agent.DisposeAsync()
    │   │   │   ├── Raise AssistantDisposed event
    │   │   │   └── semaphore.Release()  [next queued task unblocks]
    │   │   └── Return all results
    │   │
    │   └── Update TotalTasks/CompletedTasks on each completion
    │
    ├── TransitionTo(Aggregating)
    │   ├── Build aggregation prompt:
    │   │   "AGGREGATE RESULTS for Iteration #{N}.
    │   │    Tasks completed: {count}
    │   │    Results: {JSON of AssistantResult list}
    │   │    Previous learnings: {context.Learnings}
    │   │    Produce: 1) Per-task summary 2) Statistics 3) Recommendations 4) Learnings"
    │   ├── Send to Manager LLM
    │   ├── Parse IterationReport
    │   ├── Store report in context
    │   ├── Update _context.PreviousIterationSummary
    │   ├── Add report to chat as Manager message (collapsible, Markdown)
    │   └── Raise IterationCompleted event
    │
    ├── TransitionTo(Resting)
    │   ├── _context.CompletedIterations++
    │   ├── interval = TimeSpan.FromMinutes(config.CheckIntervalMinutes)
    │   ├── await _scheduler.WaitForNextIterationAsync(interval)
    │   │   ├── Every 1s: Raise RestCountdownTick → UI updates countdown
    │   │   └── Completes when: timer elapsed OR CancelRest() called
    │   └── Add "Next check starting..." system message
    │
    └── Loop back to top (next iteration)
```

### 8.3 Flow: User Injects Instruction Mid-Run

The instruction injection flow includes **Manager clarity evaluation**: the Manager LLM assesses whether the injected instruction is clear enough to act on. If not, the Manager initiates a multi-turn clarification conversation inline in the chat — all while the current iteration (if any) continues in the background.

```
User types in Office input: "Also check for stale branches"
    │
    ▼
OfficeViewModel.SendMessageCommand.Execute()
    │
    ├── Add User message to chat (always, regardless of phase)
    │
    ├── If CurrentPhase == Clarifying:
    │   └── IOfficeManagerService.RespondToClarificationAsync(input)
    │       ├── Send user response to Manager LLM
    │       ├── Manager evaluates: more questions needed?
    │       │   ├── Yes → Raise ClarificationRequested event
    │       │   │         → Add Manager question to chat
    │       │   │         → Stay in Clarifying, IsWaitingForClarification = true
    │       │   │
    │       │   └── No → TransitionTo(Planning)
    │       │            → Proceed to plan generation
    │       └── Store Q&A in _context.ClarificationHistory
    │
    ├── Else If IsWaitingForClarification == true (mid-run clarification):
    │   └── IOfficeManagerService.RespondToClarificationAsync(input)
    │       ├── Send user response to Manager LLM with clarification context
    │       ├── Manager evaluates: instruction now clear?
    │       │   ├── No → another clarification question → chat continues
    │       │   │
    │       │   └── Yes → build refined instruction from Q&A exchange
    │       │            → _context.InjectedInstructions.Add(refinedInstruction)
    │       │            → IsWaitingForClarification = false
    │       │            → Add System message: "📝 Refined instruction queued"
    │       └── All Q&A appears inline in the chat chronologically
    │
    ├── Else (any other phase, instruction is straightforward):
    │   ├── IOfficeManagerService.InjectInstructionAsync(input)
    │   │
    │   │   Inside InjectInstructionAsync():
    │   │   ├── Send instruction to Manager LLM for clarity evaluation:
    │   │   │   "USER INSTRUCTION: {input}. 
    │   │   │    Evaluate: Is this clear enough to act on?
    │   │   │    If clear, respond: CLEAR
    │   │   │    If unclear, respond: CLARIFY: {your question}"
    │   │   │
    │   │   ├── Parse Manager LLM response:
    │   │   │   ├── If "CLEAR":
    │   │   │   │   ├── _context.InjectedInstructions.Add(input)
    │   │   │   │   ├── Raise InstructionInjected event
    │   │   │   │   └── Add System message: "📝 Instruction queued for next iteration"
    │   │   │   │
    │   │   │   └── If "CLARIFY: {question}":
    │   │   │       ├── Add Manager question to chat
    │   │   │       ├── Raise ClarificationRequested event
    │   │   │       ├── IsWaitingForClarification = true
    │   │   │       └── Wait for user response (next SendMessage will route here)
    │   │   │
    │   │   └── Note: This LLM call happens on the Manager session
    │   │          which is safe because the main loop is either:
    │   │          - In Resting phase (scheduler waiting, no LLM calls)
    │   │          - In Executing phase (assistants have their own sessions)
    │   │          For FetchingEvents/Aggregating, the instruction is simply
    │   │          queued without clarity evaluation (absorbed next iteration).
    │   │
    │   └── Return
    │
    └── On next iteration, AbsorbInjectedInstructions() applies all queued instructions
```

### 8.6 Flow: Manager Clarification During Interruption

This flow shows the full multi-turn conversation when a user injects an ambiguous instruction while the Manager is running. The key insight: **clarification happens inline in the chat** while the iteration continues in the background.

**Example Scenario**: User says "Monitor the repos too" during Iteration #2 execution.

```
Chat Plane (chronological)
──────────────────────────────────────────────────────

  ━━ Iteration #2 ━━━━━━━━━━━━━━━━━━━━ 10:37 AM ━━ [▾]
  │
  │  [MANAGER] SCHEDULING
  │  Found 3 incidents. Assigning to pool...
  │
  │  ▸ [ASSISTANT #1] INC006: P2 Disk space warning...   (working)
  │  ▸ [ASSISTANT #2] INC007: P1 API gateway down...     (working)
  │

  [USER] 10:38 AM                              ← User interrupts mid-execution
  Monitor the repos too

  [MANAGER] 10:38 AM — CLARIFICATION           ← Manager needs more info
  I'd like to help monitor repos. A few questions:
  1. Which repositories? (specific names, org-wide, or all repos in working dir?)
  2. What should I monitor for? (PRs, issues, commits, branch staleness?)

  │  ▸ [ASSISTANT #1] INC006: ✅ Disk cleaned   ← Assistants keep working
  │

  [USER] 10:39 AM                              ← User responds to clarification
  All repos under the "platform" org. Monitor for new PRs and stale branches.

  [MANAGER] 10:39 AM — CLARIFICATION
  Got it. One more: what defines "stale" for branches — 
  30 days with no commits, or 30 days since last merge?

  │  ▸ [ASSISTANT #2] INC007: ✅ Gateway restarted   ← More results come in
  │

  [USER] 10:39 AM
  30 days since last commit

  [SYSTEM] 📝 Refined instruction queued for next iteration:
  "Monitor all repos under 'platform' org for new PRs and stale branches 
   (>30 days since last commit)"

  │
  │  [MANAGER] REPORT                           ← Iteration #2 report
  │  ## Iteration #2 Summary ...
  │
  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  ━━ Iteration #3 ━━━━━━━━━━━━━━━━━━━━ 10:42 AM ━━ [▾]
  │
  │  [MANAGER] SCHEDULING
  │  Found 2 incidents + 5 new PRs + 3 stale branches. Assigning...
  │                                               ↑ New instruction incorporated
```

**Backend Flow**:

```
User sends: "Monitor the repos too"
    │
    ├── OfficeViewModel.SendMessageCommand → InjectInstructionAsync("Monitor the repos too")
    │
    ├── Manager LLM evaluates → "CLARIFY: Which repos? What to monitor?"
    │   ├── IsWaitingForClarification = true
    │   ├── Manager question added to chat
    │   └── Background: AssistantPool continues executing (separate sessions)
    │
    ├── User sends: "All repos under platform org..."
    │   ├── Routed to RespondToClarificationAsync()
    │   ├── Manager LLM: "CLARIFY: Define stale?"
    │   └── Another question added to chat
    │
    ├── User sends: "30 days since last commit"
    │   ├── Routed to RespondToClarificationAsync()
    │   ├── Manager LLM: "CLEAR" → builds refined instruction
    │   ├── IsWaitingForClarification = false
    │   ├── Refined instruction added to _context.InjectedInstructions
    │   └── System message confirms queuing
    │
    └── Next iteration: AbsorbInjectedInstructions() picks up refined instruction
```

**Thread Safety Note**: The Manager LLM calls for clarification evaluation happen safely because:
- During **Executing** phase: the Manager session is idle (assistants use their own sessions)
- During **Resting** phase: the Manager session is idle (scheduler is just a timer)
- During **FetchingEvents/Aggregating**: the Manager session is busy, so the instruction is simply queued without evaluation (will be evaluated at next absorption)

### 8.4 Flow: User Changes Interval

```
User changes interval spinner from 5 to 10 minutes
    │
    ▼
OfficeViewModel.CheckIntervalMinutes setter
    │
    ├── _officeManagerService.UpdateCheckInterval(10)
    │   ├── config.CheckIntervalMinutes = 10
    │   ├── If currently resting:
    │   │   └── _scheduler.OverrideRestDurationAsync(TimeSpan.FromMinutes(10))
    │   └── Else: takes effect on next rest period
    │
    └── Add System message: "⏱️ Check interval updated to 10 minutes"
```

### 8.5 Flow: Reset Session

```
User clicks "Reset Session"
    │
    ▼
OfficeViewModel.ResetCommand.Execute()
    │
    ├── Confirmation dialog: "This will cancel all active tasks and dispose all sessions. Continue?"
    │
    ▼
IOfficeManagerService.ResetAsync()
    │
    ├── _loopCts.Cancel()                    // Stop iteration loop
    ├── await _assistantPool.CancelAllAsync() // Cancel all active + queued
    ├── _scheduler.CancelRest()              // Cancel any rest timer
    ├── Dispose manager session via ICopilotService
    ├── Clear _context
    ├── Clear event log
    ├── TransitionTo(Idle)
    ├── Raise ManagerReset event
    │
    ▼
OfficeViewModel handles event
    ├── Clear Messages collection
    ├── Reset all statistics
    ├── Reset UI to initial state
    └── Ready for new Start
```

---

## 9. Plan of Action — Phased Implementation

### Phase 1: Foundation (Week 1-2)

**Goal**: Minimal viable Manager loop with single-assistant execution.

**Tasks**:
1. Create `CopilotAgent.Office` project with `.csproj`
2. Implement all models: `ManagerPhase`, `OfficeConfig`, `ManagerContext`, `AssistantTask`, `AssistantResult`, `IterationReport`, `SchedulingDecision`, `OfficeChatMessage`, `OfficeColorScheme`
3. Implement events: `OfficeEventType`, `OfficeEvent` hierarchy
4. Implement `IOfficeEventLog` / `OfficeEventLog` (in-memory)
5. Implement `IAssistantAgent` / `AssistantAgent` (ephemeral worker)
6. Implement `IAssistantPool` / `AssistantPool` (semaphore-based pool, single-task initially)
7. Implement `IIterationScheduler` / `IterationScheduler` (countdown timer)
8. Implement `IOfficeManagerService` / `OfficeManagerService`:
   - State machine (Idle → Planning → AwaitingApproval → FetchingEvents → Executing → Aggregating → Resting → loop)
   - Skip clarification in Phase 1 (hardcoded READY_TO_PLAN)
   - Basic aggregation (no LLM, just collect results)
9. Add `OfficeSettings` to `CopilotAgent.Core/Models`
10. Register services in `App.xaml.cs`

**Deliverable**: Manager can start, plan (simple), execute 1 iteration with 1 assistant, rest, repeat. Console/debug output only.

### Phase 2: UI Shell (Week 2-3)

**Goal**: Basic Office tab in MainWindow with chat view.

**Tasks**:
1. Create `OfficeView.xaml` — basic chat layout with input area
2. Create `OfficeViewModel.cs` — bindings for messages, phase, commands
3. Add "🏢 Office" button to `MainWindow.xaml`
4. Add `ShowOffice` toggle to `MainWindowViewModel`
5. Wire up Start, Stop, Reset commands
6. Display chat messages (plain text, no Markdown yet)
7. Display Manager status bar (phase indicator, iteration counter)
8. Display basic countdown during rest period

**Deliverable**: User can start Office from UI, see messages appear, watch iteration loop, see countdown.

### Phase 3: Full Manager Intelligence (Week 3-4)

**Goal**: Complete LLM-driven Manager with clarification and planning.

**Tasks**:
1. Implement clarification flow (Manager asks questions, user responds)
2. Implement LLM-driven planning (Manager builds plan from prompt + tools)
3. Implement plan approval/rejection UI
4. Implement LLM-driven event fetching (JSON parsing from LLM)
5. Implement LLM-driven aggregation (narrative report from LLM)
6. Implement `AbsorbInjectedInstructions` for mid-run prompt changes
7. Implement Manager system prompt template with context evolution
8. Handle LLM errors with retry and reconnect

**Deliverable**: Full intelligent Manager loop with real LLM interaction.

### Phase 4: Pool Scheduling & Queue (Week 4-5)

**Goal**: Multi-assistant concurrency with queue-based overflow.

**Tasks**:
1. Enhance `AssistantPool` with configurable concurrency (MaxAssistants)
2. Implement priority-based task ordering
3. Add scheduling decision logging
4. Implement queue depth limits and overflow handling
5. Update UI with:
   - Task progress indicator (N/M)
   - Queue depth indicator
   - Per-assistant status in event log
6. Implement graceful cancellation of queued tasks
7. Implement retry logic for failed assistant tasks

**Deliverable**: 3+ assistants executing concurrently with queue overflow.

### Phase 5: Rich UI (Week 5-6)

**Goal**: Industry-grade chat interface with all visual elements.

**Tasks**:
1. Implement Markdown rendering for chat messages (Markdig + WPF renderer)
2. Implement collapsible/foldable messages (assistant details, reports)
3. Implement color-coded sender names (Manager blue, Assistants orange/purple/etc.)
4. Implement side panel with:
   - Configuration controls (interval, pool size)
   - Event log (scrollable, filterable)
   - Iteration statistics
5. Implement rest period progress bar with percentage
6. Implement phase indicator with color-coded pills
7. Polish responsive layout and animations

**Deliverable**: Polished, professional UI matching the design mockup.

### Phase 6: Advanced Features (Week 6-7)

**Goal**: Dynamic controls, persistence, and edge cases.

**Tasks**:
1. Implement dynamic interval change (takes effect immediately if resting)
2. Implement pause/resume with custom duration
3. Implement instruction injection with confirmation
4. Implement iteration history viewer (browse past reports)
5. Add persistence: save/restore OfficeConfig, iteration reports
6. Implement OfficeConfigDialog for initial setup
7. Handle edge cases:
   - App shutdown during execution → graceful dispose
   - Network disconnects during assistant work → retry
   - User rapid-fires multiple instructions → batch at boundary

**Deliverable**: Production-ready feature set.

### Phase 7: Testing & Hardening (Week 7-8)

**Goal**: Comprehensive tests and robustness.

**Tasks**:
1. Unit tests:
   - `OfficeManagerService` state machine transitions
   - `AssistantPool` concurrency and queue behavior
   - `IterationScheduler` countdown accuracy
   - `OfficeEventLog` query correctness
   - All model serialization/deserialization
2. Integration tests:
   - Full iteration loop with mock `ICopilotService`
   - Pool overflow with 10 tasks and 3 assistants
   - Cancellation at every phase
   - Instruction injection timing
3. Stress tests:
   - 50 tasks, 5 assistants, 10 iterations
   - Rapid interval changes
   - Concurrent Start/Stop/Reset
4. Memory leak testing:
   - Verify all sessions disposed after each iteration
   - Verify no event handler leaks

**Deliverable**: High-confidence test suite, production-hardened code.

---

## 10. Appendix

### 10.1 Comparison: Office vs. Teams Architecture Decisions

| Decision | Agent Teams Approach | Agent Office Approach | Rationale |
|----------|---------------------|----------------------|-----------|
| Manager session | Per-task, disposed after | Long-lived, persists | Office needs context continuity across iterations |
| Worker/Assistant creation | Batch-created for all chunks | On-demand from pool | Pool + queue enables backpressure |
| Concurrency model | `Task.WhenAll` all workers | `SemaphoreSlim` gated | Office needs bounded concurrency with overflow |
| State machine | 7 phases | 11 phases (adds FetchingEvents, Scheduling, Resting, Stopped) | Office has richer lifecycle |
| Event frequency | Per-batch | Per-task + per-second countdown | Office UI needs higher-fidelity updates |
| User interaction | One-shot (submit → done) | Continuous (inject, pause, interval change) | Office is interactive during execution |

### 10.2 Settings Model (CopilotAgent.Core)

```csharp
namespace CopilotAgent.Core.Models;

/// <summary>
/// Persisted settings for the Agent Office feature.
/// </summary>
public class OfficeSettings
{
    public int DefaultCheckIntervalMinutes { get; set; } = 5;
    public int DefaultMaxAssistants { get; set; } = 3;
    public string DefaultManagerModelId { get; set; } = string.Empty;
    public string DefaultAssistantModelId { get; set; } = string.Empty;
    public int DefaultAssistantTimeoutMinutes { get; set; } = 10;
    public int DefaultMaxRetries { get; set; } = 1;
    public bool DefaultAutoApprovePlan { get; set; } = false;
    public int DefaultMaxQueueDepth { get; set; } = 50;
}
```

### 10.3 Project References

```xml
<!-- CopilotAgent.Office.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CopilotAgent.Core\CopilotAgent.Core.csproj" />
  </ItemGroup>
</Project>
```

```xml
<!-- CopilotAgent.App.csproj — add reference -->
<ProjectReference Include="..\CopilotAgent.Office\CopilotAgent.Office.csproj" />
```

### 10.4 Naming Conventions

Following existing codebase patterns:
- Interfaces: `I` prefix (`IOfficeManagerService`)
- Models: No prefix, in `Models/` folder
- Events: Suffix with `Event` (`AssistantEvent`, `PhaseChangedEvent`)
- Enums: PascalCase values (`ManagerPhase.FetchingEvents`)
- ViewModels: Suffix with `ViewModel` (`OfficeViewModel`)
- Views: Suffix with `View` or `Dialog` (`OfficeView`, `OfficeConfigDialog`)
- Services: Suffix with `Service` (`OfficeManagerService`)

### 10.5 Key Reuse Points from Existing Codebase

| Existing Component | Reuse In Office | How |
|-------------------|----------------|-----|
| `ICopilotService` | Manager + Assistant sessions | Direct dependency injection |
| `Session` model | Create manager/assistant sessions | Same model, different lifecycle |
| `ViewModelBase` | OfficeViewModel base | Inherit directly |
| `BoolToVisibilityConverter` | Phase-conditional UI | Existing converter |
| `StringToBrushConverter` | Color-coded messages | Existing converter |
| `AnsiParser` | Parse terminal output in assistant responses | Existing helper |
| `OrchestratorService` pattern | State machine in `OfficeManagerService` | Architectural pattern (not code copy) |
| `AgentPool` pattern | `AssistantPool` concurrency | SemaphoreSlim pattern (refined for queue) |
| `WorkerAgent` pattern | `AssistantAgent` lifecycle | Create → prompt → collect → dispose |

---

*End of Agent Office Design Document*