# 🏛️ Panel Discussion Final Report — Code & Folder Structure Analysis

> **Date**: February 11, 2026  
> **Panel Composition**: Software Architect · Security Expert · Performance Engineer · Devil's Advocate  
> **Verdict**: **Unanimous approval** of the proposed structure (with recorded caveats)  
> **Scope**: Project-level and folder-level organization for the CopilotAgent .NET 8 WPF solution

---

## 1. Executive Summary

| # | Key Finding |
|---|-------------|
| **1** | **`Core` is a "shared junk drawer"** — it mixes lightweight interfaces with heavy implementations (Playwright 1.52.0), causing every downstream project to transitively load ~8 unnecessary assemblies on cold start. Splitting into `Core.Abstractions` + `Core` eliminates this at compile time. |
| **2** | **Panel's DDD structure is over-engineered** — `Domain/ValueObjects/` (4 files of thin wrappers), `Domain/Policies/` (1 file), and 3-level-deep navigation were unanimously retired in favor of flat `Models/` + `Abstractions/` consistent with other projects. |
| **3** | **MultiAgent and Office's flat structure is under-engineered** — 22 files in a single `Services/` folder mixes interfaces, implementations, and strategy patterns with no structural signal. An `Abstractions/` subfolder and optional `Infrastructure/` folder solve this. |
| **4** | **`Persistence` as a standalone project is indefensible** — one file implementing one interface. It should fold into `Core` (with security hardening of the JSON storage). |
| **5** | **The test structure is the highest-value gap** — only `Tests/Office/` exists. No tests for Core (20 services including security-critical command policy), MultiAgent (22 services), or Panel (the most complex project). Mirroring `src/` in `tests/` is the change that most directly improves quality. |

---

## 2. Detailed Analysis

### 2.1 Current Structure — What Exists Today

```
CopilotAgent.sln
├── src/
│   ├── CopilotAgent.App/              # WPF shell (MVVM)
│   │   ├── Converters/                # 7 files — BoolToVisibility, UtcToLocal, etc.
│   │   ├── Helpers/                   # 3 files — AnsiParser, NextStepsParser, TemplateSelector
│   │   ├── Resources/                 # app.ico, app.png, create-icon.ps1
│   │   ├── Services/                  # 1 file — ToolApprovalUIService.cs
│   │   ├── ViewModels/               # 14 files — ChatVM, AgentTeamVM, OfficeVM, PanelVM, etc.
│   │   └── Views/                     # 16 files — ChatView, AgentTeamView, OfficeView, etc.
│   │
│   ├── CopilotAgent.Core/            # Shared interfaces + implementations (THE PROBLEM)
│   │   ├── Models/                    # 18 files — includes PanelSettings, OfficeSettings, MultiAgentSettings
│   │   └── Services/                  # 20 files — mixes I* interfaces with implementations + Playwright
│   │
│   ├── CopilotAgent.MultiAgent/      # Agent Team engine
│   │   ├── Events/                    # 1 file — OrchestratorEvent.cs
│   │   ├── Models/                    # 16 files — AgentRole, WorkChunk, RetryPolicy, etc.
│   │   └── Services/                  # 22 files — ALL interfaces + implementations + strategies FLAT
│   │
│   ├── CopilotAgent.Office/          # Agent Office engine
│   │   ├── Events/                    # 2 files — OfficeEvent, OfficeEventType
│   │   ├── Models/                    # 14 files — ManagerPhase, AssistantTask, LiveCommentary, etc.
│   │   └── Services/                  # 14 files — interfaces + implementations mixed
│   │
│   ├── CopilotAgent.Panel/           # Panel Discussion engine (DDD-style — OVER-STRUCTURED)
│   │   ├── Agents/                    # 5 files — HeadAgent, PanelistAgent, ModeratorAgent, etc.
│   │   ├── Domain/                    # DDD hierarchy (being retired)
│   │   │   ├── Entities/             # 3 files — PanelSession, PanelMessage, AgentInstance
│   │   │   ├── Enums/                # 6 files — PanelPhase, PanelTrigger, etc.
│   │   │   ├── Events/               # 10 files — AgentMessageEvent, CostUpdateEvent, etc.
│   │   │   ├── Interfaces/           # 5 files — IPanelOrchestrator, IPanelAgent, etc.
│   │   │   ├── Policies/             # 1 file — GuardRailPolicy.cs
│   │   │   └── ValueObjects/         # 4 files — TurnNumber, TokenBudget, etc.
│   │   ├── Models/                    # 8 files — PanelSettings, PanelistProfile, etc.
│   │   ├── Resilience/               # 3 files — CircuitBreaker, RetryPolicy, Sandbox
│   │   ├── Services/                  # 5 files — PanelOrchestrator, ConvergenceDetector, etc.
│   │   └── StateMachine/             # 1 file — PanelStateMachine.cs
│   │
│   └── CopilotAgent.Persistence/     # JSON storage (ONE FILE — INDEFENSIBLE)
│       └── JsonPersistenceService.cs
│
├── tests/
│   └── CopilotAgent.Tests/           # Unified test project
│       └── Office/                    # ONLY Office has tests
│
└── docs/                              # 13 architecture docs
```

