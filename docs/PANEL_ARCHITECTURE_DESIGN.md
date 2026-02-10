# Panel Discussion — Architecture & Design Specification

> **Version**: 1.0  
> **Status**: Greenfield Design — Pre-Implementation  
> **Date**: February 2026  
> **Scope**: `CopilotAgent.Panel` — Standalone multi-agent panel discussion system  
> **Audience**: Engineers implementing CopilotDesktop Panel Discussion feature  
> **Non-Negotiable**: ZERO regression to existing Agent Team, Agent Office, Chat, or Iterative Task features

---

## Table of Contents

**Part I — Architecture Foundation**

1. [Executive Vision & Constraints](#1-executive-vision--constraints)
2. [Non-Negotiable Principles](#2-non-negotiable-principles)
3. [Project Structure & Dependency Graph](#3-project-structure--dependency-graph)
4. [Technology Stack](#4-technology-stack)
5. [System Architecture](#5-system-architecture)

**Part II — Domain Model**

6. [Core Domain Entities](#6-core-domain-entities)
7. [Value Objects](#7-value-objects)
8. [Enumerations](#8-enumerations)
9. [Domain Events](#9-domain-events)
10. [Guard-Rail Policy](#10-guard-rail-policy)

**Part III — State Machine**

11. [Panel Session Lifecycle](#11-panel-session-lifecycle)
12. [State Machine Implementation](#12-state-machine-implementation)

**Part IV — Agent System**

13. [Agent Hierarchy](#13-agent-hierarchy)
14. [Head Agent](#14-head-agent)
15. [Moderator Agent](#15-moderator-agent)
16. [Panelist Agents](#16-panelist-agents)
17. [Agent Factory](#17-agent-factory)
18. [Convergence Detection](#18-convergence-detection)

**Part V — Orchestration Engine**

19. [Discussion Orchestrator](#19-discussion-orchestrator)
20. [Turn Management](#20-turn-management)
21. [Pause / Resume / Stop / Reset](#21-pause--resume--stop--reset)
22. [Conversation Memory Management](#22-conversation-memory-management)
23. [Knowledge Brief (Post-Discussion Context)](#23-knowledge-brief-post-discussion-context)

**Part VI — Resilience & Production**

24. [Circuit Breaker for Tools](#24-circuit-breaker-for-tools)
25. [Tool Sandboxing](#25-tool-sandboxing)
26. [Background Execution Model](#26-background-execution-model)
27. [Resource Cleanup & Memory Management](#27-resource-cleanup--memory-management)
28. [Error Recovery Playbook](#28-error-recovery-playbook)
29. [Cost Estimation & Budget Management](#29-cost-estimation--budget-management)

**Part VII — UI Architecture**

30. [Three-Pane Layout](#30-three-pane-layout)
31. [Settings & Commentary Mode](#31-settings--commentary-mode)
32. [Meta-Question Support](#32-meta-question-support)
33. [Live Visualization](#33-live-visualization)

**Part VIII — Integration & Testing**

34. [Integration with Existing Application](#34-integration-with-existing-application)
35. [Service Interface Contract](#35-service-interface-contract)
36. [Testing Strategy](#36-testing-strategy)
37. [Implementation Roadmap](#37-implementation-roadmap)
38. [Production Readiness Checklist](#38-production-readiness-checklist)

---

## 1. Executive Vision & Constraints

### 1.1 Product Vision

Build a **desktop-native, in-process, multi-agent panel discussion system** where a configurable number of AI agents collaborate, debate, and converge on complex analytical tasks — all orchestrated through a deterministic state machine and surfaced through a world-class, live-updating UI.

### 1.2 Constraints

| Attribute | Decision | Rationale |
|-----------|----------|-----------|
| **Target Platform** | Windows 10/11 (WPF, .NET 8 LTS) | Existing application stack |
| **Runtime** | In-process; core logic runs on background threads | Zero deployment friction, instant startup |
| **AI Backend** | Copilot SDK (`ICopilotService`) | Existing proven integration |
| **State Management** | Stateless library (NuGet) + JSON persistence | Deterministic FSM + existing persistence |
| **UI Framework** | WPF with MVVM (CommunityToolkit.Mvvm) | Consistent with existing tabs |
| **Distribution** | New tab in existing CopilotAgent.App | Zero standalone deployment |
| **Regression Policy** | ZERO changes to existing projects except `App.xaml.cs` DI registration and `MainWindow.xaml` tab addition | Non-negotiable |

### 1.3 Key Differentiators

1. **In-Process Orchestration** — Entire agent pipeline runs inside the desktop process. Zero network hops for orchestration logic.
2. **Full State Machine** — Every discussion phase is a first-class state with explicit transitions, guards, and side-effects.
3. **Live Commentary** — Users see exactly what every agent is doing, thinking, and calling — in real time with configurable verbosity.
4. **Follow-Up with Full Context** — After discussion concludes, the Head retains a compressed knowledge brief and can answer user questions without re-running the panel.
5. **Pause/Resume at Safe Points** — Users can pause mid-discussion and resume exactly where they left off.
6. **Structured Convergence** — Moderator produces JSON decisions with convergence scores, not just text.

---

## 2. Non-Negotiable Principles

| Principle | How We Apply It |
|-----------|-----------------|
| **KISS** | Every component does one thing. No god classes. No premature abstraction. |
| **SOLID** | Single responsibility per class. Open for extension (new tools, new agent roles). Depend on abstractions. |
| **Clean Architecture** | Domain layer has zero external dependencies. Infrastructure is swappable. UI is a thin presentation layer. |
| **Observable by Default** | Every state transition, every agent action, every tool call emits a structured event. |
| **Fail-Safe** | All LLM calls have timeouts, retries, and circuit breakers. The Moderator can kill a runaway discussion. |
| **Memory-Safe** | Deterministic disposal of every agent, every tool handle, every conversation buffer when a discussion ends. |
| **Zero Regression** | New `CopilotAgent.Panel` project touches NOTHING in existing `MultiAgent`, `Office`, or `Core` code. Only DI registration and tab addition in `App`. |
| **Codebase-First** | Reuse `ICopilotService`, `IToolApprovalService`, `IPersistenceService`, `ISessionManager` from `CopilotAgent.Core`. |

---

## 3. Project Structure & Dependency Graph

### 3.1 New Project

```
src/CopilotAgent.Panel/
├── CopilotAgent.Panel.csproj
├── Domain/
│   ├── Entities/
│   │   ├── PanelSession.cs
│   │   ├── AgentInstance.cs
│   │   └── PanelMessage.cs
│   ├── ValueObjects/
│   │   ├── PanelSessionId.cs
│   │   ├── ModelIdentifier.cs
│   │   ├── TurnNumber.cs
│   │   └── TokenBudget.cs
│   ├── Enums/
│   │   ├── PanelPhase.cs
│   │   ├── PanelTrigger.cs
│   │   ├── PanelAgentRole.cs
│   │   ├── PanelAgentStatus.cs
│   │   ├── CommentaryMode.cs
│   │   └── PanelMessageType.cs
│   ├── Events/
│   │   ├── PanelEvent.cs              (base)
│   │   ├── PhaseChangedEvent.cs
│   │   ├── AgentMessageEvent.cs
│   │   ├── AgentStatusChangedEvent.cs
│   │   ├── ToolCallEvent.cs
│   │   ├── ModerationEvent.cs
│   │   ├── CommentaryEvent.cs
│   │   ├── ProgressEvent.cs
│   │   └── ErrorEvent.cs
│   ├── Policies/
│   │   └── GuardRailPolicy.cs
│   └── Interfaces/
│       ├── IPanelOrchestrator.cs
│       ├── IPanelAgent.cs
│       ├── IPanelAgentFactory.cs
│       ├── IConvergenceDetector.cs
│       ├── IWorkChunkSelector.cs
│       └── IKnowledgeBriefService.cs
├── StateMachine/
│   └── PanelStateMachine.cs
├── Orchestration/
│   ├── PanelOrchestrator.cs
│   ├── TurnManager.cs
│   └── AgentSupervisor.cs
├── Agents/
│   ├── HeadAgent.cs
│   ├── ModeratorAgent.cs
│   ├── PanelistAgent.cs
│   ├── PanelAgentBase.cs
│   ├── PanelAgentFactory.cs
│   └── ConvergenceDetector.cs
├── Services/
│   ├── KnowledgeBriefService.cs
│   ├── PanelConversationManager.cs
│   ├── PanelToolRouter.cs
│   └── CostEstimationService.cs
├── Resilience/
│   ├── ToolCircuitBreaker.cs
│   └── PanelRetryPolicy.cs
└── Models/
    ├── PanelSettings.cs
    ├── PanelConfig.cs
    ├── ModeratorDecision.cs
    ├── CritiqueVerdict.cs
    ├── KnowledgeBrief.cs
    ├── CostEstimate.cs
    └── PanelistProfile.cs
```

### 3.2 Dependency Graph

```
CopilotAgent.App                    (EXISTING — minimal changes)
  ├── CopilotAgent.Panel            (NEW — the entire panel feature)
  ├── CopilotAgent.MultiAgent       (EXISTING — untouched)
  ├── CopilotAgent.Office           (EXISTING — untouched)
  ├── CopilotAgent.Core             (EXISTING — untouched, consumed by Panel)
  └── CopilotAgent.Persistence      (EXISTING — untouched, consumed by Panel)

CopilotAgent.Panel ──► CopilotAgent.Core      (shared models, services, interfaces)
CopilotAgent.Panel ──► Stateless              (NuGet: state machine library)
CopilotAgent.Panel ──► System.Reactive        (NuGet: IObservable event streaming)
```

### 3.3 What Changes in Existing Projects

| File | Change | Risk |
|------|--------|------|
| `CopilotAgent.App/App.xaml.cs` | Add DI registration for Panel services | Zero — additive only |
| `CopilotAgent.App/MainWindow.xaml` | Add Panel tab item | Zero — additive only |
| `CopilotAgent.App/MainWindow.xaml.cs` | Add Panel tab lazy initialization | Zero — additive only |
| `CopilotAgent.App/CopilotAgent.App.csproj` | Add `<ProjectReference>` to `CopilotAgent.Panel` | Zero — additive only |
| `CopilotAgent.Core/Models/AppSettings.cs` | Add `PanelSettings` property | Zero — additive, no existing field changes |

**NO changes** to any existing service, ViewModel, View, or model.

---

## 4. Technology Stack

### 4.1 Core Dependencies

| Component | Technology | NuGet Package | Justification |
|-----------|------------|---------------|---------------|
| **State Machine** | Stateless | `Stateless` | Battle-tested FSM. Supports triggers with parameters, async guards, serializable transitions. |
| **Event Streaming** | System.Reactive | `System.Reactive` | `IObservable<PanelEvent>` lets every UI component subscribe to exactly the events it needs. Automatic thread marshalling with `ObserveOn`. |
| **MVVM** | CommunityToolkit.Mvvm | `CommunityToolkit.Mvvm` | Already used by existing app. Source generators reduce boilerplate. |
| **DI** | Microsoft.Extensions.DI | `Microsoft.Extensions.DependencyInjection` | Already used by existing app. |
| **AI Backend** | Copilot SDK | Already referenced via `CopilotAgent.Core` | `ICopilotService` for all LLM interactions. |
| **Resilience** | Polly v8 | `Microsoft.Extensions.Http.Resilience` | Circuit breaker, retry, timeout policies for tool execution. |
| **Logging** | ILogger<T> | Already available | Structured logging consistent with existing features. |

### 4.2 Shared from CopilotAgent.Core (Reused, Not Duplicated)

| Service | Usage in Panel |
|---------|----------------|
| `ICopilotService` | All LLM calls (Head, Moderator, Panelists) |
| `ISessionManager` | Session creation and lifecycle |
| `IToolApprovalService` | Tool approval for panelist tool calls |
| `IApprovalQueue` | Serialized approval requests |
| `IPersistenceService` | Save/load panel settings and discussion history |
| `ChatMessage` | Message model for conversations |
| `Session` | Session configuration for Copilot SDK |
| `AppSettings` | Application-wide settings container |

---

## 5. System Architecture

```
┌────────────────────────────────────────────────────────────────────────┐
│                    CopilotAgent.App (WPF Shell)                        │
│  ┌────────────────┐ ┌────────────────┐ ┌───────────────────────────┐  │
│  │ AgentTeamView   │ │ OfficeView     │ │ PanelView (NEW)          │  │
│  │ (EXISTING)      │ │ (EXISTING)     │ │ ┌─────────────────────┐  │  │
│  │                 │ │                │ │ │ PanelViewModel      │  │  │
│  │                 │ │                │ │ │ (MVVM, Dirty Track) │  │  │
│  └────────┬────────┘ └────────┬───────┘ │ └────────┬────────────┘  │  │
│           │                   │         │          │               │  │
│           │                   │         │  IObservable<PanelEvent>  │  │
│           │                   │         │          │               │  │
│  ┌────────▼────────┐ ┌───────▼────────┐│ ┌────────▼────────────┐  │  │
│  │ OrchestratorSvc │ │ OfficeMgrSvc   ││ │ PanelOrchestrator   │  │  │
│  │ (EXISTING)      │ │ (EXISTING)     ││ │ (NEW)               │  │  │
│  └────────┬────────┘ └───────┬────────┘│ │ ┌─────────────────┐ │  │  │
│           │                   │         │ │ │ Stateless FSM   │ │  │  │
│           │                   │         │ │ │ AgentSupervisor │ │  │  │
│           │                   │         │ │ │ TurnManager     │ │  │  │
│           │                   │         │ │ └─────────────────┘ │  │  │
│           │                   │         │ └────────┬────────────┘  │  │
├───────────┴───────────────────┴─────────┴──────────┴──────────────┤  │
│                        CopilotAgent.Core (SHARED — UNTOUCHED)      │  │
│  ┌──────────────┐  ┌───────────────┐  ┌───────────────────────┐   │  │
│  │ICopilotService│  │ISessionManager│  │IToolApprovalService   │   │  │
│  └──────────────┘  └───────────────┘  └───────────────────────┘   │  │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 6. Core Domain Entities

### 6.1 PanelSession — Aggregate Root

```csharp
namespace CopilotAgent.Panel.Domain.Entities;

/// <summary>
/// Aggregate root for a panel discussion. Encapsulates all state for one discussion
/// session including phase, agents, messages, and configuration.
/// 
/// IMMUTABLE AFTER COMPLETION: Once phase reaches Completed/Stopped/Failed,
/// only Reset transitions are allowed.
/// 
/// THREAD SAFETY: Internal collections are guarded by PanelOrchestrator.
/// Direct mutation is only allowed from the orchestrator's execution context.
/// </summary>
public sealed class PanelSession : IAsyncDisposable
{
    public PanelSessionId Id { get; }
    public PanelPhase Phase { get; private set; }
    public string OriginalUserPrompt { get; }
    public string? RefinedTopicOfDiscussion { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public GuardRailPolicy GuardRails { get; }

    private readonly List<PanelMessage> _messages = [];
    public IReadOnlyList<PanelMessage> Messages => _messages.AsReadOnly();

    private readonly List<AgentInstance> _agents = [];
    public IReadOnlyList<AgentInstance> Agents => _agents.AsReadOnly();

    public PanelSession(
        PanelSessionId id,
        string userPrompt,
        GuardRailPolicy guardRails)
    {
        Id = id;
        OriginalUserPrompt = userPrompt 
            ?? throw new ArgumentNullException(nameof(userPrompt));
        GuardRails = guardRails 
            ?? throw new ArgumentNullException(nameof(guardRails));
        Phase = PanelPhase.Idle;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void TransitionTo(PanelPhase newPhase)
    {
        Phase = newPhase;
        if (newPhase is PanelPhase.Completed or PanelPhase.Stopped or PanelPhase.Failed)
            CompletedAt = DateTimeOffset.UtcNow;
    }

    public void SetRefinedTopic(string topic) =>
        RefinedTopicOfDiscussion = topic 
            ?? throw new ArgumentNullException(nameof(topic));

    public void AddMessage(PanelMessage message) => _messages.Add(message);
    public void RegisterAgent(AgentInstance agent) => _agents.Add(agent);
    public void UnregisterAgent(AgentInstance agent) => _agents.Remove(agent);

    public async ValueTask DisposeAsync()
    {
        _messages.Clear();
        foreach (var agent in _agents)
            agent.MarkDisposed();
        _agents.Clear();
    }
}
```

### 6.2 AgentInstance — Lightweight Agent Descriptor

```csharp
namespace CopilotAgent.Panel.Domain.Entities;

/// <summary>
/// Tracks the identity and status of an agent within a panel session.
/// This is a domain descriptor — the actual LLM session is managed by PanelAgentBase.
/// </summary>
public sealed class AgentInstance
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public PanelAgentRole Role { get; }
    public ModelIdentifier Model { get; }
    public PanelAgentStatus Status { get; private set; } = PanelAgentStatus.Created;
    public int TurnsCompleted { get; private set; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    public AgentInstance(string name, PanelAgentRole role, ModelIdentifier model)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Role = role;
        Model = model;
    }

    public void Activate() => Status = PanelAgentStatus.Active;
    public void SetThinking() => Status = PanelAgentStatus.Thinking;
    public void SetIdle() => Status = PanelAgentStatus.Idle;
    public void IncrementTurn() => TurnsCompleted++;
    public void MarkDisposed() => Status = PanelAgentStatus.Disposed;
}
```

### 6.3 PanelMessage — Discussion Message

```csharp
namespace CopilotAgent.Panel.Domain.Entities;

/// <summary>
/// Immutable record representing a single message in the panel discussion.
/// Uses record semantics for value equality and immutability.
/// </summary>
public sealed record PanelMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required PanelSessionId SessionId { get; init; }
    public required Guid AuthorAgentId { get; init; }
    public required string AuthorName { get; init; }
    public required PanelAgentRole AuthorRole { get; init; }
    public required string Content { get; init; }
    public required PanelMessageType Type { get; init; }
    public Guid? InReplyTo { get; init; }
    public IReadOnlyList<ToolCallRecord>? ToolCalls { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Factory method for creating messages with required fields.
    /// </summary>
    public static PanelMessage Create(
        PanelSessionId sessionId,
        Guid authorId,
        string authorName,
        PanelAgentRole role,
        string content,
        PanelMessageType type,
        Guid? inReplyTo = null,
        IReadOnlyList<ToolCallRecord>? toolCalls = null)
    {
        return new PanelMessage
        {
            SessionId = sessionId,
            AuthorAgentId = authorId,
            AuthorName = authorName,
            AuthorRole = role,
            Content = content,
            Type = type,
            InReplyTo = inReplyTo,
            ToolCalls = toolCalls
        };
    }
}

/// <summary>
/// Records a tool invocation by an agent for audit and UI display.
/// </summary>
public sealed record ToolCallRecord(
    string ToolName,
    string Input,
    string? Output,
    bool Succeeded,
    TimeSpan Duration);
```

---

## 7. Value Objects

```csharp
namespace CopilotAgent.Panel.Domain.ValueObjects;

/// <summary>
/// Strongly-typed session identifier. Prevents accidental mixing with other GUIDs.
/// </summary>
public readonly record struct PanelSessionId(Guid Value)
{
    public static PanelSessionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N")[..8];
}

/// <summary>
/// Identifies a specific AI model by provider and name.
/// Immutable — safe to pass by value across threads.
/// </summary>
public readonly record struct ModelIdentifier(string Provider, string ModelName)
{
    public override string ToString() => $"{Provider}/{ModelName}";
}

/// <summary>
/// Type-safe turn counter with overflow protection.
/// </summary>
public readonly record struct TurnNumber(int Value)
{
    public TurnNumber Increment() => new(Value + 1);
    public bool Exceeds(int max) => Value >= max;
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Token budget for cost management. Tracks per-turn and total limits.
/// </summary>
public readonly record struct TokenBudget(int MaxTokensPerTurn, int MaxTotalTokens)
{
    public bool IsExceeded(int currentTurnTokens, int totalTokens) =>
        currentTurnTokens > MaxTokensPerTurn || totalTokens > MaxTotalTokens;
}
```

---

## 8. Enumerations

```csharp
namespace CopilotAgent.Panel.Domain.Enums;

/// <summary>
/// The 11-state lifecycle of a panel discussion session.
/// Every user action maps to a state-machine trigger; no "magic" booleans.
/// 
/// Derived from competitive analysis of 5 architectural proposals.
/// The 11-state model provides the most granular control with explicit
/// user approval gates and error handling.
/// </summary>
public enum PanelPhase
{
    /// <summary>No active discussion. Waiting for user input.</summary>
    Idle,
    
    /// <summary>Head is asking clarification questions to refine the task.</summary>
    Clarifying,
    
    /// <summary>Head has proposed the Topic of Discussion. Awaiting user approval.</summary>
    AwaitingApproval,
    
    /// <summary>Spinning up panelists, validating models, initializing tools.</summary>
    Preparing,
    
    /// <summary>Panel discussion is actively running. Panelists are debating.</summary>
    Running,
    
    /// <summary>User has paused the discussion. Can be resumed.</summary>
    Paused,
    
    /// <summary>Moderator detected convergence. Final positions being collected.</summary>
    Converging,
    
    /// <summary>Head is aggregating all findings into a final report.</summary>
    Synthesizing,
    
    /// <summary>Discussion complete. Head available for follow-up questions.</summary>
    Completed,
    
    /// <summary>User stopped the discussion. Agents disposed.</summary>
    Stopped,
    
    /// <summary>Fatal error occurred. Recovery possible via Reset.</summary>
    Failed
}

/// <summary>
/// Triggers that cause state transitions in the panel state machine.
/// </summary>
public enum PanelTrigger
{
    UserSubmitted,
    ClarificationsComplete,
    UserApproved,
    PanelistsReady,
    TurnCompleted,
    ConvergenceDetected,
    Timeout,
    SynthesisComplete,
    UserPaused,
    UserResumed,
    UserStopped,
    Error,
    Reset
}

/// <summary>
/// Roles within a panel discussion.
/// Unlike AgentRole in MultiAgent (which represents domain expertise),
/// these represent structural positions in the panel hierarchy.
/// </summary>
public enum PanelAgentRole
{
    /// <summary>Manages user interaction, clarification, and synthesis.</summary>
    Head,
    
    /// <summary>Enforces guard rails, detects convergence, controls flow.</summary>
    Moderator,
    
    /// <summary>Provides expert analysis on the discussion topic.</summary>
    Panelist,
    
    /// <summary>The human user interacting with the Head.</summary>
    User
}

/// <summary>
/// Lifecycle status of an agent instance.
/// </summary>
public enum PanelAgentStatus
{
    Created,
    Active,
    Thinking,
    Idle,
    Paused,
    Disposed
}

/// <summary>
/// Controls the verbosity of agent commentary shown in the UI.
/// </summary>
public enum CommentaryMode
{
    /// <summary>Show all reasoning traces, tool calls, and internal decisions.</summary>
    Detailed,
    
    /// <summary>Show key decisions and tool results only.</summary>
    Brief,
    
    /// <summary>Show results only, no commentary.</summary>
    Off
}

/// <summary>
/// Classifies messages in the discussion transcript.
/// </summary>
public enum PanelMessageType
{
    UserMessage,
    Clarification,
    TopicOfDiscussion,
    PanelistArgument,
    ModerationNote,
    ToolCallResult,
    Commentary,
    Synthesis,
    SystemNotification,
    Error
}
```

---

## 9. Domain Events

```csharp
namespace CopilotAgent.Panel.Domain.Events;

/// <summary>
/// Base class for all panel events. Carries session context and timestamp.
/// All events are immutable records — safe to share across threads.
/// 
/// UI subscribes via IObservable{PanelEvent} and filters by subtype.
/// </summary>
public abstract record PanelEvent(
    PanelSessionId SessionId,
    DateTimeOffset Timestamp);

public sealed record PhaseChangedEvent(
    PanelSessionId SessionId,
    PanelPhase OldPhase,
    PanelPhase NewPhase,
    string? CorrelationId,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);

public sealed record AgentMessageEvent(
    PanelSessionId SessionId,
    PanelMessage Message,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);

public sealed record AgentStatusChangedEvent(
    PanelSessionId SessionId,
    Guid AgentId,
    string AgentName,
    PanelAgentRole Role,
    PanelAgentStatus NewStatus,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);

public sealed record ToolCallEvent(
    PanelSessionId SessionId,
    Guid AgentId,
    string AgentName,
    string ToolName,
    string Input,
    string? Output,
    bool Succeeded,
    TimeSpan Duration,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);

public sealed record ModerationEvent(
    PanelSessionId SessionId,
    string Action,
    string Reason,
    double? ConvergenceScore,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);

public sealed record CommentaryEvent(
    PanelSessionId SessionId,
    Guid AgentId,
    string AgentName,
    PanelAgentRole Role,
    string Commentary,
    CommentaryMode MinimumLevel,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);

public sealed record ProgressEvent(
    PanelSessionId SessionId,
    int CompletedTurns,
    int EstimatedTotalTurns,
    int ActivePanelists,
    int DonePanelists,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);

public sealed record ErrorEvent(
    PanelSessionId SessionId,
    string Source,
    string ErrorMessage,
    Exception? Exception,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);

public sealed record CostUpdateEvent(
    PanelSessionId SessionId,
    int TokensConsumedThisTurn,
    int TotalTokensConsumed,
    int EstimatedTokensRemaining,
    DateTimeOffset Timestamp) : PanelEvent(SessionId, Timestamp);
```

---

## 10. Guard-Rail Policy

```csharp
namespace CopilotAgent.Panel.Domain.Policies;

/// <summary>
/// First-class domain entity that enforces safety and resource limits on a panel discussion.
/// Created once per session. Immutable after construction.
/// 
/// The Moderator agent checks this policy on every turn. If any limit is exceeded,
/// the Moderator forces convergence or terminates the discussion.
///
/// DEFAULT VALUES are chosen for a typical 30-minute analysis session with 3-5 panelists.
/// Override via PanelSettings for specific use cases.
/// </summary>
public sealed class GuardRailPolicy
{
    /// <summary>Maximum number of turns across all panelists before forced convergence.</summary>
    public int MaxTurnsPerDiscussion { get; init; } = 30;

    /// <summary>Maximum tokens a single panelist can produce in one turn.</summary>
    public int MaxTokensPerTurn { get; init; } = 4000;

    /// <summary>Maximum total tokens across all agents for the entire discussion.</summary>
    public int MaxTotalTokens { get; init; } = 100_000;

    /// <summary>Maximum tool calls a single panelist can make in one turn.</summary>
    public int MaxToolCallsPerTurn { get; init; } = 5;

    /// <summary>Maximum total tool calls across the entire discussion.</summary>
    public int MaxToolCallsPerDiscussion { get; init; } = 50;

    /// <summary>Maximum wall-clock time for the entire discussion.</summary>
    public TimeSpan MaxDiscussionDuration { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Maximum time for a single agent's turn before timeout.</summary>
    public TimeSpan MaxSingleTurnDuration { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>Regex patterns that will cause message blocking if matched.</summary>
    public IReadOnlyList<string> ProhibitedContentPatterns { get; init; } = [];

    /// <summary>Allowed domains for web crawling tools. Empty = all allowed.</summary>
    public IReadOnlyList<string> AllowedDomains { get; init; } = [];

    /// <summary>Whether panelists can access the local file system.</summary>
    public bool AllowFileSystemAccess { get; init; } = true;

    /// <summary>Allowed file paths for file system access. Empty = all allowed.</summary>
    public IReadOnlyList<string> AllowedFilePaths { get; init; } = [];

    /// <summary>Maximum critique-refine iterations per topic before force-accept.</summary>
    public int MaxCritiqueRounds { get; init; } = 2;

    /// <summary>
    /// Creates a policy from PanelSettings with validation.
    /// </summary>
    public static GuardRailPolicy FromSettings(PanelSettings settings)
    {
        return new GuardRailPolicy
        {
            MaxTurnsPerDiscussion = Math.Clamp(settings.MaxTurns, 5, 100),
            MaxTotalTokens = Math.Clamp(settings.MaxTotalTokens, 10_000, 500_000),
            MaxDiscussionDuration = TimeSpan.FromMinutes(
                Math.Clamp(settings.MaxDurationMinutes, 5, 120)),
            MaxToolCallsPerDiscussion = Math.Clamp(settings.MaxToolCalls, 10, 200),
            AllowFileSystemAccess = settings.AllowFileSystemAccess
        };
    }
}
```

---

## 11. Panel Session Lifecycle

### 11.1 State Diagram

```
                                    ┌─────────┐
                              ┌────►│  Idle    │◄──────────────────────────────────┐
                              │     └────┬─────┘                                   │
                              │          │ UserSubmitted                            │
                              │          ▼                                         │
                              │ ┌─────────────────────┐                            │
                              │ │   Clarifying         │◄──┐                       │
                              │ └──────────┬──────────┘    │ (more questions)      │
                              │            │ ClarificationsComplete                 │
                              │            ▼                                       │
                              │ ┌─────────────────────┐                            │
                              │ │  AwaitingApproval    │                            │
                              │ └──────────┬──────────┘                            │
                              │            │ UserApproved                           │
                              │            ▼                                       │
                              │ ┌─────────────────────┐                            │
                              │ │     Preparing        │ (spawn panelists)         │
                              │ └──────────┬──────────┘                            │
                              │            │ PanelistsReady                         │
                              │            ▼                                       │
                     ┌────────│ ┌─────────────────────┐ ──────────┐                │
                     │        │ │     Running          │           │                │
        UserPaused   │        │ └──────────┬──────────┘  UserStopped               │
                     │        │            │              / Timeout                  │
                     ▼        │            │ ConvergenceDetected  / Error           │
               ┌──────────┐   │            ▼              │                        │
               │  Paused  │   │ ┌─────────────────────┐   ▼                        │
               └─────┬────┘   │ │    Converging        │ ┌──────────┐              │
                     │        │ └──────────┬──────────┘ │ Stopped/  │              │
        UserResumed  │        │            │ TurnCompleted │ Failed  │              │
                     │        │            ▼              └────┬─────┘              │
                     └────►Running  ┌─────────────────┐       │                    │
                              │     │  Synthesizing    │       │ Reset              │
                              │     └──────────┬──────┘       │                    │
                              │                │ SynthesisComplete                  │
                              │                ▼              │                    │
                              │     ┌─────────────────┐       │                    │
                              │     │   Completed      │──────┘────────────────────┘
                              │     └─────────────────┘
                              │                │ UserSubmitted (follow-up)
                              │                └──► Clarifying
                              │
                              └─── Reset from any terminal state
```

---

## 12. State Machine Implementation

```csharp
namespace CopilotAgent.Panel.StateMachine;

using Stateless;

/// <summary>
/// Deterministic state machine for panel discussion lifecycle.
/// Uses the Stateless library for explicit, testable state transitions.
/// 
/// DESIGN RULES:
///   1. Every transition emits a PhaseChangedEvent via the event bus.
///   2. Invalid triggers are logged but do not throw — fail-safe behavior.
///   3. The state machine delegates to PanelSession.TransitionTo() for state storage.
///   4. All transitions are auditable — the Stateless library supports DOT graph export.
/// </summary>
public sealed class PanelStateMachine
{
    private readonly StateMachine<PanelPhase, PanelTrigger> _machine;
    private readonly PanelSession _session;
    private readonly ISubject<PanelEvent> _eventStream;
    private readonly ILogger<PanelStateMachine> _logger;

    public PanelPhase CurrentPhase => _machine.State;
    public bool CanFire(PanelTrigger trigger) => _machine.CanFire(trigger);

    public PanelStateMachine(
        PanelSession session,
        ISubject<PanelEvent> eventStream,
        ILogger<PanelStateMachine> logger)
    {
        _session = session;
        _eventStream = eventStream;
        _logger = logger;

        _machine = new StateMachine<PanelPhase, PanelTrigger>(
            () => _session.Phase,
            s => _session.TransitionTo(s));

        ConfigureStates();
    }

    private void ConfigureStates()
    {
        // ─── IDLE ───
        _machine.Configure(PanelPhase.Idle)
            .Permit(PanelTrigger.UserSubmitted, PanelPhase.Clarifying)
            .OnEntry(() => PublishPhaseChange(PanelPhase.Idle));

        // ─── CLARIFYING ───
        _machine.Configure(PanelPhase.Clarifying)
            .Permit(PanelTrigger.ClarificationsComplete, PanelPhase.AwaitingApproval)
            .Permit(PanelTrigger.UserStopped, PanelPhase.Stopped)
            .PermitReentry(PanelTrigger.TurnCompleted)
            .OnEntry(() => PublishPhaseChange(PanelPhase.Clarifying));

        // ─── AWAITING APPROVAL ───
        _machine.Configure(PanelPhase.AwaitingApproval)
            .Permit(PanelTrigger.UserApproved, PanelPhase.Preparing)
            .Permit(PanelTrigger.UserStopped, PanelPhase.Stopped)
            .OnEntry(() => PublishPhaseChange(PanelPhase.AwaitingApproval));

        // ─── PREPARING ───
        _machine.Configure(PanelPhase.Preparing)
            .Permit(PanelTrigger.PanelistsReady, PanelPhase.Running)
            .Permit(PanelTrigger.Error, PanelPhase.Failed)
            .Permit(PanelTrigger.UserStopped, PanelPhase.Stopped)
            .OnEntry(() => PublishPhaseChange(PanelPhase.Preparing));

        // ─── RUNNING ───
        _machine.Configure(PanelPhase.Running)
            .Permit(PanelTrigger.ConvergenceDetected, PanelPhase.Converging)
            .Permit(PanelTrigger.UserPaused, PanelPhase.Paused)
            .Permit(PanelTrigger.UserStopped, PanelPhase.Stopped)
            .Permit(PanelTrigger.Timeout, PanelPhase.Converging)
            .Permit(PanelTrigger.Error, PanelPhase.Failed)
            .PermitReentry(PanelTrigger.TurnCompleted)
            .OnEntry(() => PublishPhaseChange(PanelPhase.Running));

        // ─── PAUSED ───
        _machine.Configure(PanelPhase.Paused)
            .Permit(PanelTrigger.UserResumed, PanelPhase.Running)
            .Permit(PanelTrigger.UserStopped, PanelPhase.Stopped)
            .OnEntry(() => PublishPhaseChange(PanelPhase.Paused));

        // ─── CONVERGING ───
        _machine.Configure(PanelPhase.Converging)
            .Permit(PanelTrigger.TurnCompleted, PanelPhase.Synthesizing)
            .Permit(PanelTrigger.UserStopped, PanelPhase.Stopped)
            .Permit(PanelTrigger.Error, PanelPhase.Failed)
            .OnEntry(() => PublishPhaseChange(PanelPhase.Converging));

        // ─── SYNTHESIZING ───
        _machine.Configure(PanelPhase.Synthesizing)
            .Permit(PanelTrigger.SynthesisComplete, PanelPhase.Completed)
            .Permit(PanelTrigger.Error, PanelPhase.Failed)
            .Permit(PanelTrigger.UserStopped, PanelPhase.Stopped)
            .OnEntry(() => PublishPhaseChange(PanelPhase.Synthesizing));

        // ─── COMPLETED ───
        _machine.Configure(PanelPhase.Completed)
            .Permit(PanelTrigger.UserSubmitted, PanelPhase.Clarifying) // follow-up
            .Permit(PanelTrigger.Reset, PanelPhase.Idle)
            .OnEntry(() => PublishPhaseChange(PanelPhase.Completed));

        // ─── STOPPED ───
        _machine.Configure(PanelPhase.Stopped)
            .Permit(PanelTrigger.Reset, PanelPhase.Idle)
            .OnEntry(() => PublishPhaseChange(PanelPhase.Stopped));

        // ─── FAILED ───
        _machine.Configure(PanelPhase.Failed)
            .Permit(PanelTrigger.Reset, PanelPhase.Idle)
            .OnEntry(() => PublishPhaseChange(PanelPhase.Failed));

        // Global unhandled trigger — log warning, never throw
        _machine.OnUnhandledTrigger((state, trigger) =>
        {
            _logger.LogWarning(
                "[PanelFSM] Unhandled trigger {Trigger} in state {State} for session {Id}",
                trigger, state, _session.Id);
        });
    }

    public async Task FireAsync(PanelTrigger trigger)
    {
        _logger.LogInformation(
            "[PanelFSM] Session {Id}: {OldState} ──[{Trigger}]──► ...",
            _session.Id, _machine.State, trigger);
        await _machine.FireAsync(trigger);
    }

    /// <summary>Generates a DOT graph for documentation and debugging.</summary>
    public string ToDotGraph() => UmlDotGraph.Format(_machine.GetInfo());

    private void PublishPhaseChange(PanelPhase newPhase)
    {
        _eventStream.OnNext(new PhaseChangedEvent(
            _session.Id,
            _machine.State,
            newPhase,
            CorrelationId: null,
            DateTimeOffset.UtcNow));
    }
}
```

---

## 13. Agent Hierarchy

### 13.1 Interface Contract

```csharp
namespace CopilotAgent.Panel.Domain.Interfaces;

/// <summary>
/// Contract for all panel agents (Head, Moderator, Panelists).
/// Agents are stateful within a session but disposable when the session ends.
/// 
/// LIFECYCLE:
///   1. Created by IPanelAgentFactory
///   2. Initialized with session context
///   3. ProcessAsync called for each turn
///   4. PauseAsync/ResumeAsync for user control
///   5. DisposeAsync when session ends
/// </summary>
public interface IPanelAgent : IAsyncDisposable
{
    Guid Id { get; }
    string Name { get; }
    PanelAgentRole Role { get; }
    PanelAgentStatus Status { get; }

    /// <summary>
    /// Execute one turn of processing. Returns the agent's contribution.
    /// Must respect cancellation token. Must emit CommentaryEvents for reasoning.
    /// </summary>
    Task<AgentOutput> ProcessAsync(
        AgentInput input,
        CancellationToken ct = default);

    Task PauseAsync();
    Task ResumeAsync();
}

/// <summary>Input context for an agent's turn.</summary>
public sealed record AgentInput(
    PanelSessionId SessionId,
    IReadOnlyList<PanelMessage> ConversationHistory,
    string SystemPrompt,
    TurnNumber CurrentTurn,
    IReadOnlyList<string>? ToolOutputs);

/// <summary>Output from an agent's turn.</summary>
public sealed record AgentOutput(
    PanelMessage Message,
    IReadOnlyList<ToolCallRecord>? ToolCalls,
    bool RequestsMoreTurns,
    string? InternalReasoning);
```

---

## 14. Head Agent

### 14.1 Responsibilities

1. **Clarification Phase**: Analyze user prompt, ask targeted questions to eliminate ambiguity.
2. **Topic Generation**: Compose comprehensive "Topic of Discussion" from clarification exchange.
3. **Synthesis Phase**: Aggregate all panelist findings into a structured final report.
4. **Follow-Up Q&A**: Answer user questions using the Knowledge Brief after panel concludes.
5. **Meta-Questions**: Respond to user queries about panel status during execution.

### 14.2 Implementation

```csharp
namespace CopilotAgent.Panel.Agents;

/// <summary>
/// The Head agent manages user interaction and final synthesis.
/// Uses a dedicated Copilot SDK session that persists across the entire discussion.
/// 
/// KEY DESIGN: The Head's session is LONG-LIVED (reused for clarification, synthesis,
/// and follow-up). Panelist sessions are ephemeral.
/// </summary>
public sealed class HeadAgent : PanelAgentBase
{
    public override string Name => "Head";
    public override PanelAgentRole Role => PanelAgentRole.Head;

    private readonly IKnowledgeBriefService _knowledgeBriefService;
    private KnowledgeBrief? _knowledgeBrief;

    public HeadAgent(
        ICopilotService copilotService,
        ISubject<PanelEvent> eventStream,
        IKnowledgeBriefService knowledgeBriefService,
        ILogger<HeadAgent> logger)
        : base(copilotService, eventStream, logger)
    {
        _knowledgeBriefService = knowledgeBriefService;
    }

    /// <summary>
    /// Clarification: Analyze user prompt and generate targeted questions.
    /// Returns "CLEAR: No further clarification needed." if prompt is sufficient.
    /// </summary>
    public async Task<string> ClarifyAsync(
        string userPrompt, CancellationToken ct)
    {
        EmitCommentary("Analyzing user request to identify ambiguities...");

        var prompt = $"""
            You are the HEAD of a multi-agent panel discussion system.
            Analyze the following user request and generate 2-5 specific 
            clarification questions to ensure the panel can produce 
            the most useful analysis.

            Focus on:
            - Scope boundaries (what's in/out of scope)
            - Priority areas  
            - Specific concerns or known issues
            - Expected output format
            - Any constraints (time, technology, budget)

            If the request is already crystal clear, respond with:
            "CLEAR: No further clarification needed."

            USER REQUEST:
            {userPrompt}
            """;

        var response = await SendToLlmAsync(prompt, ct);
        EmitCommentary("Clarification questions generated.");
        return response;
    }

    /// <summary>
    /// Build the Topic of Discussion from the clarification exchange.
    /// This becomes the prompt given to all panelists.
    /// </summary>
    public async Task<string> BuildTopicOfDiscussionAsync(
        IReadOnlyList<PanelMessage> clarificationExchange,
        CancellationToken ct)
    {
        EmitCommentary("Synthesizing clarifications into discussion topic...");

        var exchange = string.Join("\n\n",
            clarificationExchange.Select(m => $"**{m.AuthorName}**: {m.Content}"));

        var prompt = $"""
            Based on the original request and the following clarification exchange:

            {exchange}

            Compose a comprehensive "Topic of Discussion" for a panel of expert AI analysts.
            The topic should:
            1. State the exact analysis goal
            2. List specific areas to investigate  
            3. Define success criteria
            4. Specify any constraints or boundaries
            5. Indicate what tools/data sources are available
            6. Define the expected output format

            Also recommend the number of panelists (2-6) and their specializations.

            Be thorough but concise. This prompt will guide the entire panel discussion.
            """;

        var response = await SendToLlmAsync(prompt, ct);
        EmitCommentary($"Topic of Discussion ready ({response.Length} chars).");
        return response;
    }

    /// <summary>
    /// Synthesize all panel findings into a final comprehensive report.
    /// After synthesis, builds a Knowledge Brief for follow-up questions.
    /// </summary>
    public async Task<string> SynthesizeAsync(
        IReadOnlyList<PanelMessage> panelMessages,
        CancellationToken ct)
    {
        EmitCommentary($"Synthesizing {panelMessages.Count} messages...");

        var messages = string.Join("\n\n---\n\n",
            panelMessages.Select(m =>
                $"**{m.AuthorName}** ({m.AuthorRole}, Turn {m.Timestamp:HH:mm:ss}):\n{m.Content}"));

        var prompt = $"""
            The panel discussion has concluded. Below are all contributions:

            {messages}

            Synthesize all findings into a comprehensive final report with:
            1. **Executive Summary** — Key findings in 3-5 bullet points
            2. **Detailed Analysis** — Organized by topic/area
            3. **Agreements** — Points all panelists agreed on
            4. **Disagreements** — Points of contention with different perspectives
            5. **Recommendations** — Concrete, actionable next steps
            6. **Risk Assessment** — Potential risks and mitigations
            7. **Follow-Up Items** — Questions or areas needing further investigation

            Use rich markdown formatting. Be comprehensive and elaborative.
            """;

        var synthesis = await SendToLlmAsync(prompt, ct);

        // Build Knowledge Brief for follow-up questions
        _knowledgeBrief = await _knowledgeBriefService.BuildBriefAsync(
            panelMessages, synthesis, ct);

        EmitCommentary("Synthesis complete. Knowledge brief stored for follow-up.");
        return synthesis;
    }

    /// <summary>
    /// Answer follow-up questions using the Knowledge Brief.
    /// Available after Completed phase without re-running the panel.
    /// </summary>
    public async Task<string> AnswerFollowUpAsync(
        string userQuestion, CancellationToken ct)
    {
        if (_knowledgeBrief is null)
            return "No panel discussion has been completed yet. Please start a discussion first.";

        EmitCommentary($"Answering follow-up: \"{Truncate(userQuestion, 50)}\"");

        var prompt = $"""
            You are the Head of a completed panel discussion.
            Use the following Knowledge Brief to answer the user's question.

            ## Knowledge Brief
            {_knowledgeBrief.CompressedSummary}

            ## Key Findings
            {string.Join("\n", _knowledgeBrief.KeyFindings.Select(f => $"- {f}"))}

            ## User Question
            {userQuestion}

            Answer based on the discussion findings. If the question is outside 
            the scope of the completed discussion, say so and suggest starting 
            a new panel for that topic.
            """;

        return await SendToLlmAsync(prompt, ct);
    }

    /// <summary>
    /// Handle meta-questions about panel status during execution.
    /// E.g., "How long will this take?", "What are they discussing?"
    /// </summary>
    public string HandleMetaQuestion(
        string question,
        int currentTurn,
        int maxTurns,
        int panelistCount,
        PanelPhase currentPhase)
    {
        var remainingTurns = maxTurns - currentTurn;
        var estimatedSeconds = remainingTurns * 15 * panelistCount;
        var eta = TimeSpan.FromSeconds(estimatedSeconds);

        return $"""
            **Panel Status**
            - Phase: {currentPhase}
            - Progress: Turn {currentTurn}/{maxTurns}
            - Active Panelists: {panelistCount}
            - Estimated Time Remaining: ~{eta.TotalMinutes:F0} minutes

            The panel is actively discussing your request. You can:
            - **Pause** to temporarily halt discussion
            - **Stop** to end and get partial results
            - Wait for the panel to complete naturally
            """;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}
```

---

## 15. Moderator Agent

### 15.1 Responsibilities

1. **Guard Rail Enforcement**: Validate every message against policy limits.
2. **Convergence Detection**: Identify when panelists have substantially agreed.
3. **Speaker Selection**: Decide which panelist speaks next (structured JSON decision).
4. **Flow Redirection**: Nudge panelists back on topic if discussion drifts.
5. **Resource Monitoring**: Track token consumption, tool calls, and time spent.

### 15.2 Structured Decision Output

```csharp
namespace CopilotAgent.Panel.Models;

/// <summary>
/// Structured decision from the Moderator after evaluating a turn.
/// Parsed from the Moderator LLM's JSON response.
/// 
/// If parsing fails, defaults to: continue with all eligible panelists,
/// convergence score 0, stop = false (fail-open).
/// </summary>
public sealed record ModeratorDecision
{
    /// <summary>Which panelist should speak next. Null = all panelists in round-robin.</summary>
    public string? NextSpeaker { get; init; }

    /// <summary>Convergence score 0-100. Above 80 = trigger convergence phase.</summary>
    public int ConvergenceScore { get; init; }

    /// <summary>Whether to stop discussion and begin synthesis.</summary>
    public bool StopDiscussion { get; init; }

    /// <summary>Reason for the decision — logged and optionally shown in commentary.</summary>
    public string? Reason { get; init; }

    /// <summary>Optional redirect message to refocus a drifting panelist.</summary>
    public string? RedirectMessage { get; init; }

    /// <summary>
    /// Fallback decision when LLM response cannot be parsed.
    /// Fail-open: continue discussion, no convergence, no redirect.
    /// </summary>
    public static ModeratorDecision Fallback(string reason) => new()
    {
        NextSpeaker = null,
        ConvergenceScore = 0,
        StopDiscussion = false,
        Reason = $"Fallback decision: {reason}"
    };
}
```

### 15.3 Moderation Result

```csharp
namespace CopilotAgent.Panel.Models;

/// <summary>
/// Result of validating a panelist message against guard rail policy.
/// </summary>
public sealed record ModerationResult(
    ModerationAction Action,
    string? Reason)
{
    public static ModerationResult Approved() => new(ModerationAction.Approved, null);
    public static ModerationResult Blocked(string reason) => new(ModerationAction.Blocked, reason);
    public static ModerationResult Redirect(string reason) => new(ModerationAction.Redirect, reason);
    public static ModerationResult ForceConverge(string reason) => new(ModerationAction.ForceConverge, reason);
    public static ModerationResult ConvergenceDetected() => new(ModerationAction.ConvergenceDetected, null);
}

public enum ModerationAction
{
    Approved,
    Blocked,
    Redirect,
    ForceConverge,
    ConvergenceDetected
}
```

---

## 16. Panelist Agents

### 16.1 Panelist Profile

```csharp
namespace CopilotAgent.Panel.Models;

/// <summary>
/// Configuration for a specific panelist. Created by the Head during the Preparing phase
/// based on the Topic of Discussion analysis.
/// 
/// Each panelist has a unique persona, model assignment, and tool access profile.
/// </summary>
public sealed record PanelistProfile
{
    /// <summary>Human-readable name shown in UI (e.g., "Security Analyst", "Performance Expert").</summary>
    public required string Name { get; init; }

    /// <summary>Emoji/icon for UI avatar.</summary>
    public required string Icon { get; init; }

    /// <summary>Hex color for UI accent.</summary>
    public required string AccentColor { get; init; }

    /// <summary>System prompt defining this panelist's expertise and perspective.</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>Model to use for this panelist. Null = use default from settings.</summary>
    public ModelIdentifier? Model { get; init; }

    /// <summary>Tool categories this panelist is authorized to use.</summary>
    public IReadOnlyList<string> AllowedToolCategories { get; init; } = [];

    /// <summary>Temperature setting (higher = more creative/diverse perspectives).</summary>
    public double Temperature { get; init; } = 0.7;
}
```

### 16.2 Default Panelist Registry

```csharp
namespace CopilotAgent.Panel.Agents;

/// <summary>
/// Registry of default panelist profiles for common analysis scenarios.
/// The Head agent selects from these based on the Topic of Discussion.
/// Custom profiles can be defined in PanelSettings.
/// </summary>
public static class DefaultPanelistProfiles
{
    public static IReadOnlyDictionary<string, PanelistProfile> Profiles { get; } =
        new Dictionary<string, PanelistProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["SecurityAnalyst"] = new()
            {
                Name = "Security Analyst",
                Icon = "🛡️",
                AccentColor = "#EF5350",
                SystemPrompt = """
                    You are a security analyst on an expert panel. Your expertise:
                    - Threat modeling (STRIDE, DREAD)
                    - Authentication and authorization review
                    - Input validation and injection prevention
                    - OWASP Top 10 compliance
                    - Secrets management and data protection
                    Be adversarial. Assume every input is hostile. Flag every risk.
                    Always cite specific CWE/CVE numbers when applicable.
                    """
            },
            ["PerformanceExpert"] = new()
            {
                Name = "Performance Expert",
                Icon = "⚡",
                AccentColor = "#FFB74D",
                SystemPrompt = """
                    You are a performance engineer on an expert panel. Your expertise:
                    - Algorithmic complexity analysis (time and space)
                    - Memory allocation patterns and GC pressure
                    - Cache strategy and data locality
                    - Concurrency bottlenecks and contention
                    - Profiling methodology and benchmark design
                    Quantify everything. No vague claims — provide Big-O, measurements, or estimates.
                    """
            },
            ["Architect"] = new()
            {
                Name = "Architect",
                Icon = "🏗️",
                AccentColor = "#4FC3F7",
                SystemPrompt = """
                    You are a software architect on an expert panel. Your expertise:
                    - System design and component decomposition
                    - API contract design and interface boundaries
                    - Design pattern selection and trade-offs
                    - Scalability, maintainability, extensibility
                    Focus on structural decisions. Justify every choice with trade-offs.
                    """
            },
            ["QAEngineer"] = new()
            {
                Name = "QA Engineer",
                Icon = "🧪",
                AccentColor = "#81C784",
                SystemPrompt = """
                    You are a QA engineer on an expert panel. Your expertise:
                    - Test strategy (unit, integration, e2e, property-based)
                    - Edge case identification and boundary analysis
                    - Regression test design and coverage analysis
                    - Test data generation and fixture management
                    Every claim must be testable. Every path must be covered.
                    """
            },
            ["CodeReviewer"] = new()
            {
                Name = "Code Reviewer",
                Icon = "🔍",
                AccentColor = "#CE93D8",
                SystemPrompt = """
                    You are a senior code reviewer on an expert panel. Your expertise:
                    - Code quality, readability, maintainability
                    - Design pattern compliance and anti-pattern detection
                    - Error handling completeness and edge cases
                    - SOLID principles and clean code practices
                    Be constructive but thorough. Every suggestion must include the 'why'.
                    """
            },
            ["DatabaseExpert"] = new()
            {
                Name = "Database Expert",
                Icon = "🗄️",
                AccentColor = "#A1887F",
                SystemPrompt = """
                    You are a database expert on an expert panel. Your expertise:
                    - Schema design and normalization
                    - Query optimization and index strategy
                    - Migration planning and backward compatibility
                    - Data integrity constraints and transactions
                    Every schema change must consider migration cost and query performance.
                    """
            },
            ["Researcher"] = new()
            {
                Name = "Researcher",
                Icon = "📚",
                AccentColor = "#FFF176",
                SystemPrompt = """
                    You are a technical researcher on an expert panel. Your expertise:
                    - Technology evaluation and competitive analysis
                    - Documentation review and knowledge synthesis
                    - Best practice research across industry sources
                    - Data gathering, analysis, and structured reporting
                    Cite sources. Distinguish facts from opinions.
                    """
            },
            ["EdgeCaseHunter"] = new()
            {
                Name = "Edge Case Hunter",
                Icon = "🎯",
                AccentColor = "#FF8A65",
                SystemPrompt = """
                    You are an edge case specialist on an expert panel. Your expertise:
                    - Identifying boundary conditions and corner cases
                    - Fuzzing strategy and chaos engineering
                    - Failure mode analysis (FMEA)
                    - Race condition and concurrency edge cases
                    - Resource exhaustion scenarios
                    Think about what WILL break, not what SHOULD work.
                    """
            }
        };
}
```

---

## 17. Agent Factory

```csharp
namespace CopilotAgent.Panel.Agents;

/// <summary>
/// Creates and configures panel agents with proper session lifecycle.
/// Each agent gets its own Copilot SDK session for isolation.
/// 
/// DISPOSAL: The factory does NOT own agents. The AgentSupervisor
/// is responsible for disposing agents when the session ends.
/// </summary>
public sealed class PanelAgentFactory : IPanelAgentFactory
{
    private readonly ICopilotService _copilotService;
    private readonly ISubject<PanelEvent> _eventStream;
    private readonly IKnowledgeBriefService _knowledgeBriefService;
    private readonly ILoggerFactory _loggerFactory;

    public PanelAgentFactory(
        ICopilotService copilotService,
        ISubject<PanelEvent> eventStream,
        IKnowledgeBriefService knowledgeBriefService,
        ILoggerFactory loggerFactory)
    {
        _copilotService = copilotService;
        _eventStream = eventStream;
        _knowledgeBriefService = knowledgeBriefService;
        _loggerFactory = loggerFactory;
    }

    public HeadAgent CreateHead(PanelSettings settings)
    {
        return new HeadAgent(
            _copilotService,
            _eventStream,
            _knowledgeBriefService,
            _loggerFactory.CreateLogger<HeadAgent>());
    }

    public ModeratorAgent CreateModerator(
        GuardRailPolicy policy, PanelSettings settings)
    {
        return new ModeratorAgent(
            _copilotService,
            _eventStream,
            policy,
            _loggerFactory.CreateLogger<ModeratorAgent>());
    }

    public PanelistAgent CreatePanelist(
        PanelistProfile profile,
        PanelSettings settings)
    {
        var model = profile.Model 
            ?? new ModelIdentifier("default", 
                settings.PanelistModels.Count > 0
                    ? settings.PanelistModels[Random.Shared.Next(settings.PanelistModels.Count)]
                    : settings.PrimaryModel);

        return new PanelistAgent(
            profile,
            model,
            _copilotService,
            _eventStream,
            _loggerFactory.CreateLogger<PanelistAgent>());
    }
}
```

---

## 18. Convergence Detection

```csharp
namespace CopilotAgent.Panel.Agents;

/// <summary>
/// Dedicated component for AI-powered convergence detection.
/// Separated from ModeratorAgent for testability and single responsibility.
/// 
/// CONVERGENCE CRITERIA:
///   1. Key findings are consistent across panelists
///   2. No major new points are being raised
///   3. Disagreements have been addressed or acknowledged
///   4. ConvergenceScore >= 80 out of 100
/// 
/// CHECK FREQUENCY: Every 3 turns after turn 5 (configurable).
/// This prevents premature convergence on the first few turns.
/// </summary>
public sealed class ConvergenceDetector : IConvergenceDetector
{
    private readonly ICopilotService _copilotService;
    private readonly ILogger<ConvergenceDetector> _logger;

    public ConvergenceDetector(
        ICopilotService copilotService,
        ILogger<ConvergenceDetector> logger)
    {
        _copilotService = copilotService;
        _logger = logger;
    }

    /// <summary>
    /// Analyze recent discussion for convergence.
    /// Returns a score 0-100 and recommendation.
    /// </summary>
    public async Task<ConvergenceResult> CheckAsync(
        IReadOnlyList<PanelMessage> recentMessages,
        TurnNumber currentTurn,
        string sessionId,
        CancellationToken ct)
    {
        // Don't check convergence on early turns
        if (currentTurn.Value < 5)
            return ConvergenceResult.NotReady;

        // Only check every 3 turns to minimize LLM cost
        if (currentTurn.Value % 3 != 0)
            return ConvergenceResult.SkippedThisTurn;

        var recentText = string.Join("\n\n",
            recentMessages.TakeLast(10).Select(m =>
                $"[{m.AuthorName}]: {m.Content}"));

        var prompt = $"""
            Analyze the following panel discussion excerpt for convergence.
            Convergence means:
            1. Key findings are consistent across panelists
            2. No major new points are being raised  
            3. Disagreements have been addressed or acknowledged

            Discussion excerpt:
            {recentText}

            Respond with ONLY a JSON object:
            {{"convergenceScore": 0-100, "converged": true/false, "reason": "brief explanation"}}
            """;

        try
        {
            var response = await _copilotService.SendMessageAsync(
                sessionId, prompt, ct);

            var result = ParseConvergenceResponse(response);
            _logger.LogInformation(
                "[ConvergenceDetector] Turn {Turn}: Score={Score}, Converged={Converged}",
                currentTurn.Value, result.Score, result.IsConverged);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ConvergenceDetector] Check failed — assuming not converged");
            return ConvergenceResult.Error(ex.Message);
        }
    }

    private static ConvergenceResult ParseConvergenceResponse(string response)
    {
        try
        {
            // Extract JSON from response
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start)
                return ConvergenceResult.ParseFailed;

            var json = response[start..(end + 1)];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var score = root.GetProperty("convergenceScore").GetInt32();
            var converged = root.GetProperty("converged").GetBoolean();
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;

            return new ConvergenceResult(
                Score: Math.Clamp(score, 0, 100),
                IsConverged: converged || score >= 80,
                Reason: reason,
                Status: ConvergenceCheckStatus.Completed);
        }
        catch
        {
            return ConvergenceResult.ParseFailed;
        }
    }
}

public sealed record ConvergenceResult(
    int Score,
    bool IsConverged,
    string? Reason,
    ConvergenceCheckStatus Status)
{
    public static ConvergenceResult NotReady => 
        new(0, false, "Too early to check", ConvergenceCheckStatus.TooEarly);
    public static ConvergenceResult SkippedThisTurn => 
        new(0, false, "Not a check turn", ConvergenceCheckStatus.Skipped);
    public static ConvergenceResult ParseFailed => 
        new(0, false, "Failed to parse convergence response", ConvergenceCheckStatus.ParseError);
    public static ConvergenceResult Error(string reason) => 
        new(0, false, reason, ConvergenceCheckStatus.Error);
}

public enum ConvergenceCheckStatus
{
    Completed,
    TooEarly,
    Skipped,
    ParseError,
    Error
}
```

---

## 19. Discussion Orchestrator

```csharp
namespace CopilotAgent.Panel.Orchestration;

/// <summary>
/// Central orchestrator for panel discussions. Coordinates the Head, Moderator,
/// and Panelist agents through the state machine lifecycle.
/// 
/// THREAD SAFETY: All public methods are safe for concurrent access.
/// The orchestrator uses SemaphoreSlim internally to serialize state transitions.
/// 
/// DISPOSAL: Implements IAsyncDisposable. Disposes all agents, sessions,
/// and internal resources when the session ends or the application shuts down.
/// </summary>
public sealed class PanelOrchestrator : IPanelOrchestrator, IAsyncDisposable
{
    private readonly IPanelAgentFactory _agentFactory;
    private readonly IConvergenceDetector _convergenceDetector;
    private readonly ICopilotService _copilotService;
    private readonly IPersistenceService _persistenceService;
    private readonly ISubject<PanelEvent> _eventStream;
    private readonly ILogger<PanelOrchestrator> _logger;

    private PanelSession? _session;
    private PanelStateMachine? _stateMachine;
    private HeadAgent? _head;
    private ModeratorAgent? _moderator;
    private readonly List<PanelistAgent> _panelists = [];
    private CancellationTokenSource? _discussionCts;
    private readonly SemaphoreSlim _pauseSemaphore = new(1, 1);
    private bool _isPaused;

    // ── Public State ────────────────────────────────────────
    public PanelSessionId? ActiveSessionId => _session?.Id;
    public PanelPhase CurrentPhase => _stateMachine?.CurrentPhase ?? PanelPhase.Idle;
    public IObservable<PanelEvent> Events => _eventStream.AsObservable();

    public PanelOrchestrator(
        IPanelAgentFactory agentFactory,
        IConvergenceDetector convergenceDetector,
        ICopilotService copilotService,
        IPersistenceService persistenceService,
        ISubject<PanelEvent> eventStream,
        ILogger<PanelOrchestrator> logger)
    {
        _agentFactory = agentFactory;
        _convergenceDetector = convergenceDetector;
        _copilotService = copilotService;
        _persistenceService = persistenceService;
        _eventStream = eventStream;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════
    //  START — User submits a task
    // ═══════════════════════════════════════════════════════
    public async Task<PanelSessionId> StartAsync(
        string userPrompt,
        PanelSettings settings,
        CancellationToken ct = default)
    {
        _discussionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var guardRails = GuardRailPolicy.FromSettings(settings);
        _session = new PanelSession(PanelSessionId.New(), userPrompt, guardRails);

        _stateMachine = new PanelStateMachine(
            _session, _eventStream,
            _logger as ILogger<PanelStateMachine>
                ?? LoggerFactory.Create(b => b.AddConsole())
                    .CreateLogger<PanelStateMachine>());

        _head = _agentFactory.CreateHead(settings);

        _logger.LogInformation(
            "[PanelOrchestrator] Session {Id} started. Prompt: {Prompt}",
            _session.Id, Truncate(userPrompt, 100));

        // Transition to Clarifying
        await _stateMachine.FireAsync(PanelTrigger.UserSubmitted);

        // Start clarification
        var clarification = await _head.ClarifyAsync(userPrompt, _discussionCts.Token);

        if (clarification.Contains("CLEAR:", StringComparison.OrdinalIgnoreCase))
        {
            // No clarification needed — move to topic generation
            await _stateMachine.FireAsync(PanelTrigger.ClarificationsComplete);
            var topic = await _head.BuildTopicOfDiscussionAsync(
                _session.Messages, _discussionCts.Token);
            _session.SetRefinedTopic(topic);
        }
        else
        {
            // Emit clarification questions to UI
            _session.AddMessage(PanelMessage.Create(
                _session.Id, _head.Id, _head.Name, _head.Role,
                clarification, PanelMessageType.Clarification));
        }

        return _session.Id;
    }

    // ═══════════════════════════════════════════════════════
    //  USER RESPONDS to clarification
    // ═══════════════════════════════════════════════════════
    public async Task SendUserMessageAsync(string message, CancellationToken ct = default)
    {
        if (_session is null || _head is null) return;
        var token = _discussionCts?.Token ?? ct;

        _session.AddMessage(PanelMessage.Create(
            _session.Id, Guid.Empty, "User", PanelAgentRole.User,
            message, PanelMessageType.UserMessage));

        switch (CurrentPhase)
        {
            case PanelPhase.Clarifying:
                // Process clarification response
                var response = await _head.ClarifyAsync(message, token);
                if (response.Contains("CLEAR:", StringComparison.OrdinalIgnoreCase))
                {
                    await _stateMachine!.FireAsync(PanelTrigger.ClarificationsComplete);
                    var topic = await _head.BuildTopicOfDiscussionAsync(
                        _session.Messages, token);
                    _session.SetRefinedTopic(topic);
                }
                break;

            case PanelPhase.Completed:
                // Follow-up question
                var answer = await _head.AnswerFollowUpAsync(message, token);
                _session.AddMessage(PanelMessage.Create(
                    _session.Id, _head.Id, _head.Name, _head.Role,
                    answer, PanelMessageType.Synthesis));
                break;

            case PanelPhase.Running:
                // Meta-question during execution
                var meta = _head.HandleMetaQuestion(
                    message,
                    _session.Messages.Count(m => m.Type == PanelMessageType.PanelistArgument),
                    _session.GuardRails.MaxTurnsPerDiscussion,
                    _panelists.Count,
                    CurrentPhase);
                _session.AddMessage(PanelMessage.Create(
                    _session.Id, _head.Id, _head.Name, _head.Role,
                    meta, PanelMessageType.SystemNotification));
                break;
        }
    }

    // ═══════════════════════════════════════════════════════
    //  APPROVE — User approves the Topic of Discussion
    // ═══════════════════════════════════════════════════════
    public async Task ApproveAndStartPanelAsync(CancellationToken ct = default)
    {
        if (_session is null || _stateMachine is null || _head is null) return;
        var token = _discussionCts?.Token ?? ct;

        await _stateMachine.FireAsync(PanelTrigger.UserApproved);

        // Prepare panelists
        // (In production, the Head LLM recommends panelist profiles based on the topic)
        var profiles = SelectPanelistProfiles(_session.RefinedTopicOfDiscussion ?? "");
        var settings = LoadCurrentSettings();

        _moderator = _agentFactory.CreateModerator(_session.GuardRails, settings);

        foreach (var profile in profiles)
        {
            var panelist = _agentFactory.CreatePanelist(profile, settings);
            _panelists.Add(panelist);
            _session.RegisterAgent(new AgentInstance(
                profile.Name, PanelAgentRole.Panelist,
                profile.Model ?? new ModelIdentifier("default", settings.PrimaryModel)));
        }

        await _stateMachine.FireAsync(PanelTrigger.PanelistsReady);

        // Start the debate loop on a background thread
        _ = Task.Run(() => RunDebateLoopAsync(token), token);
    }

    // ═══════════════════════════════════════════════════════
    //  THE DEBATE LOOP — Core execution engine
    // ═══════════════════════════════════════════════════════
    private async Task RunDebateLoopAsync(CancellationToken ct)
    {
        var turn = new TurnNumber(0);
        var discussionTimer = Stopwatch.StartNew();

        try
        {
            while (_stateMachine!.CurrentPhase == PanelPhase.Running)
            {
                ct.ThrowIfCancellationRequested();

                // ── PAUSE CHECK (SemaphoreSlim pattern) ──
                await _pauseSemaphore.WaitAsync(ct);
                _pauseSemaphore.Release();

                // ── TIME LIMIT CHECK ──
                if (discussionTimer.Elapsed > _session!.GuardRails.MaxDiscussionDuration)
                {
                    _logger.LogWarning("[PanelOrchestrator] Time limit exceeded — forcing convergence");
                    await _stateMachine.FireAsync(PanelTrigger.Timeout);
                    break;
                }

                // ── MODERATOR DECISION ──
                EmitCommentary("Moderator", "Evaluating discussion and selecting next speaker...");
                var decision = await _moderator!.DecideNextTurnAsync(
                    _session.Messages, turn, ct);

                if (decision.StopDiscussion || decision.ConvergenceScore >= 80)
                {
                    _logger.LogInformation(
                        "[PanelOrchestrator] Convergence detected. Score={Score}, Reason={Reason}",
                        decision.ConvergenceScore, decision.Reason);
                    await _stateMachine.FireAsync(PanelTrigger.ConvergenceDetected);
                    break;
                }

                // ── EXECUTE PANELIST TURN ──
                var selectedPanelists = SelectPanelists(decision);
                foreach (var panelist in selectedPanelists)
                {
                    ct.ThrowIfCancellationRequested();

                    EmitCommentary(panelist.Name, $"Analyzing... (turn {turn.Value})");

                    using var turnTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    turnTimeout.CancelAfter(_session.GuardRails.MaxSingleTurnDuration);

                    var input = new AgentInput(
                        _session.Id,
                        _session.Messages,
                        "", // System prompt is internal to the panelist
                        turn,
                        null);

                    var output = await panelist.ProcessAsync(input, turnTimeout.Token);

                    // ── MODERATE THE OUTPUT ──
                    var modResult = await _moderator.ValidateMessageAsync(
                        output.Message, turn, ct);

                    if (modResult.Action == ModerationAction.Blocked)
                    {
                        _logger.LogWarning("[PanelOrchestrator] Message blocked: {Reason}",
                            modResult.Reason);
                        continue;
                    }

                    if (modResult.Action == ModerationAction.ForceConverge)
                    {
                        await _stateMachine.FireAsync(PanelTrigger.ConvergenceDetected);
                        break;
                    }

                    _session.AddMessage(output.Message);
                    _eventStream.OnNext(new AgentMessageEvent(
                        _session.Id, output.Message, DateTimeOffset.UtcNow));

                    // ── CONVERGENCE CHECK ──
                    var convergence = await _convergenceDetector.CheckAsync(
                        _session.Messages, turn, _moderator.SessionId, ct);

                    if (convergence.IsConverged)
                    {
                        _eventStream.OnNext(new ModerationEvent(
                            _session.Id, "ConvergenceDetected",
                            convergence.Reason ?? "Panelists converged",
                            convergence.Score,
                            DateTimeOffset.UtcNow));
                        await _stateMachine.FireAsync(PanelTrigger.ConvergenceDetected);
                        break;
                    }
                }

                turn = turn.Increment();

                // ── TURN LIMIT CHECK ──
                if (turn.Exceeds(_session.GuardRails.MaxTurnsPerDiscussion))
                {
                    _logger.LogWarning("[PanelOrchestrator] Turn limit reached — forcing convergence");
                    await _stateMachine.FireAsync(PanelTrigger.Timeout);
                    break;
                }

                await _stateMachine.FireAsync(PanelTrigger.TurnCompleted);

                // Emit progress
                _eventStream.OnNext(new ProgressEvent(
                    _session.Id, turn.Value,
                    _session.GuardRails.MaxTurnsPerDiscussion,
                    _panelists.Count, 0, DateTimeOffset.UtcNow));
            }

            // ── SYNTHESIS PHASE ──
            if (_stateMachine.CurrentPhase is PanelPhase.Converging)
            {
                await _stateMachine.FireAsync(PanelTrigger.TurnCompleted); // → Synthesizing

                var panelMessages = _session!.Messages
                    .Where(m => m.Type == PanelMessageType.PanelistArgument)
                    .ToList();

                var synthesis = await _head!.SynthesizeAsync(panelMessages, ct);
                _session.AddMessage(PanelMessage.Create(
                    _session.Id, _head.Id, _head.Name, _head.Role,
                    synthesis, PanelMessageType.Synthesis));

                await _stateMachine.FireAsync(PanelTrigger.SynthesisComplete);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[PanelOrchestrator] Discussion cancelled");
            if (_stateMachine!.CanFire(PanelTrigger.UserStopped))
                await _stateMachine.FireAsync(PanelTrigger.UserStopped);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PanelOrchestrator] Discussion failed");
            _eventStream.OnNext(new ErrorEvent(
                _session!.Id, "PanelOrchestrator", ex.Message, ex, DateTimeOffset.UtcNow));
            if (_stateMachine!.CanFire(PanelTrigger.Error))
                await _stateMachine.FireAsync(PanelTrigger.Error);
        }
        finally
        {
            discussionTimer.Stop();
        }
    }

    // ═══════════════════════════════════════════════════════
    //  CONTROL — Pause / Resume / Stop / Reset
    // ═══════════════════════════════════════════════════════

    public async Task PauseAsync()
    {
        if (_stateMachine?.CanFire(PanelTrigger.UserPaused) != true) return;
        _isPaused = true;
        await _pauseSemaphore.WaitAsync(); // Block the debate loop
        await _stateMachine.FireAsync(PanelTrigger.UserPaused);
        _logger.LogInformation("[PanelOrchestrator] Discussion paused");
    }

    public async Task ResumeAsync()
    {
        if (_stateMachine?.CanFire(PanelTrigger.UserResumed) != true) return;
        _isPaused = false;
        _pauseSemaphore.Release(); // Unblock the debate loop
        await _stateMachine.FireAsync(PanelTrigger.UserResumed);
        _logger.LogInformation("[PanelOrchestrator] Discussion resumed");
    }

    public async Task StopAsync()
    {
        _discussionCts?.Cancel();
        if (_stateMachine?.CanFire(PanelTrigger.UserStopped) == true)
            await _stateMachine.FireAsync(PanelTrigger.UserStopped);
        await DisposeAgentsAsync();
        _logger.LogInformation("[PanelOrchestrator] Discussion stopped");
    }

    public async Task ResetAsync()
    {
        await StopAsync();
        if (_stateMachine?.CanFire(PanelTrigger.Reset) == true)
            await _stateMachine.FireAsync(PanelTrigger.Reset);
        _session = null;
        _stateMachine = null;
        _head = null;
        _moderator = null;
        _panelists.Clear();
        _logger.LogInformation("[PanelOrchestrator] Session reset");
    }

    // ═══════════════════════════════════════════════════════
    //  DISPOSAL
    // ═══════════════════════════════════════════════════════

    private async Task DisposeAgentsAsync()
    {
        foreach (var p in _panelists)
            await p.DisposeAsync();
        _panelists.Clear();

        if (_moderator is not null)
            await _moderator.DisposeAsync();
        _moderator = null;

        // Head is NOT disposed — it stays alive for follow-up questions
        // Head is only disposed on full Reset
    }

    public async ValueTask DisposeAsync()
    {
        _discussionCts?.Cancel();
        _discussionCts?.Dispose();

        foreach (var p in _panelists)
            await p.DisposeAsync();

        if (_moderator is not null) await _moderator.DisposeAsync();
        if (_head is not null) await _head.DisposeAsync();
        if (_session is not null) await _session.DisposeAsync();

        if (_isPaused)
        {
            try { _pauseSemaphore.Release(); } catch { /* already released */ }
        }
        _pauseSemaphore.Dispose();

        _logger.LogInformation("[PanelOrchestrator] Fully disposed");
    }

    // ── Helpers ──────────────────────────────────────────

    private IReadOnlyList<PanelistAgent> SelectPanelists(ModeratorDecision decision)
    {
        if (decision.NextSpeaker is not null)
        {
            var selected = _panelists.FirstOrDefault(p =>
                p.Name.Equals(decision.NextSpeaker, StringComparison.OrdinalIgnoreCase));
            return selected is not null ? [selected] : _panelists;
        }
        return _panelists;
    }

    private static IReadOnlyList<PanelistProfile> SelectPanelistProfiles(string topic)
    {
        // Default: 3 panelists with diverse perspectives
        // In production, the Head LLM recommends profiles based on topic analysis
        return
        [
            DefaultPanelistProfiles.Profiles["Architect"],
            DefaultPanelistProfiles.Profiles["SecurityAnalyst"],
            DefaultPanelistProfiles.Profiles["QAEngineer"]
        ];
    }

    private PanelSettings LoadCurrentSettings()
    {
        // Load from AppSettings via persistence
        return new PanelSettings();
    }

    private void EmitCommentary(string agentName, string text)
    {
        _eventStream.OnNext(new CommentaryEvent(
            _session!.Id, Guid.Empty, agentName, PanelAgentRole.Moderator,
            text, CommentaryMode.Brief, DateTimeOffset.UtcNow));
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}
```

---

## 20. Turn Management

The Turn Manager is responsible for tracking execution metrics within the debate loop:

```csharp
namespace CopilotAgent.Panel.Orchestration;

/// <summary>
/// Tracks per-turn and aggregate execution metrics.
/// Used by the Moderator and Orchestrator for resource management.
/// </summary>
public sealed class TurnManager
{
    private readonly Stopwatch _sessionTimer = new();
    private int _totalTokensConsumed;
    private int _totalToolCalls;
    private TurnNumber _currentTurn = new(0);
    
    public TurnNumber CurrentTurn => _currentTurn;
    public int TotalTokensConsumed => _totalTokensConsumed;
    public int TotalToolCalls => _totalToolCalls;
    public TimeSpan ElapsedTime => _sessionTimer.Elapsed;

    public void Start() => _sessionTimer.Start();
    public void Stop() => _sessionTimer.Stop();

    public void RecordTurn(int tokensUsed, int toolCallsMade)
    {
        _currentTurn = _currentTurn.Increment();
        Interlocked.Add(ref _totalTokensConsumed, tokensUsed);
        Interlocked.Add(ref _totalToolCalls, toolCallsMade);
    }

    public bool IsTokenBudgetExceeded(TokenBudget budget) =>
        budget.IsExceeded(0, _totalTokensConsumed);
}
```

---

## 21. Pause / Resume / Stop / Reset

### 21.1 SemaphoreSlim Pause Pattern

The pause mechanism uses `SemaphoreSlim` for elegant, thread-safe suspension:

```
NORMAL FLOW:
    Semaphore initialized with count=1
    Debate loop: WaitAsync() → succeeds immediately → Release() → continue
    
PAUSE:
    PauseAsync() calls WaitAsync() → acquires the semaphore (count=0)
    Debate loop: WaitAsync() → BLOCKS (semaphore exhausted)
    
RESUME:
    ResumeAsync() calls Release() → count=1
    Debate loop: WaitAsync() unblocks → Release() → continue
```

### 21.2 Stop & Reset Lifecycle

```
User clicks STOP
    │
    ├── CancellationTokenSource.Cancel()
    │   └── All pending LLM calls and tool calls abort
    ├── StateMachine.Fire(UserStopped) → phase = Stopped
    ├── DisposeAgentsAsync()
    │   ├── Dispose all PanelistAgents (terminate sessions)
    │   └── Dispose ModeratorAgent
    │   └── Head retained for follow-up questions
    └── UI shows "Stopped" state

User clicks RESET
    │
    ├── StopAsync() (if not already stopped)
    ├── StateMachine.Fire(Reset) → phase = Idle
    ├── Dispose Head agent (final cleanup)
    ├── Clear session, state machine, all references
    └── UI returns to "Enter your task" state
```

---

## 22. Conversation Memory Management

Follows Pattern 10 from SHARED_ARCHITECTURE_PATTERNS.md — the `IAgentConversation` pattern:

```csharp
namespace CopilotAgent.Panel.Services;

/// <summary>
/// Manages conversation memory for the panel discussion.
/// Implements auto-trim, thread safety, and export capabilities.
/// 
/// MAX MESSAGES: 1000 per session (configurable).
/// TRIM STRATEGY: Remove oldest 20%, preserve system messages.
/// </summary>
public sealed class PanelConversationManager : IDisposable
{
    private readonly List<PanelMessage> _messages = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _maxMessages;
    private readonly ILogger<PanelConversationManager> _logger;

    public int MessageCount => _messages.Count;

    public PanelConversationManager(
        ILogger<PanelConversationManager> logger,
        int maxMessages = 1000)
    {
        _logger = logger;
        _maxMessages = Math.Max(10, maxMessages);
    }

    public async Task AddAsync(PanelMessage message, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _messages.Add(message);

            if (_messages.Count > _maxMessages)
            {
                var trimCount = _maxMessages / 5;
                var startIndex = _messages.Count > 0
                    && _messages[0].Type == PanelMessageType.SystemNotification ? 1 : 0;
                _messages.RemoveRange(startIndex, Math.Min(trimCount, _messages.Count - startIndex));
                _logger.LogInformation("[PanelConversation] Auto-trimmed {Count} messages", trimCount);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<PanelMessage> GetHistory() => _messages.ToList().AsReadOnly();
    public IReadOnlyList<PanelMessage> GetHistory(int lastN)
    {
        if (lastN <= 0) return [];
        return _messages.Skip(Math.Max(0, _messages.Count - lastN)).ToList().AsReadOnly();
    }

    public void Dispose()
    {
        _gate.Dispose();
        _messages.Clear();
    }
}
```

---

## 23. Knowledge Brief (Post-Discussion Context)

```csharp
namespace CopilotAgent.Panel.Models;

/// <summary>
/// Compressed summary of a completed panel discussion.
/// Retained by the Head agent for answering follow-up questions
/// WITHOUT keeping the full conversation history in memory.
/// 
/// This pattern enables cost-efficient post-discussion Q&A:
/// - Full discussion may be 100K+ tokens
/// - Knowledge Brief is ~2K tokens
/// - Head can answer follow-ups using only the brief
/// </summary>
public sealed record KnowledgeBrief
{
    public required PanelSessionId SessionId { get; init; }
    public required string CompressedSummary { get; init; }
    public required IReadOnlyList<string> KeyFindings { get; init; }
    public required IReadOnlyList<string> Disagreements { get; init; }
    public required IReadOnlyList<string> ActionItems { get; init; }
    public required int TotalTurns { get; init; }
    public required int TotalPanelists { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
```

---

## 24. Circuit Breaker for Tools

```csharp
namespace CopilotAgent.Panel.Resilience;

/// <summary>
/// Circuit breaker for tool execution. Prevents repeated calls to failing tools.
/// 
/// STATES:
///   CLOSED (normal) → failure count < threshold → continue calling
///   OPEN (tripped)  → reject all calls immediately for cooldown period
///   HALF-OPEN       → allow one probe call after cooldown
/// 
/// PER-TOOL: Each tool has its own circuit breaker instance.
/// </summary>
public sealed class ToolCircuitBreaker
{
    private enum CircuitState { Closed, Open, HalfOpen }

    private CircuitState _state = CircuitState.Closed;
    private int _failureCount;
    private DateTimeOffset _lastFailure;
    private readonly int _failureThreshold;
    private readonly TimeSpan _cooldownPeriod;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _logger;

    public string ToolName { get; }
    public bool IsOpen => _state == CircuitState.Open;

    public ToolCircuitBreaker(
        string toolName,
        ILogger logger,
        int failureThreshold = 5,
        TimeSpan? cooldownPeriod = null)
    {
        ToolName = toolName;
        _logger = logger;
        _failureThreshold = failureThreshold;
        _cooldownPeriod = cooldownPeriod ?? TimeSpan.FromSeconds(60);
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_state == CircuitState.Open)
            {
                if (DateTimeOffset.UtcNow - _lastFailure > _cooldownPeriod)
                {
                    _state = CircuitState.HalfOpen;
                    _logger.LogInformation("[CircuitBreaker] {Tool}: HALF-OPEN (probe allowed)", ToolName);
                }
                else
                {
                    throw new CircuitBreakerOpenException(ToolName, _cooldownPeriod);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            var result = await action(ct);
            await RecordSuccess();
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordFailure();
            throw;
        }
    }

    private async Task RecordSuccess()
    {
        await _gate.WaitAsync();
        try
        {
            _failureCount = 0;
            if (_state == CircuitState.HalfOpen)
            {
                _state = CircuitState.Closed;
                _logger.LogInformation("[CircuitBreaker] {Tool}: CLOSED (recovered)", ToolName);
            }
        }
        finally { _gate.Release(); }
    }

    private async Task RecordFailure()
    {
        await _gate.WaitAsync();
        try
        {
            _failureCount++;
            _lastFailure = DateTimeOffset.UtcNow;

            if (_failureCount >= _failureThreshold)
            {
                _state = CircuitState.Open;
                _logger.LogWarning(
                    "[CircuitBreaker] {Tool}: OPEN (failures={Count}, cooldown={Cooldown}s)",
                    ToolName, _failureCount, _cooldownPeriod.TotalSeconds);
            }
        }
        finally { _gate.Release(); }
    }
}

public sealed class CircuitBreakerOpenException : Exception
{
    public string ToolName { get; }
    public TimeSpan CooldownRemaining { get; }

    public CircuitBreakerOpenException(string toolName, TimeSpan cooldown)
        : base($"Circuit breaker OPEN for tool '{toolName}'. Cooldown: {cooldown.TotalSeconds:F0}s")
    {
        ToolName = toolName;
        CooldownRemaining = cooldown;
    }
}
```

---

## 25. Tool Sandboxing

```csharp
/// <summary>
/// Tool execution wrapper that enforces sandboxing constraints.
/// Each tool call runs with restricted permissions based on GuardRailPolicy.
/// 
/// SECURITY MODEL:
///   - File system access: restricted to AllowedFilePaths
///   - Network access: restricted to AllowedDomains
///   - Execution timeout: MaxSingleTurnDuration from GuardRailPolicy
///   - Output size: capped at 50KB per tool call
/// </summary>
public sealed class SandboxedToolExecutor
{
    private readonly IToolApprovalService _approvalService;
    private readonly IApprovalQueue _approvalQueue;
    private readonly GuardRailPolicy _policy;
    private readonly Dictionary<string, ToolCircuitBreaker> _circuitBreakers = [];
    private readonly ILogger<SandboxedToolExecutor> _logger;

    public async Task<ToolCallRecord> ExecuteAsync(
        string toolName,
        string arguments,
        string sessionId,
        CancellationToken ct)
    {
        // 1. Check circuit breaker
        if (!_circuitBreakers.TryGetValue(toolName, out var breaker))
        {
            breaker = new ToolCircuitBreaker(toolName, _logger);
            _circuitBreakers[toolName] = breaker;
        }

        if (breaker.IsOpen)
        {
            return new ToolCallRecord(toolName, arguments,
                $"Tool '{toolName}' is temporarily disabled (circuit breaker open)",
                false, TimeSpan.Zero);
        }

        // 2. Check approval
        if (!_approvalService.IsApproved(sessionId, toolName, arguments))
        {
            var request = new ToolApprovalRequest
            {
                SessionId = sessionId,
                ToolName = toolName,
                Arguments = arguments,
                Source = "Panel Discussion"
            };

            var approval = await _approvalQueue.EnqueueApprovalAsync(request);
            if (approval.Decision != ApprovalDecision.Approve)
            {
                return new ToolCallRecord(toolName, arguments,
                    "Tool call denied by user", false, TimeSpan.Zero);
            }
        }

        // 3. Execute with timeout and circuit breaker
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await breaker.ExecuteAsync(async token =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(_policy.MaxSingleTurnDuration);

                // Actual tool execution via ICopilotService
                // (tool calls are handled by the Copilot SDK session)
                return "Tool execution result"; // Placeholder
            }, ct);

            sw.Stop();
            return new ToolCallRecord(toolName, arguments, result, true, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "[SandboxedTool] Tool {Tool} failed", toolName);
            return new ToolCallRecord(toolName, arguments, ex.Message, false, sw.Elapsed);
        }
    }
}
```

---

## 26. Background Execution Model

```csharp
/// <summary>
/// The panel discussion runs on a background thread via Task.Run().
/// This ensures the discussion continues even if:
/// - The UI thread is busy rendering
/// - The user switches to another tab
/// - The window is minimized
/// 
/// All UI updates are marshalled via IObservable subscriptions with
/// ObserveOn(DispatcherScheduler) in the ViewModel.
/// 
/// CRITICAL: The debate loop NEVER touches UI-bound properties directly.
/// All communication is via PanelEvent emissions through the ISubject stream.
/// </summary>
```

Architecture:

```
Background Thread (Task.Run)               UI Thread (Dispatcher)
┌──────────────────────────┐               ┌──────────────────────────┐
│ PanelOrchestrator        │               │ PanelViewModel           │
│  ├── RunDebateLoopAsync  │    Events     │  ├── Subscribe to Events │
│  │   ├── Moderator.Decide│───────────►   │  │   ├── Phase changes   │
│  │   ├── Panelist.Process│   (Rx.NET     │  │   ├── Messages        │
│  │   ├── Convergence.Check│   IObservable)│  │   ├── Agent status   │
│  │   └── Head.Synthesize │               │  │   └── Progress        │
│  └── Emit PanelEvents    │               │  └── Update UI-bound     │
└──────────────────────────┘               │      properties          │
                                           └──────────────────────────┘
```

---

## 27. Resource Cleanup & Memory Management

### 27.1 Disposal Hierarchy

```
PanelOrchestrator.DisposeAsync()
├── Cancel _discussionCts
├── For each PanelistAgent:
│   └── DisposeAsync() → terminate Copilot SDK session, clear chat history
├── ModeratorAgent.DisposeAsync() → terminate session
├── HeadAgent.DisposeAsync() → terminate session (only on full Reset)
├── PanelSession.DisposeAsync() → clear messages, clear agent list
├── _pauseSemaphore.Dispose()
└── Log "Fully disposed"
```

### 27.2 Memory Rules

| Resource | Limit | Cleanup Trigger |
|----------|-------|-----------------|
| Panel messages | 1000 per session | Auto-trim on overflow |
| Panelist sessions | N (configurable, default 3-5) | Disposed after convergence |
| Head session | 1 per session | Disposed on Reset only |
| Moderator session | 1 per session | Disposed after convergence |
| Event log (UI) | 500 entries | Oldest removed on overflow |
| Knowledge Brief | ~2K tokens | Replaced on new discussion |
| Circuit breakers | Per-tool | Cleared on session Reset |

### 27.3 Background Cleanup Service

```csharp
/// <summary>
/// Periodic cleanup service that scans for zombie sessions.
/// Runs every 5 minutes and terminates any sessions that have been
/// in Running/Paused state for longer than 2x MaxDiscussionDuration.
/// </summary>
public sealed class PanelCleanupService : IDisposable
{
    private readonly Timer _timer;
    private readonly IPanelOrchestrator _orchestrator;
    private readonly ILogger<PanelCleanupService> _logger;

    public PanelCleanupService(
        IPanelOrchestrator orchestrator,
        ILogger<PanelCleanupService> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
        _timer = new Timer(Cleanup, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private void Cleanup(object? state)
    {
        // Check if current session has exceeded safety limits
        if (_orchestrator.ActiveSessionId is not null
            && _orchestrator.CurrentPhase is PanelPhase.Running or PanelPhase.Paused)
        {
            _logger.LogDebug("[PanelCleanup] Active session check: Phase={Phase}",
                _orchestrator.CurrentPhase);
            // Additional cleanup logic based on elapsed time
        }
    }

    public void Dispose() => _timer.Dispose();
}
```

---

## 28. Error Recovery Playbook

| Error Class | Examples | Detection | Recovery | User Communication |
|-------------|----------|-----------|----------|-------------------|
| **Transient** | Network timeout, rate limit, 503 | Specific exception types | Retry with exponential backoff (max 3) | "Retrying... (attempt 2/3)" |
| **Connection Lost** | Session disconnect | `HasActiveSession() == false` | Recreate session, replay system prompt | "Reconnecting..." |
| **LLM Parse Failure** | Invalid JSON from Moderator | `JsonException` | Use `ModeratorDecision.Fallback()` — fail-open | Transparent (handled internally) |
| **Panelist Failure** | Panelist timeout/crash | Exception in `ProcessAsync` | Skip panelist this turn, continue with others | "Panelist [name] unavailable — continuing" |
| **Tool Failure** | MCP server down | Circuit breaker trips | Disable tool, notify panelists | "Tool [name] temporarily unavailable" |
| **Fatal** | Out of memory, disk full | Unhandled exception | Transition to Failed, persist state | "Error occurred. Click Reset to try again." |
| **User Cancel** | Stop button | `OperationCanceledException` | Clean shutdown, preserve messages | "Stopped. Your progress has been saved." |

---

## 29. Cost Estimation & Budget Management

```csharp
namespace CopilotAgent.Panel.Models;

/// <summary>
/// Cost estimate presented to the user before starting a panel.
/// Shown during AwaitingApproval phase so the user can make
/// an informed decision about proceeding.
/// </summary>
public sealed record CostEstimate
{
    public required int EstimatedPanelistCount { get; init; }
    public required int EstimatedTurns { get; init; }
    public required int EstimatedTotalTokens { get; init; }
    public required TimeSpan EstimatedDuration { get; init; }
    public required string Summary { get; init; }

    public static CostEstimate Calculate(
        int panelistCount, int maxTurns, int avgTokensPerTurn)
    {
        var totalTokens = panelistCount * maxTurns * avgTokensPerTurn;
        var durationSeconds = panelistCount * maxTurns * 15; // ~15s per turn per panelist

        return new CostEstimate
        {
            EstimatedPanelistCount = panelistCount,
            EstimatedTurns = maxTurns,
            EstimatedTotalTokens = totalTokens,
            EstimatedDuration = TimeSpan.FromSeconds(durationSeconds),
            Summary = $"~{panelistCount} panelists × {maxTurns} turns ≈ " +
                      $"{totalTokens / 1000}K tokens, ~{durationSeconds / 60} minutes"
        };
    }
}
```

---

## 30. Three-Pane Layout

```
┌────────────────────────────────────────────────────────────────────────┐
│  [≡] CopilotAgent  │ Chat │ Teams │ Office │ [Panel Discussion]  [⚙] │
├────────────────────┬──────────────────────────────────┬────────────────┤
│                    │                                  │                │
│  USER ↔ HEAD       │      PANEL VISUALIZER           │   AGENT        │
│  CONVERSATION      │      (Live Canvas)              │   INSPECTOR    │
│  ──────────────    │      ───────────────            │   ──────────   │
│  User: Analyze..   │      [Head] 💬                  │   Selected:    │
│  Head: I need to   │         ↓                       │   SecurityAnlst│
│    clarify...      │      [Panelist A] 🛡️            │   ──────────   │
│  User: Focus on    │      [Panelist B] ⚡            │   Reasoning:   │
│    security and    │      [Panelist C] 🏗️            │   [Collapse ▼] │
│    performance     │         ↓                       │   ──────────   │
│  Head: Starting    │      [Moderator] ⚖️             │   Tools Used:  │
│    panel with 3    │         ↓                       │   • FileScan   │
│    experts...      │      Convergence: 45%           │   • WebSearch  │
│                    │      Turn 5/30                   │                │
│  ──────────────    ├──────────────────────────────────┤   Status:     │
│  [▶ Play] [⏸ Pause]│  DISCUSSION STREAM               │   Thinking... │
│  [⏹ Stop] [🔄 Reset]│  SecurityAnalyst: I found...    │                │
│                    │  PerformanceExpert: The hot...   │   Commentary:  │
│  State: Running    │  Architect: Considering the...   │   [Detailed ▼] │
│  Turn: 5/30        │                                  │                │
│  ETA: ~8 min       │  [Send message to Head]          │                │
└────────────────────┴──────────────────────────────────┴────────────────┘
```

---

## 31. Settings & Commentary Mode

### 31.1 PanelSettings Model

```csharp
namespace CopilotAgent.Panel.Models;

/// <summary>
/// Settings DTO stored in CopilotAgent.Core/Models/AppSettings.cs
/// under a new PanelSettings property.
/// 
/// Follows the same snapshot-based dirty tracking pattern as
/// MultiAgentSettings and OfficeSettings.
/// </summary>
public class PanelSettings
{
    /// <summary>Model used for Head and Moderator agents.</summary>
    public string PrimaryModel { get; set; } = string.Empty;

    /// <summary>Pool of models for panelist agents (random selection).</summary>
    public List<string> PanelistModels { get; set; } = [];

    /// <summary>Maximum number of panelists per discussion.</summary>
    public int MaxPanelists { get; set; } = 5;

    /// <summary>Maximum turns before forced convergence.</summary>
    public int MaxTurns { get; set; } = 30;

    /// <summary>Maximum discussion duration in minutes.</summary>
    public int MaxDurationMinutes { get; set; } = 30;

    /// <summary>Maximum total tokens across all agents.</summary>
    public int MaxTotalTokens { get; set; } = 100_000;

    /// <summary>Maximum tool calls across the discussion.</summary>
    public int MaxToolCalls { get; set; } = 50;

    /// <summary>Whether panelists can access the file system.</summary>
    public bool AllowFileSystemAccess { get; set; } = true;

    /// <summary>Commentary verbosity: Detailed, Brief, or Off.</summary>
    public string CommentaryMode { get; set; } = "Brief";

    /// <summary>Working directory for file system tools.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Enabled MCP servers for panelist tools.</summary>
    public List<string> EnabledMcpServers { get; set; } = [];

    /// <summary>Convergence score threshold (0-100) to trigger synthesis.</summary>
    public int ConvergenceThreshold { get; set; } = 80;
}
```

### 31.2 Commentary Mode Filtering

```csharp
// In PanelViewModel — filter commentary based on user preference
_orchestrator.Events
    .OfType<CommentaryEvent>()
    .Where(e => ShouldShowCommentary(e.MinimumLevel))
    .ObserveOn(DispatcherScheduler.Current)
    .Subscribe(e => Commentary.Add(FormatCommentary(e)));

private bool ShouldShowCommentary(CommentaryMode eventLevel)
{
    var userPref = Enum.Parse<CommentaryMode>(Settings.CommentaryMode);
    return userPref switch
    {
        CommentaryMode.Detailed => true,                    // Show everything
        CommentaryMode.Brief => eventLevel <= CommentaryMode.Brief,
        CommentaryMode.Off => false,                        // Show nothing
        _ => true
    };
}
```

---

## 32. Meta-Question Support

Users can ask the Head questions about panel status **while the panel is running**, without interrupting the discussion:

```
User types: "How long will this take?"
    │
    ├── PanelViewModel detects message during Running phase
    ├── Calls orchestrator.SendUserMessageAsync(message)
    ├── Orchestrator delegates to Head.HandleMetaQuestion()
    │   └── Returns formatted status without LLM call (instant response)
    ├── Response added to user ↔ Head chat panel
    └── Debate loop continues uninterrupted on background thread
```

---

## 33. Live Visualization

### 33.1 Agent Avatar States

| Agent Status | Visual | Animation |
|-------------|--------|-----------|
| Created | Semi-transparent avatar | Fade in |
| Active | Full opacity, accent border | None |
| Thinking | Full opacity, spinning ring | Indeterminate progress |
| Idle | 80% opacity | None |
| Paused | Amber overlay | Pulse |
| Disposed | Faded out | Fade out + remove |

### 33.2 Convergence Progress Bar

```xml
<!-- Convergence visualization -->
<ProgressBar Minimum="0" Maximum="100"
             Value="{Binding ConvergenceScore}"
             Foreground="{Binding ConvergenceColor}"/>
<TextBlock Text="{Binding ConvergenceScore, StringFormat='Convergence: {0}%'}"/>

<!-- Color logic:
     0-40:  Red (#F44336)    — early discussion
     40-70: Amber (#FFC107)  — progressing
     70-80: Yellow (#FFEB3B) — approaching threshold
     80+:   Green (#4CAF50)  — convergence reached
-->
```

---

## 34. Integration with Existing Application

### 34.1 DI Registration (App.xaml.cs)

```csharp
// Add to existing ConfigureServices in App.xaml.cs
// ── Panel Discussion Services (NEW — additive only) ──
services.AddSingleton<ISubject<PanelEvent>>(new Subject<PanelEvent>());
services.AddSingleton<IPanelOrchestrator, PanelOrchestrator>();
services.AddSingleton<IPanelAgentFactory, PanelAgentFactory>();
services.AddSingleton<IConvergenceDetector, ConvergenceDetector>();
services.AddSingleton<IKnowledgeBriefService, KnowledgeBriefService>();
services.AddSingleton<PanelCleanupService>();
services.AddTransient<PanelViewModel>();
```

### 34.2 Tab Addition (MainWindow.xaml)

```xml
<!-- Add after existing tabs -->
<TabItem Header="Panel Discussion">
    <local:PanelView x:Name="PanelTab"/>
</TabItem>
```

### 34.3 Lazy Initialization (MainWindow.xaml.cs)

```csharp
// Same lazy init pattern as AgentTeamView and OfficeView
private void OnPanelTabSelected(object sender, SelectionChangedEventArgs e)
{
    if (PanelTab.DataContext is null)
    {
        PanelTab.DataContext = _serviceProvider.GetRequiredService<PanelViewModel>();
    }
}
```

---

## 35. Service Interface Contract

```csharp
namespace CopilotAgent.Panel.Domain.Interfaces;

/// <summary>
/// Primary interface for Panel Discussion functionality.
/// The ViewModel interacts exclusively through this interface.
/// </summary>
public interface IPanelOrchestrator
{
    PanelSessionId? ActiveSessionId { get; }
    PanelPhase CurrentPhase { get; }
    IObservable<PanelEvent> Events { get; }

    Task<PanelSessionId> StartAsync(string userPrompt, PanelSettings settings, CancellationToken ct = default);
    Task SendUserMessageAsync(string message, CancellationToken ct = default);
    Task ApproveAndStartPanelAsync(CancellationToken ct = default);
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
    Task ResetAsync();
}
```

---

## 36. Testing Strategy

### 36.1 Unit Tests

| Area | Tests | Mock Strategy |
|------|-------|---------------|
| **State Machine** | All valid transitions, all invalid transitions, guard clauses | None (pure logic) |
| **GuardRailPolicy** | Validation, FromSettings with clamping, edge values | None (pure logic) |
| **ConvergenceDetector** | Parse valid JSON, parse invalid JSON, score thresholds | Mock `ICopilotService` |
| **ModeratorDecision** | Parse valid JSON, fallback on invalid, convergence scores | Mock `ICopilotService` |
| **PanelConversationManager** | Add, trim, thread safety, export | None (pure logic) |
| **ToolCircuitBreaker** | Open/close/half-open transitions, concurrent access | None (pure logic) |
| **CostEstimate** | Calculation accuracy, edge cases | None (pure logic) |
| **KnowledgeBrief** | Build from messages, answer follow-ups | Mock `ICopilotService` |

### 36.2 Integration Tests

| Scenario | Description |
|----------|-------------|
| Full discussion flow | Start → Clarify → Approve → Run → Converge → Synthesize → Complete |
| Pause/Resume | Start → Run → Pause → Resume → Complete |
| Stop mid-discussion | Start → Run → Stop → verify agent disposal |
| Reset | Start → Run → Reset → verify clean state |
| Follow-up Q&A | Complete → ask follow-up → verify Knowledge Brief used |
| Error recovery | Inject failure → verify graceful degradation |

### 36.3 Test Project Structure

```
tests/CopilotAgent.Tests/Panel/
├── Domain/
│   ├── PanelSessionTests.cs
│   ├── GuardRailPolicyTests.cs
│   └── ValueObjectTests.cs
├── StateMachine/
│   └── PanelStateMachineTests.cs
├── Agents/
│   ├── ConvergenceDetectorTests.cs
│   ├── ModeratorDecisionParsingTests.cs
│   └── HeadAgentTests.cs
├── Orchestration/
│   ├── PanelOrchestratorTests.cs
│   └── TurnManagerTests.cs
├── Resilience/
│   └── ToolCircuitBreakerTests.cs
└── Services/
    ├── PanelConversationManagerTests.cs
    └── CostEstimationTests.cs
```

---

## 37. Implementation Roadmap

### Sprint 0: Foundation (Week 1-2)
- [ ] Create `CopilotAgent.Panel` project with dependency references
- [ ] Define all domain entities, value objects, and enums
- [ ] Define all interfaces
- [ ] Add `PanelSettings` to `AppSettings`
- [ ] Set up test project structure

### Sprint 1: State Machine & Domain (Week 3-4)
- [ ] Implement `PanelStateMachine` using Stateless library
- [ ] Implement `GuardRailPolicy`
- [ ] Write unit tests for all state transitions (valid + invalid)
- [ ] Write unit tests for guard rail validation

### Sprint 2: Agent Framework (Week 5-6)
- [ ] Implement `PanelAgentBase` with Copilot SDK session management
- [ ] Implement `HeadAgent` (clarification, topic generation)
- [ ] Implement `ModeratorAgent` (structured JSON decisions)
- [ ] Implement `PanelistAgent` with tool access
- [ ] Implement `ConvergenceDetector`
- [ ] Implement `PanelAgentFactory`

### Sprint 3: Orchestration Engine (Week 7-8)
- [ ] Implement `PanelOrchestrator` with full debate loop
- [ ] Implement `TurnManager`
- [ ] Implement SemaphoreSlim pause/resume
- [ ] Implement `PanelConversationManager`
- [ ] Integration test: full discussion flow

### Sprint 4: Resilience (Week 9-10)
- [ ] Implement `ToolCircuitBreaker`
- [ ] Implement `SandboxedToolExecutor`
- [ ] Implement `PanelRetryPolicy`
- [ ] Implement `PanelCleanupService`
- [ ] Implement `KnowledgeBriefService`
- [ ] Integration test: error recovery scenarios

### Sprint 5: UI - Core (Week 11-12)
- [ ] Create `PanelView.xaml` with three-pane layout
- [ ] Create `PanelViewModel` with Rx.NET event subscriptions
- [ ] Implement settings side panel with dirty tracking
- [ ] Implement user ↔ Head chat panel
- [ ] Add tab to `MainWindow.xaml`

### Sprint 6: UI - Visualization (Week 13-14)
- [ ] Implement agent avatar visualization
- [ ] Implement convergence progress bar
- [ ] Implement discussion stream panel
- [ ] Implement commentary expander with mode filtering
- [ ] Implement Play/Pause/Stop/Reset controls

### Sprint 7: Polish & Integration (Week 15-16)
- [ ] Implement cost estimation display
- [ ] Implement meta-question support
- [ ] Memory profiling (create/destroy 50 sessions → flat memory)
- [ ] Full regression test of existing features
- [ ] Documentation and user guide

---

## 38. Production Readiness Checklist

### Before Shipping

| # | Check | Status |
|---|-------|--------|
| 1 | State machine: all 11 phases with explicit transitions | ☐ |
| 2 | Stateless library: deterministic FSM with DOT graph export | ☐ |
| 3 | Guard rails: all limits enforced (turns, tokens, time, tools) | ☐ |
| 4 | Pause/Resume: SemaphoreSlim pattern, safe at turn boundaries | ☐ |
| 5 | IAsyncDisposable: all agents, sessions, and resources | ☐ |
| 6 | Circuit breaker: per-tool, with open/half-open/closed states | ☐ |
| 7 | Conversation memory: auto-trim at 1000 messages | ☐ |
| 8 | Knowledge Brief: compressed context for follow-up Q&A | ☐ |
| 9 | Convergence: AI-powered detection with structured JSON scores | ☐ |
| 10 | Moderator decisions: structured JSON with fallback | ☐ |
| 11 | Event streaming: IObservable with Rx.NET | ☐ |
| 12 | Background execution: debate loop on Task.Run, immune to UI | ☐ |
| 13 | Settings: snapshot-based dirty tracking (same pattern as existing) | ☐ |
| 14 | Cost estimation: shown before panel approval | ☐ |
| 15 | Meta-questions: user can ask Head during execution | ☐ |
| 16 | Commentary mode: Detailed / Brief / Off filtering | ☐ |
| 17 | Three-pane layout: User chat + Visualizer + Inspector | ☐ |
| 18 | Structured logging: all events with [PanelXxx] prefix | ☐ |
| 19 | Zero regression: existing features unchanged | ☐ |
| 20 | Unit tests: >80% coverage on domain, state machine, resilience | ☐ |

### Before Production

| # | Check | Status |
|---|-------|--------|
| 1 | Memory profile: 50 session create/destroy → flat memory | ☐ |
| 2 | Long session: 2-hour continuous discussion without leak | ☐ |
| 3 | Pause/Resume: pause for 30 minutes, resume works correctly | ☐ |
| 4 | Network disconnect: session recreation and state recovery | ☐ |
| 5 | UI responsiveness: 60 FPS during heavy panelist activity | ☐ |
| 6 | Existing features: full regression test passes | ☐ |
| 7 | Tab switching: no event handler leaks across tab changes | ☐ |
| 8 | Settings persistence: round-trip save → close → open → load | ☐ |
| 9 | Error display: no stack traces visible to users | ☐ |
| 10 | Cleanup service: zombie sessions terminated after 2x max duration | ☐ |

---

## Appendix A: Credits & Sources

This architecture incorporates the best patterns from competitive analysis of five independent architectural proposals:

| Source | Key Contributions |
|--------|-------------------|
| **Gemini** | Stateless FSM library, System.Reactive event streaming, SemaphoreSlim pause, Moderator JSON decisions, IAsyncDisposable disposal patterns |
| **Gork** | Domain-expertise agent roles, critique-refine quality gates, moderator-based dynamic orchestration, production readiness standards |
| **Haiku** | Value Objects (readonly record structs), Guard-Rail Policy entity, rich event taxonomy, ConvergenceDetector as separate service, three-pane UI layout, comprehensive agent base class |
| **KIMI** | Background IHostedService execution, MediatR CQRS pattern (evaluated — using Rx.NET instead), circuit breaker for tools, tool sandboxing, SQLite persistence (evaluated — using JSON for consistency), 20-week roadmap |
| **Perplexity** | 11-state lifecycle, Knowledge Brief pattern, meta-question support, commentary mode setting, cost estimation, workspace-to-MCP mapping, background cleanup job, SignalR streaming (evaluated — using Rx.NET for desktop) |

All patterns have been adapted to fit the existing CopilotDesktop architecture (WPF, Copilot SDK, CopilotAgent.Core) while maintaining zero regression to existing features.

---

*End of Panel Discussion Architecture & Design Specification — Version 1.0*

*This document is the canonical reference for implementing the Panel Discussion feature.*  
*Every pattern is designed for an application serving millions of users.*  
*No shortcuts. No quick fixes. Clean, extensible, debuggable, maintainable.*