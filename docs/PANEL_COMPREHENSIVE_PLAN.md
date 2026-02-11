# Panel Discussion — Comprehensive Implementation Plan

> **Version**: 1.0  
> **Status**: Approved Design — Implementation Ready  
> **Date**: February 2026  
> **Scope**: `CopilotAgent.Panel` — Multi-agent panel discussion system  
> **Companion**: [`PANEL_ARCHITECTURE_DESIGN.md`](PANEL_ARCHITECTURE_DESIGN.md) — Detailed code-level specification  
> **Non-Negotiable**: ZERO regression to existing features

---

## Table of Contents

1. [Architecture](#1-architecture)
2. [Use Cases & Examples](#2-use-cases--examples)
3. [High-Level Design (HLD)](#3-high-level-design-hld)
4. [Low-Level Design (LLD)](#4-low-level-design-lld)
5. [Technical Design, UI & Code Flow](#5-technical-design-ui--code-flow)
6. [Plan of Action & Phases](#6-plan-of-action--phases)

---

# 1. Architecture

## 1.1 Product Vision

A **desktop-native, in-process, multi-agent panel discussion system** where a configurable team of AI agents — led by a **Head**, moderated by a **Moderator**, and staffed by domain-expert **Panelists** — collaborate, debate, and converge on complex analytical tasks.

**Key Differentiators**:
- In-process orchestration (zero network hops for orchestration logic)
- Full deterministic state machine (11 states, explicit transitions)
- Live commentary with configurable verbosity (Detailed / Brief / Off)
- Follow-up Q&A after discussion via compressed Knowledge Brief
- Pause/Resume at safe turn boundaries via SemaphoreSlim
- Structured convergence detection with JSON-scored decisions

## 1.2 Architecture Principles

| Principle | Application |
|-----------|-------------|
| **KISS** | Every component does one thing. No god classes. |
| **SOLID** | Single responsibility per class. Open for extension (new agent roles, tools). |
| **Clean Architecture** | Domain layer has zero external dependencies. Infrastructure swappable. |
| **Observable by Default** | Every state transition, agent action, tool call → `IObservable<PanelEvent>` |
| **Fail-Safe** | Timeouts, retries, circuit breakers on all LLM/tool calls |
| **Memory-Safe** | `IAsyncDisposable` on every agent, session, conversation buffer |
| **Zero Regression** | Only additive changes to `App.xaml.cs`, `MainWindow.xaml`, `AppSettings.cs` |
| **Codebase-First** | Reuse `ICopilotService`, `IToolApprovalService`, `IPersistenceService`, `ISessionManager` |

## 1.3 Technology Stack

| Component | Technology | NuGet Package | Justification |
|-----------|------------|---------------|---------------|
| State Machine | Stateless | `Stateless` | Battle-tested FSM with async guards, serializable transitions |
| Event Streaming | System.Reactive | `System.Reactive` | `IObservable<PanelEvent>` with thread-safe `ObserveOn` |
| MVVM | CommunityToolkit.Mvvm | Already in app | Source generators, consistent with existing ViewModels |
| DI | Microsoft.Extensions.DI | Already in app | Consistent registration pattern |
| AI Backend | Copilot SDK | Via `CopilotAgent.Core` | `ICopilotService` for all LLM calls |
| Resilience | Polly v8 | `Microsoft.Extensions.Http.Resilience` | Circuit breaker, retry, timeout |
| Logging | ILogger<T> | Already available | Structured logging with `[PanelXxx]` prefix |

## 1.4 Project Structure & Dependency Graph

```
CopilotAgent.App                    (EXISTING — minimal additive changes)
  ├── CopilotAgent.Panel            (NEW — entire panel feature)
  ├── CopilotAgent.MultiAgent       (EXISTING — UNTOUCHED)
  ├── CopilotAgent.Office           (EXISTING — UNTOUCHED)
  ├── CopilotAgent.Core             (EXISTING — UNTOUCHED, consumed by Panel)
  └── CopilotAgent.Persistence      (EXISTING — UNTOUCHED, consumed by Panel)

CopilotAgent.Panel ──► CopilotAgent.Core
CopilotAgent.Panel ──► Stateless (NuGet)
CopilotAgent.Panel ──► System.Reactive (NuGet)
```

### New Project Directory Layout

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
│   │   ├── PanelEvent.cs
│   │   ├── PhaseChangedEvent.cs
│   │   ├── AgentMessageEvent.cs
│   │   ├── AgentStatusChangedEvent.cs
│   │   ├── ToolCallEvent.cs
│   │   ├── ModerationEvent.cs
│   │   ├── CommentaryEvent.cs
│   │   ├── ProgressEvent.cs
│   │   ├── ErrorEvent.cs
│   │   └── CostUpdateEvent.cs
│   ├── Policies/
│   │   └── GuardRailPolicy.cs
│   └── Interfaces/
│       ├── IPanelOrchestrator.cs
│       ├── IPanelAgent.cs
│       ├── IPanelAgentFactory.cs
│       ├── IConvergenceDetector.cs
│       └── IKnowledgeBriefService.cs
├── StateMachine/
│   └── PanelStateMachine.cs
├── Orchestration/
│   ├── PanelOrchestrator.cs
│   ├── TurnManager.cs
│   └── AgentSupervisor.cs
├── Agents/
│   ├── PanelAgentBase.cs
│   ├── HeadAgent.cs
│   ├── ModeratorAgent.cs
│   ├── PanelistAgent.cs
│   ├── PanelAgentFactory.cs
│   ├── ConvergenceDetector.cs
│   └── DefaultPanelistProfiles.cs
├── Services/
│   ├── KnowledgeBriefService.cs
│   ├── PanelConversationManager.cs
│   ├── PanelToolRouter.cs
│   └── CostEstimationService.cs
├── Resilience/
│   ├── ToolCircuitBreaker.cs
│   ├── SandboxedToolExecutor.cs
│   └── PanelRetryPolicy.cs
└── Models/
    ├── PanelSettings.cs
    ├── PanelConfig.cs
    ├── ModeratorDecision.cs
    ├── ModerationResult.cs
    ├── CritiqueVerdict.cs
    ├── KnowledgeBrief.cs
    ├── CostEstimate.cs
    ├── PanelistProfile.cs
    ├── AgentInput.cs
    ├── AgentOutput.cs
    ├── ConvergenceResult.cs
    └── ToolCallRecord.cs
```

### Changes to Existing Projects (Exhaustive List)

| File | Change Type | Exact Change | Risk |
|------|------------|--------------|------|
| `CopilotAgent.App/CopilotAgent.App.csproj` | Add | `<ProjectReference Include="..\CopilotAgent.Panel\CopilotAgent.Panel.csproj" />` | Zero |
| `CopilotAgent.App/App.xaml.cs` | Add | DI registration block for Panel services (~10 lines) | Zero — additive |
| `CopilotAgent.App/MainWindow.xaml` | Add | One `<TabItem Header="Panel Discussion">` element | Zero — additive |
| `CopilotAgent.App/MainWindow.xaml.cs` | Add | Lazy init handler for Panel tab (~5 lines) | Zero — additive |
| `CopilotAgent.Core/Models/AppSettings.cs` | Add | `public PanelSettings Panel { get; set; } = new();` property | Zero — additive |

**NO other files in existing projects are modified.**

---

# 2. Use Cases & Examples

## 2.1 Primary Use Cases

### UC-1: Code Review & Analysis Panel

**Actor**: Developer  
**Goal**: Get a comprehensive multi-perspective analysis of a codebase or PR

**Example Flow**:
1. User types: *"Analyze the authentication module in src/auth/ for security vulnerabilities, performance issues, and test gaps"*
2. **Head** asks clarifying questions: *"Should I focus on OAuth2 flows or API key auth? Any known issues?"*
3. User responds: *"Focus on OAuth2. We had a token refresh bug last month."*
4. Head produces **Topic of Discussion** — user approves
5. Panel spawns: **Security Analyst** 🛡️, **Performance Expert** ⚡, **QA Engineer** 🧪
6. Panelists debate for ~8 turns:
   - Security Analyst: *"Token refresh lacks PKCE validation. CWE-287 risk."*
   - Performance Expert: *"Token cache lookup is O(n) with 50K users. Use dictionary."*
   - QA Engineer: *"Zero integration tests for refresh flow. 3 boundary cases uncovered."*
7. **Moderator** detects convergence at score 85
8. **Head** synthesizes final report with Executive Summary, Agreements, Disagreements, Action Items
9. User asks follow-up: *"How should we fix the PKCE issue?"* → Head answers from Knowledge Brief

### UC-2: Architecture Design Review

**Actor**: Tech Lead  
**Goal**: Evaluate a proposed system design from multiple angles

**Example**:
- User: *"Review our proposed microservices migration plan in docs/migration.md"*
- Panel: **Architect** 🏗️, **Database Expert** 🗄️, **DevOps Specialist** 🚀, **Edge Case Hunter** 🎯
- Output: Structured report with design trade-offs, migration risks, data consistency concerns, deployment strategy gaps

### UC-3: Research & Technology Evaluation

**Actor**: Engineering Manager  
**Goal**: Compare technology options for a key decision

**Example**:
- User: *"Compare Redis vs Memcached vs DragonflyDB for our session cache. We need 100K ops/sec, 50GB data, multi-region."*
- Panel: **Researcher** 📚, **Performance Expert** ⚡, **Architect** 🏗️
- Output: Structured comparison matrix with benchmarks, TCO analysis, migration effort

### UC-4: Bug Investigation

**Actor**: Developer  
**Goal**: Multi-angle root cause analysis of a complex bug

**Example**:
- User: *"Our API returns 500 intermittently under load. Only happens with >100 concurrent users. Error logs show timeout exceptions."*
- Panel: **Performance Expert** ⚡, **Code Reviewer** 🔍, **Edge Case Hunter** 🎯
- Output: Ranked list of probable causes, reproduction steps, suggested fixes

### UC-5: Paused & Resumed Discussion

**Actor**: Any user  
**Goal**: Pause a long discussion, attend a meeting, resume later

**Flow**:
1. Panel running at turn 12/30
2. User clicks **Pause** → discussion halts at next safe turn boundary
3. User leaves for 30 minutes
4. User clicks **Resume** → discussion continues from turn 12

### UC-6: Follow-Up Q&A (Post-Discussion)

**Actor**: Any user  
**Goal**: Ask specific questions about a completed discussion

**Flow**:
1. Panel completed with synthesis report
2. User: *"What was the main disagreement between Security Analyst and Architect?"*
3. Head answers using Knowledge Brief (compressed context, no re-running panel)
4. User: *"Draft a JIRA ticket for the PKCE fix"* → Head drafts based on discussion findings

### UC-7: Meta-Questions During Execution

**Actor**: Impatient user  
**Goal**: Check panel status without interrupting

**Flow**:
1. Panel running at turn 8/30
2. User types: *"How long will this take?"*
3. Head responds instantly (no LLM call): *"Phase: Running, Turn 8/30, ~5 minutes remaining"*
4. Debate continues uninterrupted on background thread

## 2.2 Edge Cases & Boundary Scenarios

| Scenario | Expected Behavior |
|----------|------------------|
| User submits empty prompt | Head asks for more detail (Clarifying phase) |
| LLM says "CLEAR" immediately | Skip clarification, go directly to topic generation |
| User rejects Topic of Discussion | Return to Clarifying with feedback |
| All panelists agree on turn 2 | Convergence detected early, proceed to synthesis |
| Panelist LLM times out | Skip that panelist's turn, continue with others |
| Circuit breaker trips for a tool | Tool disabled, panelist continues without that tool |
| User stops mid-discussion | Clean shutdown, partial results preserved |
| Discussion hits MaxTurns limit | Force convergence, synthesize what's available |
| Discussion hits MaxDuration limit | Same as MaxTurns — force convergence |
| Network disconnect during discussion | Session reconnect, or graceful error with preserved state |
| User switches to another tab during discussion | Background thread continues, events queue up |

---

# 3. High-Level Design (HLD)

## 3.1 System Context Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         CopilotAgent.App (WPF Shell)                     │
│                                                                          │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌─────────────────────────┐  │
│  │ ChatView │  │ TeamView │  │OfficeView│  │    PanelView (NEW)      │  │
│  │(EXISTING)│  │(EXISTING)│  │(EXISTING)│  │  ┌───────────────────┐  │  │
│  └──────────┘  └──────────┘  └──────────┘  │  │ PanelViewModel    │  │  │
│                                             │  │ (MVVM + Rx.NET)   │  │  │
│                                             │  └─────────┬─────────┘  │  │
│                                             │            │             │  │
│                                             │    IObservable<Event>    │  │
│                                             │            │             │  │
│                                             │  ┌─────────▼─────────┐  │  │
│                                             │  │ PanelOrchestrator │  │  │
│                                             │  │ ┌───────────────┐ │  │  │
│                                             │  │ │ Stateless FSM │ │  │  │
│                                             │  │ │ HeadAgent     │ │  │  │
│                                             │  │ │ ModeratorAgent│ │  │  │
│                                             │  │ │ PanelistAgent │ │  │  │
│                                             │  │ │ TurnManager   │ │  │  │
│                                             │  │ │ Convergence   │ │  │  │
│                                             │  │ └───────────────┘ │  │  │
│                                             │  └─────────┬─────────┘  │  │
│                                             └────────────┼────────────┘  │
│                                                          │               │
├──────────────────────────────────────────────────────────┼───────────────┤
│                    CopilotAgent.Core (SHARED — UNTOUCHED)│               │
│  ┌──────────────┐ ┌───────────────┐ ┌──────────────────┐│               │
│  │ICopilotService│ │ISessionManager│ │IToolApprovalSvc ◄┘               │
│  └──────────────┘ └───────────────┘ └──────────────────┘                │
└─────────────────────────────────────────────────────────────────────────┘
```

## 3.2 Component Interaction (HLD)

```
User ──► PanelViewModel ──► PanelOrchestrator ──► PanelStateMachine (FSM)
                                    │
                                    ├──► HeadAgent ──► ICopilotService (LLM)
                                    ├──► ModeratorAgent ──► ICopilotService (LLM)
                                    ├──► PanelistAgent[] ──► ICopilotService (LLM)
                                    ├──► ConvergenceDetector ──► ICopilotService (LLM)
                                    ├──► TurnManager (metrics tracking)
                                    ├──► PanelConversationManager (message storage)
                                    ├──► SandboxedToolExecutor ──► IToolApprovalService
                                    └──► KnowledgeBriefService (post-discussion Q&A)
```

## 3.3 Data Flow (HLD)

```
                      ┌──────────────────────────────────────────────┐
                      │              DATA FLOW                        │
                      │                                               │
  User Prompt ───────►│ HEAD (clarify) ───► Topic of Discussion ─────►│
                      │                                               │
                      │ For each turn:                                │
                      │   MODERATOR (decide next speaker) ───────────►│
                      │   PANELIST (analyze, produce argument) ──────►│
                      │   MODERATOR (validate, check convergence) ───►│
                      │   Store message in ConversationManager        │
                      │   Emit AgentMessageEvent to UI                │
                      │                                               │
                      │ On convergence:                               │
                      │   HEAD (synthesize all messages) ────────────►│
                      │   KnowledgeBriefService (compress for Q&A) ──►│
                      │   Emit SynthesisComplete                      │
                      │                                               │
  Follow-up Q&A ─────►│ HEAD (answer from KnowledgeBrief) ──────────►│
                      └──────────────────────────────────────────────┘
```

## 3.4 State Machine (HLD)

```
Idle ──[UserSubmitted]──► Clarifying ──[ClarificationsComplete]──► AwaitingApproval
                                                                        │
                                                            [UserApproved]
                                                                        ▼
                                                                   Preparing
                                                                        │
                                                            [PanelistsReady]
                                                                        ▼
                          ┌──[UserPaused]── Running ──[ConvergenceDetected]──► Converging
                          │                    │                                     │
                          ▼                    │                          [TurnCompleted]
                        Paused ──[UserResumed]─┘                                    ▼
                                                                              Synthesizing
                                                                                    │
                                                                      [SynthesisComplete]
                                                                                    ▼
                                                                               Completed
                                                                                    │
                                                                     [UserSubmitted] (follow-up)
                                                                                    ▼
                                                                              Clarifying

Terminal States: Stopped, Failed ──[Reset]──► Idle
UserStopped: accessible from Running, Clarifying, AwaitingApproval, Preparing, Converging, Synthesizing
Error: accessible from Running, Preparing, Converging, Synthesizing
```

**11 States**: Idle, Clarifying, AwaitingApproval, Preparing, Running, Paused, Converging, Synthesizing, Completed, Stopped, Failed

**13 Triggers**: UserSubmitted, ClarificationsComplete, UserApproved, PanelistsReady, TurnCompleted, ConvergenceDetected, Timeout, SynthesisComplete, UserPaused, UserResumed, UserStopped, Error, Reset

## 3.5 Agent Hierarchy (HLD)

```
IPanelAgent (interface)
├── PanelAgentBase (abstract, shared LLM session management)
│   ├── HeadAgent
│   │   ├── ClarifyAsync() — analyze prompt, ask questions
│   │   ├── BuildTopicOfDiscussionAsync() — generate ToD from clarifications
│   │   ├── SynthesizeAsync() — produce final report from all messages
│   │   ├── AnswerFollowUpAsync() — answer using KnowledgeBrief
│   │   └── HandleMetaQuestion() — instant status response (no LLM)
│   ├── ModeratorAgent
│   │   ├── DecideNextTurnAsync() — structured JSON: next speaker, convergence score
│   │   ├── ValidateMessageAsync() — check message against GuardRailPolicy
│   │   └── ForceConvergenceAsync() — when limits exceeded
│   └── PanelistAgent
│       ├── ProcessAsync() — produce one turn of analysis
│       └── Internal: system prompt from PanelistProfile, tool access
```

## 3.6 Shared Core Services (Reused, Not Duplicated)

| Service from Core | How Panel Uses It |
|-------------------|-------------------|
| `ICopilotService` | All LLM calls (Head, Moderator, each Panelist gets own session) |
| `ISessionManager` | Session creation, lifecycle management |
| `IToolApprovalService` | Tool approval for panelist tool calls |
| `IApprovalQueue` | Serialized approval requests (prevent dialog storms) |
| `IPersistenceService` | Save/load PanelSettings and discussion history |
| `ChatMessage` | Message model for conversations |
| `Session` | Session configuration for Copilot SDK |
| `AppSettings` | Application-wide settings container (add `PanelSettings` property) |

---

# 4. Low-Level Design (LLD)

## 4.1 Domain Entities — Complete Class Catalog

### 4.1.1 `PanelSession` (Aggregate Root)

| Member | Type | Description |
|--------|------|-------------|
| `Id` | `PanelSessionId` | Unique session identifier (value object) |
| `Phase` | `PanelPhase` | Current lifecycle phase |
| `OriginalUserPrompt` | `string` | Original user input |
| `RefinedTopicOfDiscussion` | `string?` | Head-generated topic after clarification |
| `CreatedAt` | `DateTimeOffset` | Session creation timestamp |
| `CompletedAt` | `DateTimeOffset?` | When terminal state reached |
| `GuardRails` | `GuardRailPolicy` | Immutable policy for this session |
| `Messages` | `IReadOnlyList<PanelMessage>` | All discussion messages |
| `Agents` | `IReadOnlyList<AgentInstance>` | Registered agents |
| `TransitionTo(phase)` | method | Update phase, set CompletedAt on terminal |
| `SetRefinedTopic(topic)` | method | Set refined topic |
| `AddMessage(msg)` | method | Append message to list |
| `RegisterAgent(agent)` | method | Register an agent |
| `UnregisterAgent(agent)` | method | Remove an agent |
| `DisposeAsync()` | method | Clear messages, mark agents disposed |

### 4.1.2 `AgentInstance` (Lightweight Descriptor)

| Member | Type | Description |
|--------|------|-------------|
| `Id` | `Guid` | Unique agent ID |
| `Name` | `string` | Display name (e.g., "Security Analyst") |
| `Role` | `PanelAgentRole` | Head, Moderator, Panelist, User |
| `Model` | `ModelIdentifier` | Assigned LLM model |
| `Status` | `PanelAgentStatus` | Created → Active → Thinking → Idle → Disposed |
| `TurnsCompleted` | `int` | Counter |
| `CreatedAt` | `DateTimeOffset` | Creation time |
| `Activate()`, `SetThinking()`, `SetIdle()`, `IncrementTurn()`, `MarkDisposed()` | methods | Status transitions |

### 4.1.3 `PanelMessage` (Immutable Record)

| Member | Type | Description |
|--------|------|-------------|
| `Id` | `Guid` | Unique message ID |
| `SessionId` | `PanelSessionId` | Owning session |
| `AuthorAgentId` | `Guid` | Who wrote it |
| `AuthorName` | `string` | Display name |
| `AuthorRole` | `PanelAgentRole` | Role of author |
| `Content` | `string` | Message text |
| `Type` | `PanelMessageType` | Classification (UserMessage, Clarification, PanelistArgument, etc.) |
| `InReplyTo` | `Guid?` | For threaded discussion |
| `ToolCalls` | `IReadOnlyList<ToolCallRecord>?` | Tool invocations |
| `Timestamp` | `DateTimeOffset` | When created |
| `Create(...)` | static factory | Required-field factory method |

## 4.2 Value Objects — Complete List

| Value Object | Type | Fields | Key Behavior |
|-------------|------|--------|-------------|
| `PanelSessionId` | `readonly record struct` | `Guid Value` | `New()`, `ToString()` → first 8 chars |
| `ModelIdentifier` | `readonly record struct` | `string Provider`, `string ModelName` | `ToString()` → "Provider/ModelName" |
| `TurnNumber` | `readonly record struct` | `int Value` | `Increment()`, `Exceeds(max)` |
| `TokenBudget` | `readonly record struct` | `int MaxTokensPerTurn`, `int MaxTotalTokens` | `IsExceeded(currentTurn, total)` |

## 4.3 Enumerations — Complete List

| Enum | Values | Purpose |
|------|--------|---------|
| `PanelPhase` | Idle, Clarifying, AwaitingApproval, Preparing, Running, Paused, Converging, Synthesizing, Completed, Stopped, Failed (11) | Session lifecycle states |
| `PanelTrigger` | UserSubmitted, ClarificationsComplete, UserApproved, PanelistsReady, TurnCompleted, ConvergenceDetected, Timeout, SynthesisComplete, UserPaused, UserResumed, UserStopped, Error, Reset (13) | State machine triggers |
| `PanelAgentRole` | Head, Moderator, Panelist, User (4) | Structural roles in panel |
| `PanelAgentStatus` | Created, Active, Thinking, Idle, Paused, Disposed (6) | Agent lifecycle |
| `CommentaryMode` | Detailed, Brief, Off (3) | UI verbosity control |
| `PanelMessageType` | UserMessage, Clarification, TopicOfDiscussion, PanelistArgument, ModerationNote, ToolCallResult, Commentary, Synthesis, SystemNotification, Error (10) | Message classification |
| `ModerationAction` | Approved, Blocked, Redirect, ForceConverge, ConvergenceDetected (5) | Moderator decisions |
| `ConvergenceCheckStatus` | Completed, TooEarly, Skipped, ParseError, Error (5) | Convergence check outcomes |

## 4.4 Domain Events — Complete List

| Event | Key Fields | Emitted When |
|-------|-----------|-------------|
| `PanelEvent` (abstract base) | `SessionId`, `Timestamp` | — |
| `PhaseChangedEvent` | `OldPhase`, `NewPhase`, `CorrelationId` | Every state transition |
| `AgentMessageEvent` | `PanelMessage` | Agent produces a message |
| `AgentStatusChangedEvent` | `AgentId`, `AgentName`, `Role`, `NewStatus` | Agent status change |
| `ToolCallEvent` | `AgentId`, `ToolName`, `Input`, `Output`, `Succeeded`, `Duration` | Tool invocation |
| `ModerationEvent` | `Action`, `Reason`, `ConvergenceScore` | Moderator decision |
| `CommentaryEvent` | `AgentId`, `AgentName`, `Role`, `Commentary`, `MinimumLevel` | Agent reasoning/commentary |
| `ProgressEvent` | `CompletedTurns`, `EstimatedTotalTurns`, `ActivePanelists` | Turn completion |
| `ErrorEvent` | `Source`, `ErrorMessage`, `Exception` | Error occurred |
| `CostUpdateEvent` | `TokensConsumedThisTurn`, `TotalTokensConsumed`, `EstimatedRemaining` | Cost tracking |

## 4.5 GuardRailPolicy — Complete Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxTurnsPerDiscussion` | `int` | 30 | Max turns before forced convergence |
| `MaxTokensPerTurn` | `int` | 4000 | Max tokens per panelist per turn |
| `MaxTotalTokens` | `int` | 100,000 | Max total tokens for entire discussion |
| `MaxToolCallsPerTurn` | `int` | 5 | Max tool calls per panelist per turn |
| `MaxToolCallsPerDiscussion` | `int` | 50 | Max total tool calls |
| `MaxDiscussionDuration` | `TimeSpan` | 30 min | Wall-clock time limit |
| `MaxSingleTurnDuration` | `TimeSpan` | 3 min | Per-turn timeout |
| `ProhibitedContentPatterns` | `IReadOnlyList<string>` | [] | Regex patterns to block |
| `AllowedDomains` | `IReadOnlyList<string>` | [] | Web crawling whitelist |
| `AllowFileSystemAccess` | `bool` | true | FS access toggle |
| `AllowedFilePaths` | `IReadOnlyList<string>` | [] | FS whitelist |
| `MaxCritiqueRounds` | `int` | 2 | Max critique-refine loops |
| `FromSettings(PanelSettings)` | static method | — | Factory with clamping |

## 4.6 Interfaces — Complete Contracts

### `IPanelOrchestrator`

| Method | Returns | Description |
|--------|---------|-------------|
| `ActiveSessionId` | `PanelSessionId?` | Current session ID |
| `CurrentPhase` | `PanelPhase` | Current FSM phase |
| `Events` | `IObservable<PanelEvent>` | Event stream for UI subscription |
| `StartAsync(prompt, settings, ct)` | `Task<PanelSessionId>` | Begin new discussion |
| `SendUserMessageAsync(message, ct)` | `Task` | Send user message (clarification / follow-up / meta) |
| `ApproveAndStartPanelAsync(ct)` | `Task` | User approves ToD, spawn panelists |
| `PauseAsync()` | `Task` | Pause at next safe boundary |
| `ResumeAsync()` | `Task` | Resume paused discussion |
| `StopAsync()` | `Task` | Stop and preserve partial results |
| `ResetAsync()` | `Task` | Full reset to Idle |

### `IPanelAgent`

| Method | Returns | Description |
|--------|---------|-------------|
| `Id` | `Guid` | Agent unique ID |
| `Name` | `string` | Display name |
| `Role` | `PanelAgentRole` | Structural role |
| `Status` | `PanelAgentStatus` | Current status |
| `ProcessAsync(input, ct)` | `Task<AgentOutput>` | Execute one turn |
| `PauseAsync()` | `Task` | Pause agent |
| `ResumeAsync()` | `Task` | Resume agent |
| `DisposeAsync()` | `ValueTask` | Cleanup |

### `IPanelAgentFactory`

| Method | Returns | Description |
|--------|---------|-------------|
| `CreateHead(settings)` | `HeadAgent` | Create Head agent |
| `CreateModerator(policy, settings)` | `ModeratorAgent` | Create Moderator agent |
| `CreatePanelist(profile, settings)` | `PanelistAgent` | Create Panelist agent |

### `IConvergenceDetector`

| Method | Returns | Description |
|--------|---------|-------------|
| `CheckAsync(messages, turn, sessionId, ct)` | `Task<ConvergenceResult>` | AI-powered convergence check |

### `IKnowledgeBriefService`

| Method | Returns | Description |
|--------|---------|-------------|
| `BuildBriefAsync(messages, synthesis, ct)` | `Task<KnowledgeBrief>` | Compress discussion into brief |

## 4.7 Models — Complete List

### `PanelSettings` (DTO in AppSettings)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `PrimaryModel` | `string` | `""` | Model for Head & Moderator |
| `PanelistModels` | `List<string>` | `[]` | Pool of models for panelists |
| `MaxPanelists` | `int` | 5 | Max panelists per discussion |
| `MaxTurns` | `int` | 30 | Max turns |
| `MaxDurationMinutes` | `int` | 30 | Max duration |
| `MaxTotalTokens` | `int` | 100,000 | Max tokens |
| `MaxToolCalls` | `int` | 50 | Max tool calls |
| `AllowFileSystemAccess` | `bool` | true | FS access |
| `CommentaryMode` | `string` | "Brief" | Detailed/Brief/Off |
| `WorkingDirectory` | `string` | `""` | Working dir for tools |
| `EnabledMcpServers` | `List<string>` | `[]` | MCP servers for panelist tools |
| `ConvergenceThreshold` | `int` | 80 | Score 0-100 to trigger convergence |

### `ModeratorDecision`

| Property | Type | Description |
|----------|------|-------------|
| `NextSpeaker` | `string?` | Which panelist speaks next (null = all) |
| `ConvergenceScore` | `int` | 0-100 convergence score |
| `StopDiscussion` | `bool` | Force stop |
| `Reason` | `string?` | Decision explanation |
| `RedirectMessage` | `string?` | Message to refocus drifting panelist |
| `Fallback(reason)` | static method | Default on parse failure |

### `ModerationResult`

| Property | Type | Description |
|----------|------|-------------|
| `Action` | `ModerationAction` | Approved/Blocked/Redirect/ForceConverge/ConvergenceDetected |
| `Reason` | `string?` | Explanation |
| Static factories: `Approved()`, `Blocked(reason)`, `Redirect(reason)`, `ForceConverge(reason)`, `ConvergenceDetected()` |

### `KnowledgeBrief`

| Property | Type | Description |
|----------|------|-------------|
| `SessionId` | `PanelSessionId` | Owning session |
| `CompressedSummary` | `string` | ~2K token compressed summary |
| `KeyFindings` | `IReadOnlyList<string>` | Bullet points |
| `Disagreements` | `IReadOnlyList<string>` | Points of contention |
| `ActionItems` | `IReadOnlyList<string>` | Next steps |
| `TotalTurns` | `int` | How many turns |
| `TotalPanelists` | `int` | How many panelists |
| `CreatedAt` | `DateTimeOffset` | When brief was built |

### `CostEstimate`

| Property | Type | Description |
|----------|------|-------------|
| `EstimatedPanelistCount` | `int` | Number of panelists |
| `EstimatedTurns` | `int` | Expected turns |
| `EstimatedTotalTokens` | `int` | Expected token consumption |
| `EstimatedDuration` | `TimeSpan` | Expected wall-clock time |
| `Summary` | `string` | Human-readable summary |
| `Calculate(panelistCount, maxTurns, avgTokensPerTurn)` | static method | Factory |

### `PanelistProfile`

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Display name (e.g., "Security Analyst") |
| `Icon` | `string` | Emoji for UI avatar |
| `AccentColor` | `string` | Hex color for UI |
| `SystemPrompt` | `string` | Persona definition |
| `Model` | `ModelIdentifier?` | Override model (null = default) |
| `AllowedToolCategories` | `IReadOnlyList<string>` | Tool whitelist |
| `Temperature` | `double` | LLM temperature (default 0.7) |

### `ToolCallRecord`

| Property | Type | Description |
|----------|------|-------------|
| `ToolName` | `string` | Tool identifier |
| `Input` | `string` | Arguments |
| `Output` | `string?` | Result |
| `Succeeded` | `bool` | Success flag |
| `Duration` | `TimeSpan` | Execution time |

### `AgentInput` / `AgentOutput`

| `AgentInput` | Type | Description |
|-------------|------|-------------|
| `SessionId` | `PanelSessionId` | Session context |
| `ConversationHistory` | `IReadOnlyList<PanelMessage>` | Messages so far |
| `SystemPrompt` | `string` | Agent's system prompt |
| `CurrentTurn` | `TurnNumber` | Turn number |
| `ToolOutputs` | `IReadOnlyList<string>?` | Prior tool results |

| `AgentOutput` | Type | Description |
|-------------|------|-------------|
| `Message` | `PanelMessage` | Agent's contribution |
| `ToolCalls` | `IReadOnlyList<ToolCallRecord>?` | Tools invoked |
| `RequestsMoreTurns` | `bool` | Agent wants to continue |
| `InternalReasoning` | `string?` | For commentary display |

### `ConvergenceResult`

| Property | Type | Description |
|----------|------|-------------|
| `Score` | `int` | 0-100 |
| `IsConverged` | `bool` | Whether convergence threshold met |
| `Reason` | `string?` | Explanation |
| `Status` | `ConvergenceCheckStatus` | Completed/TooEarly/Skipped/ParseError/Error |

## 4.8 Default Panelist Profiles (8 Profiles)

| Profile Key | Name | Icon | Color | Expertise |
|-------------|------|------|-------|-----------|
| `SecurityAnalyst` | Security Analyst | 🛡️ | #EF5350 | STRIDE, OWASP, CWE/CVE |
| `PerformanceExpert` | Performance Expert | ⚡ | #FFB74D | Big-O, GC, cache, profiling |
| `Architect` | Architect | 🏗️ | #4FC3F7 | System design, API contracts |
| `QAEngineer` | QA Engineer | 🧪 | #81C784 | Test strategy, edge cases |
| `CodeReviewer` | Code Reviewer | 🔍 | #CE93D8 | Quality, patterns, SOLID |
| `DatabaseExpert` | Database Expert | 🗄️ | #A1887F | Schema, query optimization |
| `Researcher` | Researcher | 📚 | #FFF176 | Tech evaluation, analysis |
| `EdgeCaseHunter` | Edge Case Hunter | 🎯 | #FF8A65 | Boundary, fuzzing, FMEA |

## 4.9 Resilience Components

### `ToolCircuitBreaker`

| State | Description | Transition |
|-------|-------------|-----------|
| Closed | Normal operation | failure count < threshold → stays Closed |
| Open | Reject all calls | failure count >= 5 → enters Open; cooldown 60s |
| HalfOpen | Allow one probe | success → Closed; failure → Open |

- Per-tool instances (each tool has its own breaker)
- Thread-safe via `SemaphoreSlim`
- `ExecuteAsync<T>(action, ct)` — wrapper method

### `SandboxedToolExecutor`

1. Check circuit breaker → reject if open
2. Check `IToolApprovalService.IsApproved()` → queue approval if not
3. Execute with timeout (`MaxSingleTurnDuration`)
4. Cap output at 50KB
5. Return `ToolCallRecord` with timing

### `PanelRetryPolicy`

- Max retries: 3
- Exponential backoff with ±25% jitter
- Max delay cap: 60 seconds

---

# 5. Technical Design, UI & Code Flow

## 5.1 UI Layout — Three-Pane Design

```
┌────────────────────────────────────────────────────────────────────────┐
│  [≡] CopilotAgent  │ Chat │ Teams │ Office │ [Panel Discussion]  [⚙] │
├────────────────────┬──────────────────────────────────┬────────────────┤
│  PANE 1            │  PANE 2                          │  PANE 3        │
│  User ↔ Head       │  Panel Visualizer                │  Agent         │
│  Conversation      │  (Live Canvas)                   │  Inspector     │
│  ──────────────    │  ──────────────                  │  ──────────    │
│  [Chat messages]   │  [Agent avatars with status]     │  [Selected     │
│                    │  [Connection lines/flow]          │   agent detail]│
│                    │  [Convergence bar: 45%]           │  [Reasoning]   │
│                    │  [Turn 5/30]                      │  [Tools used]  │
│  ──────────────    ├──────────────────────────────────┤  [Status]      │
│  [▶ Play]          │  Discussion Stream               │  [Commentary]  │
│  [⏸ Pause]         │  [Scrolling message list]        │                │
│  [⏹ Stop]          │  [Agent name + role + content]   │                │
│  [🔄 Reset]        │                                  │                │
│  State: Running    │  [Send message to Head]          │                │
│  Turn: 5/30        │                                  │                │
│  ETA: ~8 min       │                                  │                │
└────────────────────┴──────────────────────────────────┴────────────────┘
```

### Pane 1 — User ↔ Head Chat
- Standard chat interface (similar to existing ChatView)
- Shows: user messages, Head clarification questions, Head synthesis, follow-up Q&A
- Control buttons: Play, Pause, Stop, Reset
- Status display: current phase, turn counter, ETA

### Pane 2 — Panel Visualizer + Discussion Stream
- **Top**: Visual agent avatars with real-time status indicators
- **Middle**: Convergence progress bar (0-100%, color-coded)
- **Bottom**: Scrollable discussion stream showing all panelist messages
- Input field to send messages to Head during execution (meta-questions)

### Pane 3 — Agent Inspector (collapsible side panel)
- Click an agent avatar → shows: reasoning trace, tools used, status, turn count
- Commentary expander with mode filtering (Detailed/Brief/Off)
- Settings panel (fly-in from right, same pattern as AgentTeamView)

## 5.2 Agent Avatar Visual States

| Status | Visual | Animation |
|--------|--------|-----------|
| Created | Semi-transparent, accent border | Fade in |
| Active | Full opacity, accent border | None |
| Thinking | Full opacity, spinning ring | Indeterminate progress |
| Idle | 80% opacity | None |
| Paused | Amber overlay | Pulse |
| Disposed | Faded out | Fade out + remove |

## 5.3 Convergence Progress Bar

- 0–40%: Red `#F44336` (early discussion)
- 40–70%: Amber `#FFC107` (progressing)
- 70–80%: Yellow `#FFEB3B` (approaching threshold)
- 80%+: Green `#4CAF50` (convergence reached)

## 5.4 Settings Side Panel

Follows exact same pattern as `AgentTeamView.xaml` side panel:
- Fly-in/fly-out animation (300ms in, 250ms out, `CubicEase`)
- Snapshot-based dirty tracking
- Apply/Discard bar when `HasPendingChanges = true`
- "Settings require restart" badge when applied during active session

### Settings Properties in Side Panel

| Setting | Control | Binding |
|---------|---------|---------|
| Primary Model | ComboBox | `SelectedPrimaryModel` |
| Panelist Models | Multi-select list | `SelectedPanelistModels` |
| Max Panelists | Slider (2-8) | `SettingsMaxPanelists` |
| Max Turns | Slider (5-100) | `SettingsMaxTurns` |
| Max Duration | Slider (5-120 min) | `SettingsMaxDurationMinutes` |
| Max Total Tokens | Slider (10K-500K) | `SettingsMaxTotalTokens` |
| Max Tool Calls | Slider (10-200) | `SettingsMaxToolCalls` |
| Allow File System | Toggle | `SettingsAllowFileSystem` |
| Commentary Mode | ComboBox (Detailed/Brief/Off) | `SettingsCommentaryMode` |
| Working Directory | TextBox + Browse | `SettingsWorkingDirectory` |
| Convergence Threshold | Slider (50-100) | `SettingsConvergenceThreshold` |

## 5.5 PanelViewModel — Complete Property/Command List

### Observable Properties

| Property | Type | Purpose |
|----------|------|---------|
| `CurrentPhaseDisplay` | `string` | Phase name for status bar |
| `CurrentPhaseColor` | `string` | Hex color for phase indicator |
| `IsDiscussionActive` | `bool` | True when Running/Paused/Converging/Synthesizing |
| `CanStartDiscussion` | `bool` | True when Idle or Completed |
| `CanPause` | `bool` | True when Running |
| `CanResume` | `bool` | True when Paused |
| `CanStop` | `bool` | True when any active phase |
| `CanApprove` | `bool` | True when AwaitingApproval |
| `UserInputText` | `string` | User's text input |
| `ConvergenceScore` | `int` | 0-100 |
| `ConvergenceColor` | `string` | Red/Amber/Yellow/Green |
| `CurrentTurn` | `int` | Turn counter |
| `MaxTurns` | `int` | From settings |
| `EstimatedTimeRemaining` | `string` | "~8 min" |
| `ActivePanelistCount` | `int` | Active panelists |
| `SelectedAgent` | `AgentDisplayItem?` | For inspector pane |
| `SessionIndicatorText` | `string` | WAITING/LIVE/IDLE/DISCONNECTED |
| `SessionIndicatorColor` | `string` | Health color |
| `IsSidePanelOpen` | `bool` | Side panel toggle |
| `HasPendingChanges` | `bool` | Dirty tracking |
| `PendingChangesCount` | `int` | Change count |
| `SettingsRequireRestart` | `bool` | Applied during active session |
| `ShowClarification` | `bool` | Clarification panel visible |
| `ShowApproval` | `bool` | Approval panel visible |
| `CostEstimateText` | `string` | Cost estimate display |

### Observable Collections

| Collection | Type | Purpose |
|-----------|------|---------|
| `ChatMessages` | `ObservableCollection<PanelChatItem>` | User ↔ Head chat |
| `DiscussionMessages` | `ObservableCollection<PanelMessageItem>` | Panel discussion stream |
| `Agents` | `ObservableCollection<AgentDisplayItem>` | Agent avatars |
| `Commentary` | `ObservableCollection<CommentaryItem>` | Commentary entries |
| `EventLog` | `ObservableCollection<string>` | Event log (capped at 500) |

### Relay Commands

| Command | When Enabled | Action |
|---------|-------------|--------|
| `SubmitCommand` | CanStartDiscussion or active | Start or send message |
| `ApproveCommand` | CanApprove | Approve ToD, start panel |
| `PauseCommand` | CanPause | Pause discussion |
| `ResumeCommand` | CanResume | Resume discussion |
| `StopCommand` | CanStop | Stop discussion |
| `ResetCommand` | Any state except Idle | Reset to Idle |
| `ToggleSidePanelCommand` | Always | Toggle settings panel |
| `ApplySettingsCommand` | HasPendingChanges | Apply settings |
| `DiscardSettingsCommand` | HasPendingChanges | Discard changes |
| `RestoreDefaultsCommand` | Always | Reset to defaults |
| `SelectAgentCommand` | Agent clicked | Show agent in inspector |
| `ExportDiscussionCommand` | Completed | Export to file |

### Settings Snapshot Record

```csharp
private sealed record PanelSettingsSnapshot(
    string PrimaryModel,
    string PanelistModels,      // Comma-separated for comparison
    int MaxPanelists,
    int MaxTurns,
    int MaxDurationMinutes,
    int MaxTotalTokens,
    int MaxToolCalls,
    bool AllowFileSystemAccess,
    string CommentaryMode,
    string WorkingDirectory,
    int ConvergenceThreshold);
```

## 5.6 Code Flow — Detailed Sequence Diagrams

### Flow 1: Start Discussion

```
User clicks Submit with prompt text
    │
    ▼
PanelViewModel.SubmitCommand
    │ → if Idle/Completed: call _orchestrator.StartAsync(prompt, settings)
    │
    ▼
PanelOrchestrator.StartAsync()
    ├── Create CancellationTokenSource
    ├── Create PanelSession(id, prompt, guardRails)
    ├── Create PanelStateMachine(session, eventStream)
    ├── Create HeadAgent via factory
    ├── Fire(UserSubmitted) → phase = Clarifying
    │   └── Emits PhaseChangedEvent
    ├── Head.ClarifyAsync(prompt)
    │   ├── Sends clarification prompt to LLM
    │   └── Returns questions or "CLEAR: ..."
    │
    ├── If "CLEAR":
    │   ├── Fire(ClarificationsComplete) → phase = AwaitingApproval
    │   ├── Head.BuildTopicOfDiscussionAsync()
    │   │   └── Returns Topic of Discussion
    │   └── session.SetRefinedTopic(topic)
    │
    └── If questions:
        ├── session.AddMessage(clarification)
        └── Emits AgentMessageEvent (questions shown in UI)
```

### Flow 2: User Answers Clarification

```
User types answer, clicks Submit
    │
    ▼
PanelViewModel.SubmitCommand
    │ → if Clarifying: call _orchestrator.SendUserMessageAsync(text)
    │
    ▼
PanelOrchestrator.SendUserMessageAsync()
    ├── Add user message to session
    ├── Head.ClarifyAsync(message) — processes answer
    │   └── Returns "CLEAR: ..." or more questions
    │
    ├── If "CLEAR":
    │   ├── Fire(ClarificationsComplete) → phase = AwaitingApproval
    │   ├── Head.BuildTopicOfDiscussionAsync()
    │   └── Emit ToD to UI for approval
    │
    └── If more questions:
        └── Add to chat, continue clarification loop
```

### Flow 3: User Approves Topic → Panel Starts

```
User clicks Approve button
    │
    ▼
PanelViewModel.ApproveCommand
    │ → call _orchestrator.ApproveAndStartPanelAsync()
    │
    ▼
PanelOrchestrator.ApproveAndStartPanelAsync()
    ├── Fire(UserApproved) → phase = Preparing
    ├── SelectPanelistProfiles(topic) → list of PanelistProfile
    ├── Factory.CreateModerator(guardRails, settings)
    ├── For each profile:
    │   ├── Factory.CreatePanelist(profile, settings)
    │   ├── Add to _panelists list
    │   └── session.RegisterAgent(new AgentInstance)
    ├── Fire(PanelistsReady) → phase = Running
    └── Task.Run(() => RunDebateLoopAsync(ct))  ← BACKGROUND THREAD
```

### Flow 4: The Debate Loop (Core Engine)

```
RunDebateLoopAsync() — runs on background thread
    │
    ▼ LOOP while phase == Running:
    │
    ├── Check cancellation token
    ├── Check SemaphoreSlim (blocks if paused)
    ├── Check time limit → if exceeded, Fire(Timeout) → break
    │
    ├── MODERATOR.DecideNextTurnAsync(messages, turn)
    │   └── Returns ModeratorDecision (JSON parsed)
    │       ├── If StopDiscussion or ConvergenceScore >= 80:
    │       │   └── Fire(ConvergenceDetected) → break
    │       └── Else: identifies next speaker(s)
    │
    ├── For each selected panelist:
    │   ├── Check cancellation
    │   ├── Create per-turn timeout CTS
    │   ├── PANELIST.ProcessAsync(input, ct)
    │   │   └── Returns AgentOutput (message + tool calls)
    │   ├── MODERATOR.ValidateMessageAsync(output.Message)
    │   │   ├── Approved → add to session, emit event
    │   │   ├── Blocked → skip, log warning
    │   │   └── ForceConverge → Fire(ConvergenceDetected) → break
    │   └── CONVERGENCE_DETECTOR.CheckAsync(messages, turn)
    │       └── If converged → emit event, Fire(ConvergenceDetected) → break
    │
    ├── turn.Increment()
    ├── Check turn limit → if exceeded, Fire(Timeout) → break
    ├── Fire(TurnCompleted) → reentry to Running
    └── Emit ProgressEvent
    │
    ▼ After loop exits (convergence or limit):
    │
    ├── If phase == Converging:
    │   ├── Fire(TurnCompleted) → phase = Synthesizing
    │   ├── HEAD.SynthesizeAsync(panelMessages)
    │   │   ├── Produces final report
    │   │   └── Builds KnowledgeBrief
    │   ├── Add synthesis message to session
    │   └── Fire(SynthesisComplete) → phase = Completed
    │
    ├── Catch OperationCanceledException:
    │   └── Fire(UserStopped) → phase = Stopped
    │
    └── Catch Exception:
        ├── Emit ErrorEvent
        └── Fire(Error) → phase = Failed
```

### Flow 5: Pause / Resume

```
PAUSE:
    PanelViewModel.PauseCommand
        → _orchestrator.PauseAsync()
            ├── _isPaused = true
            ├── _pauseSemaphore.WaitAsync()  ← ACQUIRES (count → 0)
            └── Fire(UserPaused) → phase = Paused
    
    Debate loop: next iteration hits _pauseSemaphore.WaitAsync() → BLOCKS

RESUME:
    PanelViewModel.ResumeCommand
        → _orchestrator.ResumeAsync()
            ├── _isPaused = false
            ├── _pauseSemaphore.Release()  ← RELEASES (count → 1)
            └── Fire(UserResumed) → phase = Running
    
    Debate loop: _pauseSemaphore.WaitAsync() unblocks → continues
```

### Flow 6: Follow-Up Q&A (Post-Completion)

```
User types question after Completed phase
    │
    ▼
PanelViewModel.SubmitCommand
    │ → if Completed: call _orchestrator.SendUserMessageAsync(text)
    │
    ▼
PanelOrchestrator.SendUserMessageAsync()
    ├── Detects phase == Completed
    ├── HEAD.AnswerFollowUpAsync(question)
    │   ├── Uses stored KnowledgeBrief (~2K tokens)
    │   ├── No full conversation replay needed
    │   └── Returns answer
    └── Add answer to chat messages
```

### Flow 7: Meta-Question During Execution

```
User types "How long will this take?" during Running phase
    │
    ▼
PanelOrchestrator.SendUserMessageAsync()
    ├── Detects phase == Running
    ├── HEAD.HandleMetaQuestion(question, currentTurn, maxTurns, panelistCount)
    │   ├── NO LLM call — pure calculation
    │   └── Returns formatted status string
    └── Add to chat (debate loop continues uninterrupted)
```

## 5.7 Event Subscription Flow (ViewModel ↔ Orchestrator)

```csharp
// In PanelViewModel constructor:
_orchestrator.Events
    .OfType<PhaseChangedEvent>()
    .ObserveOn(DispatcherScheduler.Current)        // Thread-safe UI marshalling
    .Subscribe(OnPhaseChanged);

_orchestrator.Events
    .OfType<AgentMessageEvent>()
    .ObserveOn(DispatcherScheduler.Current)
    .Subscribe(OnAgentMessage);

_orchestrator.Events
    .OfType<AgentStatusChangedEvent>()
    .ObserveOn(DispatcherScheduler.Current)
    .Subscribe(OnAgentStatusChanged);

_orchestrator.Events
    .OfType<CommentaryEvent>()
    .Where(e => ShouldShowCommentary(e.MinimumLevel))  // Filter by mode
    .ObserveOn(DispatcherScheduler.Current)
    .Subscribe(OnCommentary);

_orchestrator.Events
    .OfType<ProgressEvent>()
    .ObserveOn(DispatcherScheduler.Current)
    .Subscribe(OnProgress);

_orchestrator.Events
    .OfType<ErrorEvent>()
    .ObserveOn(DispatcherScheduler.Current)
    .Subscribe(OnError);

_orchestrator.Events
    .OfType<ModerationEvent>()
    .ObserveOn(DispatcherScheduler.Current)
    .Subscribe(OnModeration);
```

## 5.8 Thread Model

```
UI Thread (Dispatcher)                    Background Thread (Task.Run)
─────────────────────                     ──────────────────────────
PanelViewModel                            PanelOrchestrator.RunDebateLoopAsync()
├── Property changes                      ├── Moderator.DecideNextTurnAsync()
├── Collection mutations                  ├── Panelist.ProcessAsync()
├── Command handlers                      ├── ConvergenceDetector.CheckAsync()
└── Rx.NET ObserveOn(Dispatcher)          ├── Head.SynthesizeAsync()
    ← receives events from ──────────────┤── Emits PanelEvent via ISubject
                                          └── NEVER touches UI properties directly
```

## 5.9 DI Registration (Exact Code)

```csharp
// In App.xaml.cs ConfigureServices():

// ── Panel Discussion Services ──
services.AddSingleton<ISubject<PanelEvent>>(new Subject<PanelEvent>());
services.AddSingleton<IPanelOrchestrator, PanelOrchestrator>();
services.AddSingleton<IPanelAgentFactory, PanelAgentFactory>();
services.AddSingleton<IConvergenceDetector, ConvergenceDetector>();
services.AddSingleton<IKnowledgeBriefService, KnowledgeBriefService>();
services.AddSingleton<PanelCleanupService>();
services.AddTransient<PanelViewModel>();
```

## 5.10 Memory & Resource Limits

| Resource | Limit | Cleanup |
|----------|-------|---------|
| Panel messages | 1000 per session | Auto-trim oldest 20%, preserve system messages |
| Panelist sessions | N (default 3-5) | Disposed after convergence or stop |
| Head session | 1 per session | Disposed only on Reset |
| Moderator session | 1 per session | Disposed after convergence or stop |
| Event log (UI) | 500 entries | Oldest removed on overflow |
| Knowledge Brief | ~2K tokens | Replaced on new discussion |
| Circuit breakers | Per-tool | Cleared on session Reset |
| Commentary items | 200 entries | Oldest removed on overflow |

## 5.11 Error Recovery Matrix

| Error | Detection | Recovery | User Message |
|-------|-----------|----------|-------------|
| Transient (timeout, rate limit) | Specific exception | Retry 3x with exponential backoff | "Retrying... (2/3)" |
| Session disconnect | `HasActiveSession() == false` | Recreate session, replay prompt | "Reconnecting..." |
| LLM parse failure (bad JSON) | `JsonException` | `ModeratorDecision.Fallback()` | Transparent |
| Panelist timeout | `OperationCanceledException` | Skip panelist turn, continue | "Panelist [name] unavailable" |
| Tool failure | Circuit breaker trips | Disable tool, notify panelists | "Tool [name] unavailable" |
| Fatal error | Unhandled exception | → Failed state, preserve messages | "Error occurred. Click Reset." |
| User cancel | Stop button | Clean shutdown, save messages | "Stopped. Progress saved." |

---

# 6. Plan of Action & Phases

## 6.1 Implementation Phases Overview

| Phase | Duration | Focus | Deliverables |
|-------|----------|-------|-------------|
| **Phase 0** | Week 1-2 | Foundation | Project, domain model, interfaces, settings |
| **Phase 1** | Week 3-4 | State Machine | FSM implementation + unit tests |
| **Phase 2** | Week 5-6 | Agent Framework | All agents + factory + convergence |
| **Phase 3** | Week 7-8 | Orchestration | Debate loop + turn management + pause/resume |
| **Phase 4** | Week 9-10 | Resilience | Circuit breaker + tool sandboxing + cleanup |
| **Phase 5** | Week 11-12 | UI Core | Three-pane layout + ViewModel + settings |
| **Phase 6** | Week 13-14 | UI Polish | Visualization + animations + commentary |
| **Phase 7** | Week 15-16 | Integration & QA | End-to-end testing + regression + docs |

## 6.2 Phase 0: Foundation (Week 1-2)

### Checklist

- [ ] **P0.1** Create `src/CopilotAgent.Panel/CopilotAgent.Panel.csproj`
  - Target: `net8.0-windows`
  - References: `CopilotAgent.Core`
  - NuGet: `Stateless`, `System.Reactive`
- [ ] **P0.2** Add `<ProjectReference>` in `CopilotAgent.App.csproj`
- [ ] **P0.3** Create directory structure (Domain/, StateMachine/, Orchestration/, Agents/, Services/, Resilience/, Models/)
- [ ] **P0.4** Implement all Value Objects:
  - [ ] `Domain/ValueObjects/PanelSessionId.cs` — `readonly record struct`
  - [ ] `Domain/ValueObjects/ModelIdentifier.cs` — `readonly record struct`
  - [ ] `Domain/ValueObjects/TurnNumber.cs` — `readonly record struct` with `Increment()`, `Exceeds()`
  - [ ] `Domain/ValueObjects/TokenBudget.cs` — `readonly record struct` with `IsExceeded()`
- [ ] **P0.5** Implement all Enumerations:
  - [ ] `Domain/Enums/PanelPhase.cs` — 11 values with XML docs
  - [ ] `Domain/Enums/PanelTrigger.cs` — 13 values
  - [ ] `Domain/Enums/PanelAgentRole.cs` — 4 values
  - [ ] `Domain/Enums/PanelAgentStatus.cs` — 6 values
  - [ ] `Domain/Enums/CommentaryMode.cs` — 3 values
  - [ ] `Domain/Enums/PanelMessageType.cs` — 10 values
- [ ] **P0.6** Implement Domain Entities:
  - [ ] `Domain/Entities/PanelSession.cs` — aggregate root with `IAsyncDisposable`
  - [ ] `Domain/Entities/AgentInstance.cs` — lightweight descriptor
  - [ ] `Domain/Entities/PanelMessage.cs` — immutable record with factory method
- [ ] **P0.7** Implement all Domain Events:
  - [ ] `Domain/Events/PanelEvent.cs` — abstract base record
  - [ ] `Domain/Events/PhaseChangedEvent.cs`
  - [ ] `Domain/Events/AgentMessageEvent.cs`
  - [ ] `Domain/Events/AgentStatusChangedEvent.cs`
  - [ ] `Domain/Events/ToolCallEvent.cs`
  - [ ] `Domain/Events/ModerationEvent.cs`
  - [ ] `Domain/Events/CommentaryEvent.cs`
  - [ ] `Domain/Events/ProgressEvent.cs`
  - [ ] `Domain/Events/ErrorEvent.cs`
  - [ ] `Domain/Events/CostUpdateEvent.cs`
- [ ] **P0.8** Implement GuardRailPolicy:
  - [ ] `Domain/Policies/GuardRailPolicy.cs` — all 12 properties + `FromSettings()` with clamping
- [ ] **P0.9** Define all Interfaces:
  - [ ] `Domain/Interfaces/IPanelOrchestrator.cs`
  - [ ] `Domain/Interfaces/IPanelAgent.cs`
  - [ ] `Domain/Interfaces/IPanelAgentFactory.cs`
  - [ ] `Domain/Interfaces/IConvergenceDetector.cs`
  - [ ] `Domain/Interfaces/IKnowledgeBriefService.cs`
- [ ] **P0.10** Implement all Models:
  - [ ] `Models/PanelSettings.cs` — DTO with all 11 properties
  - [ ] `Models/PanelistProfile.cs` — record with 7 properties
  - [ ] `Models/ModeratorDecision.cs` — record with `Fallback()` factory
  - [ ] `Models/ModerationResult.cs` — record with 5 static factories
  - [ ] `Models/KnowledgeBrief.cs` — record with 8 properties
  - [ ] `Models/CostEstimate.cs` — record with `Calculate()` factory
  - [ ] `Models/AgentInput.cs` — record
  - [ ] `Models/AgentOutput.cs` — record
  - [ ] `Models/ConvergenceResult.cs` — record with static factories
  - [ ] `Models/ToolCallRecord.cs` — record
- [ ] **P0.11** Add `PanelSettings` to `CopilotAgent.Core/Models/AppSettings.cs`:
  - [ ] Add `public PanelSettings Panel { get; set; } = new();`
- [ ] **P0.12** Create test project structure:
  - [ ] `tests/CopilotAgent.Tests/Panel/Domain/`
  - [ ] `tests/CopilotAgent.Tests/Panel/StateMachine/`
  - [ ] `tests/CopilotAgent.Tests/Panel/Agents/`
  - [ ] `tests/CopilotAgent.Tests/Panel/Orchestration/`
  - [ ] `tests/CopilotAgent.Tests/Panel/Resilience/`
  - [ ] `tests/CopilotAgent.Tests/Panel/Services/`
- [ ] **P0.13** Write unit tests for Value Objects:
  - [ ] `PanelSessionId` — `New()`, `ToString()`, equality
  - [ ] `ModelIdentifier` — constructor, `ToString()`, equality
  - [ ] `TurnNumber` — `Increment()`, `Exceeds()`, value semantics
  - [ ] `TokenBudget` — `IsExceeded()` with edge cases
- [ ] **P0.14** Write unit tests for GuardRailPolicy:
  - [ ] `FromSettings()` — clamping behavior for all fields
  - [ ] Default values are within expected ranges
  - [ ] Edge values (0, negative, max+1)
- [ ] **P0.15** Verify project builds: `dotnet build CopilotAgent.sln`

### Phase 0 Exit Criteria
- All domain types compile
- All interfaces defined
- Value object and policy unit tests pass
- Project reference chain works: App → Panel → Core

## 6.3 Phase 1: State Machine (Week 3-4)

### Checklist

- [ ] **P1.1** Implement `StateMachine/PanelStateMachine.cs`:
  - [ ] 11 state configurations with `_machine.Configure()`
  - [ ] All valid transitions with `Permit()`
  - [ ] `PermitReentry` for Running/TurnCompleted and Clarifying/TurnCompleted
  - [ ] Global `OnUnhandledTrigger` → log warning, never throw
  - [ ] `PublishPhaseChange()` emitting `PhaseChangedEvent` on every `OnEntry`
  - [ ] `CanFire(trigger)` method
  - [ ] `FireAsync(trigger)` method with logging
  - [ ] `ToDotGraph()` for documentation
- [ ] **P1.2** Unit tests — Valid transitions:
  - [ ] Idle → Clarifying (UserSubmitted)
  - [ ] Clarifying → AwaitingApproval (ClarificationsComplete)
  - [ ] Clarifying → Clarifying reentry (TurnCompleted)
  - [ ] AwaitingApproval → Preparing (UserApproved)
  - [ ] Preparing → Running (PanelistsReady)
  - [ ] Running → Converging (ConvergenceDetected)
  - [ ] Running → Paused (UserPaused)
  - [ ] Running → Running reentry (TurnCompleted)
  - [ ] Running → Converging (Timeout)
  - [ ] Paused → Running (UserResumed)
  - [ ] Converging → Synthesizing (TurnCompleted)
  - [ ] Synthesizing → Completed (SynthesisComplete)
  - [ ] Completed → Clarifying (UserSubmitted) — follow-up
  - [ ] Completed → Idle (Reset)
  - [ ] Stopped → Idle (Reset)
  - [ ] Failed → Idle (Reset)
- [ ] **P1.3** Unit tests — Invalid transitions:
  - [ ] Idle → Running (should fail silently)
  - [ ] Completed → Running (should fail silently)
  - [ ] Paused → Converging (should fail silently)
  - [ ] Failed → Running (should fail silently)
- [ ] **P1.4** Unit tests — UserStopped from all active states:
  - [ ] Clarifying → Stopped
  - [ ] AwaitingApproval → Stopped
  - [ ] Preparing → Stopped
  - [ ] Running → Stopped
  - [ ] Paused → Stopped
  - [ ] Converging → Stopped
  - [ ] Synthesizing → Stopped
- [ ] **P1.5** Unit tests — Error transitions:
  - [ ] Preparing → Failed (Error)
  - [ ] Running → Failed (Error)
  - [ ] Converging → Failed (Error)
  - [ ] Synthesizing → Failed (Error)
- [ ] **P1.6** Unit tests — Event emission:
  - [ ] Every transition emits exactly one PhaseChangedEvent
  - [ ] PhaseChangedEvent contains correct OldPhase and NewPhase
- [ ] **P1.7** Generate DOT graph and save to `docs/panel-state-machine.dot`

### Phase 1 Exit Criteria
- All 11 states configured
- 100% transition coverage in tests
- Invalid transitions handled gracefully (no exceptions)
- DOT graph generated for documentation

## 6.4 Phase 2: Agent Framework (Week 5-6)

### Checklist

- [ ] **P2.1** Implement `Agents/PanelAgentBase.cs`:
  - [ ] `IAsyncDisposable` implementation
  - [ ] Copilot SDK session management (create/reuse/dispose)
  - [ ] `SendToLlmAsync(prompt, ct)` — wrapper around `ICopilotService`
  - [ ] `EmitCommentary(text)` — emit `CommentaryEvent` with `MinimumLevel`
  - [ ] Status tracking (Thinking → Idle transitions)
  - [ ] CancellationToken respect
- [ ] **P2.2** Implement `Agents/HeadAgent.cs`:
  - [ ] `ClarifyAsync(userPrompt, ct)` — generate clarification questions or "CLEAR: ..."
  - [ ] `BuildTopicOfDiscussionAsync(clarificationExchange, ct)` — compose ToD
  - [ ] `SynthesizeAsync(panelMessages, ct)` — produce final report + build KnowledgeBrief
  - [ ] `AnswerFollowUpAsync(question, ct)` — answer using KnowledgeBrief
  - [ ] `HandleMetaQuestion(question, turn, maxTurns, count, phase)` — instant response (no LLM)
  - [ ] Long-lived session (not disposed until Reset)
- [ ] **P2.3** Implement `Agents/ModeratorAgent.cs`:
  - [ ] `DecideNextTurnAsync(messages, turn, ct)` → `ModeratorDecision` (parsed JSON)
  - [ ] `ValidateMessageAsync(message, turn, ct)` → `ModerationResult`
  - [ ] `ForceConvergenceAsync(reason, ct)` — emergency convergence
  - [ ] JSON parsing with fallback to `ModeratorDecision.Fallback()`
  - [ ] GuardRailPolicy enforcement (token count, tool calls, time, content patterns)
- [ ] **P2.4** Implement `Agents/PanelistAgent.cs`:
  - [ ] `ProcessAsync(input, ct)` → `AgentOutput`
  - [ ] Receives system prompt from `PanelistProfile`
  - [ ] Tool access via `SandboxedToolExecutor` (Phase 4, stub for now)
  - [ ] Commentary emission for reasoning
  - [ ] Ephemeral session (created per panelist, disposed after discussion)
- [ ] **P2.5** Implement `Agents/ConvergenceDetector.cs`:
  - [ ] `CheckAsync(messages, turn, sessionId, ct)` → `ConvergenceResult`
  - [ ] Skip check before turn 5
  - [ ] Only check every 3 turns
  - [ ] JSON parsing for `{convergenceScore, converged, reason}`
  - [ ] Fallback on parse failure → `ConvergenceResult.ParseFailed`
- [ ] **P2.6** Implement `Agents/PanelAgentFactory.cs`:
  - [ ] `CreateHead(settings)` → `HeadAgent`
  - [ ] `CreateModerator(policy, settings)` → `ModeratorAgent`
  - [ ] `CreatePanelist(profile, settings)` → `PanelistAgent`
  - [ ] Model assignment logic (profile override vs. settings pool vs. default)
- [ ] **P2.7** Implement `Agents/DefaultPanelistProfiles.cs`:
  - [ ] Static dictionary with 8 profiles (SecurityAnalyst, PerformanceExpert, Architect, QAEngineer, CodeReviewer, DatabaseExpert, Researcher, EdgeCaseHunter)
  - [ ] Each with Name, Icon, AccentColor, SystemPrompt
- [ ] **P2.8** Unit tests:
  - [ ] HeadAgent.ClarifyAsync — returns questions or "CLEAR" (mock ICopilotService)
  - [ ] HeadAgent.HandleMetaQuestion — returns formatted status (no mock needed)
  - [ ] ModeratorDecision parsing — valid JSON, invalid JSON, fallback
  - [ ] ModerationResult — all static factories
  - [ ] ConvergenceDetector — early turn skip, every-3-turns skip, parse valid/invalid JSON
  - [ ] PanelAgentFactory — creates correct types with correct parameters
  - [ ] DefaultPanelistProfiles — all 8 profiles exist, all have required fields

### Phase 2 Exit Criteria
- All 4 agent types implemented
- Factory creates agents with correct configuration
- Convergence detection works with mock LLM
- JSON parsing has fallbacks for all error cases

## 6.5 Phase 3: Orchestration Engine (Week 7-8)

### Checklist

- [ ] **P3.1** Implement `Orchestration/TurnManager.cs`:
  - [ ] `Start()` / `Stop()` — Stopwatch control
  - [ ] `RecordTurn(tokensUsed, toolCallsMade)` — Interlocked counters
  - [ ] `CurrentTurn`, `TotalTokensConsumed`, `TotalToolCalls`, `ElapsedTime` properties
  - [ ] `IsTokenBudgetExceeded(budget)` check
- [ ] **P3.2** Implement `Services/PanelConversationManager.cs`:
  - [ ] `AddAsync(message, ct)` — thread-safe via SemaphoreSlim
  - [ ] Auto-trim at 1000 messages (remove oldest 20%, preserve system messages)
  - [ ] `GetHistory()` — defensive copy
  - [ ] `GetHistory(lastN)` — last N messages
  - [ ] `IDisposable` — release semaphore, clear list
- [ ] **P3.3** Implement `Orchestration/PanelOrchestrator.cs`:
  - [ ] `StartAsync(prompt, settings, ct)` — create session, head, fire UserSubmitted, clarify
  - [ ] `SendUserMessageAsync(message, ct)` — route by phase (Clarifying / Running / Completed)
  - [ ] `ApproveAndStartPanelAsync(ct)` — create moderator + panelists, fire PanelistsReady, launch debate loop
  - [ ] `RunDebateLoopAsync(ct)` — THE CORE LOOP:
    - [ ] Cancellation check
    - [ ] SemaphoreSlim pause check
    - [ ] Time limit check → Fire(Timeout)
    - [ ] Moderator decision
    - [ ] Panelist execution with per-turn timeout
    - [ ] Message moderation (Approved/Blocked/ForceConverge)
    - [ ] Convergence check
    - [ ] Turn increment + limit check
    - [ ] Progress event emission
    - [ ] Synthesis phase after convergence
    - [ ] KnowledgeBrief construction
    - [ ] Exception handling (OperationCanceledException → Stopped; Exception → Failed)
  - [ ] `PauseAsync()` — acquire semaphore, fire UserPaused
  - [ ] `ResumeAsync()` — release semaphore, fire UserResumed
  - [ ] `StopAsync()` — cancel CTS, fire UserStopped, dispose panelists + moderator
  - [ ] `ResetAsync()` — stop + fire Reset, dispose head, null everything
  - [ ] `DisposeAsync()` — cancel, dispose all agents/session/semaphore
- [ ] **P3.4** Implement `Orchestration/AgentSupervisor.cs`:
  - [ ] Manages agent lifecycle (activate, pause, resume, dispose)
  - [ ] Tracks agent status changes → emits `AgentStatusChangedEvent`
- [ ] **P3.5** Integration tests:
  - [ ] Full flow: Start → Clarify → Approve → Run → Converge → Synthesize → Completed
  - [ ] Pause/Resume: Start → Run → Pause → Resume → Complete
  - [ ] Stop mid-discussion: Start → Run → Stop → verify disposal
  - [ ] Reset: Start → Run → Reset → verify clean state
  - [ ] Follow-up Q&A: Complete → ask question → verify KnowledgeBrief used
  - [ ] Timeout: Configure short timeout → verify forced convergence
  - [ ] Turn limit: Configure low max turns → verify forced convergence
- [ ] **P3.6** Unit tests:
  - [ ] TurnManager: RecordTurn increments correctly, IsTokenBudgetExceeded
  - [ ] PanelConversationManager: AddAsync thread-safe, auto-trim, GetHistory(lastN)

### Phase 3 Exit Criteria
- Complete debate loop functional with mock agents
- Pause/resume works at turn boundaries
- All lifecycle flows tested end-to-end
- Resource cleanup verified (no leaks)

## 6.6 Phase 4: Resilience (Week 9-10)

### Checklist

- [ ] **P4.1** Implement `Resilience/ToolCircuitBreaker.cs`:
  - [ ] Three states: Closed, Open, HalfOpen
  - [ ] Per-tool instances
  - [ ] `ExecuteAsync<T>(action, ct)` wrapper
  - [ ] Failure threshold: 5
  - [ ] Cooldown period: 60 seconds
  - [ ] Thread-safe via SemaphoreSlim
  - [ ] `CircuitBreakerOpenException`
- [ ] **P4.2** Implement `Resilience/SandboxedToolExecutor.cs`:
  - [ ] Circuit breaker check
  - [ ] `IToolApprovalService.IsApproved()` check
  - [ ] `IApprovalQueue.EnqueueApprovalAsync()` for unapproved tools
  - [ ] Timeout enforcement (`MaxSingleTurnDuration`)
  - [ ] Output size cap (50KB)
  - [ ] Return `ToolCallRecord` with timing
- [ ] **P4.3** Implement `Resilience/PanelRetryPolicy.cs`:
  - [ ] Exponential backoff: `base * 2^attempt`
  - [ ] Jitter: ±25%
  - [ ] Max delay cap: 60 seconds
  - [ ] `GetDelay(attempt)` method
- [ ] **P4.4** Implement `Services/KnowledgeBriefService.cs`:
  - [ ] `BuildBriefAsync(messages, synthesis, ct)` → `KnowledgeBrief`
  - [ ] Uses LLM to compress full discussion into ~2K tokens
  - [ ] Extracts: key findings, disagreements, action items
- [ ] **P4.5** Implement `Services/CostEstimationService.cs`:
  - [ ] `EstimateAsync(panelistCount, maxTurns)` → `CostEstimate`
  - [ ] Presented to user during AwaitingApproval phase
- [ ] **P4.6** Implement `Services/PanelCleanupService.cs`:
  - [ ] Timer-based (every 5 minutes)
  - [ ] Checks for zombie sessions (Running/Paused > 2x MaxDuration)
  - [ ] `IDisposable` for timer cleanup
- [ ] **P4.7** Unit tests:
  - [ ] ToolCircuitBreaker: Closed → failure → Open → cooldown → HalfOpen → success → Closed
  - [ ] ToolCircuitBreaker: concurrent access thread safety
  - [ ] PanelRetryPolicy: delay calculation, jitter bounds, max cap
  - [ ] CostEstimate.Calculate: various inputs, edge cases
- [ ] **P4.8** Integration tests:
  - [ ] Tool failure → circuit breaker trips → subsequent calls rejected → cooldown → recovery
  - [ ] Tool approval flow → approved → executed; denied → rejected
  - [ ] KnowledgeBrief → build from messages → Head answers follow-up using brief

### Phase 4 Exit Criteria
- Circuit breaker protects against tool failures
- Tool sandboxing enforces all policy limits
- KnowledgeBrief enables cost-efficient follow-up Q&A
- Cost estimation shown before panel approval

## 6.7 Phase 5: UI Core (Week 11-12)

### Checklist

- [ ] **P5.1** Create `src/CopilotAgent.App/Views/PanelView.xaml`:
  - [ ] Three-pane Grid layout (left: 300px, center: *, right: 280px)
  - [ ] Left pane: User ↔ Head chat (ScrollViewer + ItemsControl for messages)
  - [ ] Left pane: Control buttons (Play, Pause, Stop, Reset)
  - [ ] Left pane: Status bar (phase, turn, ETA)
  - [ ] Center top: Agent avatars panel (ItemsControl with DataTemplate)
  - [ ] Center middle: Convergence progress bar
  - [ ] Center bottom: Discussion stream (ScrollViewer + ItemsControl)
  - [ ] Center bottom: Input TextBox for meta-questions
  - [ ] Right pane: Agent inspector (bound to SelectedAgent)
  - [ ] Right pane: Commentary expander
  - [ ] Side panel overlay for Settings (same pattern as AgentTeamView)
  - [ ] Settings toggle button in header
- [ ] **P5.2** Create `src/CopilotAgent.App/Views/PanelView.xaml.cs`:
  - [ ] Side panel slide-in/slide-out storyboard triggers
  - [ ] Auto-scroll on new messages
  - [ ] Keyboard shortcut handlers (Enter to send)
  - [ ] Minimal code-behind (all logic in ViewModel)
- [ ] **P5.3** Create `src/CopilotAgent.App/ViewModels/PanelViewModel.cs`:
  - [ ] All observable properties listed in §5.5
  - [ ] All observable collections listed in §5.5
  - [ ] All relay commands listed in §5.5
  - [ ] Rx.NET subscriptions to orchestrator events (§5.7)
  - [ ] Settings snapshot record
  - [ ] `CaptureCurrentSnapshot()` / `LoadSettingsFromSnapshot()` / `LoadSettingsFromPersistence()`
  - [ ] `RecalculateDirtyState()` — compare all 11 settings fields
  - [ ] `ApplySettingsAsync()` — write to AppSettings, persist, update snapshot
  - [ ] `DiscardSettings()` — revert to persisted snapshot
  - [ ] `RestoreDefaults()` — hardcoded defaults
  - [ ] Session health polling timer (DispatcherTimer, configurable interval)
  - [ ] Pulse timer for LIVE indicator animation
  - [ ] Event log management (cap at 500, reverse-chronological)
  - [ ] `IDisposable` — unsubscribe events, stop timers, dispose Rx subscriptions
- [ ] **P5.4** Implement display item models:
  - [ ] `PanelChatItem` — for user ↔ head chat display
  - [ ] `PanelMessageItem` — for discussion stream display
  - [ ] `AgentDisplayItem` — for agent avatar display (name, icon, color, status)
  - [ ] `CommentaryItem` — for commentary display
- [ ] **P5.5** Add settings side panel XAML:
  - [ ] All 11 settings controls from §5.4 table
  - [ ] Apply/Discard bar (same pattern as AgentTeamView)
  - [ ] Restart required badge
  - [ ] Restore Defaults button
- [ ] **P5.6** Add tab to `MainWindow.xaml`:
  - [ ] `<TabItem Header="Panel Discussion"><local:PanelView x:Name="PanelTab"/></TabItem>`
- [ ] **P5.7** Add lazy init to `MainWindow.xaml.cs`:
  - [ ] `OnPanelTabSelected` → resolve `PanelViewModel` from DI
- [ ] **P5.8** Add DI registration to `App.xaml.cs`:
  - [ ] All Panel services (§5.9 exact code)
- [ ] **P5.9** Verify:
  - [ ] App starts without errors
  - [ ] Panel tab loads
  - [ ] Settings panel opens/closes
  - [ ] No regressions on other tabs

### Phase 5 Exit Criteria
- Three-pane UI renders correctly
- Settings side panel with dirty tracking works
- Tab integration with lazy loading works
- All existing tabs unaffected

## 6.8 Phase 6: UI Polish (Week 13-14)

### Checklist

- [ ] **P6.1** Agent avatar visualization:
  - [ ] DataTemplate for each agent status (Created, Active, Thinking, Idle, Paused, Disposed)
  - [ ] Accent color borders per agent
  - [ ] Spinning ring animation for Thinking state
  - [ ] Fade-in for Created, fade-out for Disposed
  - [ ] Amber pulse for Paused
- [ ] **P6.2** Convergence progress bar:
  - [ ] Color transitions: Red → Amber → Yellow → Green
  - [ ] Percentage text overlay
  - [ ] Smooth value transitions
- [ ] **P6.3** Discussion stream styling:
  - [ ] Agent name + icon + accent color per message
  - [ ] Role badge (Head, Moderator, Panelist)
  - [ ] Timestamp
  - [ ] Tool call indicators (collapsible)
  - [ ] Markdown rendering for message content
- [ ] **P6.4** Commentary filtering:
  - [ ] CommentaryMode selector in settings
  - [ ] Rx.NET `Where()` filter in ViewModel
  - [ ] Visual distinction between Detailed/Brief commentary
- [ ] **P6.5** Side panel slide animation:
  - [ ] SlideInStoryboard: 300ms, CubicEase EaseOut
  - [ ] SlideOutStoryboard: 250ms, CubicEase EaseIn
  - [ ] TranslateTransform animation
- [ ] **P6.6** Phase indicator styling:
  - [ ] Color-coded phase display (Green=Running, Amber=Paused, Red=Failed, etc.)
  - [ ] Phase transition animation
- [ ] **P6.7** Control button states:
  - [ ] Play visible when Idle/Completed
  - [ ] Pause visible when Running
  - [ ] Resume visible when Paused
  - [ ] Stop visible when any active state
  - [ ] Reset always visible (except Idle)
  - [ ] Disabled state styling
- [ ] **P6.8** Cost estimation display:
  - [ ] Show during AwaitingApproval phase
  - [ ] Format: "~N panelists × M turns ≈ XK tokens, ~Y minutes"
- [ ] **P6.9** Approval panel:
  - [ ] Show Topic of Discussion with formatting
  - [ ] Approve / Reject buttons
  - [ ] Cost estimate alongside
- [ ] **P6.10** Auto-scroll behavior:
  - [ ] Discussion stream auto-scrolls to bottom on new messages
  - [ ] Stops auto-scrolling if user scrolls up
  - [ ] Resumes when user scrolls to bottom

### Phase 6 Exit Criteria
- All visual states render correctly
- Animations are smooth (60 FPS)
- Commentary filtering works
- All control buttons have correct enabled/disabled states

## 6.9 Phase 7: Integration & QA (Week 15-16)

### Checklist

- [ ] **P7.1** End-to-end integration tests:
  - [ ] Full discussion flow with real LLM calls
  - [ ] Pause at turn 5 → resume → complete
  - [ ] Stop at turn 3 → verify partial results preserved
  - [ ] Follow-up Q&A after completion
  - [ ] Meta-question during execution
  - [ ] Settings change → Apply → verify restart badge
- [ ] **P7.2** Regression testing:
  - [ ] Chat tab: full conversation works as before
  - [ ] Agent Team tab: full orchestration works as before
  - [ ] Office tab: full iteration loop works as before
  - [ ] Iterative Task tab: works as before
  - [ ] Settings dialog: works as before
  - [ ] MCP config: works as before
  - [ ] Tab switching: no event handler leaks
- [ ] **P7.3** Memory profiling:
  - [ ] Create/destroy 50 panel sessions → flat memory graph
  - [ ] Long session: 2-hour continuous discussion without leak
  - [ ] Pause for 30 minutes → resume works correctly
- [ ] **P7.4** Performance testing:
  - [ ] UI responsiveness: 60 FPS during heavy panelist activity
  - [ ] Discussion stream with 500+ messages renders smoothly
  - [ ] Settings persistence round-trip (save → close → open → load)
- [ ] **P7.5** Error scenario testing:
  - [ ] Network disconnect during discussion → error handling
  - [ ] LLM timeout → retry → recovery
  - [ ] All panelists fail → graceful degradation
  - [ ] Circuit breaker trip → tool disabled → continues
  - [ ] No stack traces shown to users
- [ ] **P7.6** Documentation:
  - [ ] Update `docs/PROJECT_STRUCTURE.md` with Panel project
  - [ ] Create `docs/PANEL_USER_GUIDE.md`
  - [ ] Update `README.md` with Panel feature description
  - [ ] Add Panel to `RELEASE_NOTES.md`
- [ ] **P7.7** Final verification checklist:
  - [ ] All 11 phases with explicit transitions ✅
  - [ ] Stateless FSM with DOT graph export ✅
  - [ ] Guard rails enforced (turns, tokens, time, tools) ✅
  - [ ] Pause/Resume via SemaphoreSlim, safe at turn boundaries ✅
  - [ ] IAsyncDisposable on all agents, sessions, resources ✅
  - [ ] Circuit breaker per-tool with open/half-open/closed ✅
  - [ ] Conversation memory auto-trim at 1000 messages ✅
  - [ ] Knowledge Brief for follow-up Q&A ✅
  - [ ] AI-powered convergence detection with JSON scores ✅
  - [ ] Moderator decisions structured JSON with fallback ✅
  - [ ] IObservable event streaming via Rx.NET ✅
  - [ ] Background execution via Task.Run, immune to UI ✅
  - [ ] Snapshot-based dirty tracking for settings ✅
  - [ ] Cost estimation shown before panel approval ✅
  - [ ] Meta-questions during execution ✅
  - [ ] Commentary mode filtering (Detailed/Brief/Off) ✅
  - [ ] Three-pane layout ✅
  - [ ] Structured logging with [PanelXxx] prefix ✅
  - [ ] Zero regression to existing features ✅
  - [ ] Unit tests >80% coverage ✅

### Phase 7 Exit Criteria
- All integration tests pass
- Zero regressions on existing features
- Memory profile flat after 50 sessions
- 60 FPS UI performance
- Documentation complete

---

## 6.10 Complete File Inventory (Every File to Create/Modify)

### NEW Files (43 files)

| # | File Path | Phase |
|---|-----------|-------|
| 1 | `src/CopilotAgent.Panel/CopilotAgent.Panel.csproj` | P0 |
| 2 | `src/CopilotAgent.Panel/Domain/ValueObjects/PanelSessionId.cs` | P0 |
| 3 | `src/CopilotAgent.Panel/Domain/ValueObjects/ModelIdentifier.cs` | P0 |
| 4 | `src/CopilotAgent.Panel/Domain/ValueObjects/TurnNumber.cs` | P0 |
| 5 | `src/CopilotAgent.Panel/Domain/ValueObjects/TokenBudget.cs` | P0 |
| 6 | `src/CopilotAgent.Panel/Domain/Enums/PanelPhase.cs` | P0 |
| 7 | `src/CopilotAgent.Panel/Domain/Enums/PanelTrigger.cs` | P0 |
| 8 | `src/CopilotAgent.Panel/Domain/Enums/PanelAgentRole.cs` | P0 |
| 9 | `src/CopilotAgent.Panel/Domain/Enums/PanelAgentStatus.cs` | P0 |
| 10 | `src/CopilotAgent.Panel/Domain/Enums/CommentaryMode.cs` | P0 |
| 11 | `src/CopilotAgent.Panel/Domain/Enums/PanelMessageType.cs` | P0 |
| 12 | `src/CopilotAgent.Panel/Domain/Entities/PanelSession.cs` | P0 |
| 13 | `src/CopilotAgent.Panel/Domain/Entities/AgentInstance.cs` | P0 |
| 14 | `src/CopilotAgent.Panel/Domain/Entities/PanelMessage.cs` | P0 |
| 15 | `src/CopilotAgent.Panel/Domain/Events/PanelEvent.cs` | P0 |
| 16 | `src/CopilotAgent.Panel/Domain/Events/PhaseChangedEvent.cs` | P0 |
| 17 | `src/CopilotAgent.Panel/Domain/Events/AgentMessageEvent.cs` | P0 |
| 18 | `src/CopilotAgent.Panel/Domain/Events/AgentStatusChangedEvent.cs` | P0 |
| 19 | `src/CopilotAgent.Panel/Domain/Events/ToolCallEvent.cs` | P0 |
| 20 | `src/CopilotAgent.Panel/Domain/Events/ModerationEvent.cs` | P0 |
| 21 | `src/CopilotAgent.Panel/Domain/Events/CommentaryEvent.cs` | P0 |
| 22 | `src/CopilotAgent.Panel/Domain/Events/ProgressEvent.cs` | P0 |
| 23 | `src/CopilotAgent.Panel/Domain/Events/ErrorEvent.cs` | P0 |
| 24 | `src/CopilotAgent.Panel/Domain/Events/CostUpdateEvent.cs` | P0 |
| 25 | `src/CopilotAgent.Panel/Domain/Policies/GuardRailPolicy.cs` | P0 |
| 26 | `src/CopilotAgent.Panel/Domain/Interfaces/IPanelOrchestrator.cs` | P0 |
| 27 | `src/CopilotAgent.Panel/Domain/Interfaces/IPanelAgent.cs` | P0 |
| 28 | `src/CopilotAgent.Panel/Domain/Interfaces/IPanelAgentFactory.cs` | P0 |
| 29 | `src/CopilotAgent.Panel/Domain/Interfaces/IConvergenceDetector.cs` | P0 |
| 30 | `src/CopilotAgent.Panel/Domain/Interfaces/IKnowledgeBriefService.cs` | P0 |
| 31 | `src/CopilotAgent.Panel/Models/PanelSettings.cs` | P0 |
| 32 | `src/CopilotAgent.Panel/Models/PanelistProfile.cs` | P0 |
| 33 | `src/CopilotAgent.Panel/Models/ModeratorDecision.cs` | P0 |
| 34 | `src/CopilotAgent.Panel/Models/ModerationResult.cs` | P0 |
| 35 | `src/CopilotAgent.Panel/Models/KnowledgeBrief.cs` | P0 |
| 36 | `src/CopilotAgent.Panel/Models/CostEstimate.cs` | P0 |
| 37 | `src/CopilotAgent.Panel/Models/AgentInput.cs` | P0 |
| 38 | `src/CopilotAgent.Panel/Models/AgentOutput.cs` | P0 |
| 39 | `src/CopilotAgent.Panel/Models/ConvergenceResult.cs` | P0 |
| 40 | `src/CopilotAgent.Panel/Models/ToolCallRecord.cs` | P0 |
| 41 | `src/CopilotAgent.Panel/StateMachine/PanelStateMachine.cs` | P1 |
| 42 | `src/CopilotAgent.Panel/Agents/PanelAgentBase.cs` | P2 |
| 43 | `src/CopilotAgent.Panel/Agents/HeadAgent.cs` | P2 |
| 44 | `src/CopilotAgent.Panel/Agents/ModeratorAgent.cs` | P2 |
| 45 | `src/CopilotAgent.Panel/Agents/PanelistAgent.cs` | P2 |
| 46 | `src/CopilotAgent.Panel/Agents/PanelAgentFactory.cs` | P2 |
| 47 | `src/CopilotAgent.Panel/Agents/ConvergenceDetector.cs` | P2 |
| 48 | `src/CopilotAgent.Panel/Agents/DefaultPanelistProfiles.cs` | P2 |
| 49 | `src/CopilotAgent.Panel/Orchestration/PanelOrchestrator.cs` | P3 |
| 50 | `src/CopilotAgent.Panel/Orchestration/TurnManager.cs` | P3 |
| 51 | `src/CopilotAgent.Panel/Orchestration/AgentSupervisor.cs` | P3 |
| 52 | `src/CopilotAgent.Panel/Services/PanelConversationManager.cs` | P3 |
| 53 | `src/CopilotAgent.Panel/Resilience/ToolCircuitBreaker.cs` | P4 |
| 54 | `src/CopilotAgent.Panel/Resilience/SandboxedToolExecutor.cs` | P4 |
| 55 | `src/CopilotAgent.Panel/Resilience/PanelRetryPolicy.cs` | P4 |
| 56 | `src/CopilotAgent.Panel/Services/KnowledgeBriefService.cs` | P4 |
| 57 | `src/CopilotAgent.Panel/Services/CostEstimationService.cs` | P4 |
| 58 | `src/CopilotAgent.Panel/Services/PanelCleanupService.cs` | P4 |
| 59 | `src/CopilotAgent.Panel/Services/PanelToolRouter.cs` | P4 |
| 60 | `src/CopilotAgent.App/Views/PanelView.xaml` | P5 |
| 61 | `src/CopilotAgent.App/Views/PanelView.xaml.cs` | P5 |
| 62 | `src/CopilotAgent.App/ViewModels/PanelViewModel.cs` | P5 |
| 63 | `docs/PANEL_USER_GUIDE.md` | P7 |

### MODIFIED Files (5 files)

| # | File Path | Change | Phase |
|---|-----------|--------|-------|
| 1 | `src/CopilotAgent.App/CopilotAgent.App.csproj` | Add ProjectReference to Panel | P0 |
| 2 | `src/CopilotAgent.Core/Models/AppSettings.cs` | Add `PanelSettings Panel` property | P0 |
| 3 | `src/CopilotAgent.App/App.xaml.cs` | Add DI registrations (~10 lines) | P5 |
| 4 | `src/CopilotAgent.App/MainWindow.xaml` | Add Panel tab (~3 lines) | P5 |
| 5 | `src/CopilotAgent.App/MainWindow.xaml.cs` | Add lazy init handler (~5 lines) | P5 |

### TEST Files (~12 files)

| # | File Path | Phase |
|---|-----------|-------|
| 1 | `tests/CopilotAgent.Tests/Panel/Domain/ValueObjectTests.cs` | P0 |
| 2 | `tests/CopilotAgent.Tests/Panel/Domain/GuardRailPolicyTests.cs` | P0 |
| 3 | `tests/CopilotAgent.Tests/Panel/Domain/PanelSessionTests.cs` | P0 |
| 4 | `tests/CopilotAgent.Tests/Panel/StateMachine/PanelStateMachineTests.cs` | P1 |
| 5 | `tests/CopilotAgent.Tests/Panel/Agents/HeadAgentTests.cs` | P2 |
| 6 | `tests/CopilotAgent.Tests/Panel/Agents/ModeratorDecisionParsingTests.cs` | P2 |
| 7 | `tests/CopilotAgent.Tests/Panel/Agents/ConvergenceDetectorTests.cs` | P2 |
| 8 | `tests/CopilotAgent.Tests/Panel/Agents/PanelAgentFactoryTests.cs` | P2 |
| 9 | `tests/CopilotAgent.Tests/Panel/Orchestration/PanelOrchestratorTests.cs` | P3 |
| 10 | `tests/CopilotAgent.Tests/Panel/Orchestration/TurnManagerTests.cs` | P3 |
| 11 | `tests/CopilotAgent.Tests/Panel/Services/PanelConversationManagerTests.cs` | P3 |
| 12 | `tests/CopilotAgent.Tests/Panel/Resilience/ToolCircuitBreakerTests.cs` | P4 |
| 13 | `tests/CopilotAgent.Tests/Panel/Services/CostEstimationTests.cs` | P4 |

**Total: ~63 new files + 5 modified files + ~13 test files = ~81 files**

---

## 6.11 Definition of Done (Per Phase)

Every phase must satisfy ALL of these before moving to the next:

- [ ] All checklist items completed
- [ ] All new code compiles: `dotnet build CopilotAgent.sln`
- [ ] All unit tests pass: `dotnet test`
- [ ] No compiler warnings in new code
- [ ] Structured logging with `[PanelXxx]` prefix on all log messages
- [ ] XML documentation on all public types and methods
- [ ] `IAsyncDisposable` on all resource-owning classes
- [ ] `CancellationToken` respected at every async boundary
- [ ] No breaking changes to existing projects

## 6.12 Global Definition of Done (All Phases Complete)

- [ ] All 11 FSM phases implemented and tested
- [ ] All 4 agent types functional
- [ ] Debate loop runs end-to-end with real LLM
- [ ] Pause/Resume/Stop/Reset all work correctly
- [ ] Follow-up Q&A works via KnowledgeBrief
- [ ] Three-pane UI renders at 60 FPS
- [ ] Settings with dirty tracking, Apply/Discard, Restart badge
- [ ] Circuit breaker protects tool calls
- [ ] Memory flat after 50 session create/destroy cycles
- [ ] Zero regressions on Chat, Agent Team, Office, Iterative Task
- [ ] Documentation updated
- [ ] >80% test coverage on domain, state machine, resilience

---

*End of Panel Discussion Comprehensive Implementation Plan — Version 1.0*

*This document is the canonical implementation checklist.*  
*Every file, every class, every method, every test is enumerated.*  
*Nothing should be missed during implementation.*