### 2.2 Verified Structural Defects

#### Defect 1: Playwright Contamination via `Core.csproj`

**Evidence:** `Microsoft.Playwright 1.52.0` is referenced in `CopilotAgent.Core.csproj`. Only **one file** (`PlaywrightBrowserService.cs`) uses it. But because MultiAgent, Office, Panel, and Persistence all reference Core, they **transitively load Playwright's ~8 managed assemblies on every cold start**.

**Impact:** ~200-400ms unnecessary startup overhead. Every project's compilation depends on Core, which depends on Playwright — so any Playwright update triggers recompilation of the entire solution.

**Root Cause:** No assembly-level separation between interfaces (lightweight) and implementations (heavy dependencies).

#### Defect 2: Feature Settings in Wrong Location

**Evidence:** `Core/Models/` contains `PanelSettings.cs`, `OfficeSettings.cs`, and `MultiAgentSettings.cs`. These change **when their respective features change**, not when Core changes — violating the Common Closure Principle.

**Impact:** Modifying Panel settings triggers Core recompilation, which cascades to **all 5 downstream projects**.

#### Defect 3: Interfaces Mixed with Implementations

**Evidence:** `Core/Services/` (20 files) and `MultiAgent/Services/` (22 files) place interfaces (`ICopilotService.cs`) directly alongside their implementations (`CopilotSdkService.cs`). This makes it impossible to understand at a glance **what a project exposes vs. what it encapsulates**.

**Impact:** New contributors cannot determine the public contract without reading code. IDE "Go to File" returns both contract and implementation with no structural differentiation.

#### Defect 4: Panel's DDD Over-Structure

**Evidence:**
- `Domain/ValueObjects/TurnNumber.cs` — 10-line `readonly record struct` with `Increment()` and `Exceeds()`. Three directory levels deep for a single boolean check.
- `Domain/ValueObjects/TokenBudget.cs` — 10 lines with one `IsExceeded()` method.
- `Domain/Policies/` — exactly **1 file** (`GuardRailPolicy.cs`).
- Navigation depth: `Panel/Domain/ValueObjects/TokenBudget.cs` — 4 levels to reach a data type.

**Impact:** Developer navigation overhead without proportional domain complexity. MultiAgent has **more behavioral complexity** in a flat structure that's easier to navigate.

#### Defect 5: `Persistence` Project Overhead

**Evidence:** One file (`JsonPersistenceService.cs`) implementing one interface (`IPersistenceService` defined in Core). The project adds namespace noise, build overhead, and a `.csproj` file for zero modularity benefit.

**Impact:** An unnecessary assembly load on startup (~5ms), plus cognitive overhead for new contributors trying to understand the project graph.

#### Defect 6: Ungated `Process.Start` Calls

**Evidence (Security Expert verified):** 9 files across 4 projects call `Process.Start` or `ProcessStartInfo`:
- 4 in `App/ViewModels/` (McpConfig, SessionInfo, Skills, Terminal)
- 3 in `Core/Services/` (CopilotCliService, McpService, SessionManager)
- 1 in `App/Views/` (ContentViewerDialog)
- 1 in `MultiAgent/` (GitWorktreeStrategy)

Only the path through `CopilotSdkService` → `ToolApprovalService` → `CommandPolicyService` is gated. **8 of 9 call sites bypass the approval pipeline entirely.**

**Impact:** Any compromised MCP tool or malicious input can trigger arbitrary process execution without policy evaluation.

#### Defect 7: Test Coverage Gap

**Evidence:** `tests/CopilotAgent.Tests/` has exactly one subfolder: `Office/`. Zero test files for:
- `Core` (20 services including security-critical `CommandPolicyService`)
- `MultiAgent` (22 services including `OrchestratorService`, `AgentPool`)
- `Panel` (most complex project — state machine, convergence detection, circuit breaker)
- `App` (14 ViewModels)

