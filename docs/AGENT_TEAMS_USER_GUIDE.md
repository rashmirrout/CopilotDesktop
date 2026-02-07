<p align="center">
  <img src="../src/CopilotAgent.App/Resources/app.png" alt="CopilotDesktop Logo" width="80" />
</p>

<h1 align="center">Agent Teams — User Guide</h1>

<p align="center">
  <strong>CopilotDesktop v2.0</strong> · Multi-Agent Orchestration for Enterprise Productivity<br/>
  <em>Divide complex tasks across specialised AI agents that plan, execute, and deliver — in parallel.</em>
</p>

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Purpose & Value Proposition](#2-purpose--value-proposition)
3. [Technical Architecture Brief](#3-technical-architecture-brief)
4. [Getting Started](#4-getting-started)
5. [The Orchestration Lifecycle](#5-the-orchestration-lifecycle)
6. [Feature Reference](#6-feature-reference)
   - 6.1 [Task Submission](#61-task-submission)
   - 6.2 [Clarification Dialog](#62-clarification-dialog)
   - 6.3 [Plan Review & Approval](#63-plan-review--approval)
   - 6.4 [Parallel Execution & Worker Monitoring](#64-parallel-execution--worker-monitoring)
   - 6.5 [Live Instruction Injection](#65-live-instruction-injection)
   - 6.6 [Consolidated Report & Next Steps](#66-consolidated-report--next-steps)
   - 6.7 [Settings Panel](#67-settings-panel)
   - 6.8 [Session Health Indicator](#68-session-health-indicator)
   - 6.9 [Copy & Export](#69-copy--export)
   - 6.10 [Cancel & Reset](#610-cancel--reset)
7. [UI Visual Guide](#7-ui-visual-guide)
8. [Workspace Strategies Explained](#8-workspace-strategies-explained)
9. [Agent Roles & Specialisations](#9-agent-roles--specialisations)
10. [Worker Status Reference](#10-worker-status-reference)
11. [Orchestration Phase Reference](#11-orchestration-phase-reference)
12. [Worked Example — End-to-End Walkthrough](#12-worked-example--end-to-end-walkthrough)
13. [Worked Example — Parallel Agent Collaboration](#13-worked-example--parallel-agent-collaboration)
14. [Best Practices & Tips](#14-best-practices--tips)
15. [Troubleshooting](#15-troubleshooting)
16. [Glossary](#16-glossary)
17. [Frequently Asked Questions](#17-frequently-asked-questions)
18. [Appendix — Keyboard Shortcuts](#18-appendix--keyboard-shortcuts)

---

## 1. Introduction

**Agent Teams** is the multi-agent orchestration engine within CopilotDesktop. It transforms how you work with AI by letting you describe a complex task in natural language and have it automatically decomposed into smaller work chunks, each assigned to a specialised AI worker agent. Workers execute in parallel — isolated from each other — and their results are synthesised into a single, coherent deliverable.

Think of it as a **virtual engineering squad** you manage from a single panel: you set the objective, review the plan, approve it, monitor live progress, and receive a consolidated report when the team is done.

> **Who is this for?**
> Agent Teams is designed for software developers, architects, QA engineers, DevOps professionals, and technical leads who need to accomplish multi-faceted tasks — code reviews across multiple files, parallel test generation, large-scale refactoring, documentation authoring, performance analysis, and more.

---

## 2. Purpose & Value Proposition

### The Problem

Traditional single-agent AI assistants process tasks sequentially. When you ask one agent to "review the authentication module, write unit tests, and update the documentation," it must do everything step by step. For complex codebases, this is slow, context-limited, and error-prone.

### The Solution

Agent Teams introduces a **divide-and-conquer paradigm**:

| Capability | Benefit |
|---|---|
| **Automatic task decomposition** | An orchestrator LLM analyses your request and breaks it into independent, dependency-aware work chunks |
| **Parallel execution** | Up to 8 worker agents run simultaneously, each in an isolated workspace |
| **Specialised roles** | Workers are assigned roles (Code Analysis, Testing, Performance, etc.) with tailored system prompts |
| **Human-in-the-loop** | You review and approve the plan before any execution begins |
| **Fault tolerance** | Configurable retry policies and automatic skip/abort for failed dependencies |
| **Consolidated reporting** | Results from all workers are synthesised into a single conversational summary with actionable next steps |

### Business Impact

- **3–8× faster** completion of multi-file, multi-concern tasks
- **Higher quality** through specialised agent roles and isolated workspaces
- **Reduced risk** via plan approval and cancellation controls
- **Full transparency** with real-time worker status, session health, and detailed reports

---

## 3. Technical Architecture Brief

Agent Teams is built on a modular, event-driven orchestration architecture:

```
┌─────────────────────────────────────────────────────┐
│                    USER INTERFACE                     │
│              AgentTeamView (WPF/XAML)                │
│   Task Input → Plan Review → Worker Grid → Report   │
└──────────────────────┬──────────────────────────────┘
                       │ MVVM Binding
┌──────────────────────▼──────────────────────────────┐
│              AgentTeamViewModel                       │
│     State Machine · Commands · UI Orchestration      │
└──────────────────────┬──────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────┐
│             OrchestratorService                       │
│   Phase Management · Event Dispatch · Coordination   │
├──────────────────────────────────────────────────────┤
│  LlmTaskDecomposer  │  DependencyScheduler           │
│  AgentPool           │  WorkerAgent(s)                │
│  ResultAggregator    │  ApprovalQueue                 │
├──────────────────────────────────────────────────────┤
│            Workspace Strategies                       │
│  GitWorktree  │  FileLocking  │  InMemory             │
└──────────────────────────────────────────────────────┘
```

**Key components:**

| Component | Responsibility |
|---|---|
| **OrchestratorService** | Manages the full lifecycle from task submission to report generation |
| **LlmTaskDecomposer** | Sends the task to an LLM that produces a structured plan of work chunks |
| **DependencyScheduler** | Resolves inter-chunk dependencies and determines execution order |
| **AgentPool** | Manages a pool of concurrent worker agents |
| **WorkerAgent** | Executes a single work chunk in an isolated workspace |
| **ResultAggregator** | Collects all worker results and produces a consolidated report |
| **Workspace Strategies** | Provide file/branch isolation between workers (Git Worktree, File Locking, In-Memory) |

**Technology stack:** .NET 8 · WPF · CommunityToolkit.Mvvm · Copilot SDK · Emoji.Wpf · MdXaml

---

## 4. Getting Started

### Prerequisites

1. CopilotDesktop is installed and running
2. A valid Copilot session is active (check the **Session** tab for a green health indicator)
3. Your working directory is set to the project you want to work on

### Accessing Agent Teams

1. Open CopilotDesktop
2. Click the **🤖 Agent Team** tab in the main navigation bar
3. You will see the Agent Teams panel with:
   - A **task input** area at the top
   - A **settings** gear icon (⚙) in the header
   - A **team status** indicator showing `💤 Waiting for task`
   - A **session health** dot in the top-right corner

### Your First Task

1. Type a task description in the input box, e.g.:
   ```
   Review the authentication module for security vulnerabilities,
   write unit tests for the user service, and update the API
   documentation for the login endpoint.
   ```
2. Press **Enter** or click the **Submit** button (➤)
3. The orchestrator will analyse your task and either:
   - **Ask clarifying questions** (if the task is ambiguous), or
   - **Present a plan** for your review and approval

---

## 5. The Orchestration Lifecycle

Every Agent Teams task follows a predictable, transparent lifecycle:

```
    ┌───────┐
    │ IDLE  │ ◄── Initial state / after reset
    └───┬───┘
        │ User submits task
        ▼
  ┌───────────┐
  │ CLARIFYING│ ◄── Orchestrator may ask questions (optional)
  └─────┬─────┘
        │ Task is clear
        ▼
  ┌──────────┐
  │ PLANNING │ ◄── LLM decomposes task into work chunks
  └─────┬────┘
        │ Plan generated
        ▼
┌─────────────────┐
│AWAITING APPROVAL│ ◄── User reviews the plan
└────────┬────────┘
         │ User approves
         ▼
   ┌───────────┐
   │ EXECUTING │ ◄── Workers run in parallel
   └─────┬─────┘
         │ All workers done
         ▼
  ┌─────────────┐
  │ AGGREGATING │ ◄── Results synthesised
  └──────┬──────┘
         │ Report ready
         ▼
   ┌───────────┐
   │ COMPLETED │ ◄── Consolidated report shown
   └───────────┘
```

At any point during execution, you can **cancel** the orchestration. If you **reject** the plan, the orchestration returns to **Idle**.

---

## 6. Feature Reference

### 6.1 Task Submission

The task input area is the starting point for every orchestration.

**How to use:**
1. Type your task description in the text box at the top of the Agent Team panel
2. Be as detailed as possible — include file names, module names, and specific requirements
3. Press **Enter** or click the **➤ Submit** button

**Tips for effective task descriptions:**
- ✅ `"Refactor the PaymentService to use the Strategy pattern, add unit tests for each payment provider, and update the class diagram in docs/"`
- ✅ `"Analyse src/api/ for performance bottlenecks, profile the database queries in UserRepository, and suggest caching improvements"`
- ❌ `"Fix the code"` (too vague — the orchestrator will ask for clarification)

**Input behaviour:**
- The input box supports multi-line text
- While an orchestration is in progress, the input box is disabled
- During the clarification phase, a separate response input appears

### 6.2 Clarification Dialog

If the orchestrator determines your task is ambiguous or lacks critical details, it will enter the **Clarifying** phase.

**What you'll see:**
- The orchestrator's question displayed in a styled message area with a purple 🤖 header
- A **clarification response** text box below the question
- A **Respond** button to submit your answer

**How to use:**
1. Read the orchestrator's question carefully
2. Type your response in the clarification input
3. Click **Respond** or press **Enter**
4. The orchestrator may ask additional questions or proceed to planning

**Example:**
> **Orchestrator:** "You mentioned 'update the tests.' Could you specify which test framework you're using (xUnit, NUnit, MSTest) and whether you want integration tests as well?"
>
> **You:** "We use xUnit. Only unit tests are needed — no integration tests."

### 6.3 Plan Review & Approval

After understanding your task, the orchestrator generates an **Orchestration Plan** — a structured breakdown of work chunks with dependencies.

**What you'll see:**
- The plan displayed as a Markdown-rendered document in the orchestrator response area
- Each work chunk listed with:
  - **Title** and description
  - **Assigned role** (e.g., CodeAnalysis, Testing)
  - **Dependencies** (which chunks must complete first)
  - **Complexity** estimate (Low / Medium / High)
- Three action buttons:

| Button | Action | Effect |
|---|---|---|
| **✅ Approve** | Accept the plan as-is | Execution begins immediately |
| **✏ Request Changes** | Ask for modifications | Returns to clarification with your feedback |
| **❌ Reject** | Discard the plan entirely | Returns to Idle state |

**Best practices:**
- Review the dependency order — ensure no circular logic
- Check that the right roles are assigned (e.g., Testing role for test-writing chunks)
- If a chunk seems too broad, request the orchestrator split it further
- If unnecessary work is included, request its removal

### 6.4 Parallel Execution & Worker Monitoring

Once you approve the plan, the **Executing** phase begins. This is where Agent Teams demonstrates its full power.

**What you'll see:**
- The **Team Status** header updates to `⚙ Coordinating X/Y Active` with a green rotating animation
- A **Worker Grid** appears, showing each work chunk as a card:
  - **Chunk title** and sequence number
  - **Assigned role** badge (colour-coded)
  - **Status pill** — real-time status with colour indicator
  - **Progress** information

**Worker status pills and their colours:**

| Status | Colour | Meaning |
|---|---|---|
| 🔵 **Pending** | Gray | Not yet scheduled |
| 🔵 **Queued** | Blue | Ready to execute, waiting for a pool slot |
| ⏳ **Waiting** | Amber | Blocked on an upstream dependency |
| 🟢 **Running** | Green (pulsing) | Actively executing |
| ✅ **Succeeded** | Green | Completed successfully |
| ❌ **Failed** | Red | Execution failed |
| 🔄 **Retrying** | Orange | Failed and retrying (based on retry policy) |
| ⛔ **Aborted** | Dark Red | Permanently failed (max retries exceeded) |
| ⏭ **Skipped** | Gray | Skipped due to upstream failure |

**Parallel execution in action:**
- Workers without dependencies start immediately (up to the configured parallel limit)
- As workers complete, dependent workers are automatically unblocked
- The dependency scheduler ensures correct execution order at all times
- You can watch workers transition through states in real time

### 6.5 Live Instruction Injection

During the **Executing** phase, you can send additional instructions to the orchestrator without cancelling the current run.

**How to use:**
1. During execution, a **"Send instruction to team"** input appears at the bottom
2. Type your instruction (e.g., `"Prioritise the testing chunk — skip the documentation chunk if time is short"`)
3. Click **Inject** or press **Enter**
4. The orchestrator receives your instruction and can adjust worker behaviour accordingly

**Use cases:**
- Reprioritise remaining work
- Provide additional context that wasn't in the original task
- Request early termination of specific chunks
- Add new requirements discovered during execution

### 6.6 Consolidated Report & Next Steps

When all workers complete (or are skipped/aborted), the orchestrator enters the **Aggregating** phase and produces a **Consolidated Report**.

**What you'll see:**
- A Markdown-rendered report containing:
  - **Conversational summary** of what was accomplished
  - **Per-worker results** with details on each chunk's outcome
  - **Orchestration statistics:**
    - Total chunks, succeeded, failed, retried, skipped
    - Total execution duration
    - Total tokens used
  - **Recommended Next Steps** — actionable follow-up items

**Next Steps feature:**
- Next steps are displayed as clickable buttons below the report
- Each button represents a recommended follow-up action (e.g., "Run unit tests", "Deploy to staging", "Review the generated code")
- Clicking a next step button **automatically pre-fills the task input** with that action, ready for a new orchestration
- This enables **chained workflows** — completing one task flows naturally into the next

**Copy functionality:**
- A **📋 Copy** button appears at the bottom-right of the report area
- Click to copy the full report text to your clipboard for sharing or documentation

### 6.7 Settings Panel

The settings panel lets you configure the orchestration engine to match your project's needs and constraints.

**Accessing settings:**
- Click the **⚙** (gear) icon in the Agent Team header
- The settings panel slides open inline

**Available settings:**

| Setting | Options | Default | Description |
|---|---|---|---|
| **Parallel Workers** | 1, 2, 3, 5, 8 | 3 | Maximum number of workers executing simultaneously |
| **Workspace Strategy** | InMemory, FileLocking, GitWorktree | GitWorktree | How file isolation is managed between workers (see [§8](#8-workspace-strategies-explained)) |
| **Worker Timeout** | 1–60 minutes | 10 min | Maximum time a single worker can run before timeout |
| **Max Retries** | 0–5 | 2 | Number of retry attempts for failed workers |
| **Retry Delay** | 1–60 seconds | 5 sec | Delay between retry attempts |
| **Auto-Approve Read-Only Tools** | On/Off | On | Automatically approve tool calls that only read data (no writes) |

**Additional settings actions:**
- **Restore Defaults** — resets all settings to their factory values

**When to change settings:**
- Increase **Parallel Workers** for large tasks with many independent chunks
- Decrease **Parallel Workers** if you notice resource contention or rate limiting
- Switch to **InMemory** strategy for read-only analysis tasks (faster, no file conflicts)
- Increase **Worker Timeout** for complex chunks that require extensive computation
- Set **Max Retries** to 0 for deterministic tasks where retries won't help

### 6.8 Session Health Indicator

A colour-coded health dot in the top-right area of the Agent Team panel shows the real-time status of your Copilot connection.

| Indicator | Colour | Meaning |
|---|---|---|
| **IDLE** | ⚪ Gray | No active session, ready to start |
| **WAITING** | 🟡 Yellow | Connecting or waiting for response |
| **LIVE** | 🟢 Green (blinking) | Active and streaming data |
| **DISCONNECTED** | 🔴 Red | Connection lost — check your network |
| **ERROR** | 🔴 Red | An error occurred — see error details |

**What to do if health shows red:**
1. Check your internet connection
2. Verify your Copilot subscription is active
3. Try resetting the orchestrator (Reset button)
4. Switch to the Session tab to check session details

### 6.9 Copy & Export

Agent Teams provides copy functionality at two key points:

1. **Orchestrator Message** — During clarification or plan review, a 📋 **Copy** button at the bottom-right of the orchestrator's response lets you copy the message text
2. **Completion Report** — After orchestration completes, the report area has its own 📋 **Copy** button to copy the full consolidated report

Both copy actions place plain text on your system clipboard, suitable for pasting into documents, emails, chat messages, or issue trackers.

### 6.10 Cancel & Reset

**Cancel (during execution):**
- A red **✕ Cancel** button appears during active orchestration
- Clicking it immediately stops all running workers and transitions to **Idle**
- Work already completed by finished workers is preserved in memory
- Partially completed work from in-progress workers is discarded

**Reset:**
- The **Reset** button (↻) clears all state and returns to a fresh Idle state
- Use this after a completed orchestration to start a new task
- Also useful if the UI gets into an unexpected state

---

## 7. UI Visual Guide

The Agent Teams panel is divided into several visual zones:

```
┌──────────────────────────────────────────────────────────┐
│  🤖 Agent Team          ⚙ Settings    🟢 LIVE            │  ← Header Bar
├──────────────────────────────────────────────────────────┤
│  ⚙ Coordinating 3/5 Active                   ✕ Cancel   │  ← Team Status Bar
├──────────────────────────────────────────────────────────┤
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │  🤖 Orchestrator                                  │   │
│  │                                                    │   │
│  │  Here is my plan for your task:                    │   │  ← Orchestrator
│  │  1. Chunk A: Analyse auth module (CodeAnalysis)    │   │     Response Area
│  │  2. Chunk B: Write unit tests (Testing)            │   │
│  │  3. Chunk C: Update docs (Implementation)          │   │
│  │                                                    │   │
│  │                                          📋 Copy   │   │
│  └──────────────────────────────────────────────────┘   │
│                                                          │
│  ┌─ Workers ─────────────────────────────────────────┐  │
│  │  [1] Analyse auth     CodeAnalysis  ✅ Succeeded   │  │
│  │  [2] Write tests      Testing       🟢 Running     │  │  ← Worker Grid
│  │  [3] Update docs      Implementation ⏳ Waiting     │  │
│  └───────────────────────────────────────────────────┘  │
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │  Type your task...                          ➤     │   │  ← Task Input
│  └──────────────────────────────────────────────────┘   │
│                                                          │
│  ✅ Approve    ✏ Request Changes    ❌ Reject            │  ← Plan Actions
│                                                          │
│  Send instruction to team...                   💉       │  ← Instruction Inject
└──────────────────────────────────────────────────────────┘
```

**Visual feedback cues:**
- **Pulsing green dot** — Worker actively running
- **Rotating gear** — Team is in executing phase
- **Purple badge** — Planning phase active
- **Amber badge** — Clarification needed
- **Blue badge** — Awaiting your approval
- **Teal badge** — Aggregating results
- **Colour-coded status pills** — Instant visual read of each worker's state

---

## 8. Workspace Strategies Explained

Workspace strategies determine how worker agents are isolated from each other to prevent file conflicts.

### GitWorktree (Recommended for code changes)

```
main branch ──┬── worktree-chunk-1/  (Worker 1 writes here)
              ├── worktree-chunk-2/  (Worker 2 writes here)
              └── worktree-chunk-3/  (Worker 3 writes here)
```

- Creates a separate **Git worktree** (branch) for each worker
- Full file isolation — workers cannot interfere with each other
- Changes can be merged back via standard Git operations
- **Best for:** Code modifications, refactoring, implementation tasks
- **Requires:** A Git repository as the working directory

### FileLocking (For shared-directory work)

- Workers share the same directory but acquire **file-level locks**
- A worker must obtain a lock before modifying a file
- Prevents simultaneous writes to the same file
- **Best for:** Configuration changes, file updates in non-Git projects
- **Trade-off:** Potential contention if multiple workers need the same files

### InMemory (For read-only analysis)

- Workers operate on an **in-memory read-only snapshot** of the workspace
- No file system writes — fastest execution
- **Best for:** Code review, security audit, documentation analysis, performance assessment
- **Trade-off:** Cannot make actual file changes

### Choosing the Right Strategy

| Task Type | Recommended Strategy |
|---|---|
| Multi-file code implementation | **GitWorktree** |
| Refactoring across modules | **GitWorktree** |
| Code review / security audit | **InMemory** |
| Performance analysis | **InMemory** |
| Test generation | **GitWorktree** |
| Configuration updates | **FileLocking** |
| Documentation review | **InMemory** |

---

## 9. Agent Roles & Specialisations

Each worker agent is assigned a **role** that shapes its behaviour, expertise, and system prompt. The orchestrator selects roles automatically based on the nature of each work chunk.

| Role | Icon | Specialisation | Typical Tasks |
|---|---|---|---|
| **Generic** | 🤖 | General-purpose AI assistant | Simple, unspecialised tasks |
| **Planning** | 🧠 | Architecture and design planning | System design, architecture review, roadmap planning |
| **CodeAnalysis** | 🔍 | Code review and static analysis | Bug detection, code smell identification, security review |
| **MemoryDiagnostics** | 💾 | Memory and resource analysis | Memory leak detection, resource usage profiling |
| **Performance** | ⚡ | Performance optimisation | Bottleneck analysis, query optimisation, caching strategy |
| **Testing** | 🧪 | Test authoring and validation | Unit tests, integration tests, test coverage analysis |
| **Implementation** | 🔨 | Code implementation and changes | Feature implementation, refactoring, bug fixes |
| **Synthesis** | 📊 | Result aggregation and reporting | Combining results, generating summaries, final reports |

**How role assignment works:**
1. The orchestrator LLM analyses each work chunk's description and requirements
2. It assigns the most appropriate role based on the chunk's nature
3. The assigned role configures the worker's system prompt for optimal performance
4. You can see the assigned role in the worker grid during execution

---

## 10. Worker Status Reference

Workers transition through a well-defined lifecycle during execution:

```
                    ┌─────────┐
                    │ Pending │
                    └────┬────┘
                         │
              ┌──────────┼──────────┐
              ▼                     ▼
  ┌───────────────────┐      ┌──────────┐
  │WaitingForDependency│      │  Queued  │
  └─────────┬─────────┘      └────┬─────┘
            │ deps met              │ pool slot available
            └──────────┬───────────┘
                       ▼
                 ┌──────────┐
                 │ Running  │
                 └────┬─────┘
                      │
           ┌──────────┼──────────┐
           ▼          ▼          ▼
     ┌───────────┐ ┌────────┐ ┌─────────┐
     │ Succeeded │ │ Failed │ │ Aborted │
     └───────────┘ └───┬────┘ └─────────┘
                       │
                       ▼ (if retries remain)
                 ┌──────────┐
                 │ Retrying │──► Running
                 └──────────┘

    (Upstream failure) ──► Skipped
```

| Status | Description | User Action Needed? |
|---|---|---|
| **Pending** | Chunk created, not yet scheduled | No — wait |
| **WaitingForDependencies** | Blocked until upstream chunks finish | No — automatic |
| **Queued** | Dependencies met, waiting for an available worker slot | No — automatic |
| **Running** | Worker is actively processing | No — monitor progress |
| **Succeeded** | Chunk completed successfully | No — review results in report |
| **Failed** | Worker encountered an error | Automatic retry (if configured) or review error |
| **Retrying** | Worker is re-attempting after failure | No — monitor |
| **Aborted** | Max retries exceeded | Review report for failure details |
| **Skipped** | Upstream dependency failed or was aborted | Review which upstream chunk failed |

---

## 11. Orchestration Phase Reference

| Phase | UI Badge | Colour | Description |
|---|---|---|---|
| **Idle** | 💤 Waiting for task | Gray | No active task. Ready for input. |
| **Clarifying** | ❓ Clarifying | Amber | Orchestrator is asking you questions to understand the task |
| **Planning** | 🧠 Planning | Purple | LLM is decomposing the task into work chunks |
| **AwaitingApproval** | 📋 Awaiting Approval | Blue | Plan is ready for your review |
| **Executing** | ⚙ Coordinating | Green | Workers are running in parallel |
| **Aggregating** | 📊 Aggregating | Teal | Collecting and synthesising worker results |
| **Completed** | ✅ Completed | Light Green | Report is ready |
| **Cancelled** | — | — | User cancelled the orchestration |

---

## 12. Worked Example — End-to-End Walkthrough

### Scenario
You have a .NET 8 web API project and want to improve the `UserService` class — fix a known bug, add tests, and update the Swagger documentation.

---

**Step 1 — Submit the Task**

You type:
```
Fix the null reference exception in UserService.GetUserById when the user is
not found (should return 404 instead of crashing). Add xUnit tests for all
public methods of UserService. Update the Swagger XML documentation for the
Users controller endpoints.
```

**Step 2 — Orchestrator Plans**

The team status changes to `🧠 Planning` (purple). After a few seconds, the orchestrator presents its plan:

> **🤖 Orchestrator Plan**
>
> I've analysed your request and created the following execution plan:
>
> **Chunk 1 — Fix Null Reference Bug** `[Implementation]`
> - Modify `UserService.GetUserById` to handle the null case
> - Return appropriate 404 response instead of throwing
> - Complexity: Low
> - Dependencies: None
>
> **Chunk 2 — Write xUnit Tests** `[Testing]`
> - Create comprehensive unit tests for all public methods
> - Include tests for the null-user scenario (regression test for the fix)
> - Complexity: Medium
> - Dependencies: Chunk 1 (needs the fix to be in place for test verification)
>
> **Chunk 3 — Update Swagger Documentation** `[Implementation]`
> - Add/update XML comments on Users controller endpoints
> - Include response type annotations for 200, 404, 500
> - Complexity: Low
> - Dependencies: None

**Step 3 — Review & Approve**

You see that Chunk 3 (docs) has no dependency on Chunk 1, meaning it will run **in parallel** with the bug fix. Chunk 2 (tests) depends on Chunk 1, so it will start only after the fix is done. This makes sense.

You click **✅ Approve**.

**Step 4 — Monitor Parallel Execution**

The team status changes to `⚙ Coordinating 2/3 Active` (green, rotating).

The worker grid shows:
| # | Chunk | Role | Status |
|---|---|---|---|
| 1 | Fix Null Reference Bug | 🔨 Implementation | 🟢 Running |
| 2 | Write xUnit Tests | 🧪 Testing | ⏳ Waiting |
| 3 | Update Swagger Documentation | 🔨 Implementation | 🟢 Running |

Chunks 1 and 3 start simultaneously. Chunk 2 waits for Chunk 1.

After Chunk 1 succeeds, the grid updates:
| # | Chunk | Role | Status |
|---|---|---|---|
| 1 | Fix Null Reference Bug | 🔨 Implementation | ✅ Succeeded |
| 2 | Write xUnit Tests | 🧪 Testing | 🟢 Running |
| 3 | Update Swagger Documentation | 🔨 Implementation | ✅ Succeeded |

**Step 5 — Receive Consolidated Report**

Once all workers complete, the phase transitions through `📊 Aggregating` → `✅ Completed`.

A rich Markdown report appears:

> **Consolidated Report**
>
> All 3 work chunks completed successfully.
>
> **Summary:**
> - **Bug Fix:** Modified `UserService.GetUserById` to check for null result from the repository and return `null` (mapped to HTTP 404 by the controller). The NullReferenceException is eliminated.
> - **Unit Tests:** Created `UserServiceTests.cs` with 12 test methods covering `GetUserById`, `CreateUser`, `UpdateUser`, and `DeleteUser`. Includes regression test for the null-user scenario.
> - **Documentation:** Updated XML comments on 5 controller endpoints with `<response>` tags for 200, 404, and 500 status codes.
>
> **Stats:** 3/3 succeeded · Duration: 1m 42s · Tokens: 8,240
>
> **Recommended Next Steps:**

Below the report, you see clickable buttons:
- `[Run unit tests]` `[Review code changes]` `[Build and verify]`

Clicking **Run unit tests** pre-fills the task input with "Run unit tests" for a follow-up orchestration.

---

## 13. Worked Example — Parallel Agent Collaboration

### Scenario
You need a comprehensive code quality assessment of a microservices project before a major release.

**Task submitted:**
```
Perform a full code quality audit of the OrderService microservice:
1. Security review of all API endpoints
2. Performance analysis of database queries
3. Memory leak detection in the event handlers
4. Test coverage gap analysis
```

### How the Agents Collaborate in Parallel

**Plan generated (4 chunks, all independent):**

```
Chunk 1: Security Review        [CodeAnalysis]  → No dependencies
Chunk 2: Performance Analysis   [Performance]   → No dependencies
Chunk 3: Memory Diagnostics     [MemoryDiag]    → No dependencies
Chunk 4: Test Coverage Analysis  [Testing]       → No dependencies
```

Since all 4 chunks are independent, with **Parallel Workers = 5** and **Workspace Strategy = InMemory** (read-only analysis), all 4 agents launch simultaneously:

```
Time ─────────────────────────────────────────────────────►

Worker 1 (CodeAnalysis):   ████████████████░░░░░░░░░░░ ✅ 45s
Worker 2 (Performance):    ██████████████████████░░░░░░ ✅ 58s
Worker 3 (MemoryDiag):     ████████████████████████░░░░ ✅ 1m 05s
Worker 4 (Testing):        ████████████████░░░░░░░░░░░ ✅ 47s

Aggregation:               ░░░░░░░░░░░░░░░░░░░░░░░░████ ✅ 12s
                                                    ▲
                                        All done, synthesis starts
```

**What each specialised agent does:**

| Agent | Role | Analysis Performed |
|---|---|---|
| Worker 1 | 🔍 CodeAnalysis | Scans all API endpoints for injection vulnerabilities, improper authentication checks, missing input validation, CORS misconfigurations |
| Worker 2 | ⚡ Performance | Analyses SQL queries for N+1 problems, missing indices, unnecessary eager loading, identifies slow endpoints |
| Worker 3 | 💾 MemoryDiagnostics | Reviews event handler registrations for missing unsubscriptions, identifies potential memory leaks from disposable objects, checks for circular references |
| Worker 4 | 🧪 Testing | Maps existing tests to source methods, calculates coverage percentages, identifies untested critical paths and edge cases |

**Consolidated Report excerpt:**

> **Security Review** found 3 issues:
> - SQL injection risk in `OrderRepository.Search()` (HIGH)
> - Missing `[Authorize]` on `GET /orders/export` (MEDIUM)
> - CORS allows wildcard origin in production config (LOW)
>
> **Performance Analysis** found 2 bottlenecks:
> - N+1 query in `OrderService.GetOrdersWithItems()` — suggest eager loading (HIGH)
> - Missing index on `Orders.CustomerId` column (MEDIUM)
>
> **Memory Diagnostics** found 1 issue:
> - `EventBus` handler in `NotificationService` never unsubscribes on dispose (MEDIUM)
>
> **Test Coverage** gaps identified:
> - `OrderService.CancelOrder()` — 0% coverage (critical business logic)
> - `PaymentProcessor.Refund()` — only happy path tested
> - Overall coverage estimate: 62% (target: 80%)
>
> **Next Steps:**

Buttons: `[Fix SQL injection in OrderRepository]` `[Add [Authorize] to export endpoint]` `[Fix N+1 query]` `[Add tests for CancelOrder]`

This demonstrates how 4 specialised agents working **simultaneously** can complete in ~1 minute what would take a single sequential agent 4–5 minutes.

---

## 14. Best Practices & Tips

### Task Description

| Do | Don't |
|---|---|
| Be specific about files, modules, and technologies | Use vague language like "fix everything" |
| Include acceptance criteria | Assume the AI knows your project conventions |
| Mention constraints (framework, patterns, naming) | Leave ambiguity about scope |
| Reference specific file paths when relevant | Submit tasks unrelated to your working directory |

### Settings Optimisation

| Scenario | Parallel Workers | Strategy | Timeout |
|---|---|---|---|
| Quick code review (3–5 files) | 3 | InMemory | 5 min |
| Large refactoring (10+ files) | 5 | GitWorktree | 15 min |
| Test generation suite | 3 | GitWorktree | 10 min |
| Read-only security audit | 5–8 | InMemory | 10 min |
| Single complex implementation | 1–2 | GitWorktree | 20 min |

### Plan Review

- **Always review the plan** before approving — this is your quality gate
- Check that **dependencies make logical sense** (e.g., tests depending on implementation)
- If chunks are too granular, request consolidation
- If chunks are too broad, request decomposition
- Use **Request Changes** liberally — it's cheaper to adjust the plan than to re-run

### During Execution

- Monitor the **session health indicator** — a red dot means connectivity issues
- Use **instruction injection** sparingly and clearly — it's a powerful override
- Let workers complete unless there's a critical reason to cancel
- Workers with the **Retrying** status may succeed on the next attempt — be patient

### After Completion

- Read the **full report** before acting on next steps
- Use the **Copy** button to save the report for team documentation
- Click **Next Steps** buttons to chain into follow-up tasks
- **Reset** the orchestrator before starting an unrelated task

---

## 15. Troubleshooting

### Common Issues

| Symptom | Likely Cause | Resolution |
|---|---|---|
| Task submission does nothing | Session not connected | Check session health; reconnect if red |
| Plan never appears | LLM timeout | Increase orchestrator timeout; check network |
| All workers fail immediately | Invalid working directory | Verify the session's working directory exists |
| Workers stuck in "Waiting" | Upstream dependency failed | Check which chunk failed; review retry settings |
| Report is empty | Aggregation error | Reset and re-submit; check logs |
| Settings won't save | UI state conflict | Close settings panel and reopen |
| Health shows DISCONNECTED | Network/auth issue | Check internet; re-authenticate Copilot |

### Performance Considerations

- **High parallel worker count** (5–8) increases speed but also increases API rate limit pressure and resource usage
- **GitWorktree strategy** requires disk space for each worktree
- **Complex tasks** with many dependencies may not benefit from high parallelism if most chunks are sequential
- **Worker timeout** should be generous enough for the task complexity but not so long that stuck workers waste time

### Error Recovery

1. If an orchestration fails mid-execution:
   - Worker results that completed successfully are preserved in the report
   - Failed/aborted chunks are documented with error details
   - You can copy the partial report and start a new orchestration for the remaining work

2. If the UI becomes unresponsive:
   - Click **Reset** to clear all orchestration state
   - If Reset doesn't work, switch to another tab and back
   - As a last resort, restart CopilotDesktop

---

## 16. Glossary

| Term | Definition |
|---|---|
| **Orchestrator** | The central LLM-powered coordinator that decomposes tasks, generates plans, and synthesises results |
| **Worker Agent** | An individual AI agent that executes a single work chunk |
| **Work Chunk** | An atomic unit of work derived from task decomposition; has a title, prompt, role, and dependencies |
| **Orchestration Plan** | A structured breakdown of a task into ordered, dependency-aware work chunks |
| **Dependency** | A relationship between chunks — chunk B depends on chunk A means B cannot start until A succeeds |
| **Agent Pool** | The managed pool of concurrent worker agents (size = Parallel Workers setting) |
| **Workspace Strategy** | The isolation mechanism used to prevent file conflicts between parallel workers |
| **Consolidated Report** | The final synthesised output combining all worker results into a coherent summary |
| **Next Steps** | Recommended follow-up actions parsed from the completion report, displayed as clickable buttons |
| **Instruction Injection** | The ability to send additional guidance to the orchestrator during active execution |
| **Session Health** | Real-time indicator of the connection status between CopilotDesktop and the Copilot backend |
| **Plan Approval** | The human-in-the-loop checkpoint where you review and approve/modify/reject the orchestration plan |
| **Retry Policy** | Configuration for how many times and how long to wait before retrying a failed worker |
| **Phase** | A discrete stage in the orchestration lifecycle (Idle → Clarifying → Planning → AwaitingApproval → Executing → Aggregating → Completed) |

---

## 17. Frequently Asked Questions

**Q: Can I use Agent Teams for non-coding tasks?**
A: Yes. While Agent Teams is optimised for software engineering workflows, it can decompose any complex task — documentation writing, research analysis, data processing, and more. Use the **InMemory** workspace strategy for non-file-modifying tasks.

**Q: What happens if one worker fails?**
A: The system follows your configured retry policy. If retries are exhausted, the chunk is marked **Aborted**. Chunks that depend on the aborted chunk are automatically **Skipped**. Independent chunks continue executing normally. The final report includes details about all failures.

**Q: Can I change settings during an active orchestration?**
A: Settings changes apply to the **next** orchestration. They do not affect an in-progress run. This ensures consistency during execution.

**Q: How many workers should I use?**
A: Start with **3** (the default). Increase to 5–8 for tasks with many independent chunks. Use 1–2 for tasks where most chunks have sequential dependencies (parallelism won't help).

**Q: Is there a limit on task description length?**
A: There is no hard UI limit, but very long descriptions may exceed LLM context windows. Aim for clear, structured descriptions under ~2,000 words. For extremely complex tasks, consider breaking them into multiple orchestrations.

**Q: Can I run multiple orchestrations at the same time?**
A: Each Agent Team panel manages one orchestration at a time. Complete or cancel the current orchestration before starting a new one.

**Q: What is the difference between Cancel and Reset?**
A: **Cancel** stops all running workers and moves to Idle, preserving whatever state exists. **Reset** clears all state completely — plan, worker results, and report — returning to a fresh state.

**Q: Where are task logs stored?**
A: Task logs are stored locally in JSON format via the persistence service. They include full orchestration history, worker results, and timestamps.

**Q: Can I review the plan after approving it?**
A: The plan details are included in the consolidated report after completion. During execution, you can see individual chunk information in the worker grid.

---

## 18. Appendix — Keyboard Shortcuts

| Shortcut | Context | Action |
|---|---|---|
| **Enter** | Task input focused | Submit task |
| **Enter** | Clarification input focused | Send clarification response |
| **Enter** | Instruction input focused | Inject instruction |

---

<p align="center">
  <em>CopilotDesktop Agent Teams — Ship faster with AI-powered parallel orchestration.</em><br/>
  <strong>© 2026 CopilotDesktop. All rights reserved.</strong>
</p>