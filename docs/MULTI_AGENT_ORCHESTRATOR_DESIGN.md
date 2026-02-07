# Multi-Agent Orchestrator — Comprehensive Design Document

> **Version:** 3.0  
> **Date:** 2025-02-07  
> **Status:** Draft — Awaiting Review  
> **Audience:** Engineering, Architecture Review

---

## Table of Contents

1. [Architecture](#1-architecture)
2. [Use Cases & Examples](#2-use-cases--examples)
3. [High-Level Design (HLD)](#3-high-level-design-hld)
4. [Low-Level Design (LLD)](#4-low-level-design-lld)
5. [Technical Design, UI & Code Flow](#5-technical-design-ui--code-flow)
6. [Plan of Action & Phases](#6-plan-of-action--phases)
7. [Feature: Interactive Planning Phase](#7-feature-interactive-planning-phase)
8. [Feature: Worker Tool & Skill Parity](#8-feature-worker-tool--skill-parity)
9. [Feature: Industry-Leading Modern Chat Interface](#9-feature-industry-leading-modern-chat-interface)
10. [Feature: Role-Specialized Agents](#10-feature-role-specialized-agents)
11. [Feature: Observability & Debugging](#11-feature-observability--debugging)

---

## 1. Architecture

### 1.1 Design Philosophy

The Multi-Agent Orchestrator follows a **Manager–Worker** pattern built on top of the GitHub Copilot SDK for .NET. A single **Orchestrator Agent** (the manager) accepts a high-level task, decomposes it into a dependency-aware plan, and delegates work chunks to **Worker Agents** — each running in its own isolated Copilot SDK session with its own workspace. The orchestrator monitors progress, handles failures with configurable retry policies, consolidates results, and maintains conversational context for follow-up questions.

**Core Tenets:**

| Tenet | Description |
|---|---|
| **Separation of Concerns** | New `CopilotAgent.MultiAgent` project; orchestration logic never leaks into existing single-agent code |
| **Configuration over Convention** | Every behavioral knob (parallelism, retry limits, workspace strategy, model) is config-driven |
| **Reliability First** | Retry with re-prompting, circuit-breaker abort policies, structured error propagation |
| **Observable** | Real-time UI progress per worker, structured logs, event-driven status updates |
| **Extensible** | Strategy pattern for workspace isolation, pluggable decomposition, aggregation strategies |

### 1.2 Solution Structure

```
CopilotAgent.sln
├── src/
│   ├── CopilotAgent.Core/              # Existing: Models, service interfaces, shared services
│   ├── CopilotAgent.MultiAgent/        # NEW: Orchestration engine (no UI dependency)
│   │   ├── CopilotAgent.MultiAgent.csproj
│   │   ├── Models/
│   │   │   ├── MultiAgentConfig.cs
│   │   │   ├── OrchestrationPlan.cs
│   │   │   ├── WorkChunk.cs
│   │   │   ├── WorkChunkDependency.cs
│   │   │   ├── AgentResult.cs
│   │   │   ├── AgentStatus.cs
│   │   │   ├── AgentRole.cs                # NEW V3 (Section 10)
│   │   │   ├── AgentRoleConfig.cs           # NEW V3 (Section 10)
│   │   │   ├── ChunkExecutionContext.cs     # NEW V3
│   │   │   ├── OrchestratorContext.cs
│   │   │   ├── ConsolidatedReport.cs
│   │   │   ├── WorkspaceStrategyType.cs
│   │   │   ├── RetryPolicy.cs
│   │   │   ├── TeamChatMessage.cs
│   │   │   ├── TeamColorScheme.cs
│   │   │   └── LogEntry.cs                  # NEW V3 (Section 11)
│   │   ├── Services/
│   │   │   ├── IOrchestratorService.cs
│   │   │   ├── OrchestratorService.cs
│   │   │   ├── ITaskDecomposer.cs           # UPDATED V3 (JSON schema validation)
│   │   │   ├── LlmTaskDecomposer.cs
│   │   │   ├── IAgentPool.cs
│   │   │   ├── AgentPool.cs
│   │   │   ├── IWorkerAgent.cs
│   │   │   ├── WorkerAgent.cs
│   │   │   ├── IAgentRoleProvider.cs        # NEW V3 (Section 10)
│   │   │   ├── AgentRoleProvider.cs         # NEW V3 (Section 10)
│   │   │   ├── IWorkspaceStrategy.cs
│   │   │   ├── GitWorktreeStrategy.cs
│   │   │   ├── FileLockingStrategy.cs
│   │   │   ├── InMemoryStrategy.cs
│   │   │   ├── IResultAggregator.cs
│   │   │   ├── ResultAggregator.cs
│   │   │   ├── IDependencyScheduler.cs
│   │   │   ├── DependencyScheduler.cs
│   │   │   ├── IApprovalQueue.cs
│   │   │   ├── ApprovalQueue.cs
│   │   │   ├── ITaskLogStore.cs             # NEW V3 (Section 11)
│   │   │   └── JsonTaskLogStore.cs          # NEW V3 (Section 11)
│   │   └── Events/
│   │       ├── OrchestratorEvent.cs
│   │       ├── WorkerProgressEvent.cs
│   │       └── OrchestrationCompletedEvent.cs
│   ├── CopilotAgent.App/              # Existing: WPF UI (new "Agent Team" tab added)
│   │   ├── ViewModels/
│   │   │   ├── AgentTeamViewModel.cs       # NEW (V3 — modern chat)
│   │   │   ├── AgentTeamSettingsViewModel.cs # NEW
│   │   │   └── WorkerPillViewModel.cs      # NEW V3 (ephemeral pills)
│   │   └── Views/
│   │       ├── AgentTeamView.xaml           # NEW (V3 — industry-leading chat)
│   │       ├── AgentTeamView.xaml.cs        # NEW
│   │       ├── AgentTeamSettingsDialog.xaml # NEW
│   │       ├── AgentTeamSettingsDialog.xaml.cs # NEW
│   │       └── TeamMessageTemplateSelector.cs # NEW
│   ├── CopilotAgent.Persistence/      # Existing: JSON persistence
│   └── CopilotAgent.App/              # Existing
├── tests/
│   ├── CopilotAgent.Tests/            # Existing
│   └── CopilotAgent.MultiAgent.Tests/ # NEW: Unit + integration tests for orchestration
│       ├── CopilotAgent.MultiAgent.Tests.csproj
│       ├── OrchestratorServiceTests.cs
│       ├── DependencySchedulerTests.cs
│       ├── AgentPoolTests.cs
│       ├── WorkspaceStrategyTests.cs
│       ├── ResultAggregatorTests.cs
│       └── TaskLogStoreTests.cs        # NEW V3
└── docs/
    └── MULTI_AGENT_ORCHESTRATOR_DESIGN.md  # This document
```

### 1.3 Project Dependencies

```
CopilotAgent.App
  ├── CopilotAgent.Core
  ├── CopilotAgent.MultiAgent
  └── CopilotAgent.Persistence

CopilotAgent.MultiAgent
  └── CopilotAgent.Core  (for ICopilotService, models, shared interfaces)

CopilotAgent.MultiAgent.Tests
  ├── CopilotAgent.MultiAgent
  └── CopilotAgent.Core
```

### 1.4 Key Architectural Decisions

| Decision | Rationale |
|---|---|
| Separate `CopilotAgent.MultiAgent` project | Clean separation of concerns; orchestration logic is independently testable and doesn't pollute single-agent code |
| Orchestrator uses Copilot SDK session for planning | The orchestrator itself is a Copilot session that generates plans via LLM, ensuring plans are context-aware |
| Worker agents are short-lived SDK sessions | Each chunk gets a fresh session with focused context, disposed after completion. Results are returned to orchestrator |
| Strategy pattern for workspace isolation | `IWorkspaceStrategy` allows runtime selection of Git worktree, file locking, or in-memory based on config |
| Dependency-aware scheduling via DAG | `IDependencyScheduler` topologically sorts work chunks, respects dependencies, and schedules ready chunks up to `MaxParallelSessions` |
| Event-driven progress reporting | `OrchestratorEvent` stream consumed by UI via `IObservable<T>` or event handlers — decouples engine from UI |
| Config-driven everything | `MultiAgentConfig` in `appsettings.json` or session-local overrides — no magic numbers |
| Role-specialized agents (V3) | `AgentRole` + `AgentRoleConfig` allow PlannerAgent, SynthesisAgent, and domain-specific workers with tailored system instructions and tool sets |
| Separate chat model (V3) | `TeamChatMessage` is completely independent from existing `ChatMessage` — new industry-standard chat UI |
| Ephemeral worker bar (V3) | Compact pills that auto-appear during execution and auto-hide on completion — not a permanent panel |

---

## 2. Use Cases & Examples

### 2.1 Use Case 1: Debug Memory Leak in Two Areas

**User Prompt:**
> "Debug the memory leak in the image processing pipeline and the cache eviction module. Both are causing OOM in production."

**Orchestrator Behavior:**

1. **Plan Creation** — Orchestrator LLM session analyzes the prompt and generates:
   ```
   Plan: "Debug Memory Leaks — 2 Areas"
   ├── Chunk 1: "Analyze image processing pipeline for memory leaks"
   │   - WorkingDir: worktree-1 (or file-lock scope: src/ImageProcessing/)
   │   - Skills: code_analysis, terminal
   │   - Dependencies: none
   │   - Role: MemoryDiagnostics
   ├── Chunk 2: "Analyze cache eviction module for memory leaks"  
   │   - WorkingDir: worktree-2 (or file-lock scope: src/CacheEviction/)
   │   - Dependencies: none
   │   - Role: MemoryDiagnostics
   └── Chunk 3: "Consolidate findings and propose unified fix"
       - Dependencies: [Chunk 1, Chunk 2]
       - Role: Synthesis
   ```

2. **Execution** — Chunks 1 and 2 run in parallel (independent). Chunk 3 waits for both.

3. **Result** — Orchestrator receives `AgentResult` from each worker, feeds them into the consolidation chunk (or its own session), and produces:
   - **Structured Report**: Per-agent findings with file paths, line numbers, root cause analysis
   - **Conversational Summary**: "I found two memory leaks. In the image pipeline, `BitmapDecoder` instances are not disposed in `ProcessFrame()` at line 142. In the cache module, the `WeakReference` collection grows unbounded because..."

4. **Follow-up**: User asks "Can you fix the image pipeline leak?" → Orchestrator has full context, delegates a new single-worker task with the original analysis as context.

### 2.2 Use Case 2: Multi-Service Feature Implementation

**User Prompt:**
> "Add user preference persistence to the settings module, update the API to expose it, and add UI controls in the settings dialog."

**Orchestrator Plan:**
```
Plan: "User Preference Persistence — Full Stack"
├── Chunk 1: "Add UserPreference model and persistence layer"
│   - Dependencies: none
│   - Role: Implementation
├── Chunk 2: "Add API endpoints for user preferences"  
│   - Dependencies: [Chunk 1]  ← needs the model
│   - Role: Implementation
├── Chunk 3: "Add UI controls in settings dialog"
│   - Dependencies: [Chunk 1]  ← needs the model for binding
│   - Role: Implementation
└── Chunk 4: "Integration test across all layers"
    - Dependencies: [Chunk 1, Chunk 2, Chunk 3]
    - Role: Testing
```

Chunks 2 and 3 run in parallel after Chunk 1 completes. Chunk 4 waits for all.

### 2.3 Use Case 3: Code Review Across Multiple PRs

**User Prompt:**
> "Review these 3 PRs for security issues: #101, #205, #312"

**Orchestrator Plan:**
```
├── Chunk 1: "Security review PR #101" — Dependencies: none — Role: CodeAnalysis
├── Chunk 2: "Security review PR #205" — Dependencies: none — Role: CodeAnalysis
├── Chunk 3: "Security review PR #312" — Dependencies: none — Role: CodeAnalysis
└── Chunk 4: "Consolidated security report"
    - Dependencies: [Chunk 1, Chunk 2, Chunk 3]
    - Role: Synthesis
```

All 3 reviews run in parallel (up to `MaxParallelSessions`). Consolidation produces a unified security report.

### 2.4 Use Case 4: Sequential Refactoring with Dependencies

**User Prompt:**
> "Refactor the logging framework: first extract the interface, then update all 12 consumers, then remove the old implementation."

**Orchestrator Plan:**
```
├── Chunk 1: "Extract ILogger interface from concrete LogManager"
│   - Dependencies: none — Role: Implementation
├── Chunk 2..13: "Update consumer [N] to use ILogger"
│   - Dependencies: [Chunk 1] — Role: Implementation
│   - Parallelism: up to MaxParallelSessions at a time
└── Chunk 14: "Remove old LogManager, verify no references"
    - Dependencies: [Chunk 2..13] — Role: Implementation
```

Stage 1: Chunk 1 alone. Stage 2: Chunks 2-13 in parallel waves. Stage 3: Chunk 14 after all.

---

## 3. High-Level Design (HLD)

### 3.1 Component Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                        CopilotAgent.App (WPF)                       │
│  ┌─────────────┐  ┌──────────────────┐  ┌───────────────────────┐  │
│  │ Agent Tab    │  │ Agent Team Tab   │  │ Settings              │  │
│  │ (existing)   │  │ (NEW — V3 UI)    │  │ (global + local)      │  │
│  │              │  │ ┌──────────────┐ │  │                       │  │
│  │              │  │ │ Ephemeral    │ │  │ MultiAgentConfig:     │  │
│  │              │  │ │ Worker Bar   │ │  │ - MaxParallelSessions │  │
│  │              │  │ ├──────────────┤ │  │ - WorkspaceStrategy   │  │
│  │              │  │ │ Modern Chat  │ │  │ - RetryPolicy         │  │
│  │              │  │ │ Interface    │ │  │ - ModelId             │  │
│  │              │  │ ├──────────────┤ │  │ - AbortThreshold      │  │
│  │              │  │ │ Advanced     │ │  │ - RoleConfigs         │  │
│  │              │  │ │ Input Bar    │ │  └───────────────────────┘  │
│  │              │  │ └──────────────┘ │                              │
│  └─────────────┘  └──────────────────┘                              │
└────────────────────────────┬────────────────────────────────────────┘
                             │ DI / Events
┌────────────────────────────┴────────────────────────────────────────┐
│                    CopilotAgent.MultiAgent                           │
│                                                                     │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                    OrchestratorService                       │   │
│  │  - Accepts task from UI                                     │   │
│  │  - Uses ITaskDecomposer to generate plan via LLM            │   │
│  │  - Uses IDependencyScheduler to order execution             │   │
│  │  - Uses IAgentPool to dispatch workers                      │   │
│  │  - Uses IAgentRoleProvider for role-specific sessions (V3)  │   │
│  │  - Monitors progress, handles retries                       │   │
│  │  - Uses IResultAggregator to consolidate                    │   │
│  │  - Uses ITaskLogStore for observability (V3)                │   │
│  │  - Maintains OrchestratorContext for follow-ups             │   │
│  └──────────┬──────────────┬──────────────┬────────────────────┘   │
│             │              │              │                          │
│  ┌──────────▼──┐  ┌───────▼──────┐  ┌───▼──────────────────┐      │
│  │TaskDecomposer│  │AgentPool     │  │DependencyScheduler   │      │
│  │(LLM-based)  │  │(manages pool)│  │(DAG topo-sort)       │      │
│  │+ JSON Schema│  └───────┬──────┘  └──────────────────────┘      │
│  │  Validation │          │                                         │
│  └─────────────┘ ┌────────▼────────────┐                           │
│                  │    WorkerAgent (N)    │                           │
│                  │  - Own CopilotSession │                           │
│                  │  - Own Workspace      │                           │
│                  │  - AgentRole (V3)     │                           │
│                  │  - ChunkExecCtx (V3)  │                           │
│                  │  - Reports progress   │                           │
│                  │  - Returns AgentResult│                           │
│                  └────────────┬──────────┘                           │
│                               │                                     │
│                  ┌────────────▼────────────┐                        │
│                  │  IWorkspaceStrategy      │                        │
│                  │  ├─ GitWorktreeStrategy  │                        │
│                  │  ├─ FileLockingStrategy  │                        │
│                  │  └─ InMemoryStrategy    │                        │
│                  └─────────────────────────┘                        │
└─────────────────────────────────────────────────────────────────────┘
                             │
┌────────────────────────────┴────────────────────────────────────────┐
│                      CopilotAgent.Core                              │
│  ┌──────────────┐  ┌─────────────┐  ┌────────────────────────┐    │
│  │ICopilotService│  │ISessionMgr  │  │IToolApprovalService    │    │
│  │(SDK wrapper)  │  │             │  │                        │    │
│  └──────────────┘  └─────────────┘  └────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.2 Data Flow — End-to-End

```
User Input (Agent Team Tab)
    │
    ▼
AgentTeamViewModel.SendCommand()
    │
    ▼
IOrchestratorService.ExecuteTaskAsync(taskPrompt, config, cancellationToken)
    │
    ├──► [1] ITaskDecomposer.DecomposeAsync(taskPrompt)
    │         │
    │         ▼
    │    Orchestrator's own CopilotSession sends structured prompt
    │    LLM returns JSON: OrchestrationPlan { WorkChunks[], Dependencies[] }
    │         │
    │         ▼
    │    Validate against JSON schema (V3) → Parse & validate plan
    │
    ├──► [2] IDependencyScheduler.Schedule(plan)
    │         │
    │         ▼
    │    Build DAG, topological sort → execution stages
    │    Stage 0: independent chunks
    │    Stage 1: chunks depending on Stage 0
    │    Stage N: ...
    │
    ├──► [3] For each stage (sequential):
    │         For each chunk in stage (parallel, up to MaxParallelSessions):
    │         │
    │         ├──► IAgentRoleProvider.CreateRoleAgentAsync(chunk.Role, config) (V3)
    │         ├──► IWorkspaceStrategy.PrepareAsync(chunk)
    │         ├──► IAgentPool.DispatchAsync(chunk)
    │         │       │
    │         │       ▼
    │         │    WorkerAgent.ExecuteAsync(chunk)
    │         │       ├── ChunkExecutionContext created (V3)
    │         │       ├── Create role-specialized CopilotSession
    │         │       ├── ITaskLogStore.SaveLogEntryAsync() for each event (V3)
    │         │       ├── Stream events → OrchestratorEvent pipeline
    │         │       ├── Handle tool approvals (IToolApprovalService)
    │         │       ├── Collect response → AgentResult
    │         │       └── Dispose session
    │         │
    │         ├──► IWorkspaceStrategy.CleanupAsync(chunk)
    │         └──► On failure: RetryPolicy evaluation
    │
    ├──► [4] IResultAggregator.AggregateAsync(allResults)
    │         │
    │         ▼
    │    SynthesisAgent (V3) produces ConsolidatedReport
    │
    └──► [5] Return ConsolidatedReport to UI
```

### 3.3 Session Lifecycle

```
Orchestrator Session (Long-Lived)
┌────────────────────────────────────────────────────┐
│  Created once per Agent Team tab                   │
│  Survives across tasks (maintains follow-up ctx)   │
│  Uses CopilotSdkService.GetOrCreateSdkSessionAsync │
│  Model: configurable (default: same as global)     │
│  Role: Planning (V3)                               │
│                                                    │
│  Task 1: Plan → Dispatch → Consolidate             │
│  Task 2: Follow-up (has context of Task 1)         │
│  Task 3: New task (optionally reset context)        │
└────────────────────────────────────────────────────┘

Worker Sessions (Short-Lived)
┌──────────────────────────────────────────┐
│  Created per WorkChunk                   │
│  Role-specialized system instructions(V3)│
│  Fresh context + chunk prompt            │
│  Working dir = isolated workspace        │
│  ChunkExecutionContext tracks state (V3) │
│  Disposed after completion               │
│  Result returned to orchestrator         │
└──────────────────────────────────────────┘
```

---

## 4. Low-Level Design (LLD)

### 4.1 Models

#### 4.1.1 `MultiAgentConfig`

```csharp
namespace CopilotAgent.MultiAgent.Models;

public sealed class MultiAgentConfig
{
    public int MaxParallelSessions { get; set; } = 5;
    public WorkspaceStrategyType WorkspaceStrategy { get; set; } = WorkspaceStrategyType.GitWorktree;
    public RetryPolicy RetryPolicy { get; set; } = new();
    public string? OrchestratorModelId { get; set; }
    public string? WorkerModelId { get; set; }
    public string WorkingDirectory { get; set; } = string.Empty;
    public List<string> EnabledMcpServers { get; set; } = new();
    public List<string> DisabledSkills { get; set; } = new();
    public bool AutoApproveReadOnlyTools { get; set; } = true;
    public TimeSpan WorkerTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public bool MaintainFollowUpContext { get; set; } = true;

    /// <summary>Role-specific configurations (V3). Keyed by AgentRole.</summary>
    public Dictionary<AgentRole, AgentRoleConfig> RoleConfigs { get; set; } = new();
}
```

#### 4.1.2 `WorkspaceStrategyType`

```csharp
namespace CopilotAgent.MultiAgent.Models;

public enum WorkspaceStrategyType
{
    GitWorktree,
    FileLocking,
    InMemory
}
```

#### 4.1.3 `RetryPolicy`

```csharp
namespace CopilotAgent.MultiAgent.Models;

public sealed class RetryPolicy
{
    public int MaxRetriesPerChunk { get; set; } = 2;
    public int AbortFailureThreshold { get; set; } = 3;
    public bool RepromptOnRetry { get; set; } = true;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}
```

#### 4.1.4 `OrchestrationPlan`

```csharp
namespace CopilotAgent.MultiAgent.Models;

public sealed class OrchestrationPlan
{
    public string PlanId { get; set; } = Guid.NewGuid().ToString();
    public string TaskDescription { get; set; } = string.Empty;
    public string PlanSummary { get; set; } = string.Empty;
    public List<WorkChunk> Chunks { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
```

#### 4.1.5 `WorkChunk`

```csharp
namespace CopilotAgent.MultiAgent.Models;

public sealed class WorkChunk
{
    public string ChunkId { get; set; } = Guid.NewGuid().ToString();
    public int SequenceIndex { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public List<string> DependsOnChunkIds { get; set; } = new();
    public string? WorkingScope { get; set; }
    public List<string> RequiredSkills { get; set; } = new();
    public ChunkComplexity Complexity { get; set; } = ChunkComplexity.Medium;

    /// <summary>Role assigned to the worker for this chunk (V3).</summary>
    public AgentRole AssignedRole { get; set; } = AgentRole.Generic;

    // --- Runtime state (set during execution) ---
    public AgentStatus Status { get; set; } = AgentStatus.Pending;
    public int RetryCount { get; set; }
    public AgentResult? Result { get; set; }
    public string? AssignedWorkspace { get; set; }
    public string? AssignedSessionId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public enum ChunkComplexity { Low, Medium, High }
```

#### 4.1.6 `AgentStatus`

```csharp
namespace CopilotAgent.MultiAgent.Models;

public enum AgentStatus
{
    Pending,
    WaitingForDependencies,
    Queued,
    Running,
    Succeeded,
    Failed,
    Retrying,
    Aborted,
    Skipped
}
```

#### 4.1.7 `AgentResult`

```csharp
namespace CopilotAgent.MultiAgent.Models;

public sealed class AgentResult
{
    public string ChunkId { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string Response { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public List<string> FilesModified { get; set; } = new();
    public List<ToolCallRecord> ToolCallsExecuted { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public int TokensUsed { get; set; }
}

public sealed class ToolCallRecord
{
    public string ToolName { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public bool WasApproved { get; set; }
    public string? Result { get; set; }
}
```

#### 4.1.8 `ChunkExecutionContext` (V3)

```csharp
namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Runtime execution context for a single chunk. Separates runtime state from
/// the WorkChunk definition, enabling clean observability and replay.
/// </summary>
public sealed class ChunkExecutionContext
{
    /// <summary>The chunk being executed.</summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>Role assigned to this chunk's worker.</summary>
    public AgentRole AssignedRole { get; set; } = AgentRole.Generic;

    /// <summary>Current execution status.</summary>
    public AgentStatus Status { get; set; } = AgentStatus.Pending;

    /// <summary>The worker session ID (Copilot SDK session).</summary>
    public string? WorkerSessionId { get; set; }

    /// <summary>Streaming log entries captured during execution.</summary>
    public List<string> StreamingLogs { get; set; } = new();

    /// <summary>The final result after execution completes.</summary>
    public AgentResult? Result { get; set; }

    /// <summary>When execution started.</summary>
    public DateTime? StartedAtUtc { get; set; }

    /// <summary>When execution completed (success or failure).</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Number of retry attempts made.</summary>
    public int RetryCount { get; set; }

    /// <summary>The workspace path assigned to this chunk.</summary>
    public string? WorkspacePath { get; set; }

    /// <summary>Tool calls made during this execution.</summary>
    public List<ToolCallRecord> ToolCalls { get; set; } = new();

    /// <summary>Tokens consumed during execution.</summary>
    public int TokensUsed { get; set; }

    /// <summary>Error details if failed.</summary>
    public string? ErrorDetails { get; set; }
}
```

#### 4.1.9 `OrchestratorContext`

```csharp
namespace CopilotAgent.MultiAgent.Models;

public sealed class OrchestratorContext
{
    public string ContextId { get; set; } = Guid.NewGuid().ToString();
    public string OrchestratorSessionId { get; set; } = string.Empty;
    public List<OrchestrationPlan> ExecutedPlans { get; set; } = new();
    public List<ConsolidatedReport> Reports { get; set; } = new();
    public List<ChatMessage> ConversationHistory { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
}
```

#### 4.1.10 `ConsolidatedReport`

```csharp
namespace CopilotAgent.MultiAgent.Models;

public sealed class ConsolidatedReport
{
    public string PlanId { get; set; } = string.Empty;
    public string ConversationalSummary { get; set; } = string.Empty;
    public List<AgentResult> WorkerResults { get; set; } = new();
    public OrchestrationStats Stats { get; set; } = new();
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class OrchestrationStats
{
    public int TotalChunks { get; set; }
    public int SucceededChunks { get; set; }
    public int FailedChunks { get; set; }
    public int RetriedChunks { get; set; }
    public int SkippedChunks { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public int TotalTokensUsed { get; set; }
}
```

### 4.2 Service Interfaces

#### 4.2.1 `IOrchestratorService`

See [Section 7.4](#74-updated-iorchestratorservice-interface) for the full phased API.

#### 4.2.2 `ITaskDecomposer` (Updated V3 — JSON Schema Validation)

```csharp
namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Decomposes a high-level task prompt into an OrchestrationPlan via LLM.
/// V3: Includes JSON schema validation for LLM-generated plans.
/// </summary>
public interface ITaskDecomposer
{
    /// <summary>
    /// Send the task to the orchestrator's LLM session and parse the structured plan.
    /// Validates the returned JSON against a predefined schema before parsing.
    /// </summary>
    Task<OrchestrationPlan> DecomposeAsync(
        string taskPrompt,
        string orchestratorSessionId,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate a raw JSON plan string against the orchestration plan schema.
    /// Returns validation errors if the JSON is malformed or missing required fields.
    /// </summary>
    PlanValidationResult ValidatePlanJson(string rawJson);
}

/// <summary>
/// Result of JSON schema validation for an LLM-generated plan.
/// </summary>
public sealed class PlanValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public string? SanitizedJson { get; set; }
}
```

**JSON Schema for Plan Validation (V3):**

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["planSummary", "chunks"],
  "properties": {
    "planSummary": { "type": "string", "minLength": 1 },
    "chunks": {
      "type": "array",
      "minItems": 1,
      "items": {
        "type": "object",
        "required": ["sequenceIndex", "title", "prompt"],
        "properties": {
          "sequenceIndex": { "type": "integer", "minimum": 0 },
          "title": { "type": "string", "minLength": 1 },
          "prompt": { "type": "string", "minLength": 10 },
          "dependsOnIndexes": {
            "type": "array",
            "items": { "type": "integer", "minimum": 0 }
          },
          "workingScope": { "type": "string" },
          "requiredSkills": {
            "type": "array",
            "items": { "type": "string" }
          },
          "complexity": {
            "type": "string",
            "enum": ["Low", "Medium", "High"]
          },
          "role": {
            "type": "string",
            "enum": ["Generic", "Planning", "CodeAnalysis", "MemoryDiagnostics", "Performance", "Testing", "Implementation", "Synthesis"]
          }
        }
      }
    }
  }
}
```

The `LlmTaskDecomposer` implementation:
1. Sends the structured prompt to the LLM
2. Extracts JSON from the response (handles markdown code fences)
3. Validates against the schema → if invalid, retries with error feedback
4. Parses into `OrchestrationPlan`
5. Assigns `ChunkId` GUIDs and maps `dependsOnIndexes` to `DependsOnChunkIds`

#### 4.2.3 `IDependencyScheduler`

```csharp
namespace CopilotAgent.MultiAgent.Services;

public interface IDependencyScheduler
{
    List<ExecutionStage> BuildSchedule(OrchestrationPlan plan);
    ValidationResult ValidateDependencies(OrchestrationPlan plan);
}

public sealed class ExecutionStage
{
    public int StageIndex { get; set; }
    public List<WorkChunk> Chunks { get; set; } = new();
}

public sealed class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}
```

#### 4.2.4 `IAgentPool`

```csharp
namespace CopilotAgent.MultiAgent.Services;

public interface IAgentPool
{
    Task<AgentResult> DispatchAsync(
        WorkChunk chunk,
        string orchestratorSessionId,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default);

    Task<List<AgentResult>> DispatchBatchAsync(
        List<WorkChunk> chunks,
        string orchestratorSessionId,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default);

    int ActiveWorkerCount { get; }
    event EventHandler<WorkerProgressEvent>? WorkerProgress;
}
```

#### 4.2.5 `IWorkerAgent`

```csharp
namespace CopilotAgent.MultiAgent.Services;

public interface IWorkerAgent : IAsyncDisposable
{
    string WorkerId { get; }
    WorkChunk Chunk { get; }
    AgentStatus Status { get; }
    ChunkExecutionContext ExecutionContext { get; }  // V3

    Task<AgentResult> ExecuteAsync(CancellationToken cancellationToken = default);
    event EventHandler<WorkerProgressEvent>? ProgressUpdated;
}
```

#### 4.2.6 `IWorkspaceStrategy`

```csharp
namespace CopilotAgent.MultiAgent.Services;

public interface IWorkspaceStrategy
{
    WorkspaceStrategyType StrategyType { get; }
    Task<string> PrepareWorkspaceAsync(WorkChunk chunk, string baseWorkingDirectory, CancellationToken cancellationToken = default);
    Task CleanupWorkspaceAsync(string workspacePath, WorkChunk chunk, CancellationToken cancellationToken = default);
    Task MergeResultsAsync(string workspacePath, string baseWorkingDirectory, WorkChunk chunk, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(string workingDirectory);
}
```

#### 4.2.7 `IResultAggregator`

```csharp
namespace CopilotAgent.MultiAgent.Services;

public interface IResultAggregator
{
    Task<ConsolidatedReport> AggregateAsync(
        OrchestrationPlan plan,
        List<AgentResult> results,
        string orchestratorSessionId,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default);
}
```

### 4.3 Events

```csharp
namespace CopilotAgent.MultiAgent.Events;

public class OrchestratorEvent
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public OrchestratorEventType EventType { get; set; }
    public string Message { get; set; } = string.Empty;
}

public enum OrchestratorEventType
{
    PlanCreated,
    StageStarted,
    StageCompleted,
    WorkerStarted,
    WorkerProgress,
    WorkerCompleted,
    WorkerFailed,
    WorkerRetrying,
    AggregationStarted,
    AggregationCompleted,
    TaskCompleted,
    TaskFailed,
    TaskAborted,
    FollowUpSent,
    FollowUpReceived,

    // V3: Commentary events for unified chat stream
    OrchestratorCommentary,
    WorkerCommentary,
    WorkerToolInvocation,
    WorkerToolResult,
    WorkerReasoning,
    InjectionReceived,
    InjectionProcessed,
    PhaseChanged,
    ApprovalRequested,
    ApprovalResolved
}

public sealed class WorkerProgressEvent : OrchestratorEvent
{
    public string ChunkId { get; set; } = string.Empty;
    public string ChunkTitle { get; set; } = string.Empty;
    public AgentStatus WorkerStatus { get; set; }
    public string? CurrentActivity { get; set; }
    public int RetryAttempt { get; set; }
    public double? ProgressPercent { get; set; }
    public int WorkerIndex { get; set; }  // V3: for color mapping
    public AgentRole WorkerRole { get; set; }  // V3
}

public sealed class OrchestrationCompletedEvent : OrchestratorEvent
{
    public ConsolidatedReport? Report { get; set; }
    public bool WasAborted { get; set; }
}
```

### 4.4 Workspace Strategies — Implementation Sketches

#### 4.4.1 `GitWorktreeStrategy`

```
PrepareWorkspaceAsync:
  1. Verify .git exists in baseWorkingDirectory
  2. Generate branch name: multi-agent/{planId}/{chunkId}
  3. Execute: git worktree add -b {branch} {worktreePath} HEAD
  4. Return worktreePath

CleanupWorkspaceAsync:
  1. Execute: git worktree remove {worktreePath} --force
  2. Execute: git branch -D {branch}

MergeResultsAsync:
  1. In baseWorkingDirectory: git merge --no-ff {branch}
  2. Handle conflicts → report to orchestrator
```

#### 4.4.2 `FileLockingStrategy`

```
PrepareWorkspaceAsync:
  1. Use the same baseWorkingDirectory
  2. Acquire named Mutex/Semaphore for chunk.WorkingScope files
  3. Return baseWorkingDirectory

CleanupWorkspaceAsync:
  1. Release Mutex/Semaphore
```

#### 4.4.3 `InMemoryStrategy`

```
PrepareWorkspaceAsync:
  1. Return baseWorkingDirectory (read-only analysis mode)

CleanupWorkspaceAsync / MergeResultsAsync:
  1. No-op
```

### 4.5 Orchestrator Service — Core Algorithm

```csharp
// Pseudocode for OrchestratorService.ExecuteTaskAsync
public async Task<ConsolidatedReport> ExecuteTaskAsync(
    string taskPrompt, MultiAgentConfig config, CancellationToken ct)
{
    _isRunning = true;
    try
    {
        var orchSessionId = await EnsureOrchestratorSessionAsync(config, ct);

        // 1. Decompose with JSON schema validation (V3)
        RaiseEvent(PlanCreated);
        var plan = await _taskDecomposer.DecomposeAsync(taskPrompt, orchSessionId, config, ct);

        // 2. Validate dependencies
        var validation = _scheduler.ValidateDependencies(plan);
        if (!validation.IsValid) throw new InvalidPlanException(validation.Errors);

        // 3. Build execution schedule
        var stages = _scheduler.BuildSchedule(plan);
        _currentPlan = plan;

        // 4. Execute stages
        var allResults = new List<AgentResult>();
        var executionContexts = new Dictionary<string, ChunkExecutionContext>();  // V3
        int totalFailures = 0;

        foreach (var stage in stages)
        {
            RaiseEvent(StageStarted, stage);

            foreach (var chunk in stage.Chunks)
            {
                EnrichChunkWithDependencyResults(chunk, allResults);

                // V3: Create execution context
                var execCtx = new ChunkExecutionContext
                {
                    ChunkId = chunk.ChunkId,
                    AssignedRole = chunk.AssignedRole,
                    Status = AgentStatus.Queued
                };
                executionContexts[chunk.ChunkId] = execCtx;
            }

            var stageResults = await _agentPool.DispatchBatchAsync(
                stage.Chunks, orchSessionId, config, ct);

            allResults.AddRange(stageResults);

            // V3: Log results
            foreach (var result in stageResults)
            {
                await _taskLogStore.SaveLogEntryAsync(plan.PlanId, result.ChunkId,
                    new LogEntry { Level = result.IsSuccess ? LogLevel.Info : LogLevel.Error,
                                   Message = result.IsSuccess ? "Completed" : result.ErrorMessage });
            }

            var failures = stageResults.Count(r => !r.IsSuccess);
            totalFailures += failures;

            if (totalFailures >= config.RetryPolicy.AbortFailureThreshold)
            {
                RaiseEvent(TaskAborted);
                break;
            }

            RaiseEvent(StageCompleted, stage);
        }

        // 5. Aggregate — using SynthesisAgent role (V3)
        RaiseEvent(AggregationStarted);
        var report = await _resultAggregator.AggregateAsync(plan, allResults, orchSessionId, config, ct);

        _context.ExecutedPlans.Add(plan);
        _context.Reports.Add(report);

        RaiseEvent(TaskCompleted, report);
        return report;
    }
    finally
    {
        _isRunning = false;
    }
}
```

### 4.6 Task Decomposition — LLM Prompt Design

The `LlmTaskDecomposer` sends a structured system prompt to the orchestrator's Copilot session:

```
SYSTEM PROMPT (sent to orchestrator session):
---
You are a Task Orchestrator. Given a user's task, produce a JSON plan.

Rules:
1. Break the task into the smallest independent work chunks.
2. Identify dependencies between chunks (chunk B needs chunk A's output).
3. Each chunk must have a clear, self-contained prompt for a worker agent.
4. Include the working scope (files/directories) each chunk should focus on.
5. Estimate complexity: Low, Medium, High.
6. Assign a role to each chunk: Generic, Planning, CodeAnalysis, MemoryDiagnostics, Performance, Testing, Implementation, Synthesis.

Output EXACTLY this JSON format:
{
  "planSummary": "...",
  "chunks": [
    {
      "sequenceIndex": 0,
      "title": "...",
      "prompt": "...",
      "dependsOnIndexes": [],
      "workingScope": "src/SomeDir/",
      "requiredSkills": ["code_analysis", "terminal"],
      "complexity": "Medium",
      "role": "CodeAnalysis"
    }
  ]
}

IMPORTANT:
- Maximize parallelism: only add dependencies where truly needed.
- Each prompt must be self-contained — the worker has no context except what you provide.
- If a chunk depends on another, explain what data it needs from the dependency.
- The last chunk should typically have role "Synthesis" to consolidate findings.
---
USER: {taskPrompt}
```

### 4.7 Result Aggregation — LLM Prompt Design

```
SYSTEM PROMPT (sent to SynthesisAgent session):
---
You are a Result Aggregator. You dispatched {N} worker agents for the following task:
"{originalTaskPrompt}"

Here are their results:

Worker 1 ({chunk1.Title}) [Role: {chunk1.Role}]:
Status: {Success/Failed}
Response: {chunk1.Result.Response}
Files Modified: {chunk1.Result.FilesModified}

Worker 2 ({chunk2.Title}) [Role: {chunk2.Role}]:
...

Produce:
1. A conversational summary for the user (clear, concise, actionable).
2. If any workers failed, explain what wasn't completed and suggest next steps.
3. If there are conflicts between worker outputs, flag them.
---
```

---

## 5. Technical Design, UI & Code Flow

### 5.1 UI Design — "Agent Team" Tab

See [Section 9](#9-feature-industry-leading-modern-chat-interface) for the complete V3 industry-leading chat UI specification.

### 5.2 ViewModel Design

See [Section 9.7](#97-agentteamviewmodel-v3) for the updated V3 ViewModel.

### 5.3 DI Registration

In `App.xaml.cs`, add registrations for the new project:

```csharp
// CopilotAgent.MultiAgent services
services.AddSingleton<IOrchestratorService, OrchestratorService>();
services.AddSingleton<ITaskDecomposer, LlmTaskDecomposer>();
services.AddSingleton<IDependencyScheduler, DependencyScheduler>();
services.AddSingleton<IAgentPool, AgentPool>();
services.AddSingleton<IResultAggregator, ResultAggregator>();
services.AddSingleton<IAgentRoleProvider, AgentRoleProvider>();  // V3
services.AddSingleton<ITaskLogStore, JsonTaskLogStore>();         // V3
services.AddSingleton<IApprovalQueue, ApprovalQueue>();

// Workspace strategies
services.AddSingleton<GitWorktreeStrategy>();
services.AddSingleton<FileLockingStrategy>();
services.AddSingleton<InMemoryStrategy>();
services.AddSingleton<Func<WorkspaceStrategyType, IWorkspaceStrategy>>(sp => type => type switch
{
    WorkspaceStrategyType.GitWorktree => sp.GetRequiredService<GitWorktreeStrategy>(),
    WorkspaceStrategyType.FileLocking => sp.GetRequiredService<FileLockingStrategy>(),
    WorkspaceStrategyType.InMemory => sp.GetRequiredService<InMemoryStrategy>(),
    _ => throw new ArgumentOutOfRangeException(nameof(type))
});

// ViewModels
services.AddTransient<AgentTeamViewModel>();
services.AddTransient<AgentTeamSettingsViewModel>();
```

### 5.4 Error Handling Strategy

| Scenario | Handling |
|---|---|
| Worker SDK session creation fails | Retry with backoff → count as chunk failure |
| Worker times out | Cancel worker, mark as failed, retry per policy |
| Worker returns incoherent response | Retry with modified prompt |
| Git worktree creation fails | Fall back to FileLocking strategy if configured |
| Dependency cycle detected | Fail fast at validation step, report to user |
| Abort threshold reached | Stop dispatching, aggregate partial results |
| JSON schema validation fails (V3) | Retry LLM with error feedback, up to 3 attempts |
| Orchestrator session drops | Re-create session, attempt to restore context |
| SDK rate limit hit | Exponential backoff with configurable max wait |
| Merge conflict (git worktree) | Report to orchestrator LLM for resolution |

### 5.5 Thread Safety

| Component | Threading Model |
|---|---|
| `OrchestratorService` | `SemaphoreSlim(1,1)` for task execution. Thread-safe event dispatch. |
| `AgentPool` | `SemaphoreSlim(maxParallel)`. `ConcurrentDictionary` for active workers. |
| `WorkerAgent` | Each instance runs on its own task. No shared mutable state. |
| `ChunkExecutionContext` (V3) | Per-worker instance, no cross-worker sharing. |
| `ITaskLogStore` (V3) | Thread-safe write operations via `SemaphoreSlim`. |
| `GitWorktreeStrategy` | Per-branch isolation. Mutex for git CLI serialization. |
| `FileLockingStrategy` | Named semaphores per file scope. |
| UI updates | All `Dispatcher.InvokeAsync()` for ObservableCollection mutations. |

---

## 6. Plan of Action & Phases

### Phase 1: Foundation (Week 1-2)

| Task | Description | Files |
|---|---|---|
| 1.1 | Create `CopilotAgent.MultiAgent` project | `CopilotAgent.MultiAgent.csproj` |
| 1.2 | Add project reference from App → MultiAgent | `CopilotAgent.App.csproj` |
| 1.3 | Implement all models (including V3: `AgentRole`, `AgentRoleConfig`, `ChunkExecutionContext`, `LogEntry`) | `Models/*.cs` |
| 1.4 | Implement all event types | `Events/*.cs` |
| 1.5 | Define all service interfaces (including V3: `IAgentRoleProvider`, `ITaskLogStore`) | `Services/I*.cs` |
| 1.6 | Implement `DependencyScheduler` (DAG + topo-sort) | `Services/DependencyScheduler.cs` |
| 1.7 | Unit tests for DependencyScheduler | `Tests/DependencySchedulerTests.cs` |
| 1.8 | Add `MultiAgentConfig` to `AppSettings` | `CopilotAgent.Core/Models/AppSettings.cs` |

### Phase 2: Workspace Strategies (Week 2-3)

| Task | Description | Files |
|---|---|---|
| 2.1 | Implement `IWorkspaceStrategy` interface | `Services/IWorkspaceStrategy.cs` |
| 2.2 | Implement `GitWorktreeStrategy` | `Services/GitWorktreeStrategy.cs` |
| 2.3 | Implement `FileLockingStrategy` | `Services/FileLockingStrategy.cs` |
| 2.4 | Implement `InMemoryStrategy` | `Services/InMemoryStrategy.cs` |
| 2.5 | Strategy factory with DI registration | `App.xaml.cs` DI setup |
| 2.6 | Unit + integration tests for strategies | `Tests/WorkspaceStrategyTests.cs` |

### Phase 3: Worker Agent & Agent Pool (Week 3-4)

| Task | Description | Files |
|---|---|---|
| 3.1 | Implement `WorkerAgent` with `ChunkExecutionContext` (V3) | `Services/WorkerAgent.cs` |
| 3.2 | Implement `AgentPool` with concurrency control | `Services/AgentPool.cs` |
| 3.3 | Integrate with `ICopilotService` for session creation | Worker ↔ CopilotSdkService |
| 3.4 | Implement retry logic with re-prompting | In `AgentPool` |
| 3.5 | Worker progress event streaming | `WorkerAgent` → `WorkerProgressEvent` |
| 3.6 | Unit tests for AgentPool (mock workers) | `Tests/AgentPoolTests.cs` |

### Phase 4: Task Decomposer & Result Aggregator (Week 4-5)

| Task | Description | Files |
|---|---|---|
| 4.1 | Implement `LlmTaskDecomposer` with JSON schema validation (V3) | `Services/LlmTaskDecomposer.cs` |
| 4.2 | Design & test decomposition prompts (with role assignment) | Prompt templates in code |
| 4.3 | JSON parsing with schema validation & retry on invalid (V3) | In `LlmTaskDecomposer` |
| 4.4 | Implement `ResultAggregator` (using SynthesisAgent role, V3) | `Services/ResultAggregator.cs` |
| 4.5 | Design & test aggregation prompts | Prompt templates in code |
| 4.6 | Dependency context injection | In `OrchestratorService` |
| 4.7 | Unit tests with mocked LLM responses | `Tests/` |

### Phase 5: Orchestrator Service + Interactive Planning (Week 5-6)

| Task | Description | Files |
|---|---|---|
| 5.1 | Implement `OrchestratorService` with state machine (Section 7) | `Services/OrchestratorService.cs` |
| 5.2 | Add `OrchestrationPhase` enum, `OrchestratorResponse` model | `Models/` |
| 5.3 | Implement clarification prompt + JSON parsing | In `OrchestratorService` |
| 5.4 | Implement plan review / approval flow | In `OrchestratorService` |
| 5.5 | Implement injection handler | In `OrchestratorService` |
| 5.6 | Follow-up support with context | `SendFollowUpAsync` |
| 5.7 | Cancellation support | `CancelAsync` |
| 5.8 | Unit tests for phase transitions | `Tests/OrchestratorPhaseTests.cs` |

### Phase 5B: Worker Tool & Skill Parity (Week 5-6)

| Task | Description | Files |
|---|---|---|
| 5B.1 | Add `Source` field to `ToolApprovalRequest` | `CopilotAgent.Core/Models/ToolApprovalModels.cs` |
| 5B.2 | Implement `IApprovalQueue` | `Services/ApprovalQueue.cs` |
| 5B.3 | Wire worker `OnPreToolUse` hook to shared `IToolApprovalService` | `Services/WorkerAgent.cs` |
| 5B.4 | Pass MCP/skill config to worker sessions | `Services/WorkerAgent.cs` |
| 5B.5 | Update DI to register `IApprovalQueue` as singleton | `App.xaml.cs` |
| 5B.6 | Test: approval scope propagation across workers | `Tests/WorkerToolApprovalTests.cs` |

### Phase 5C: Role-Specialized Agents (V3) (Week 6)

| Task | Description | Files |
|---|---|---|
| 5C.1 | Implement `AgentRole` enum and `AgentRoleConfig` | `Models/AgentRole.cs`, `Models/AgentRoleConfig.cs` |
| 5C.2 | Implement `IAgentRoleProvider` and `AgentRoleProvider` | `Services/IAgentRoleProvider.cs`, `Services/AgentRoleProvider.cs` |
| 5C.3 | Wire role-based session creation into `WorkerAgent` | `Services/WorkerAgent.cs` |
| 5C.4 | Add role configs to `MultiAgentConfig` | `Models/MultiAgentConfig.cs` |
| 5C.5 | Unit tests for role provider | `Tests/AgentRoleProviderTests.cs` |

### Phase 5D: Observability & Debugging (V3) (Week 6)

| Task | Description | Files |
|---|---|---|
| 5D.1 | Implement `ITaskLogStore` interface and `JsonTaskLogStore` | `Services/ITaskLogStore.cs`, `Services/JsonTaskLogStore.cs` |
| 5D.2 | Add `LogEntry` model | `Models/LogEntry.cs` |
| 5D.3 | Wire logging into `OrchestratorService` and `WorkerAgent` | Service files |
| 5D.4 | Add log export/replay support | `Services/JsonTaskLogStore.cs` |
| 5D.5 | Unit tests for log store | `Tests/TaskLogStoreTests.cs` |

### Phase 6: Industry-Leading Agent Team UI (V3) (Week 7-9)

| Task | Description | Files |
|---|---|---|
| 6.1 | `TeamChatMessage` model | `MultiAgent/Models/TeamChatMessage.cs` |
| 6.2 | `TeamColorScheme` | `MultiAgent/Models/TeamColorScheme.cs` |
| 6.3 | `TeamMessageTemplateSelector` | `App/Views/TeamMessageTemplateSelector.cs` |
| 6.4 | `AgentTeamView.xaml` — Modern chat with ephemeral worker bar, rich Markdown, avatars, animations | `App/Views/AgentTeamView.xaml` |
| 6.5 | 8+ DataTemplates: Chat, PlanReview, WorkerCommentary, ToolApproval, Injection, PhaseTransition, Report, Error, Code | In `AgentTeamView.xaml` Resources |
| 6.6 | `AgentTeamViewModel` (V3) — phase-aware routing, injection, commentary, ephemeral worker bar | `App/ViewModels/AgentTeamViewModel.cs` |
| 6.7 | `WorkerPillViewModel` (V3) — ephemeral status pills | `App/ViewModels/WorkerPillViewModel.cs` |
| 6.8 | Rich Markdown rendering with syntax highlighting | Custom Markdown renderer |
| 6.9 | Code blocks with copy button + language badge | DataTemplate |
| 6.10 | Inline tool approval rendering in chat stream | DataTemplate + ViewModel |
| 6.11 | Plan review interactive card with approve/edit/reject | DataTemplate |
| 6.12 | Virtualized scrolling + event batching for performance | `AgentTeamView.xaml` |
| 6.13 | Search/navigation in chat history | ViewModel + View |
| 6.14 | `AgentTeamSettingsDialog.xaml` — including role configs | `App/Views/AgentTeamSettingsDialog.xaml` |
| 6.15 | Add tab to `MainWindow.xaml` | `App/MainWindow.xaml` |
| 6.16 | DI registration for all new services/VMs | `App/App.xaml.cs` |
| 6.17 | Integration test: end-to-end UI flow with mock orchestrator | `Tests/AgentTeamViewModelTests.cs` |

### Phase 7: Polish & Hardening (Week 9-10)

| Task | Description |
|---|---|
| 7.1 | Structured logging throughout orchestration pipeline |
| 7.2 | Edge case handling (empty plans, single chunk, all fail, etc.) |
| 7.3 | Performance tuning (semaphore sizing, event batching) |
| 7.4 | Settings persistence (global + session-local save/load) |
| 7.5 | Workspace strategy auto-detection |
| 7.6 | Graceful shutdown (cancel all workers on app close) |
| 7.7 | Memory leak review (SDK session disposal, event handler cleanup) |
| 7.8 | Full integration test suite |
| 7.9 | Documentation update (README, RELEASE_NOTES) |

### Phase 8: Advanced Features (Future)

| Feature | Description |
|---|---|
| 8.1 | Dynamic re-planning (orchestrator adjusts plan mid-execution) |
| 8.2 | Worker-to-worker communication |
| 8.3 | Cost tracking (token usage per worker) |
| 8.4 | Plan templates (save/reuse decomposition patterns) |
| 8.5 | Multi-project orchestration |
| 8.6 | Orchestrator learning (feedback loop) |
| 8.7 | Cloud scalability (ASP.NET Core + SignalR) — see Appendix E |

---

## 7. Feature: Interactive Planning Phase

### 7.1 Overview

The orchestrator must NOT auto-execute upon receiving a task prompt. Instead, it follows a **human-in-the-loop planning protocol**: a multi-step conversation where the orchestrator clarifies requirements, generates a plan, presents it for review, and only begins execution after explicit user confirmation.

### 7.2 Orchestrator State Machine

```
                    ┌──────────────┐
                    │   IDLE       │
                    │  (waiting)   │
                    └──────┬───────┘
                           │ User sends task prompt
                           ▼
                    ┌──────────────┐
            ┌──────│  CLARIFYING   │◄────────┐
            │      │  (asking Qs)  │         │
            │      └──────┬───────┘         │
            │             │ LLM has enough   │ User answers
            │             │ context          │ questions
            │             ▼                  │
            │      ┌──────────────┐         │
            │      │  PLANNING    │─────────┘
            │      │  (building)  │  Needs more info
            │      └──────┬───────┘
            │             │ Plan ready
            │             ▼
            │      ┌──────────────┐
            │      │  REVIEWING   │◄────────┐
            │      │  (user sees  │         │
            │      │   the plan)  │         │ User requests
            │      └──────┬───────┘         │ changes
            │             │                  │
            │             ├── User approves ─┘
            │             │   with edits → re-plan
            │             │
            │             │ User approves (✅ Start)
            │             ▼
            │      ┌──────────────┐
            │      │  EXECUTING   │
            │      │  (workers    │◄── Injection received
            │      │   running)   │    (see Section 9)
            │      └──────┬───────┘
            │             │ All workers done
            │             ▼
            │      ┌──────────────┐
            │      │ AGGREGATING  │
            │      │ (consolidate)│
            │      └──────┬───────┘
            │             │ Report ready
            │             ▼
            │      ┌──────────────┐
            └─────►│  COMPLETED   │
                   │  (follow-up  │
                   │   ready)     │
                   └──────────────┘
```

### 7.3 State Enum

```csharp
namespace CopilotAgent.MultiAgent.Models;

public enum OrchestrationPhase
{
    Idle,
    Clarifying,
    Planning,
    AwaitingApproval,
    Executing,
    Aggregating,
    Completed,
    Cancelled
}
```

### 7.4 Updated `IOrchestratorService` Interface

```csharp
namespace CopilotAgent.MultiAgent.Services;

public interface IOrchestratorService
{
    Task<OrchestratorResponse> SubmitTaskAsync(
        string taskPrompt, MultiAgentConfig config, CancellationToken cancellationToken = default);

    Task<OrchestratorResponse> RespondToClarificationAsync(
        string userResponse, CancellationToken cancellationToken = default);

    Task<OrchestratorResponse> ApprovePlanAsync(
        PlanApprovalDecision decision, string? feedback = null, CancellationToken cancellationToken = default);

    Task<OrchestratorResponse> InjectInstructionAsync(
        string instruction, CancellationToken cancellationToken = default);

    Task<OrchestratorResponse> SendFollowUpAsync(
        string followUpPrompt, CancellationToken cancellationToken = default);

    OrchestrationPhase CurrentPhase { get; }
    OrchestrationPlan? CurrentPlan { get; }
    bool IsRunning { get; }

    Task CancelAsync();
    void ResetContext();

    event EventHandler<OrchestratorEvent>? EventReceived;
}

public enum PlanApprovalDecision { Approve, RequestChanges, Reject }
```

### 7.5 `OrchestratorResponse` — Unified Return Type

```csharp
namespace CopilotAgent.MultiAgent.Models;

public sealed class OrchestratorResponse
{
    public OrchestrationPhase Phase { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string>? ClarifyingQuestions { get; set; }
    public OrchestrationPlan? Plan { get; set; }
    public ConsolidatedReport? Report { get; set; }
    public bool RequiresUserInput { get; set; }
}
```

### 7.6 Plan Review UI

When the orchestrator is in `AwaitingApproval` phase, the chat shows an interactive plan card:

```
┌──────────────────────────────────────────────────────────┐
│  📋 EXECUTION PLAN — Review Required                     │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  Summary: Debug memory leaks in 2 modules                │
│  Workers: 3  |  Parallelism: Stage 0 (2∥), Stage 1 (1)  │
│                                                          │
│  ┌─ Stage 0 (Parallel) ─────────────────────────────┐   │
│  │  📦 Chunk 1: Analyze image pipeline memory       │   │
│  │     Scope: src/Imaging/  |  Role: MemoryDiag     │   │
│  │     Tools: code_analysis, terminal               │   │
│  │                                                  │   │
│  │  📦 Chunk 2: Analyze cache eviction memory       │   │
│  │     Scope: src/Cache/  |  Role: MemoryDiag       │   │
│  │     Tools: code_analysis                         │   │
│  └──────────────────────────────────────────────────┘   │
│                                                          │
│  ┌─ Stage 1 (After Stage 0) ────────────────────────┐   │
│  │  📦 Chunk 3: Consolidate findings & propose fix  │   │
│  │     Depends on: Chunk 1, Chunk 2                 │   │
│  │     Role: Synthesis                              │   │
│  └──────────────────────────────────────────────────┘   │
│                                                          │
│  [✅ Approve & Start]  [✏️ Request Changes]  [❌ Reject] │
└──────────────────────────────────────────────────────────┘
```

### 7.7 LLM-Driven Phase Transitions

The orchestrator LLM decides when to transition between phases. The system prompt instructs it:

```
Evaluate the task and decide:
1. If the task is clear enough → respond with: {"action": "plan", "ready": true}
2. If you need clarification → respond with:
{
  "action": "clarify",
  "questions": ["Question 1?", "Question 2?"],
  "reasoning": "Why these questions matter"
}
```

The orchestrator never hardcodes transitions — the LLM evaluates context and produces structured JSON to signal the next phase.

---

## 8. Feature: Worker Tool & Skill Parity

### 8.1 Overview

Every worker session must have **identical tool and skill access** as the orchestrator session. Workers use the **same `IToolApprovalService`** for tool access checks and user approval, ensuring consistent security policy across all sessions.

### 8.2 Design Principles

| Principle | Description |
|---|---|
| **Parity** | Worker sessions inherit orchestrator's full tool/skill/MCP configuration |
| **Shared Approval Service** | Workers use the existing `IToolApprovalService` — same rules, same UI flow |
| **Centralized Approval Queue** | Tool approval requests from any worker are serialized through `IApprovalQueue`, preventing dialog storms |
| **Risk Awareness** | `IToolApprovalService.GetToolRiskLevel()` applies uniformly to worker tool calls |
| **Rule Reuse** | A "Session" scope approval applies to the orchestration context (covers all workers), "Global" applies everywhere |

### 8.3 Worker Session Configuration

```csharp
// Inside WorkerAgent.ExecuteAsync()
var sessionConfig = new SessionConfig
{
    WorkingDirectory = _workspacePath,
    Model = _config.WorkerModelId ?? _config.OrchestratorModelId,
    McpServers = _config.EnabledMcpServers,
    DisabledSkills = _config.DisabledSkills,
    Streaming = true,
    Hooks = new SessionHooks
    {
        OnPreToolUse = async (toolCall) =>
        {
            var request = new ToolApprovalRequest
            {
                SessionId = _orchestratorSessionId,
                ToolName = toolCall.ToolName,
                Arguments = toolCall.Arguments,
                RiskLevel = _toolApprovalService.GetToolRiskLevel(toolCall.ToolName),
                Source = $"Worker [{_chunk.Title}]"
            };

            if (_toolApprovalService.IsApproved(
                _orchestratorSessionId, toolCall.ToolName, toolCall.Arguments))
                return ToolApprovalResult.Approve;

            var response = await _approvalQueue.EnqueueApprovalAsync(request);
            return response.Decision == ApprovalDecision.Approve
                ? ToolApprovalResult.Approve
                : ToolApprovalResult.Deny;
        }
    }
};
```

### 8.4 Centralized Approval Queue

```csharp
namespace CopilotAgent.MultiAgent.Services;

public interface IApprovalQueue
{
    Task<ToolApprovalResponse> EnqueueApprovalAsync(
        ToolApprovalRequest request, CancellationToken cancellationToken = default);
    int PendingCount { get; }
    event EventHandler<int>? PendingCountChanged;
}

public class ApprovalQueue : IApprovalQueue
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IToolApprovalService _approvalService;
    private int _pendingCount;

    public async Task<ToolApprovalResponse> EnqueueApprovalAsync(
        ToolApprovalRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _pendingCount);
        PendingCountChanged?.Invoke(this, _pendingCount);

        await _gate.WaitAsync(ct);
        try
        {
            return await _approvalService.RequestApprovalAsync(request, ct);
        }
        finally
        {
            _gate.Release();
            Interlocked.Decrement(ref _pendingCount);
            PendingCountChanged?.Invoke(this, _pendingCount);
        }
    }
}
```

### 8.5 Approval Scope for Multi-Agent

| Scope | Behavior in Multi-Agent |
|---|---|
| **Once** | Approved for this single tool invocation only |
| **Session** | Approved for all workers in this orchestration (keyed to orchestrator session ID) |
| **Global** | Approved everywhere (all orchestrations, all sessions) |

---

## 9. Feature: Industry-Leading Modern Chat Interface

### 9.1 Overview & Design Philosophy

The Agent Team tab features a **completely separate, industry-leading chat interface** — designed from the ground up to compete with ChatGPT, Claude, and Perplexity UIs. It is NOT a reuse of the existing `ChatView`/`ChatMessage`/`ChatViewModel`. Every aspect — model, ViewModel, View, rendering — is independent and purpose-built for multi-agent orchestration with modern UX standards.

**Design Goals:**

| Goal | Description |
|---|---|
| **Visual Parity with Best-in-Class** | Rich Markdown rendering, syntax-highlighted code blocks, smooth animations, professional typography |
| **Multi-Source Awareness** | Every message is color-coded by source (orchestrator, workers, user, system) with avatars and colored rings |
| **Ephemeral Worker Status** | Compact pills auto-appear during execution, auto-hide after completion — NOT a permanent panel |
| **Interactive Elements** | Plan approval buttons, tool approval inline, injection toggle — all within the chat stream |
| **Performance** | Virtualized scrolling for thousands of messages, throttled event batching, lazy rendering |
| **Accessibility** | Keyboard navigation, screen reader support, high-contrast mode compatibility |

### 9.2 Ephemeral Worker Status Bar

The worker status bar is a **compact, ephemeral horizontal strip** that appears at the top of the chat area ONLY during execution and auto-hides 2 seconds after all workers complete.

#### 9.2.1 Behavior

| State | Visibility |
|---|---|
| `Idle` / `Clarifying` / `Planning` / `AwaitingApproval` | **Hidden** — no worker bar |
| `Executing` — workers active | **Visible** — slides down with animation |
| `Aggregating` — workers done | **Visible** — shows all completed |
| `Completed` — 2 seconds after | **Auto-hide** — slides up with fade-out animation |
| User clicks a pill | Chat scrolls to that worker's latest message |

#### 9.2.2 Worker Pill Layout

Each worker is represented as a compact pill in a horizontal wrap:

```
┌─────────────────────────────────────────────────────────────┐
│  🔵 Image Pipeline ██░░ 65%  │  🟢 Cache Module ✅  │  ⬜ Consolidate ⏳  │
└─────────────────────────────────────────────────────────────┘
```

#### 9.2.3 Worker Pill XAML

```xml
<!-- Single Worker Pill -->
<Border Background="{Binding StatusColor}" CornerRadius="16" Padding="12,6"
        Margin="4,0" Cursor="Hand"
        MouseLeftButtonUp="OnWorkerPillClicked"
        ToolTip="{Binding TooltipText}">
    <Border.Effect>
        <DropShadowEffect ShadowDepth="1" BlurRadius="4" Opacity="0.15"/>
    </Border.Effect>
    <StackPanel Orientation="Horizontal">
        <TextBlock Text="{Binding StatusIcon}" FontSize="14" Margin="0,0,6,0"
                   VerticalAlignment="Center"/>
        <TextBlock Text="{Binding Title}" FontWeight="Medium" FontSize="12"
                   VerticalAlignment="Center" MaxWidth="160"
                   TextTrimming="CharacterEllipsis"/>
        <TextBlock Text="{Binding ProgressText}" Margin="8,0,0,0" Opacity="0.8"
                   FontSize="11" VerticalAlignment="Center"/>
        <ProgressBar Value="{Binding ProgressPercent}" Width="40" Height="4"
                     Margin="8,0,0,0" VerticalAlignment="Center"
                     Visibility="{Binding IsRunning, Converter={StaticResource BoolToVisibility}}"/>
    </StackPanel>
</Border>
```

#### 9.2.4 Worker Bar Container XAML

```xml
<!-- Ephemeral Worker Status Bar -->
<Border x:Name="WorkerBar"
        Background="#1A1A2E"
        CornerRadius="0,0,12,12"
        Padding="12,8"
        Visibility="{Binding IsWorkerBarVisible, Converter={StaticResource BoolToVisibility}}">
    <Border.RenderTransform>
        <TranslateTransform x:Name="WorkerBarTranslate" Y="-50"/>
    </Border.RenderTransform>
    <Border.Triggers>
        <EventTrigger RoutedEvent="Border.Loaded">
            <BeginStoryboard>
                <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="WorkerBarTranslate"
                                     Storyboard.TargetProperty="Y"
                                     From="-50" To="0" Duration="0:0:0.3">
                        <DoubleAnimation.EasingFunction>
                            <CubicEase EasingMode="EaseOut"/>
                        </DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                </Storyboard>
            </BeginStoryboard>
        </EventTrigger>
    </Border.Triggers>

    <ItemsControl ItemsSource="{Binding WorkerPills}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
                <WrapPanel Orientation="Horizontal"/>
            </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
    </ItemsControl>
</Border>
```

#### 9.2.5 `WorkerPillViewModel`

```csharp
namespace CopilotAgent.App.ViewModels;

public partial class WorkerPillViewModel : ViewModelBase
{
    [ObservableProperty] private string _chunkId = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private AgentStatus _status = AgentStatus.Pending;
    [ObservableProperty] private AgentRole _role = AgentRole.Generic;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _statusIcon = "⏳";
    [ObservableProperty] private string _statusColor = "#2A2A3E";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _tooltipText = string.Empty;

    partial void OnStatusChanged(AgentStatus value)
    {
        StatusIcon = value switch
        {
            AgentStatus.Pending => "⏳",
            AgentStatus.WaitingForDependencies => "⏸️",
            AgentStatus.Queued => "📋",
            AgentStatus.Running => "🔄",
            AgentStatus.Succeeded => "✅",
            AgentStatus.Failed => "❌",
            AgentStatus.Retrying => "🔁",
            AgentStatus.Aborted => "🚫",
            AgentStatus.Skipped => "⏭️",
            _ => "❓"
        };

        StatusColor = value switch
        {
            AgentStatus.Running => "#1565C0",   // Blue
            AgentStatus.Succeeded => "#2E7D32", // Green
            AgentStatus.Failed => "#C62828",     // Red
            AgentStatus.Retrying => "#F57F17",   // Amber
            _ => "#2A2A3E"                       // Dark neutral
        };

        IsRunning = value == AgentStatus.Running;
        TooltipText = $"{Title}\nStatus: {value}\nRole: {Role}";
    }
}
```

### 9.3 Advanced Input Bar

The input bar supports multiple modes and features:

```
┌──────────────────────────────────────────────────────────────────┐
│  [💉]  💬 Type your message...                    [📎] [⚙️] [➤] │
│   ↑                                                ↑    ↑    ↑   │
│   Injection toggle                           Attach Settings Send│
│   (gold border when active)                  files               │
└──────────────────────────────────────────────────────────────────┘
```

#### 9.3.1 Input Bar Features

| Feature | Description |
|---|---|
| **Injection Toggle** (💉) | When ON: gold border, placeholder changes to "Inject instruction to orchestrator...", messages routed to `InjectInstructionAsync()` |
| **File Attach** (📎) | Attach files for context — injected into next message prompt |
| **Settings** (⚙️) | Opens `AgentTeamSettingsDialog` |
| **Send** (➤) | Send message (or `Ctrl+Enter`) |
| **Multi-line** | `Shift+Enter` for newline, `Enter` or `Ctrl+Enter` to send |
| **Keyboard Shortcuts** | `Ctrl+I` toggle injection, `Ctrl+/` focus search, `Escape` cancel |
| **Auto-disable** | Input disabled during `Planning` and `Aggregating` phases (orchestrator working) |
| **Phase-aware placeholder** | Changes based on current phase: "Ask a question..." → "Respond to clarification..." → "Add follow-up..." |

#### 9.3.2 Input Bar XAML

```xml
<Border Background="#1E1E2E" CornerRadius="12" Padding="8"
        BorderBrush="{Binding InputBorderBrush}" BorderThickness="1.5">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>

        <!-- Injection Toggle -->
        <ToggleButton Grid.Column="0" IsChecked="{Binding IsInjectionMode}"
                      ToolTip="Toggle injection mode (Ctrl+I)"
                      Style="{StaticResource InjectionToggleStyle}">
            <TextBlock Text="💉" FontSize="16"/>
        </ToggleButton>

        <!-- Input TextBox -->
        <TextBox Grid.Column="1" Text="{Binding UserInput, UpdateSourceTrigger=PropertyChanged}"
                 AcceptsReturn="True" TextWrapping="Wrap" MaxHeight="120"
                 Background="Transparent" BorderThickness="0" Foreground="White"
                 FontSize="14" Margin="8,0"
                 local:PlaceholderBehavior.Placeholder="{Binding InputPlaceholder}"/>

        <!-- File Attach -->
        <Button Grid.Column="2" Command="{Binding AttachFileCommand}"
                ToolTip="Attach files" Style="{StaticResource IconButtonStyle}">
            <TextBlock Text="📎" FontSize="16"/>
        </Button>

        <!-- Settings -->
        <Button Grid.Column="3" Command="{Binding OpenSettingsCommand}"
                ToolTip="Settings" Style="{StaticResource IconButtonStyle}">
            <TextBlock Text="⚙️" FontSize="16"/>
        </Button>

        <!-- Send -->
        <Button Grid.Column="4" Command="{Binding SendMessageCommand}"
                ToolTip="Send (Ctrl+Enter)" Style="{StaticResource SendButtonStyle}">
            <TextBlock Text="➤" FontSize="16"/>
        </Button>
    </Grid>
</Border>
```

### 9.4 Modern Chat Interface — Message Cards

Every message in the chat stream is rendered as a professional message card with avatar, color ring, content, and hover actions.

#### 9.4.1 Message Card XAML

```xml
<!-- Chat Message Card Template -->
<DataTemplate x:Key="ChatMessageTemplate">
    <Border Background="{Binding BackgroundBrush}" CornerRadius="8"
            Padding="16" Margin="8,4"
            x:Name="MessageCard">
        <Border.Triggers>
            <EventTrigger RoutedEvent="Border.Loaded">
                <BeginStoryboard>
                    <Storyboard>
                        <DoubleAnimation Storyboard.TargetProperty="Opacity"
                                         From="0" To="1" Duration="0:0:0.2"/>
                        <DoubleAnimation Storyboard.TargetProperty="(UIElement.RenderTransform).(TranslateTransform.Y)"
                                         From="10" To="0" Duration="0:0:0.25">
                            <DoubleAnimation.EasingFunction>
                                <CubicEase EasingMode="EaseOut"/>
                            </DoubleAnimation.EasingFunction>
                        </DoubleAnimation>
                    </Storyboard>
                </BeginStoryboard>
            </EventTrigger>
        </Border.Triggers>
        <Border.RenderTransform>
            <TranslateTransform/>
        </Border.RenderTransform>

        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <!-- Avatar with Color Ring -->
            <Border Grid.Column="0" CornerRadius="20" Width="40" Height="40"
                    BorderBrush="{Binding ColorBrush}" BorderThickness="2.5"
                    Background="#2A2A3E" VerticalAlignment="Top" Margin="0,2,0,0">
                <TextBlock Text="{Binding Avatar}" FontSize="18"
                           VerticalAlignment="Center" HorizontalAlignment="Center"/>
            </Border>

            <!-- Message Content -->
            <StackPanel Grid.Column="1" Margin="12,0,0,0">
                <!-- Header: Name + Timestamp -->
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <TextBlock Grid.Column="0" Text="{Binding SourceDisplayName}"
                               FontWeight="SemiBold" FontSize="13"
                               Foreground="{Binding ColorBrush}"/>
                    <Border Grid.Column="1" CornerRadius="4" Padding="6,1"
                            Margin="8,0,0,0" Background="#333"
                            Visibility="{Binding RoleBadge, Converter={StaticResource StringToVisibility}}">
                        <TextBlock Text="{Binding RoleBadge}" FontSize="10"
                                   Foreground="#AAA"/>
                    </Border>
                    <TextBlock Grid.Column="3" Text="{Binding TimestampLocal}"
                               HorizontalAlignment="Right" Opacity="0.5"
                               FontSize="11"/>
                </Grid>

                <!-- Rich Content (Markdown rendered) -->
                <ContentPresenter Content="{Binding RenderedContent}" Margin="0,6,0,0"/>

                <!-- Hover Action Buttons -->
                <StackPanel Orientation="Horizontal" Margin="0,6,0,0"
                            Opacity="0" x:Name="ActionButtons">
                    <Button Content="📋" ToolTip="Copy" Command="{Binding CopyCommand}"
                            Style="{StaticResource ActionIconButton}" Margin="0,0,4,0"/>
                    <Button Content="🔄" ToolTip="Regenerate"
                            Style="{StaticResource ActionIconButton}" Margin="0,0,4,0"
                            Visibility="{Binding CanRegenerate, Converter={StaticResource BoolToVisibility}}"/>
                </StackPanel>
            </StackPanel>
        </Grid>
    </Border>

    <DataTemplate.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
            <Setter TargetName="ActionButtons" Property="Opacity" Value="1"/>
        </Trigger>
    </DataTemplate.Triggers>
</DataTemplate>
```

#### 9.4.2 Code Block with Copy Button + Language Badge

```xml
<!-- Code Block Template -->
<DataTemplate x:Key="CodeBlockTemplate">
    <Border Background="#0D1117" CornerRadius="8" Margin="0,8" Padding="0">
        <!-- Header with language badge + copy button -->
        <StackPanel>
            <Border Background="#161B22" CornerRadius="8,8,0,0" Padding="12,6">
                <Grid>
                    <TextBlock Text="{Binding Language}" FontSize="11"
                               Foreground="#8B949E" FontWeight="Medium"
                               HorizontalAlignment="Left" VerticalAlignment="Center"/>
                    <Button Content="📋 Copy" HorizontalAlignment="Right"
                            Command="{Binding CopyCodeCommand}"
                            FontSize="11" Foreground="#8B949E"
                            Style="{StaticResource CodeCopyButton}"/>
                </Grid>
            </Border>

            <!-- Syntax-highlighted code -->
            <RichTextBox IsReadOnly="True" Background="Transparent"
                         BorderThickness="0" Padding="12" FontFamily="Cascadia Code, Consolas"
                         FontSize="13" Foreground="#C9D1D9"
                         Document="{Binding SyntaxHighlightedDocument}"/>
        </StackPanel>
    </Border>
</DataTemplate>
```

### 9.5 Rich Markdown Rendering

The chat interface supports full Markdown rendering comparable to ChatGPT/Claude:

| Feature | Implementation |
|---|---|
| **Headers** (H1-H6) | `TextBlock` with scaled `FontSize` and `FontWeight` |
| **Bold / Italic / Strikethrough** | `Run` elements with appropriate `FontStyle`/`FontWeight`/`TextDecorations` |
| **Inline code** | `Border` with `Background="#2A2A3E"`, `CornerRadius="3"`, `Padding="3,1"` |
| **Code blocks** | Custom `CodeBlockTemplate` with syntax highlighting (see 9.4.2) |
| **Bullet / Numbered lists** | `StackPanel` with bullet `TextBlock` + content |
| **Tables** | WPF `DataGrid` with read-only styling |
| **Links** | `Hyperlink` elements with click handler |
| **Block quotes** | `Border` with left `BorderBrush="#666"`, `BorderThickness="3,0,0,0"` |
| **Horizontal rules** | `Separator` with styling |
| **Images** | `Image` element with lazy loading |
| **Collapsible sections** | `Expander` control for `<details>` blocks and worker reasoning |

### 9.6 `TeamChatMessage` — Separate from Existing `ChatMessage`

```csharp
namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Chat message model for the Agent Team UI.
/// Completely independent from CopilotAgent.Core.Models.ChatMessage.
/// </summary>
public sealed class TeamChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public TeamMessageSource Source { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string SourceDisplayName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public TeamMessageType MessageType { get; set; }
    public OrchestrationPlan? PlanData { get; set; }
    public ToolApprovalRequest? ApprovalRequest { get; set; }
    public bool IsStreaming { get; set; }
    public string ColorKey { get; set; } = string.Empty;
    public string? ChunkId { get; set; }
    public bool IsCollapsible { get; set; }
    public bool IsExpanded { get; set; }

    /// <summary>Avatar emoji for the source.</summary>
    public string Avatar { get; set; } = "🤖";

    /// <summary>Role badge text (e.g., "MemoryDiag", "Synthesis") — V3.</summary>
    public string? RoleBadge { get; set; }

    /// <summary>Thread/chunk group for message threading.</summary>
    public string? ThreadId { get; set; }
}

public enum TeamMessageSource
{
    User,
    Orchestrator,
    Worker,
    System,
    Injection
}

public enum TeamMessageType
{
    Chat,
    ClarificationRequest,
    PlanReview,
    WorkerCommentary,
    WorkerToolEvent,
    WorkerReasoning,
    ToolApproval,
    UserInjection,
    InjectionResponse,
    PhaseTransition,
    Error,
    Report
}
```

### 9.7 Color Coding Scheme

```csharp
namespace CopilotAgent.MultiAgent.Models;

public static class TeamColorScheme
{
    public static readonly Dictionary<string, string> SourceColors = new()
    {
        ["user"]         = "#FFFFFF",   // White
        ["orchestrator"] = "#7B68EE",   // Medium Slate Blue
        ["system"]       = "#808080",   // Gray
        ["injection"]    = "#FFD700",   // Gold
        ["worker-0"]     = "#4FC3F7",   // Light Blue
        ["worker-1"]     = "#81C784",   // Light Green
        ["worker-2"]     = "#FFB74D",   // Orange
        ["worker-3"]     = "#F06292",   // Pink
        ["worker-4"]     = "#BA68C8",   // Purple
        ["worker-5"]     = "#4DD0E1",   // Cyan
        ["worker-6"]     = "#AED581",   // Light Green 2
        ["worker-7"]     = "#FF8A65",   // Deep Orange
    };

    public static string GetWorkerColor(int workerIndex)
    {
        var key = $"worker-{workerIndex % 8}";
        return SourceColors.GetValueOrDefault(key, "#FFFFFF");
    }
}
```

### 9.8 Overall Layout — Agent Team Tab (V3)

```
┌─────────────────────────────────────────────────────────────────────┐
│  [Agent] [Agent Team] [Terminal] [Settings]                         │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌──── EPHEMERAL WORKER BAR (auto-show/hide) ──────────────────┐   │
│  │  🔵 Image Pipeline ██░░ 65%  │  🟢 Cache Module ✅  │ ⬜ ⏳  │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                     │
│  ┌──── SEARCH BAR (Ctrl+/ to focus) ───────────────────────────┐   │
│  │  🔍 Search messages...                    [↑] [↓] [×]       │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                     │
│  ┌──── UNIFIED CHAT STREAM (virtualized) ──────────────────────┐   │
│  │                                                              │   │
│  │  ┌─ 👤 You ─────────────────────── 5:23 PM ─────────────┐  │   │
│  │  │ Debug the memory leak in image processing and cache   │  │   │
│  │  └───────────────────────────────────────────────────────┘  │   │
│  │                                                              │   │
│  │  ┌─ 🤖 Orchestrator ─── Planning ──── 5:23 PM ──────────┐  │   │
│  │  │ I have a few questions before creating the plan:      │  │   │
│  │  │ 1. Which image processing module?                     │  │   │
│  │  │ 2. Static analysis or profiling?                      │  │   │
│  │  └───────────────────────────────────────────────────────┘  │   │
│  │                                                              │   │
│  │  ┌─ 🤖 Orchestrator ─── Planning ──── 5:24 PM ──────────┐  │   │
│  │  │ 📋 EXECUTION PLAN — Review Required                   │  │   │
│  │  │ [Interactive Plan Card with Approve/Edit/Reject]       │  │   │
│  │  └───────────────────────────────────────────────────────┘  │   │
│  │                                                              │   │
│  │  ┌─ ⚙️ System ──────────────────── 5:25 PM ─────────────┐  │   │
│  │  │ ▶ Execution started — Stage 0 (2 workers)            │  │   │
│  │  └───────────────────────────────────────────────────────┘  │   │
│  │                                                              │   │
│  │  ┌─ 🔬 W-1: Image Pipeline ─ MemoryDiag ── 5:25 PM ────┐  │   │
│  │  │ Analyzing src/Imaging/BitmapDecoder.cs...             │  │   │
│  │  │ Found potential leak: Dispose() not called on ln 142  │  │   │
│  │  │ ```csharp                                    [📋 Copy]│  │   │
│  │  │ // Missing dispose pattern                            │  │   │
│  │  │ var decoder = new BitmapDecoder();                     │  │   │
│  │  │ ```                                                   │  │   │
│  │  └───────────────────────────────────────────────────────┘  │   │
│  │                                                              │   │
│  │  ┌─ 🧪 W-2: Cache Module ─ MemoryDiag ──── 5:25 PM ────┐  │   │
│  │  │ ⚠️ Tool Approval: `run_terminal("dotnet build")`     │  │   │
│  │  │ Source: Worker [Cache Module]                         │  │   │
│  │  │    [✅ Allow] [✅ Allow for Session] [❌ Deny]        │  │   │
│  │  └───────────────────────────────────────────────────────┘  │   │
│  │                                                              │   │
│  │  ┌─ 💉 You (Injection) ────────────── 5:26 PM ──────────┐  │   │
│  │  │ Also check the dispose pattern in MediaCodec          │  │   │
│  │  └───────────────────────────────────────────────────────┘  │   │
│  │                                                              │   │
│  │  ┌─ 🤖 Orchestrator ─── Executing ──── 5:26 PM ─────────┐  │   │
│  │  │ Acknowledged. I'll instruct W-1 to also check         │  │   │
│  │  │ MediaCodec in its analysis scope.                      │  │   │
│  │  └───────────────────────────────────────────────────────┘  │   │
│  │                                                              │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                     │
│  ┌──── ADVANCED INPUT BAR ─────────────────────────────────────┐   │
│  │  [💉]  💬 Type your message...                [📎] [⚙️] [➤] │   │
│  └──────────────────────────────────────────────────────────────┘   │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### 9.9 `TeamMessageTemplateSelector`

```csharp
namespace CopilotAgent.App.Views;

public class TeamMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ChatTemplate { get; set; }
    public DataTemplate? PlanReviewTemplate { get; set; }
    public DataTemplate? WorkerCommentaryTemplate { get; set; }
    public DataTemplate? ToolApprovalTemplate { get; set; }
    public DataTemplate? InjectionTemplate { get; set; }
    public DataTemplate? PhaseTransitionTemplate { get; set; }
    public DataTemplate? ReportTemplate { get; set; }
    public DataTemplate? ErrorTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        if (item is not TeamChatMessage msg) return ChatTemplate!;

        return msg.MessageType switch
        {
            TeamMessageType.PlanReview => PlanReviewTemplate!,
            TeamMessageType.WorkerCommentary or
            TeamMessageType.WorkerToolEvent or
            TeamMessageType.WorkerReasoning => WorkerCommentaryTemplate!,
            TeamMessageType.ToolApproval => ToolApprovalTemplate!,
            TeamMessageType.UserInjection or
            TeamMessageType.InjectionResponse => InjectionTemplate!,
            TeamMessageType.PhaseTransition => PhaseTransitionTemplate!,
            TeamMessageType.Report => ReportTemplate!,
            TeamMessageType.Error => ErrorTemplate!,
            _ => ChatTemplate!
        };
    }
}
```

### 9.10 `AgentTeamViewModel` (V3)

```csharp
public partial class AgentTeamViewModel : ViewModelBase
{
    private readonly IOrchestratorService _orchestratorService;

    // ── Chat Stream ──
    [ObservableProperty] private string _userInput = string.Empty;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _isInjectionMode;
    [ObservableProperty] private OrchestrationPhase _currentPhase = OrchestrationPhase.Idle;
    [ObservableProperty] private string _phaseDisplayText = "Idle";
    [ObservableProperty] private int _pendingApprovals;
    [ObservableProperty] private string _inputPlaceholder = "Ask the agent team...";
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isSearchVisible;

    // ── Ephemeral Worker Bar (V3) ──
    [ObservableProperty] private bool _isWorkerBarVisible;
    public ObservableCollection<WorkerPillViewModel> WorkerPills { get; } = new();

    /// <summary>
    /// Unified commentary stream — completely separate from existing ChatMessage.
    /// </summary>
    public ObservableCollection<TeamChatMessage> Commentary { get; } = new();

    public MultiAgentConfig Config { get; set; } = new();

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput) || IsProcessing) return;

        var input = UserInput;
        UserInput = string.Empty;

        if (IsInjectionMode && CurrentPhase == OrchestrationPhase.Executing)
        {
            AddMessage(TeamMessageSource.Injection, "You (Injection)", input,
                TeamMessageType.UserInjection, "injection", avatar: "💉");

            var response = await _orchestratorService.InjectInstructionAsync(input);
            AddOrchestratorMessage(response.Message, TeamMessageType.InjectionResponse);
        }
        else
        {
            AddMessage(TeamMessageSource.User, "You", input,
                TeamMessageType.Chat, "user", avatar: "👤");

            OrchestratorResponse response = CurrentPhase switch
            {
                OrchestrationPhase.Idle =>
                    await _orchestratorService.SubmitTaskAsync(input, Config),
                OrchestrationPhase.Clarifying =>
                    await _orchestratorService.RespondToClarificationAsync(input),
                OrchestrationPhase.Completed =>
                    await _orchestratorService.SendFollowUpAsync(input),
                _ => throw new InvalidOperationException(
                    $"Cannot send message in phase {CurrentPhase}")
            };

            HandleOrchestratorResponse(response);
        }
    }

    [RelayCommand] private async Task ApprovePlanAsync() { /* ... */ }
    [RelayCommand] private async Task RequestPlanChangesAsync() { /* ... */ }
    [RelayCommand] private async Task RejectPlanAsync() { /* ... */ }
    [RelayCommand] private void ToggleInjectionMode() { IsInjectionMode = !IsInjectionMode; }
    [RelayCommand] private void ToggleSearch() { IsSearchVisible = !IsSearchVisible; }

    // ── Ephemeral Worker Bar Logic (V3) ──
    private void OnOrchestratorEvent(object? sender, OrchestratorEvent e)
    {
        App.Current.Dispatcher.InvokeAsync(() =>
        {
            switch (e.EventType)
            {
                case OrchestratorEventType.PhaseChanged:
                    UpdatePhase(e);
                    break;

                case OrchestratorEventType.PlanCreated:
                    BuildWorkerPills();
                    break;

                case OrchestratorEventType.WorkerStarted:
                    IsWorkerBarVisible = true;  // Show ephemeral bar
                    UpdateWorkerPill(e as WorkerProgressEvent);
                    break;

                case OrchestratorEventType.WorkerProgress:
                case OrchestratorEventType.WorkerCompleted:
                case OrchestratorEventType.WorkerFailed:
                case OrchestratorEventType.WorkerRetrying:
                    UpdateWorkerPill(e as WorkerProgressEvent);
                    AddWorkerCommentary(e as WorkerProgressEvent);
                    CheckAutoHideWorkerBar();
                    break;

                case OrchestratorEventType.TaskCompleted:
                case OrchestratorEventType.TaskAborted:
                    ScheduleWorkerBarHide();  // Auto-hide after 2 seconds
                    break;
            }
        });
    }

    private void BuildWorkerPills()
    {
        WorkerPills.Clear();
        var plan = _orchestratorService.CurrentPlan;
        if (plan == null) return;

        for (int i = 0; i < plan.Chunks.Count; i++)
        {
            var chunk = plan.Chunks[i];
            WorkerPills.Add(new WorkerPillViewModel
            {
                ChunkId = chunk.ChunkId,
                Title = chunk.Title,
                Status = chunk.Status,
                Role = chunk.AssignedRole
            });
        }
    }

    private async void ScheduleWorkerBarHide()
    {
        await Task.Delay(2000);
        App.Current.Dispatcher.Invoke(() => IsWorkerBarVisible = false);
    }

    private void AddMessage(TeamMessageSource source, string displayName,
        string content, TeamMessageType type, string colorKey,
        string avatar = "🤖", string? roleBadge = null, string? chunkId = null)
    {
        Commentary.Add(new TeamChatMessage
        {
            Source = source,
            SourceDisplayName = displayName,
            Content = content,
            MessageType = type,
            ColorKey = TeamColorScheme.SourceColors.GetValueOrDefault(colorKey, "#FFFFFF"),
            Avatar = avatar,
            RoleBadge = roleBadge,
            ChunkId = chunkId
        });
    }

    private void AddOrchestratorMessage(string content, TeamMessageType type,
        OrchestrationPlan? plan = null)
    {
        Commentary.Add(new TeamChatMessage
        {
            Source = TeamMessageSource.Orchestrator,
            SourceDisplayName = "Orchestrator",
            Content = content,
            MessageType = type,
            PlanData = plan,
            ColorKey = TeamColorScheme.SourceColors["orchestrator"],
            Avatar = "🤖",
            RoleBadge = "Planning"
        });
    }
}
```

### 9.11 Performance Considerations

| Concern | Solution |
|---|---|
| **High message volume** | `VirtualizingStackPanel` with `VirtualizationMode="Recycling"` |
| **Rapid worker updates** | Events batched via `BufferBlock<T>` with 100ms flush interval |
| **Memory** | Messages beyond configurable limit (default 1000) are pruned from the top |
| **Thread safety** | All `ObservableCollection` mutations via `Dispatcher.InvokeAsync()` |
| **Scroll performance** | Auto-scroll only when user is at bottom; manual scroll locks position |
| **Markdown rendering** | Lazy rendering — only visible messages render rich content |
| **Code syntax highlighting** | Cached `FlowDocument` per code block, reused on scroll recycle |

### 9.12 Separation from Existing Chat

| Aspect | Existing Chat (ChatView) | Agent Team (AgentTeamView) |
|---|---|---|
| **Model** | `ChatMessage` | `TeamChatMessage` |
| **ViewModel** | `ChatViewModel` | `AgentTeamViewModel` |
| **View** | `ChatView.xaml` | `AgentTeamView.xaml` |
| **Message roles** | `MessageRole` enum | `TeamMessageSource` + `TeamMessageType` |
| **Rendering** | Single template per role | `DataTemplateSelector` with 8+ templates |
| **Color coding** | None (single agent) | Per-source via `TeamColorScheme` |
| **Worker status** | N/A | Ephemeral pill bar (auto-show/hide) |
| **Injection** | Not supported | Full injection pipeline |
| **Tool approval** | Modal dialog | Inline in chat stream |
| **Plan review** | N/A | Interactive plan card with approve/edit/reject |
| **Markdown** | Basic | Full rich Markdown with syntax highlighting |
| **Code blocks** | Basic | Copy button + language badge + syntax highlighting |
| **Search** | None | Ctrl+/ search with navigation |
| **Service** | `ICopilotService` directly | `IOrchestratorService` (phases, workers, injection) |

---

## 10. Feature: Role-Specialized Agents

### 10.1 Overview

Instead of all workers using the same generic system instructions, V3 introduces **role-specialized agents**. The orchestrator assigns a role to each work chunk during planning, and the `IAgentRoleProvider` creates sessions with tailored system instructions, preferred tools, and MCP server configurations per role.

### 10.2 `AgentRole` Enum

```csharp
namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Specialization roles for worker agents. The orchestrator assigns
/// a role to each work chunk during planning.
/// </summary>
public enum AgentRole
{
    /// <summary>No specialization — general-purpose worker.</summary>
    Generic,

    /// <summary>Specialized in task decomposition and planning.</summary>
    Planning,

    /// <summary>Specialized in code analysis, review, and static analysis.</summary>
    CodeAnalysis,

    /// <summary>Specialized in memory diagnostics, leak detection, profiling.</summary>
    MemoryDiagnostics,

    /// <summary>Specialized in performance analysis and optimization.</summary>
    Performance,

    /// <summary>Specialized in test creation, test execution, coverage analysis.</summary>
    Testing,

    /// <summary>Specialized in code implementation, refactoring, feature development.</summary>
    Implementation,

    /// <summary>Specialized in synthesizing results from multiple workers into a cohesive report.</summary>
    Synthesis
}
```

### 10.3 `AgentRoleConfig`

```csharp
namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Configuration for a specific agent role. Defines the system instructions,
/// preferred tools, and MCP servers for workers assigned this role.
/// </summary>
public sealed class AgentRoleConfig
{
    /// <summary>The role this config applies to.</summary>
    public AgentRole Role { get; set; }

    /// <summary>
    /// System instructions prepended to the worker's session.
    /// These tailor the LLM's behavior for the specific role.
    /// </summary>
    public string SystemInstructions { get; set; } = string.Empty;

    /// <summary>
    /// Tools that this role prefers to use. These are prioritized
    /// in the worker's prompt but not exclusively enforced.
    /// </summary>
    public List<string> PreferredTools { get; set; } = new();

    /// <summary>
    /// MCP servers specifically enabled for this role.
    /// Merged with the global MCP server list.
    /// </summary>
    public List<string> EnabledMcpServers { get; set; } = new();

    /// <summary>
    /// Optional model override for this role (e.g., use a more powerful
    /// model for Synthesis, a faster one for CodeAnalysis).
    /// </summary>
    public string? ModelOverride { get; set; }

    /// <summary>
    /// Temperature override for this role's LLM calls.
    /// Lower for deterministic roles (CodeAnalysis), higher for creative (Synthesis).
    /// </summary>
    public double? TemperatureOverride { get; set; }
}
```

### 10.4 Default Role Configurations

```csharp
public static class DefaultRoleConfigs
{
    public static Dictionary<AgentRole, AgentRoleConfig> GetDefaults() => new()
    {
        [AgentRole.Planning] = new AgentRoleConfig
        {
            Role = AgentRole.Planning,
            SystemInstructions = """
                You are a Task Planning specialist. Your job is to:
                1. Analyze complex tasks and break them into atomic, parallelizable work chunks.
                2. Identify dependencies between chunks accurately.
                3. Estimate complexity and assign appropriate roles.
                4. Maximize parallelism while maintaining correctness.
                Be precise and structured. Output plans in the required JSON format.
                """,
            PreferredTools = new() { "code_analysis", "file_search" }
        },

        [AgentRole.CodeAnalysis] = new AgentRoleConfig
        {
            Role = AgentRole.CodeAnalysis,
            SystemInstructions = """
                You are a Code Analysis specialist. Focus on:
                1. Static analysis: find bugs, code smells, security vulnerabilities.
                2. Code review: readability, maintainability, adherence to patterns.
                3. Architecture analysis: coupling, cohesion, SOLID violations.
                Be thorough. Reference exact file paths and line numbers.
                """,
            PreferredTools = new() { "code_analysis", "file_search", "read_file" },
            TemperatureOverride = 0.1  // Deterministic
        },

        [AgentRole.MemoryDiagnostics] = new AgentRoleConfig
        {
            Role = AgentRole.MemoryDiagnostics,
            SystemInstructions = """
                You are a Memory Diagnostics specialist. Focus on:
                1. Identifying memory leaks (missing Dispose, event handler leaks, static references).
                2. Analyzing allocation patterns and GC pressure.
                3. Detecting unbounded collections and cache growth.
                4. Reviewing IDisposable implementations and using statements.
                Reference exact code locations with file paths and line numbers.
                """,
            PreferredTools = new() { "code_analysis", "terminal", "read_file" },
            TemperatureOverride = 0.1
        },

        [AgentRole.Performance] = new AgentRoleConfig
        {
            Role = AgentRole.Performance,
            SystemInstructions = """
                You are a Performance Analysis specialist. Focus on:
                1. Algorithmic complexity analysis (time and space).
                2. Hot path identification and optimization opportunities.
                3. Concurrency bottlenecks (lock contention, thread pool starvation).
                4. I/O patterns (async/await correctness, unnecessary blocking).
                Provide specific optimization recommendations with expected impact.
                """,
            PreferredTools = new() { "code_analysis", "terminal", "read_file" },
            TemperatureOverride = 0.2
        },

        [AgentRole.Testing] = new AgentRoleConfig
        {
            Role = AgentRole.Testing,
            SystemInstructions = """
                You are a Testing specialist. Focus on:
                1. Writing comprehensive unit tests with edge cases.
                2. Integration test design and execution.
                3. Test coverage analysis and gap identification.
                4. Test maintainability and determinism.
                Use the project's testing framework. Ensure tests are deterministic and fast.
                """,
            PreferredTools = new() { "code_analysis", "terminal", "write_file", "read_file" },
            TemperatureOverride = 0.2
        },

        [AgentRole.Implementation] = new AgentRoleConfig
        {
            Role = AgentRole.Implementation,
            SystemInstructions = """
                You are an Implementation specialist. Focus on:
                1. Writing clean, production-quality code following project conventions.
                2. Minimal, focused changes — avoid unnecessary refactoring.
                3. Proper error handling, logging, and documentation.
                4. Reusing existing utilities and patterns from the codebase.
                Follow the project's coding standards exactly.
                """,
            PreferredTools = new() { "code_analysis", "terminal", "write_file", "read_file" },
            TemperatureOverride = 0.3
        },

        [AgentRole.Synthesis] = new AgentRoleConfig
        {
            Role = AgentRole.Synthesis,
            SystemInstructions = """
                You are a Synthesis specialist. Your job is to:
                1. Consolidate findings from multiple worker agents into a cohesive report.
                2. Identify conflicts, overlaps, and gaps between worker outputs.
                3. Produce clear, actionable recommendations.
                4. Write a conversational summary that a human can understand immediately.
                Be concise but thorough. Prioritize actionable insights.
                """,
            PreferredTools = new() { "code_analysis", "read_file" },
            TemperatureOverride = 0.5  // More creative for synthesis
        },

        [AgentRole.Generic] = new AgentRoleConfig
        {
            Role = AgentRole.Generic,
            SystemInstructions = """
                You are a general-purpose coding agent. Execute the assigned task
                thoroughly and report your findings clearly.
                """,
            PreferredTools = new() { "code_analysis", "terminal", "write_file", "read_file" }
        }
    };
}
```

### 10.5 `IAgentRoleProvider`

```csharp
namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Creates role-specialized Copilot SDK sessions for worker agents.
/// </summary>
public interface IAgentRoleProvider
{
    /// <summary>
    /// Create a Copilot session configured for the specified role.
    /// Applies role-specific system instructions, tools, and model overrides.
    /// </summary>
    Task<string> CreateRoleSessionAsync(
        AgentRole role,
        string workspacePath,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the effective configuration for a role, merging defaults with user overrides.
    /// </summary>
    AgentRoleConfig GetEffectiveRoleConfig(AgentRole role, MultiAgentConfig config);
}
```

### 10.6 Role Integration Flow

```
Task Decomposition (LlmTaskDecomposer)
    │
    ├── LLM outputs: { "role": "MemoryDiagnostics", ... } for each chunk
    │
    ▼
WorkChunk.AssignedRole = AgentRole.MemoryDiagnostics
    │
    ▼
WorkerAgent.ExecuteAsync()
    │
    ├── IAgentRoleProvider.CreateRoleSessionAsync(chunk.AssignedRole, ...)
    │       │
    │       ├── Get AgentRoleConfig for MemoryDiagnostics
    │       ├── Apply SystemInstructions as session system prompt
    │       ├── Apply ModelOverride (if specified)
    │       ├── Merge PreferredTools with global tools
    │       ├── Merge EnabledMcpServers with global MCP servers
    │       └── Return sessionId
    │
    ├── Send chunk.Prompt to role-specialized session
    │
    └── Return AgentResult
```

---

## 11. Feature: Observability & Debugging

### 11.1 Overview

Production-grade orchestration requires deep observability into what happened, when, and why. V3 introduces `ITaskLogStore` for structured logging, `LogEntry` for typed log records, and replay capability for debugging failed orchestrations.

### 11.2 `LogEntry` Model

```csharp
namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Structured log entry for orchestration observability.
/// </summary>
public sealed class LogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public LogLevel Level { get; set; } = LogLevel.Info;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ChunkId { get; set; }
    public string? PlanId { get; set; }
    public AgentRole? Role { get; set; }
    public OrchestratorEventType? EventType { get; set; }

    /// <summary>Structured data for machine-readable analysis.</summary>
    public Dictionary<string, object>? Data { get; set; }

    /// <summary>Duration of the operation (if applicable).</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Token count (if applicable).</summary>
    public int? TokensUsed { get; set; }

    /// <summary>Error details (if Level == Error).</summary>
    public string? ErrorDetails { get; set; }
    public string? StackTrace { get; set; }
}

public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
    Critical
}
```

### 11.3 `ITaskLogStore`

```csharp
namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Persistent structured log store for orchestration observability.
/// Supports per-plan and per-chunk log retrieval, and replay.
/// </summary>
public interface ITaskLogStore
{
    /// <summary>Save a log entry associated with a plan and optionally a chunk.</summary>
    Task SaveLogEntryAsync(string planId, string? chunkId, LogEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>Get all log entries for a specific chunk.</summary>
    Task<List<LogEntry>> GetChunkLogsAsync(string chunkId,
        CancellationToken cancellationToken = default);

    /// <summary>Get all log entries for a plan (all chunks + orchestrator).</summary>
    Task<List<LogEntry>> GetPlanLogsAsync(string planId,
        CancellationToken cancellationToken = default);

    /// <summary>Get log entries filtered by level.</summary>
    Task<List<LogEntry>> GetLogsByLevelAsync(string planId, LogLevel minLevel,
        CancellationToken cancellationToken = default);

    /// <summary>Export all logs for a plan as JSON (for debugging/sharing).</summary>
    Task<string> ExportPlanLogsAsJsonAsync(string planId,
        CancellationToken cancellationToken = default);

    /// <summary>Get a timeline view of events for replay.</summary>
    Task<List<LogEntry>> GetTimelineAsync(string planId,
        CancellationToken cancellationToken = default);

    /// <summary>Prune logs older than the specified age.</summary>
    Task PruneLogsAsync(TimeSpan maxAge,
        CancellationToken cancellationToken = default);
}
```

### 11.4 `JsonTaskLogStore` Implementation

```csharp
namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// File-based JSON log store. Stores logs in:
///   {appData}/CopilotDesktop/logs/orchestration/{planId}/
///     ├── plan.json       (plan-level logs)
///     ├── {chunkId}.json  (per-chunk logs)
///     └── timeline.json   (ordered event timeline)
/// </summary>
public class JsonTaskLogStore : ITaskLogStore
{
    private readonly string _baseLogDir;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonTaskLogStore(string? baseDir = null)
    {
        _baseLogDir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopilotDesktop", "logs", "orchestration");
        Directory.CreateDirectory(_baseLogDir);
    }

    public async Task SaveLogEntryAsync(string planId, string? chunkId, LogEntry entry, CancellationToken ct)
    {
        entry.PlanId = planId;
        entry.ChunkId = chunkId;

        await _writeLock.WaitAsync(ct);
        try
        {
            var planDir = Path.Combine(_baseLogDir, planId);
            Directory.CreateDirectory(planDir);

            // Append to chunk-specific log
            if (!string.IsNullOrEmpty(chunkId))
            {
                var chunkFile = Path.Combine(planDir, $"{chunkId}.jsonl");
                await AppendJsonLineAsync(chunkFile, entry, ct);
            }

            // Always append to plan-level timeline
            var timelineFile = Path.Combine(planDir, "timeline.jsonl");
            await AppendJsonLineAsync(timelineFile, entry, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ... other methods read from these files and deserialize
}
```

### 11.5 Orchestration Logging Integration

The orchestrator and workers emit structured log entries at key moments:

| Event | Log Level | Data |
|---|---|---|
| Task submitted | Info | `{ "prompt_length": N }` |
| Clarification asked | Debug | `{ "question_count": N }` |
| Plan created | Info | `{ "chunk_count": N, "stage_count": N }` |
| Plan validated | Debug | `{ "is_valid": true/false, "errors": [...] }` |
| JSON schema validation failed (V3) | Warning | `{ "raw_json": "...", "errors": [...] }` |
| Worker started | Info | `{ "chunk_id": "...", "role": "MemoryDiagnostics" }` |
| Worker tool invocation | Debug | `{ "tool": "...", "args": "...", "approved": true }` |
| Worker completed | Info | `{ "duration_ms": N, "tokens": N, "files_modified": [...] }` |
| Worker failed | Error | `{ "error": "...", "retry_count": N }` |
| Worker retrying | Warning | `{ "attempt": N, "reason": "..." }` |
| Stage completed | Info | `{ "stage": N, "succeeded": N, "failed": N }` |
| Injection received | Info | `{ "instruction": "..." }` |
| Aggregation completed | Info | `{ "total_tokens": N, "duration_ms": N }` |
| Task completed | Info | `{ "stats": { ... } }` |
| Task aborted | Error | `{ "total_failures": N, "threshold": N }` |

### 11.6 Replay Support

The timeline-ordered log enables **replay** for debugging:

1. **Export**: `ITaskLogStore.ExportPlanLogsAsJsonAsync(planId)` produces a complete JSON record
2. **Inspect**: Logs contain all prompts sent, tool calls made, responses received, and timing
3. **Reproduce**: With the exported log, developers can analyze:
   - Why a plan was structured a certain way
   - What each worker did and in what order
   - Where failures occurred and what error context was provided
   - How injection instructions were processed
4. **Future**: A UI replay viewer (Phase 8) could step through the timeline visually

---

## Appendix A: Risk Register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| SDK session concurrency limit | Medium | High | Config-driven MaxParallelSessions; test at limits |
| API rate limiting | Medium | High | Exponential backoff; configurable throttle |
| Git worktree merge conflicts | Medium | Medium | Report to orchestrator LLM for resolution |
| LLM generates invalid plan JSON | High | Medium | JSON schema validation with retry (V3) |
| Worker produces irrelevant output | Medium | Medium | Retry with refined prompt; role-specialized instructions (V3) |
| Memory pressure (many sessions) | Low | High | Session disposal; configurable limits |
| Token budget exhaustion | Medium | High | Token tracking per session; early stop |
| Workspace cleanup failure | Low | Medium | Cleanup on app startup |
| Role config mismatch | Low | Medium | Default role configs with user override (V3) |

## Appendix B: Technology Stack

| Component | Technology |
|---|---|
| Runtime | .NET 8 |
| UI Framework | WPF |
| MVVM | CommunityToolkit.Mvvm |
| DI | Microsoft.Extensions.DependencyInjection |
| Copilot SDK | GitHub.Copilot.SDK (NuGet) |
| Git Operations | System.Diagnostics.Process (git CLI) |
| Concurrency | SemaphoreSlim, ConcurrentDictionary, Task.WhenAll |
| Serialization | System.Text.Json |
| JSON Schema Validation (V3) | System.Text.Json + custom validator or JsonSchema.Net |
| Testing | xUnit / MSTest (per existing project convention) |
| Logging | Microsoft.Extensions.Logging + ITaskLogStore (V3) |
| Markdown Rendering (V3) | Custom WPF Markdown renderer (or Markdig + FlowDocument) |

## Appendix C: Glossary

| Term | Definition |
|---|---|
| **Orchestrator** | Long-lived Copilot SDK session that plans, delegates, monitors, and consolidates |
| **Worker Agent** | Short-lived Copilot SDK session that executes a single work chunk |
| **Work Chunk** | An atomic unit of work derived from task decomposition |
| **Execution Stage** | A group of work chunks that can run in parallel (same dependency depth) |
| **Workspace Strategy** | The isolation mechanism for concurrent file access |
| **Agent Pool** | Concurrency-limited pool that manages worker lifecycle |
| **Consolidated Report** | Final output combining all worker results + LLM summary |
| **Follow-up** | Subsequent user query in the same orchestrator context |
| **Agent Role (V3)** | Specialization assigned to a worker (e.g., CodeAnalysis, Synthesis) |
| **ChunkExecutionContext (V3)** | Runtime state container for a single chunk execution |
| **Worker Pill (V3)** | Compact UI element in ephemeral bar showing worker status |
| **Injection (V3)** | User instruction sent to orchestrator during worker execution |
| **ITaskLogStore (V3)** | Structured log persistence for observability and replay |

## Appendix D: Updated Solution Structure (V3)

```
CopilotAgent.MultiAgent/
├── Models/
│   ├── MultiAgentConfig.cs
│   ├── OrchestrationPlan.cs
│   ├── OrchestrationPhase.cs
│   ├── OrchestratorResponse.cs
│   ├── PlanApprovalDecision.cs
│   ├── WorkChunk.cs
│   ├── AgentResult.cs
│   ├── AgentStatus.cs
│   ├── AgentRole.cs                    # NEW V3 (Section 10)
│   ├── AgentRoleConfig.cs              # NEW V3 (Section 10)
│   ├── ChunkExecutionContext.cs        # NEW V3 (Section 4.1.8)
│   ├── OrchestratorContext.cs
│   ├── ConsolidatedReport.cs
│   ├── WorkspaceStrategyType.cs
│   ├── RetryPolicy.cs
│   ├── TeamChatMessage.cs
│   ├── TeamColorScheme.cs
│   └── LogEntry.cs                     # NEW V3 (Section 11)
├── Services/
│   ├── IOrchestratorService.cs         # UPDATED (phased API)
│   ├── OrchestratorService.cs          # UPDATED (state machine + logging)
│   ├── ITaskDecomposer.cs              # UPDATED V3 (JSON schema validation)
│   ├── LlmTaskDecomposer.cs            # UPDATED V3 (schema + role assignment)
│   ├── IAgentPool.cs
│   ├── AgentPool.cs
│   ├── IWorkerAgent.cs                 # UPDATED V3 (ChunkExecutionContext)
│   ├── WorkerAgent.cs                  # UPDATED V3 (role + logging)
│   ├── IAgentRoleProvider.cs           # NEW V3 (Section 10)
│   ├── AgentRoleProvider.cs            # NEW V3 (Section 10)
│   ├── IWorkspaceStrategy.cs
│   ├── GitWorktreeStrategy.cs
│   ├── FileLockingStrategy.cs
│   ├── InMemoryStrategy.cs
│   ├── IResultAggregator.cs
│   ├── ResultAggregator.cs             # UPDATED V3 (SynthesisAgent role)
│   ├── IDependencyScheduler.cs
│   ├── DependencyScheduler.cs
│   ├── IApprovalQueue.cs
│   ├── ApprovalQueue.cs
│   ├── ITaskLogStore.cs                # NEW V3 (Section 11)
│   └── JsonTaskLogStore.cs             # NEW V3 (Section 11)
└── Events/
    ├── OrchestratorEvent.cs            # UPDATED (new event types)
    ├── WorkerProgressEvent.cs          # UPDATED (WorkerIndex, Role)
    └── OrchestrationCompletedEvent.cs

CopilotAgent.App/ (new files)
├── ViewModels/
│   ├── AgentTeamViewModel.cs           # V3 (Section 9.10)
│   ├── AgentTeamSettingsViewModel.cs
│   └── WorkerPillViewModel.cs          # NEW V3 (Section 9.2.5)
└── Views/
    ├── AgentTeamView.xaml              # V3 (Section 9.8 — modern chat)
    ├── AgentTeamView.xaml.cs
    ├── AgentTeamSettingsDialog.xaml
    ├── AgentTeamSettingsDialog.xaml.cs
    └── TeamMessageTemplateSelector.cs  # Section 9.9
```

## Appendix E: Cloud Scalability Path

### E.1 Overview

While the initial implementation runs entirely within the WPF desktop application, the architecture is designed for future cloud scalability. This appendix outlines the path to a hybrid desktop+cloud deployment.

### E.2 Architecture Evolution

```
Phase 1 (Current — Desktop Only):
┌─────────────────────────────────┐
│  CopilotAgent.App (WPF)        │
│  ├── Orchestrator (in-process) │
│  ├── Workers (in-process)      │
│  └── All local SDK sessions    │
└─────────────────────────────────┘

Phase 2 (Hybrid — Desktop + Cloud Workers):
┌─────────────────────────────────┐     ┌──────────────────────────┐
│  CopilotAgent.App (WPF)        │     │  ASP.NET Core API        │
│  ├── Orchestrator (in-process) │────►│  ├── /api/dispatch       │
│  ├── Local workers (optional)  │◄────│  ├── /api/status         │
│  └── SignalR client            │     │  ├── Worker containers   │
│                                 │     │  └── SignalR hub         │
└─────────────────────────────────┘     └──────────────────────────┘

Phase 3 (Full Cloud — Multi-Tenant):
┌──────────────┐     ┌──────────────────────────────────────┐
│  WPF Client  │     │  Cloud Backend                       │
│  (thin UI)   │────►│  ├── API Gateway                     │
│              │◄────│  ├── Orchestrator Service (per-tenant)│
│              │     │  ├── Worker Pool (auto-scaling)       │
│              │     │  ├── SignalR for real-time updates    │
│              │     │  ├── Azure Service Bus (task queue)   │
│              │     │  └── Cosmos DB (logs, plans, results) │
└──────────────┘     └──────────────────────────────────────┘
```

### E.3 Key Abstractions for Cloud Readiness

The current architecture already uses abstractions that enable cloud migration:

| Abstraction | Desktop Implementation | Cloud Implementation |
|---|---|---|
| `IOrchestratorService` | In-process, DI singleton | Cloud-hosted microservice |
| `IAgentPool` | Local `SemaphoreSlim` | Kubernetes-based autoscaling pool |
| `IWorkerAgent` | Local Copilot SDK session | Containerized worker with SDK session |
| `IWorkspaceStrategy` | Git worktree on local disk | Cloud storage + ephemeral containers |
| `ITaskLogStore` | Local JSON files | Azure Table Storage / Cosmos DB |
| `IApprovalQueue` | In-process semaphore | SignalR-backed approval channel |
| Events (`OrchestratorEvent`) | In-process event handlers | SignalR real-time push |

### E.4 SignalR Integration Points

```csharp
// Future: SignalR hub for real-time updates
public interface IOrchestratorHub
{
    Task OnWorkerProgress(WorkerProgressEvent progress);
    Task OnPhaseChanged(OrchestrationPhase phase, string message);
    Task OnApprovalRequired(ToolApprovalRequest request);
    Task OnTaskCompleted(ConsolidatedReport report);
}

// Future: SignalR client in WPF
public interface IOrchestratorHubClient
{
    Task SubmitTask(string prompt, MultiAgentConfig config);
    Task ApprovePlan(PlanApprovalDecision decision);
    Task InjectInstruction(string instruction);
    Task RespondToApproval(string requestId, ApprovalDecision decision);
}
```

### E.5 Containerized Worker Design

```dockerfile
# Future: Worker container
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY . .
ENTRYPOINT ["dotnet", "CopilotAgent.Worker.dll"]
# ENV: CHUNK_ID, PLAN_ID, WORKSPACE_URL, COPILOT_TOKEN
# Receives chunk via env/args, executes, posts result to API
```

### E.6 Migration Strategy

| Step | Description | Effort |
|---|---|---|
| 1 | Extract `IAgentPool.DispatchAsync` to support remote dispatch | Medium |
| 2 | Add SignalR client to WPF for real-time event streaming | Medium |
| 3 | Create ASP.NET Core API with worker dispatch endpoint | High |
| 4 | Containerize `WorkerAgent` as standalone service | Medium |
| 5 | Replace local `ITaskLogStore` with cloud-backed implementation | Low |
| 6 | Add authentication, tenant isolation, rate limiting | High |
| 7 | Kubernetes auto-scaling for worker pool | Medium |

---

*End of Document — V3*