**Impact:** No regression safety net for the majority of the codebase.

---

### 2.3 Dependency Graph Analysis

**Current (Star Topology — 3 stages):**
```
Stage 1:  Core
Stage 2:  MultiAgent | Office | Panel | Persistence  (parallel)
Stage 3:  App
```

**Proposed (Star + Abstractions Layer — 4 stages):**
```
Stage 1:  Core.Abstractions
Stage 2:  Core | MultiAgent | Office | Panel  (parallel — reference only Abstractions)
Stage 3:  App  (references everything)
```

**Net effect:** +1 build stage (trivial), but MultiAgent/Office/Panel no longer depend on Core's heavy implementations. Playwright contamination eliminated.

---

## 3. Agreements — Unanimous Consensus

The panel reached **unanimous agreement** on the following points:

### 3.1 Structural Changes (All Panelists: ✅)

| # | Change | Rationale | Risk |
|---|--------|-----------|------|
| 1 | **Split `Core` into `Core.Abstractions` + `Core`** | Eliminates Playwright transitive contamination. Assembly boundary enforces dependency inversion at compile time. | Low — interfaces move to new project; implementations stay. |
| 2 | **Move feature settings to owning projects** | `PanelSettings` → `Panel/Models/`, `OfficeSettings` → `Office/Models/`, `MultiAgentSettings` → `MultiAgent/Models/`. Reduces Core's change surface. | Low — 3 file moves + namespace updates. |
| 3 | **Fold `Persistence` into `Core`** | One file doesn't justify a project boundary. `JsonPersistenceService.cs` → `Core/Persistence/`. | Low — project deletion + file move. |
| 4 | **Retire Panel's `Domain/` DDD hierarchy** | Over-structured for actual domain complexity. `ValueObjects/` → `Models/`, `Interfaces/` → `Abstractions/`, `Entities/` → `Models/`, `Policies/` → `Models/` or `Services/`. | Low — internal file moves within Panel. |
| 5 | **Add `Abstractions/` subfolder** in Core and feature projects | Separates interfaces from implementations. Makes public contracts visible from folder tree. | Low — file moves within existing projects. |
| 6 | **Add `ServiceCollectionExtensions.cs`** per feature project root | Enables per-feature lazy DI registration. Prerequisite for deferred singleton initialization. | Low — one new file per project. |
| 7 | **Mirror `src/` structure in `tests/`** | One test subfolder for 150+ files is the highest-value structural gap. | Medium — requires writing new tests, not just moving files. |

### 3.2 Principles (All Panelists: ✅)

| Principle | Quoted From |
|-----------|-------------|
| *"Assembly boundaries are laws. Folder conventions are suggestions."* | Software Architect |
| *"No subfolder with fewer than 3 files."* | Density Rule (originated by Devil's Advocate) |
| *"Shallow + consistent beats deep + correct."* | Devil's Advocate, endorsed by all |
| *"Every feature project uses the same folder vocabulary."* | Consistency Rule |

---

## 4. Disagreements — Points of Contention

### 4.1 `Core.Security` as a Separate Assembly

| Position | Advocate | Argument |
|----------|----------|----------|
| **Yes — separate assembly** | Security Expert (Round 1) | Auditability. When reviewing command execution controls, you should `dir` one project. Playwright in the same assembly as `CommandPolicyService` means a Playwright vulnerability compromises the policy engine's assembly. |
| **No — folder inside Core** | Architect, Performance Engineer, Devil's Advocate | Assembly load overhead for audit convenience. A `Core/Security/` folder with a README achieves the same auditability without a third assembly. |
| **Resolution** | Security Expert conceded in Round 2. `Core/Security/` folder accepted with explicit audit boundary documentation. **Resolved.** |

### 4.2 `Infrastructure/` Subfolder in Every Feature Project

| Position | Advocate | Argument |
|----------|----------|----------|
| **Yes — always include it** | Architect (Round 1) | Makes strategy/resilience patterns visible from folder tree. A contributor knows `RedisWorkspaceStrategy` goes in `Infrastructure/`. |
| **Only when ≥3 files** | Devil's Advocate, Performance Engineer | Folder inflation. Office has no infrastructure code. Creating empty folders "just in case" violates the density rule. |
| **Resolution** | Architect accepted the density rule: `Infrastructure/` only when ≥3 files qualify. Currently only MultiAgent (3 strategies) and Panel (3 resilience files) meet threshold. **Resolved.** |

### 4.3 Whether `Core.Abstractions` Split Is Necessary vs. Moving Playwright to App

| Position | Advocate | Argument |
|----------|----------|----------|
| **Move Playwright to App (simpler)** | Devil's Advocate | All 5 references to `IBrowserAutomationService` are in App. 2-file move + 1 line deletion solves the contamination. |
| **Split Core (future-proof)** | Architect, Performance Engineer, Security Expert | Moving the interface to App creates a structural trap — if an agent ever needs browser OAuth, you'd need to move it back or create circular dependencies. The abstractions assembly prevents this at compile time. |
| **Resolution** | Devil's Advocate conceded: *"I accept the split — not because today's code demands it, but because the compile-time constraint is genuinely better than a convention."* **Resolved.** |

### 4.4 `Startup/` Folder vs. Root-Level Extension File

| Position | Advocate | Argument |
|----------|----------|----------|
| **Dedicated `Startup/` folder** | Performance Engineer (Round 1) | Structural signal for initialization costs. Enables feature-flag gating. |
| **Single file at project root** | Devil's Advocate | A folder for a single file is the same anti-pattern that condemns `Persistence`. .NET convention is `ServiceCollectionExtensions.cs` at root. |
| **Resolution** | Performance Engineer conceded: *"I'll drop the folder but hold firm on the pattern."* Single `ServiceCollectionExtensions.cs` at project root. **Resolved.** |

---

## 5. Recommendations — The Proposed Best-in-Class Structure

### 5.1 The Final Tree (Unanimously Approved)

```
CopilotAgent.sln
├── src/
│   ├── CopilotAgent.Core.Abstractions/       # NEW — Interfaces + lightweight models ONLY
│   │   ├── Models/                            # AppSettings, ChatMessage, ToolCall, shared DTOs
│   │   │   ├── AppSettings.cs
│   │   │   ├── ChatMessage.cs
│   │   │   ├── MessageRole.cs
│   │   │   ├── ToolCall.cs
│   │   │   ├── ToolResult.cs
│   │   │   ├── ToolApprovalModels.cs
│   │   │   ├── McpServerConfig.cs
│   │   │   ├── SkillDefinition.cs
│   │   │   ├── IterativeTaskConfig.cs
│   │   │   ├── Session.cs
│   │   │   ├── SessionStreamingContext.cs
│   │   │   ├── StreamingTimeoutSettings.cs
│   │   │   ├── CommandPolicy.cs
│   │   │   └── BrowserAutomationSettings.cs
│   │   └── Services/                          # Pure interfaces — zero NuGet dependencies
│   │       ├── ICopilotService.cs
│   │       ├── IMcpService.cs
│   │       ├── ISessionManager.cs
│   │       ├── ISkillsService.cs
│   │       ├── IToolApprovalService.cs
│   │       ├── ICommandPolicyService.cs
│   │       ├── IPersistenceService.cs
│   │       ├── IIterativeTaskService.cs
│   │       ├── IBrowserAutomationService.cs
│   │       └── IStreamingMessageManager.cs
│   │
│   ├── CopilotAgent.Core/                    # Implementations + heavy dependencies
│   │   ├── Security/                          # Auditable perimeter for command/approval policy
│   │   │   ├── README.md                      # "All process execution policy lives here"
│   │   │   ├── CommandPolicyService.cs
│   │   │   └── ToolApprovalService.cs
│   │   ├── Services/                          # Remaining implementations
│   │   │   ├── CopilotSdkService.cs
│   │   │   ├── CopilotCliService.cs
│   │   │   ├── McpService.cs
│   │   │   ├── SessionManager.cs
│   │   │   ├── SkillsService.cs
│   │   │   ├── IterativeTaskService.cs
│   │   │   ├── PlaywrightBrowserService.cs    # Playwright stays in Core (not Abstractions)
│   │   │   └── StreamingMessageManager.cs
│   │   ├── Persistence/                       # Folded from standalone project
│   │   │   └── JsonPersistenceService.cs
│   │   └── ServiceCollectionExtensions.cs     # AddCore() — lazy registration
│   │
│   ├── CopilotAgent.MultiAgent/
│   │   ├── Abstractions/                      # NEW — extracted from Services/
│   │   │   ├── IOrchestratorService.cs
│   │   │   ├── ITaskDecomposer.cs
│   │   │   ├── IDependencyScheduler.cs
│   │   │   ├── IAgentPool.cs
│   │   │   ├── IWorkerAgent.cs
│   │   │   ├── IAgentRoleProvider.cs
│   │   │   ├── IWorkspaceStrategy.cs
│   │   │   ├── IResultAggregator.cs
│   │   │   ├── IApprovalQueue.cs
│   │   │   └── ITaskLogStore.cs
│   │   ├── Models/                            # Unchanged + MultiAgentSettings moved here
│   │   │   ├── MultiAgentSettings.cs          # MOVED from Core/Models/
│   │   │   ├── AgentRole.cs
│   │   │   ├── AgentRoleConfig.cs
│   │   │   ├── AgentResult.cs
│   │   │   ├── AgentStatus.cs
│   │   │   ├── ChunkExecutionContext.cs
│   │   │   ├── ConsolidatedReport.cs
│   │   │   ├── LogEntry.cs
│   │   │   ├── MultiAgentConfig.cs
│   │   │   ├── OrchestrationPlan.cs
│   │   │   ├── OrchestrationPhase.cs
│   │   │   ├── OrchestratorContext.cs
│   │   │   ├── OrchestratorResponse.cs
│   │   │   ├── RetryPolicy.cs
│   │   │   ├── TeamChatMessage.cs
│   │   │   ├── TeamColorScheme.cs
│   │   │   ├── WorkChunk.cs
│   │   │   └── WorkspaceStrategyType.cs
│   │   ├── Services/                          # Implementations only (interfaces extracted)
│   │   │   ├── OrchestratorService.cs
│   │   │   ├── LlmTaskDecomposer.cs
│   │   │   ├── DependencyScheduler.cs
│   │   │   ├── AgentPool.cs
│   │   │   ├── WorkerAgent.cs
│   │   │   ├── AgentRoleProvider.cs
│   │   │   ├── ResultAggregator.cs
│   │   │   ├── ApprovalQueue.cs
│   │   │   └── JsonTaskLogStore.cs
│   │   ├── Infrastructure/                    # NEW — Strategy pattern made visible (≥3 files)
│   │   │   ├── GitWorktreeStrategy.cs
│   │   │   ├── FileLockingStrategy.cs
│   │   │   └── InMemoryStrategy.cs
│   │   ├── Events/
│   │   │   └── OrchestratorEvent.cs
│   │   └── ServiceCollectionExtensions.cs     # AddMultiAgent() — lazy registration
│   │
│   ├── CopilotAgent.Office/                   # Same pattern as MultiAgent
│   │   ├── Abstractions/
│   │   │   ├── IOfficeManagerService.cs
│   │   │   ├── IAssistantPool.cs
│   │   │   ├── IAssistantAgent.cs
│   │   │   ├── IIterationScheduler.cs
│   │   │   ├── IOfficeEventLog.cs
│   │   │   ├── IReasoningStream.cs
│   │   │   └── IAgentEventCollector.cs
│   │   ├── Models/                            # + OfficeSettings moved here
│   │   │   ├── OfficeSettings.cs              # MOVED from Core/Models/
│   │   │   ├── AssistantTask.cs
│   │   │   ├── AssistantTaskStatus.cs
│   │   │   ├── AssistantResult.cs
│   │   │   ├── CommentaryType.cs
│   │   │   ├── CommentaryStreamingMode.cs
│   │   │   ├── IterationReport.cs
│   │   │   ├── LiveCommentary.cs
│   │   │   ├── ManagerContext.cs
│   │   │   ├── ManagerPhase.cs
│   │   │   ├── OfficeChatMessage.cs
│   │   │   ├── OfficeConfig.cs
│   │   │   ├── OfficeColorScheme.cs
│   │   │   ├── SchedulingAction.cs
│   │   │   └── ToolExecution.cs
│   │   ├── Services/
│   │   │   ├── OfficeManagerService.cs
│   │   │   ├── AssistantPool.cs
│   │   │   ├── AssistantAgent.cs
│   │   │   ├── IterationScheduler.cs
│   │   │   ├── OfficeEventLog.cs
│   │   │   ├── ReasoningStream.cs
│   │   │   └── AgentEventCollector.cs
│   │   ├── Events/
│   │   │   ├── OfficeEvent.cs
│   │   │   └── OfficeEventType.cs
│   │   └── ServiceCollectionExtensions.cs     # AddOffice() — lazy registration
│   │
│   ├── CopilotAgent.Panel/                    # DDD retired → consistent pattern
│   │   ├── Abstractions/                      # FROM Domain/Interfaces/
│   │   │   ├── IPanelOrchestrator.cs
│   │   │   ├── IPanelAgent.cs
│   │   │   ├── IPanelAgentFactory.cs
│   │   │   ├── IConvergenceDetector.cs
│   │   │   └── IKnowledgeBriefService.cs
│   │   ├── Agents/                            # Unchanged — Panel-specific, justified
│   │   │   ├── PanelAgentBase.cs
│   │   │   ├── HeadAgent.cs
│   │   │   ├── PanelistAgent.cs
│   │   │   ├── ModeratorAgent.cs
│   │   │   └── PanelAgentFactory.cs
│   │   ├── Models/                            # FLATTENED — Domain/Entities + ValueObjects + Enums merged
│   │   │   ├── PanelSettings.cs               # MOVED from Core/Models/
│   │   │   ├── PanelSession.cs                # from Domain/Entities/
│   │   │   ├── PanelMessage.cs                # from Domain/Entities/
│   │   │   ├── AgentInstance.cs               # from Domain/Entities/
│   │   │   ├── PanelSessionId.cs              # from Domain/ValueObjects/
│   │   │   ├── TurnNumber.cs                  # from Domain/ValueObjects/
│   │   │   ├── TokenBudget.cs                 # from Domain/ValueObjects/
│   │   │   ├── ModelIdentifier.cs             # from Domain/ValueObjects/
│   │   │   ├── PanelPhase.cs                  # from Domain/Enums/
│   │   │   ├── PanelTrigger.cs                # from Domain/Enums/
│   │   │   ├── PanelMessageType.cs            # from Domain/Enums/
│   │   │   ├── PanelAgentStatus.cs            # from Domain/Enums/
│   │   │   ├── PanelAgentRole.cs              # from Domain/Enums/
│   │   │   ├── CommentaryMode.cs              # from Domain/Enums/
│   │   │   ├── PanelSynthesis.cs
│   │   │   ├── PanelistProfile.cs
│   │   │   ├── PanelDiscussionPlan.cs
│   │   │   ├── ModeratorDecision.cs
│   │   │   ├── DefaultPanelistProfiles.cs
│   │   │   ├── CostEstimate.cs
│   │   │   ├── CircuitBreakerState.cs
│   │   │   ├── ToolCallRecord.cs
│   │   │   └── GuardRailPolicy.cs             # from Domain/Policies/
│   │   ├── Services/
│   │   │   ├── PanelOrchestrator.cs
│   │   │   ├── ConvergenceDetector.cs
│   │   │   ├── KnowledgeBriefService.cs
│   │   │   ├── CostEstimationService.cs
│   │   │   └── PanelCleanupService.cs
│   │   ├── Infrastructure/                    # FROM Resilience/ (≥3 files — qualifies)
│   │   │   ├── ToolCircuitBreaker.cs
│   │   │   ├── PanelRetryPolicy.cs
│   │   │   └── SandboxedToolExecutor.cs
│   │   ├── StateMachine/
│   │   │   └── PanelStateMachine.cs
│   │   ├── Events/                            # FROM Domain/Events/ (10 files — qualifies)
│   │   │   ├── PanelEvent.cs
│   │   │   ├── AgentMessageEvent.cs
│   │   │   ├── AgentStatusChangedEvent.cs
│   │   │   ├── CommentaryEvent.cs
│   │   │   ├── CostUpdateEvent.cs
│   │   │   ├── ErrorEvent.cs
│   │   │   ├── ModerationEvent.cs
│   │   │   ├── PhaseChangedEvent.cs
│   │   │   ├── ProgressEvent.cs
│   │   │   └── ToolCallEvent.cs
│   │   └── ServiceCollectionExtensions.cs     # AddPanel() — lazy registration
│   │
│   └── CopilotAgent.App/                      # WPF shell — MVVM (UNCHANGED pattern)
│       ├── Converters/                        # 7 files
│       ├── Helpers/                           # 3 files
│       ├── Resources/                         # 3 files
│       ├── Services/                          # ToolApprovalUIService + PlaywrightBrowserService*
│       ├── ViewModels/                        # 14 files
│       └── Views/                             # 16 files
│
├── tests/                                     # RESTRUCTURED — mirrors src/
│   ├── CopilotAgent.Core.Tests/               # NEW
│   ├── CopilotAgent.MultiAgent.Tests/         # NEW
│   ├── CopilotAgent.Office.Tests/             # Promoted from subfolder
│   ├── CopilotAgent.Panel.Tests/              # NEW
│   └── CopilotAgent.Integration.Tests/        # NEW — cross-project workflows
│
└── docs/                                      # Unchanged
```

### 5.2 The Three Rules That Govern This Structure

| Rule | Description | Enforcement |
|------|-------------|-------------|
| **Assembly Rule** | Feature projects reference `Core.Abstractions`, never `Core`. | Compile error if violated — Playwright and other heavy deps stay invisible to features. |
| **Density Rule** | No subfolder with fewer than 3 files. | Code review convention. If `Infrastructure/` or `Events/` doesn't qualify, files stay in `Services/` or `Models/`. |
| **Consistency Rule** | Every feature project uses the same folder vocabulary: `Abstractions/`, `Models/`, `Services/`, optionally `Infrastructure/`, `Events/`, `Agents/`. No project gets special DDD ceremony. | Template enforced in docs + code review. |

### 5.3 Folder Vocabulary Reference

| Folder | Purpose | When to Use |
|--------|---------|-------------|
| `Abstractions/` | Interfaces + domain contracts | Always — every feature project |
| `Models/` | DTOs, configs, enums, value objects, entities | Always — all data types live here (flat) |
| `Services/` | Orchestration + business logic implementations | Always — the "how" |
| `Infrastructure/` | Strategies, resilience, adapters, external integrations | Only when ≥3 files with clear cross-cutting pattern |
| `Events/` | Domain events, event types | Only when ≥3 event files |
| `Agents/` | Agent implementations (base, head, panelist, etc.) | Only for Panel (agent-specific pattern) |
| `StateMachine/` | State machine definitions | Only when present |
| `Security/` | Security policy implementations | Only in Core (audit perimeter) |
| `Persistence/` | Storage implementations | Only in Core (folded from standalone project) |

---

## 6. Risk Assessment

### 6.1 Migration Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| **Namespace breaks after file moves** | High | Low | Find-and-replace namespaces; IDE refactoring tools handle automatically |
| **`Core.Abstractions` split introduces transient build failures** | Medium | Medium | Do the split in a single PR; update all `.csproj` references atomically |
| **Feature settings move breaks serialization** | Low | High | `JsonPersistenceService` serializes by property name, not namespace — verify with existing tests |
| **Folding Persistence loses git history** | Medium | Low | Use `git mv` to preserve file history |
| **Panel `Domain/` retirement breaks existing code** | Low | Low | Files move within the same project — only namespaces change |

### 6.2 Structural Risks (Post-Migration)

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| **New contributor puts implementation in `Abstractions/`** | Medium | Low | README in `Abstractions/` stating "Interfaces only — zero implementations" |
| **`Core` grows back into a junk drawer** | Medium | Medium | The `Core.Abstractions` assembly boundary prevents heavy-dep contamination; `Security/` and `Persistence/` subfolders limit sprawl |
| **Test project split creates maintenance burden** | Low | Medium | Each test project references only its matching src project + Abstractions — clean isolation |

---

## 7. Follow-Up Items

### 7.1 P0 — Security (Outside Folder Structure Scope, But Flagged by Panel)

| Item | Owner | Description |
|------|-------|-------------|
| **`ProcessExecutionGuard`** | Security Expert proposed | Create `Core/Security/ProcessExecutionGuard.cs` — wraps `Process.Start`, enforces `CommandPolicyService.EvaluateCommand()`. All 9 `Process.Start` call sites must route through this guard. |
| **Architectural test for `Process.Start`** | Security Expert proposed | Add a test in `Core.Tests` that fails if any file outside `Core/Security/` references `System.Diagnostics.Process`. |
| **Encrypt `approval-rules.json`** | Security Expert proposed | Add DPAPI/`ProtectedData` encryption to `JsonPersistenceService` before folding into Core. |

### 7.2 P0 — Performance (Outside Folder Structure Scope, But Flagged by Panel)

| Item | Owner | Description |
|------|-------|-------------|
| **Per-feature `ServiceCollectionExtensions.cs`** | Performance Engineer proposed | Replace 37 inline registrations in `App.xaml.cs` with `services.AddCore()`, `services.AddMultiAgent()`, `services.AddOffice()`, `services.AddPanel()`. Enable `Lazy<T>` factory delegates for expensive singletons. |
| **Deferred Playwright initialization** | Performance Engineer proposed | `PlaywrightBrowserService` should lazy-init on first browser call, not at DI resolution time. |

### 7.3 P1 — Test Infrastructure

| Item | Description |
|------|-------------|
| **Create 5 test projects** | `Core.Tests`, `MultiAgent.Tests`, `Office.Tests` (promote), `Panel.Tests`, `Integration.Tests` |
| **Priority test targets** | `CommandPolicyService` (security), `PanelStateMachine` (state transitions), `OrchestratorService` (task decomposition), `ToolCircuitBreaker` (resilience) |

### 7.4 P2 — Documentation Updates

| Item | Description |
|------|-------------|
| **Update `PROJECT_STRUCTURE.md`** | Reflect new folder layout, dependency graph, and folder vocabulary |
| **Add `CONTRIBUTING.md`** | Document the three rules (Assembly, Density, Consistency) for new contributors |
| **`Core/Security/README.md`** | Document the audit perimeter and `ProcessExecutionGuard` usage |

---

## 8. Migration Priority — Implementation Order

| Phase | Changes | Effort | Risk | Value |
|-------|---------|--------|------|-------|
| **Phase 1** | Move `PanelSettings`, `OfficeSettings`, `MultiAgentSettings` to owning projects | 30 min | Low | Reduces Core recompilation cascade |
| **Phase 2** | Create `Core.Abstractions` project; move interfaces + lightweight models | 2-4 hrs | Medium | **Eliminates Playwright contamination** — highest structural value |
| **Phase 3** | Add `Abstractions/` subfolder in MultiAgent, Office, Panel; move interfaces | 1-2 hrs | Low | Consistent contract visibility |
| **Phase 4** | Fold `Persistence` into `Core/Persistence/` | 30 min | Low | Removes unnecessary assembly |
| **Phase 5** | Flatten Panel's `Domain/` into `Models/` + `Abstractions/` + `Events/` | 1-2 hrs | Low | Consistent structure, reduced navigation depth |
| **Phase 6** | Add `Infrastructure/` in MultiAgent (strategies) and Panel (resilience) | 30 min | Low | Pattern visibility |
| **Phase 7** | Add `Core/Security/` folder; move `CommandPolicyService` + `ToolApprovalService` | 30 min | Low | Audit perimeter |
| **Phase 8** | Add `ServiceCollectionExtensions.cs` per feature project | 2-4 hrs | Low | Enables lazy loading — **highest performance value** |
| **Phase 9** | Restructure `tests/` — create per-project test projects | 1-2 hrs (structure only) | Low | **Highest quality value** — tests still need writing |

**Total estimated effort:** 1-2 days for structural changes. Test writing is ongoing beyond that.

---

## 9. Panel Voting Record

| Proposal | Architect | Security | Performance | Devil's Advocate | Result |
|----------|-----------|----------|-------------|------------------|--------|
| Split Core → 2 assemblies | ✅ | ✅ | ✅ | ✅ (conceded R3) | **Unanimous** |
| Split Core → 3 assemblies | ❌ | ✅→❌ (conceded R2) | ❌ | ❌ | **Rejected** |
| Move feature settings | ✅ | ✅ | ✅ | ✅ (proposed R1) | **Unanimous** |
| Fold Persistence | ✅ | ✅ (with encryption) | ✅ | ✅ (proposed R1) | **Unanimous** |
| Retire Panel `Domain/` | ✅ | ✅ | ✅ | ✅ (proposed R1) | **Unanimous** |
| `Abstractions/` subfolder | ✅ | ✅ | ✅ | ✅ (proposed R1) | **Unanimous** |
| `Infrastructure/` (≥3 files) | ✅ | ✅ | ✅ | ✅ (accepted density rule) | **Unanimous** |
| `Startup/` folder | ❌ | — | ✅→❌ (conceded R2) | ❌ | **Rejected** (file at root instead) |
| `Security/` every feature | ❌ | ✅→❌ (conceded R2) | ❌ | ❌ | **Rejected** |
| `ServiceCollectionExtensions.cs` | ✅ | ✅ | ✅ | ✅ | **Unanimous** |
| Mirror src/ in tests/ | ✅ | ✅ | ✅ | ✅ (proposed R1) | **Unanimous** |

---

> *"The best folder structure is the one where a new contributor opens the solution and knows where to put their code in under 10 seconds."* — Devil's Advocate
>
> *"Folder conventions are suggestions. Assembly boundaries are laws."* — Software Architect
>
> *"No folder structure is best-in-class if `Process.Start` can be called from 10+ locations without a centralized gate. Structure the folders however you want — but add the guard, or the structure is decorative."* — Security Expert
>
> *"Ship the structure. Then write the tests."* — Devil's Advocate (closing statement)
