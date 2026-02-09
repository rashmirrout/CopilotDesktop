<p align="center">
  <img src="../src/CopilotAgent.App/Resources/app.png" alt="CopilotDesktop Logo" width="80" />
</p>

<h1 align="center">Agent Office — User Guide</h1>

<p align="center">
  <strong>CopilotDesktop v2.0</strong> · Autonomous Operations Center for Continuous AI-Driven Workflows<br/>
  <em>Set up your own AI office — a Manager that never sleeps, backed by a pool of Assistants that execute, report, and repeat.</em>
</p>

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Purpose & Value Proposition](#2-purpose--value-proposition)
3. [Technical Architecture Brief](#3-technical-architecture-brief)
4. [Getting Started](#4-getting-started)
5. [The Manager Lifecycle](#5-the-manager-lifecycle)
6. [Feature Reference](#6-feature-reference)
   - 6.1 [Master Prompt & Session Start](#61-master-prompt--session-start)
   - 6.2 [Clarification Dialog](#62-clarification-dialog)
   - 6.3 [Plan Review & Approval](#63-plan-review--approval)
   - 6.4 [The Iteration Loop](#64-the-iteration-loop)
   - 6.5 [Assistant Pool & Queue-Based Scheduling](#65-assistant-pool--queue-based-scheduling)
   - 6.6 [Rest Period & Countdown Timer](#66-rest-period--countdown-timer)
   - 6.7 [Instruction Injection (Mid-Run)](#67-instruction-injection-mid-run)
   - 6.8 [Clarification-Aware Injection](#68-clarification-aware-injection)
   - 6.9 [Iteration Reports & Aggregation](#69-iteration-reports--aggregation)
   - 6.10 [Side Panel — Live Commentary](#610-side-panel--live-commentary)
   - 6.11 [Side Panel — Configuration Controls](#611-side-panel--configuration-controls)
   - 6.12 [Side Panel — Event Log](#612-side-panel--event-log)
   - 6.13 [Side Panel — Iteration Statistics](#613-side-panel--iteration-statistics)
   - 6.14 [Pause, Resume & Stop](#614-pause-resume--stop)
   - 6.15 [Reset Session](#615-reset-session)
7. [UI Visual Guide](#7-ui-visual-guide)
8. [Manager Phase Reference](#8-manager-phase-reference)
9. [Assistant Task Status Reference](#9-assistant-task-status-reference)
10. [Live Commentary Indicators](#10-live-commentary-indicators)
11. [Chat Message Color Coding](#11-chat-message-color-coding)
12. [Worked Example — Incident Management](#12-worked-example--incident-management)
13. [Worked Example — Multi-Repository PR Review](#13-worked-example--multi-repository-pr-review)
14. [Worked Example — Mid-Run Instruction Injection](#14-worked-example--mid-run-instruction-injection)
15. [Best Practices & Tips](#15-best-practices--tips)
16. [How Agent Office Differs from Agent Team](#16-how-agent-office-differs-from-agent-team)
17. [Troubleshooting](#17-troubleshooting)
18. [Glossary](#18-glossary)
19. [Frequently Asked Questions](#19-frequently-asked-questions)
20. [Appendix — Keyboard Shortcuts](#20-appendix--keyboard-shortcuts)

---

## 1. Introduction

**Agent Office** is the autonomous operations center within CopilotDesktop. It lets you set up a long-running **Manager agent** that continuously monitors for events — incoming tickets, support requests, PR reviews, incident alerts, code quality issues — and delegates tasks to a pool of ephemeral **Assistant agents**. Assistants execute independently, report back, and are disposed. The Manager aggregates results, rests for a configurable interval, and repeats.

Think of it as **your own tireless AI ops team**: you define the mission, approve the plan, and let the office run. You can watch the live commentary, inject new priorities mid-run, change the check interval, pause, or resume — all without restarting.

> **Who is this for?**
> Agent Office is designed for developers, DevOps engineers, SREs, team leads, and support engineers who need continuous, periodic, autonomous workflows — incident monitoring, scheduled audits, multi-repo PR reviews, support ticket triage, and any task that repeats on a cadence.

---

## 2. Purpose & Value Proposition

### The Problem

Many operational workflows are repetitive and periodic: "Check for new incidents every 5 minutes," "Review new PRs across 5 repos every 30 minutes," "Scan for security vulnerabilities every hour." Running these manually is tedious. Running a single-shot AI agent each time requires constant re-initiation.

### The Solution

Agent Office introduces a **perpetual operations center** paradigm:

| Capability | Benefit |
|---|---|
| **Continuous Manager loop** | A long-running Manager agent checks for work on a cadence — no manual re-triggering |
| **Finite assistant pool with queue** | Up to N assistants run in parallel; overflow tasks queue and auto-dispatch as slots free |
| **Clarification-aware injection** | Inject new instructions mid-run; the Manager asks clarifying questions if needed before queuing |
| **Dynamic controls** | Change interval, pool size, pause, resume, or reset — all without restarting |
| **Iteration reports** | Each cycle produces a consolidated narrative report with statistics and recommendations |
| **Live commentary** | Watch what the Manager and every Assistant are doing in real time via the side panel |
| **Context continuity** | The Manager accumulates learnings across iterations, improving its decisions over time |

### Business Impact

- **Always-on monitoring** — incidents, PRs, tickets, audits handled as they arrive
- **Reduced response time** — events processed within minutes, not hours
- **Scalable delegation** — pool size adjusts to workload; queue absorbs spikes
- **Full transparency** — every scheduling decision, assistant action, and report is logged
- **Hands-free operation** — approve once, let it run; intervene only when needed

---

## 3. Technical Architecture Brief

Agent Office is built on a modular, event-driven architecture with a long-lived Manager and ephemeral Assistants:

```
┌─────────────────────────────────────────────────────┐
│                    USER INTERFACE                     │
│              OfficeView (WPF/XAML)                   │
│   Chat Plane → Side Panel → Status Bar → Input      │
└──────────────────────┬──────────────────────────────┘
                       │ MVVM Binding
┌──────────────────────▼──────────────────────────────┐
│              OfficeViewModel                         │
│     Event Handler · Commands · UI State              │
└──────────────────────┬──────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────┐
│          OfficeManagerService                         │
│   State Machine · Iteration Loop · Aggregation       │
├──────────────────────────────────────────────────────┤
│  AssistantPool       │  IterationScheduler            │
│  (SemaphoreSlim)     │  (Countdown Timer)             │
│  ┌─────┐┌─────┐┌─────┐                              │
│  │Asst1││Asst2││Asst3│  + Task Queue (overflow)     │
│  └─────┘└─────┘└─────┘                              │
├──────────────────────────────────────────────────────┤
│  OfficeEventLog      │  LiveCommentary Stream         │
└──────────────────────────────────────────────────────┘
```

**Key components:**

| Component | Responsibility |
|---|---|
| **OfficeManagerService** | Manager state machine: start → clarify → plan → approve → iterate (fetch → schedule → execute → aggregate → rest) |
| **AssistantPool** | Finite pool of concurrent assistants with SemaphoreSlim gating and queue-based overflow |
| **AssistantAgent** | Ephemeral Copilot session per task: spawn → work → report → dispose |
| **IterationScheduler** | Rest period countdown with dynamic interval changes and override support |
| **OfficeEventLog** | Structured log of every phase transition, scheduling decision, and assistant lifecycle event |

**Technology stack:** .NET 8 · WPF · CommunityToolkit.Mvvm · Copilot SDK · MdXaml · Emoji.Wpf

---

## 4. Getting Started

### Prerequisites

1. CopilotDesktop is installed and running
2. A valid Copilot session is active (check any tab for a green health indicator)
3. Optionally, MCP servers are configured for event sources (e.g., ServiceNow, GitHub, Azure DevOps)

### Accessing Agent Office

1. Open CopilotDesktop
2. Click the **🏢 Office** tab in the main navigation bar
3. You will see the Office panel with:
   - A **chat plane** (empty initially)
   - A **status bar** showing `💤 Idle`
   - A **text input area** at the bottom
   - A **📊** button in the status bar to open the side panel

### Your First Office Session

1. Type a master prompt in the input box, e.g.:
   ```
   Monitor open incidents for Team Alpha every 5 minutes. For P1/P2
   incidents, check the runbook and attempt remediation. For P3/P4,
   add a triage note. Report findings after each check.
   ```
2. Press **Enter** or click **Send**
3. The Manager will either:
   - **Ask clarifying questions** (if the prompt is ambiguous), or
   - **Present a plan** for your review and approval
4. After you approve, the Manager enters its continuous loop

---

## 5. The Manager Lifecycle

Every Agent Office session follows a structured lifecycle with two distinct phases: **setup** (one-time) and **loop** (repeating).

### Setup Phase (One-Time)

```
    ┌───────┐
    │ IDLE  │ ◄── Initial state / after reset
    └───┬───┘
        │ User submits master prompt
        ▼
  ┌───────────┐
  │ CLARIFYING│ ◄── Manager may ask questions (optional)
  └─────┬─────┘
        │ All questions answered
        ▼
  ┌──────────┐
  │ PLANNING │ ◄── Manager builds execution strategy
  └─────┬────┘
        │ Plan generated
        ▼
┌─────────────────┐
│AWAITING APPROVAL│ ◄── User reviews the plan
└────────┬────────┘
         │ User approves
         ▼
     [Enter Loop]
```

### Continuous Loop (Repeating)

```
  ┌──────────────────────────────────────────────────────┐
  │               ITERATION LOOP (repeats)                │
  │                                                       │
  │  ┌─────────────────┐                                 │
  │  │ FETCHING EVENTS  │ ◄── Manager queries sources    │
  │  └────────┬────────┘                                 │
  │           │                                           │
  │  ┌────────▼────────┐                                 │
  │  │   SCHEDULING     │ ◄── Decompose events → tasks   │
  │  └────────┬────────┘     Assign to pool, queue rest  │
  │           │                                           │
  │  ┌────────▼────────┐                                 │
  │  │    EXECUTING     │ ◄── Assistants working          │
  │  └────────┬────────┘     Queue drains as slots free  │
  │           │                                           │
  │  ┌────────▼────────┐                                 │
  │  │   AGGREGATING    │ ◄── Manager consolidates        │
  │  └────────┬────────┘                                 │
  │           │                                           │
  │  ┌────────▼────────┐                                 │
  │  │     RESTING      │ ◄── Countdown timer active     │
  │  └────────┬────────┘                                 │
  │           │ Timer elapsed                             │
  │           └──── Loop back to FETCHING EVENTS ────────┘
  │                                                       │
  └───────────────────────────────────────────────────────┘
```

At any point during the loop, you can:
- **Inject instructions** — absorbed on the next iteration
- **Change the interval** — takes effect immediately if resting
- **Pause** — enters extended rest with custom duration
- **Resume** — skips remaining rest, starts next iteration immediately
- **Stop** — waits for active assistants to finish, then stops
- **Reset** — cancels everything, disposes all sessions, returns to Idle

---

## 6. Feature Reference

### 6.1 Master Prompt & Session Start

The master prompt is the foundation of your Office session. It tells the Manager what to monitor, how to respond, and what to prioritize.

**How to use:**
1. Type your master prompt in the input area at the bottom of the Office panel
2. Be specific about:
   - **What** to monitor (incidents, PRs, branches, test results)
   - **Where** to look (which MCP servers, repos, directories)
   - **How often** to check (the interval is also configurable in the side panel)
   - **What to do** for each category of event (remediate, triage, escalate, report)
3. Press **Enter** or click **Send**

**Tips for effective master prompts:**
- ✅ `"Monitor ServiceNow incidents for Team Alpha every 5 minutes. P1/P2: check runbook, attempt remediation, escalate if unresolved. P3/P4: add triage note."`
- ✅ `"Scan the working directory for TODO comments, failing tests, and recent security-related commits every 30 minutes. Produce a summary report."`
- ✅ `"Review new PRs across all repos in the 'platform' org every 10 minutes. Run code review skill on each PR and post feedback."`
- ❌ `"Watch stuff"` (too vague — the Manager will ask for clarification)

### 6.2 Clarification Dialog

If your master prompt is ambiguous or lacks critical details, the Manager enters the **Clarifying** phase and asks questions.

**What you'll see:**
- The Manager's question displayed as a blue-bordered message in the chat
- The input area waiting for your response

**How to use:**
1. Read the Manager's question carefully
2. Type your response in the input area
3. Press **Enter** or click **Send**
4. The Manager may ask additional questions or proceed to planning

**Example:**
> **Manager:** "You mentioned 'monitor incidents for Team Alpha.' A few questions:
> 1. Which incident source — ServiceNow, Azure DevOps, or a custom API?
> 2. What constitutes 'resolved' — a status change, or acknowledgment?"
>
> **You:** "ServiceNow. Resolved means status changed to 'Resolved'."

The Manager accumulates all clarification answers in its context and carries them across all iterations.

### 6.3 Plan Review & Approval

After understanding your objective, the Manager generates an **execution plan** — a description of how each iteration will work.

**What you'll see:**
- The plan displayed as a Markdown-rendered message in the chat
- Two action buttons:

| Button | Action | Effect |
|---|---|---|
| **✅ Approve** | Accept the plan | Manager enters the iteration loop immediately |
| **❌ Reject** | Reject with feedback | Manager revises the plan based on your feedback |

**What the plan includes:**
- Event sources the Manager will query
- Filtering criteria (priority, team, status)
- How tasks will be categorized and delegated
- What each assistant will be instructed to do
- How results will be aggregated and reported

**Best practices:**
- Ensure the plan covers all categories of events you mentioned
- Verify that the Manager understands priority handling correctly
- If the plan is too broad, ask it to narrow the scope
- If it's missing a category, reject and add the missing requirement

### 6.4 The Iteration Loop

Once you approve the plan, the Manager begins its continuous loop. Each iteration goes through 5 phases:

| Phase | What Happens | Duration |
|---|---|---|
| **Fetching Events** | Manager queries event sources (MCP tools, file system, APIs) for new work | 5–30 seconds |
| **Scheduling** | Manager decomposes events into assistant tasks, assigns to pool, queues overflow | 2–10 seconds |
| **Executing** | Assistants work in parallel; queue drains as slots free up | Depends on tasks |
| **Aggregating** | Manager consolidates all assistant results into a narrative report | 5–15 seconds |
| **Resting** | Countdown timer until next iteration | Configurable (default 5 min) |

**Iteration containers in chat:**

Each iteration is wrapped in a foldable container:

```
━━ Iteration #1 ━━━━━━━━━━━━━━━━━━━━━ 10:32 AM ━━ [▾]
│
│  [MANAGER] Found 5 incidents. Assigning 3, queuing 2.
│  ▸ [ASSISTANT #1] INC001: P1 Database pool exhaustion — ✅ Remediated
│  ▸ [ASSISTANT #2] INC002: P2 High CPU — ⚠️ Escalated
│  ▸ [ASSISTANT #3] INC003: P3 Certificate expiry — ✅ Triaged
│  ▸ [ASSISTANT #1] INC004: P4 Log rotation — ✅ Triaged
│  ▸ [ASSISTANT #2] INC005: P2 Memory leak — ✅ Remediated
│  [MANAGER] REPORT — 5 processed, 3 remediated, 1 escalated, 1 triaged
│
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**Folding behavior:**
- **Active iteration** — expanded, auto-scrolls as messages arrive
- **Completed iteration** — auto-collapses when done
- **Click to toggle** — click `[▾]`/`[▸]` to expand/collapse any iteration
- **Collapsed view** — shows a summary line: `━━ Iteration #1 ━━ 5 tasks, 3 ✅ 1 ⚠️ 1 📝 ━━ [▸]`

### 6.5 Assistant Pool & Queue-Based Scheduling

The Assistant Pool manages a finite number of concurrent assistants. When tasks exceed the pool size, overflow tasks queue and auto-dispatch as slots free up.

**How it works:**
1. Manager decomposes events into tasks (e.g., 7 tasks from 7 incidents)
2. Tasks are sorted by priority (lower number = higher priority)
3. Up to `MaxAssistants` tasks start immediately
4. Remaining tasks enter a queue
5. As each assistant finishes, the next queued task is automatically dispatched
6. Cycle continues until all tasks are complete

**Example with Pool Size = 3 and 7 tasks:**

```
Time ──────────────────────────────────────────────►

Slot 1: [INC001 P1] ████████ ✅ → [INC004 P4] ██████ ✅ → [INC007 P3] ████ ✅
Slot 2: [INC002 P1] ██████████ ✅ → [INC005 P2] ████████ ✅
Slot 3: [INC003 P2] ████████████ ✅ → [INC006 P3] ██████████ ✅
Queue:  [INC004] [INC005] [INC006] [INC007] → drains as slots free
```

**What you see in the UI:**
- Status bar: `Tasks: 3/7 │ Queue: 4` → updates in real time
- Side panel event log: assignment and dequeue events for every task
- Live commentary: "Slot freed, dequeuing INC004 for Assistant #1"

### 6.6 Rest Period & Countdown Timer

After each iteration completes, the Manager enters a **rest period** before the next iteration.

**What you see:**
- Status bar shows `⏳ RESTING` with countdown: `04:32`
- A progress bar in the chat stream:
  ```
  ⏳ Next check in 4:32  [████████░░░░░░░░░░░░]
  ```

**Controls:**
- **Change interval** — Update the interval in the side panel; takes effect immediately if resting
- **Resume early** — Click the resume button to skip remaining rest
- **Pause** — Enter an extended rest with a custom duration (e.g., "pause for 2 hours")

### 6.7 Instruction Injection (Mid-Run)

While the Manager is running, you can type new instructions at any time. The Manager absorbs them on the next iteration.

**How to use:**
1. Type your instruction in the input area during any phase
2. Press **Enter** or click **Send**
3. The Manager evaluates whether the instruction is clear
4. If clear → queued for next iteration with a system confirmation:
   ```
   [SYSTEM] 📝 Instruction queued for next iteration
   ```
5. On the next iteration, the Manager incorporates the instruction into its planning

**Example:**
> **You:** "Also check for stale branches older than 30 days and report them."
>
> **System:** 📝 Instruction queued for next iteration
>
> *Next iteration now includes stale branch checking alongside the original scope.*

### 6.8 Clarification-Aware Injection

If you inject an ambiguous instruction, the Manager doesn't blindly queue it — it asks clarifying questions **inline in the chat**, all while the current iteration continues in the background.

**Flow:**

```
You: "Monitor the repos too"
    ↓
Manager: "I'd like to help monitor repos. A few questions:
    1. Which repositories? (specific names, org-wide, all in working dir?)
    2. What to monitor? (PRs, issues, commits, branch staleness?)"
    ↓
You: "All repos under 'platform' org. Monitor for new PRs and stale branches."
    ↓
Manager: "Got it. One more: what defines 'stale' — 30 days since last commit,
    or 30 days since last merge?"
    ↓
You: "30 days since last commit"
    ↓
System: 📝 Refined instruction queued: "Monitor all repos under 'platform' org
    for new PRs and stale branches (>30 days since last commit)"
```

**Key behaviors:**
- Clarification happens **inline** in the chat — no separate dialog
- **Assistants keep working** in the background while clarification happens
- The Manager uses its own session for the clarification LLM call (safe because assistants have separate sessions)
- Once the instruction is clear, a refined version is queued
- The refined instruction is incorporated on the next iteration

### 6.9 Iteration Reports & Aggregation

At the end of each iteration, the Manager produces a **consolidated report** by sending all assistant results to the LLM for narrative synthesis.

**What the report includes:**
- Per-task summary with status (✅ success, ⚠️ escalated, ❌ failed)
- Statistics: tasks processed, success rate, duration
- Recommendations for the next iteration
- Patterns and learnings observed across iterations

**Report rendering:**
- Displayed as a collapsible Markdown message within the iteration container
- Full-width card with bordered styling
- Click to expand/collapse

**Context continuity:**
- The Manager carries a summary of the previous iteration's report into the next iteration
- Learnings accumulate across iterations (e.g., "ServiceNow API returns max 50 results; pagination needed")
- This means the Manager gets **smarter over time** within a session

### 6.10 Side Panel — Live Commentary

The Live Commentary section provides a **real-time, auto-scrolling stream** of what every agent is doing as it happens. It's like watching a control room feed.

**How to access:** Click the **📊** button in the status bar.

**Visual format:**
Each entry shows: `[emoji] [AGENT_NAME] message`

```
🔵 [MANAGER] Building execution plan...
🟢 [MANAGER] Querying ServiceNow API for open incidents...
🟡 [MANAGER] 5 events found. Scheduling to pool...
🟠 [ASSISTANT #1] Creating session for INC001...
🟠 [ASSISTANT #2] Fetching runbook for INC002...
🟠 [ASSISTANT #3] Adding triage note to INC003...
✅ [ASSISTANT #3] INC003 triage note added
🟠 [ASSISTANT #1] Calling restart-pool tool...
✅ [ASSISTANT #1] INC001 remediated successfully
❌ [ASSISTANT #2] INC002 remediation failed: timeout
🟡 [MANAGER] Slot freed, dequeuing INC004 for Assistant #3
🟠 [ASSISTANT #3] Starting INC004...
```

**Agent colors:** Manager = blue, Assistants = unique color per index (orange, purple, cyan, etc.)

**Auto-scroll:** Enabled by default; new entries appear at the bottom and the view scrolls to follow. You can scroll up to review history — auto-scroll pauses until you scroll back to the bottom.

### 6.11 Side Panel — Configuration Controls

Runtime controls for the active Office session:

| Control | Description | Effect |
|---|---|---|
| **Interval** | Number input (minutes) | Updates check interval; if resting, takes effect immediately |
| **Pool Size** | Number input | Changes max concurrent assistants (effective on next iteration) |
| **Model** | Dropdown selector | Changes Manager model (effective on next iteration) |
| **⏸ Pause** | Button | Pauses the loop; current tasks complete first, then extended rest |
| **▶ Resume** | Button | Skips remaining rest, starts next iteration immediately |
| **⏹ Stop** | Button | Gracefully stops the Manager after current tasks complete |
| **🔄 Reset** | Button | Hard reset: cancels everything, returns to Idle |

### 6.12 Side Panel — Event Log

A structured, reverse-chronological log of every significant event:

```
10:34:12  TaskAssigned     INC004 → Assistant #1
10:34:10  TaskDequeued     INC004
10:33:58  AssistantCompleted  Assistant #3 (INC003)
10:33:45  TaskQueued       INC005 (position: 2)
10:33:44  TaskAssigned     INC003 → Assistant #3
10:33:44  TaskAssigned     INC002 → Assistant #2
10:33:44  TaskAssigned     INC001 → Assistant #1
10:33:42  EventsFetched    count=5
10:33:40  PhaseChanged     Scheduling → Executing
```

**Event types logged:** Phase transitions, task assignments, queue operations, assistant lifecycle (spawned, completed, failed, disposed), scheduling decisions, user actions (injection, interval change, pause/resume).

### 6.13 Side Panel — Iteration Statistics

Aggregate metrics computed from all completed iterations:

| Metric | Description |
|---|---|
| **Completed Iterations** | Total iterations finished |
| **Total Tasks Done** | Sum of all tasks across iterations |
| **Success Rate** | Percentage of tasks that succeeded |
| **Average Duration** | Mean wall-clock time per iteration |

These metrics update in real time as iterations complete.

### 6.14 Pause, Resume & Stop

**Pause:**
- Click **⏸ Pause** in the side panel
- Current tasks complete first (assistants are not interrupted)
- Manager enters an extended rest period
- Status bar shows `⏸ PAUSED`
- Loop resumes automatically after the pause duration, or manually via Resume

**Resume:**
- Click **▶ Resume** to skip remaining rest/pause
- The next iteration starts immediately

**Stop:**
- Click **⏹ Stop** in the side panel
- Current tasks complete first
- Manager transitions to **Stopped** phase
- No more iterations run
- To restart, you must Reset and start a new session

### 6.15 Reset Session

Resets everything back to a clean slate.

**What happens:**
1. All active assistants are cancelled
2. The task queue is cleared
3. Any rest timer is cancelled
4. The Manager session is disposed
5. All context, history, and accumulated learnings are cleared
6. Phase transitions to **Idle**
7. Chat is cleared, ready for a new master prompt

**When to use:**
- After the mission is complete
- To change the fundamental objective
- If the Manager gets into an unexpected state
- To start fresh with different MCP servers or configuration

---

## 7. UI Visual Guide

The Agent Office panel is a full-width chat plane with a fly-in side panel overlay:

```
┌────────────────────────────────────────────────────────────────────────┐
│  🏢 Agent Office                                                       │
├─── Status Bar ──────────────────────────────────────────── [📊] ──────┤
│  🟢 EXECUTING  │ Iteration #3 │ Tasks: 4/7 │ Queue: 3 │ 01:42       │
├────────────────────────────────────────────────────────────────────────┤
│                                                                        │
│  ┌─── Full-Width Scrollable Chat Plane ──────────────────────────┐   │
│  │                                                                │   │
│  │  [USER] 10:30 AM                                              │   │
│  │  Analyze open incidents for Team Alpha every 5 minutes...     │   │
│  │                                                                │   │
│  │  [MANAGER] 10:30 AM                                           │   │
│  │  I have a few questions before we begin:                      │   │
│  │  1. Which incident source?  2. What defines resolved?         │   │
│  │                                                                │   │
│  │  [USER] 10:31 AM                                              │   │
│  │  ServiceNow. Resolved = status changed to "Resolved".         │   │
│  │                                                                │   │
│  │  [MANAGER] 10:31 AM — PLAN                           [fold]  │   │
│  │  ## Execution Plan ...                                        │   │
│  │  [✅ Approve] [❌ Reject]                                      │   │
│  │                                                                │   │
│  │  ━━ Iteration #1 ━━━━━━━━━━━━━━━━━━━━━ 10:32 AM ━━ [▾]      │   │
│  │  │  [MANAGER] Found 5 incidents. Assigning 3, queuing 2.     │   │
│  │  │  ▸ [ASST #1] INC001: P1 — ✅ Remediated                   │   │
│  │  │  ▸ [ASST #2] INC002: P2 — ⚠️ Escalated                   │   │
│  │  │  ▸ [ASST #3] INC003: P3 — ✅ Triaged                      │   │
│  │  │  ▸ [ASST #1] INC004: P4 — ✅ Triaged              [fold]  │   │
│  │  │  ▸ [ASST #2] INC005: P2 — ✅ Remediated           [fold]  │   │
│  │  │  [MANAGER] REPORT                                  [fold]  │   │
│  │  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━   │   │
│  │                                                                │   │
│  │  [USER] 10:35 AM                                              │   │
│  │  Also check for stale branches older than 30 days.            │   │
│  │  [SYSTEM] 📝 Instruction queued for next iteration            │   │
│  │                                                                │   │
│  │  ⏳ Next check in 3:28  [████████████░░░░░░░░░░░░░░]          │   │
│  │                                                                │   │
│  │  ━━ Iteration #2 ━━━━━━━━━━━━━━━━━━━━━ 10:37 AM ━━ [▾]      │   │
│  │  │  [MANAGER] Found 3 incidents + 12 stale branches...       │   │
│  │  │  (active, auto-scrolling)                                  │   │
│  │                                                                │   │
│  └────────────────────────────────────────────────────────────────┘   │
│                                                                        │
│  ┌─── Input Area (bottom-pinned) ────────────────────────────────┐   │
│  │ Type a message or instruction...                       [Send] │   │
│  └────────────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────────────┘
```

### Side Panel (fly-in overlay from right)

```
┌─── Side Panel ─────────────────────────── [✕] ──┐
│                                                   │
│  ┌─ 💭 Live Commentary ─────────────────── [▾] ┐ │
│  │  🔵 [MANAGER] Building execution plan...     │ │
│  │  🟢 [MANAGER] Querying ServiceNow API...     │ │
│  │  🟡 [MANAGER] 5 events found, scheduling...  │ │
│  │  🟠 [ASST #1] Creating session for INC001... │ │
│  │  ✅ [ASST #3] INC003 triage note added       │ │
│  │  ❌ [ASST #2] INC002 remediation failed       │ │
│  │  ▼ (auto-scrolling)                          │ │
│  └──────────────────────────────────────────────┘ │
│                                                   │
│  ┌─ ⚙️ Configuration ──────────────────── [▾] ┐ │
│  │  Interval: [5] min    Pool: [3] agents       │ │
│  │  Model: [gpt-4o ▾]                          │ │
│  │  [⏸ Pause] [▶ Resume] [⏹ Stop] [🔄 Reset]  │ │
│  └──────────────────────────────────────────────┘ │
│                                                   │
│  ┌─ 📊 Event Log ──────────────────────── [▾] ┐ │
│  │  10:34:12 TaskAssigned   INC004 → A1        │ │
│  │  10:34:10 TaskDequeued   INC004             │ │
│  │  10:33:58 AssistantDone  A3 (INC003)        │ │
│  │  ...                                        │ │
│  └──────────────────────────────────────────────┘ │
│                                                   │
│  ┌─ 📈 Iteration Stats ────────────────── [▾] ┐ │
│  │  Completed: 3  │  Tasks: 14  │  Rate: 92%   │ │
│  │  Avg Duration: 38s                           │ │
│  └──────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────┘
```

---

## 8. Manager Phase Reference

| Phase | UI Indicator | Color | Description |
|---|---|---|---|
| **Idle** | 💤 Idle | Gray | No active session. Ready for a master prompt. |
| **Clarifying** | ❓ Clarifying | Amber | Manager is asking you questions |
| **Planning** | 🧠 Planning | Purple | Manager is building the execution strategy |
| **AwaitingApproval** | 📋 Awaiting Approval | Blue | Plan ready for your review |
| **FetchingEvents** | 🔍 Fetching | Teal | Manager is querying event sources |
| **Scheduling** | 🟡 Scheduling | Yellow | Manager is decomposing events into tasks |
| **Executing** | 🟢 Executing | Green | Assistants are actively working |
| **Aggregating** | 📊 Aggregating | Teal | Manager is consolidating results |
| **Resting** | ⏳ Resting | Gray | Countdown timer active |
| **Error** | 🔴 Error | Red | Manager encountered a fatal error |
| **Stopped** | ⏹ Stopped | Gray | Manager has been stopped by the user |

---

## 9. Assistant Task Status Reference

```
              ┌──────────┐
              │ Pending  │
              └────┬─────┘
                   │
          ┌────────┼────────┐
          ▼                 ▼
    ┌──────────┐     ┌──────────┐
    │  Queued  │     │ Assigned │
    └────┬─────┘     └────┬─────┘
         │                │
         └───────┬────────┘
                 ▼
          ┌──────────────┐
          │  InProgress  │
          └──────┬───────┘
                 │
       ┌─────────┼─────────┐
       ▼         ▼         ▼
  ┌──────────┐ ┌────────┐ ┌───────────┐
  │Completed │ │ Failed │ │ Cancelled │
  └──────────┘ └────────┘ └───────────┘
```

| Status | Description |
|---|---|
| **Pending** | Task created, not yet submitted to pool |
| **Queued** | Waiting for an available assistant slot |
| **Assigned** | Assigned to an assistant, session being created |
| **InProgress** | Assistant is actively executing |
| **Completed** | Task finished successfully |
| **Failed** | Task failed (may be retried based on retry policy) |
| **Cancelled** | Task was cancelled (user stop/reset, or queue depth limit) |

---

## 10. Live Commentary Indicators

| Indicator | Type | Meaning |
|---|---|---|
| 🔵 | Planning | Manager is planning, strategizing, or building prompts |
| 🟢 | Discovery | Manager is fetching events, querying data sources |
| 🟡 | Scheduling | Manager is assigning tasks, managing the queue |
| 🟠 | Working | An assistant is actively executing a task |
| ✅ | Success | A task or phase completed successfully |
| ⚠️ | Warning | Non-fatal issue or degraded result |
| ❌ | Error | Task failed or fatal error occurred |

---

## 11. Chat Message Color Coding

| Sender | Color | Alignment | Style |
|---|---|---|---|
| **User** | 🟢 Green border | Right-aligned | Standard message |
| **Manager** | 🔵 Blue border | Left-aligned | Markdown rendered |
| **Assistant #1** | 🟠 Orange border | Left-aligned, indented | Collapsible |
| **Assistant #2** | 🟣 Purple border | Left-aligned, indented | Collapsible |
| **Assistant #3** | 🔵 Cyan border | Left-aligned, indented | Collapsible |
| **System** | ⚪ Gray | Center-aligned | Smaller font, informational |

Assistant colors cycle through orange, purple, cyan, pink, brown, blue-grey, lime, and indigo for higher indices.

---

## 12. Worked Example — Incident Management

### Scenario
You're an SRE responsible for Team Alpha's services. You want the Manager to check ServiceNow for open incidents every 5 minutes and handle them based on priority.

---

**Step 1 — Write the Master Prompt**

```
Monitor open ServiceNow incidents for Team Alpha every 5 minutes.
For P1/P2 incidents: check the runbook, attempt automated remediation,
escalate if unresolved. For P3/P4: add a triage note with initial analysis.
Report findings after each check.
```

**Step 2 — Answer Clarification**

> **Manager:** "A few questions:
> 1. Which ServiceNow instance? Should I use the `servicenow-mcp` server?
> 2. What constitutes a successful remediation — status change to 'Resolved'?
> 3. For escalation, should I create an escalation ticket or notify a channel?"
>
> **You:** "Yes, use servicenow-mcp. Resolved = status changed to Resolved. For escalation, add a comment tagging @oncall-alpha."

**Step 3 — Approve the Plan**

> **Manager — PLAN:**
> ## Execution Plan
> 1. **Event Source**: Query `servicenow-mcp → list_incidents` with filter: `team=Alpha, status=open`
> 2. **P1/P2 Handling**: For each P1/P2 → fetch runbook → execute remediation steps → verify status → escalate if unchanged
> 3. **P3/P4 Handling**: For each P3/P4 → analyze symptoms → add triage comment via `update_incident`
> 4. **Reporting**: Aggregate all outcomes into an iteration report
> 5. **Interval**: 5 minutes

You click **✅ Approve**.

**Step 4 — Watch the Loop**

```
━━ Iteration #1 ━━━━━━━━━━━━━━━━━━━━━━━━━ 10:32 AM ━━ [▾]
│
│  [MANAGER] FETCHING
│  Querying ServiceNow... found 5 open incidents.
│
│  [MANAGER] SCHEDULING
│  Assigning 3 immediately (pool size = 3), queuing 2.
│  - INC001 (P1) → Assistant #1
│  - INC002 (P2) → Assistant #2
│  - INC003 (P3) → Assistant #3
│  - INC004 (P4) → Queued [position 1]
│  - INC005 (P2) → Queued [position 2]
│
│  ▸ [ASSISTANT #1] INC001: P1 Database pool exhaustion
│    Fetched runbook "db-pool-restart". Executing restart-pool tool...
│    ✅ Connection pool restarted. Status changed to Resolved.
│
│  ▸ [ASSISTANT #2] INC002: P2 High CPU on web-tier-03
│    Fetched runbook "cpu-auto-scale". Triggered auto-scaling...
│    ⚠️ CPU still at 92% after scale-up. Added escalation comment @oncall-alpha.
│
│  ▸ [ASSISTANT #3] INC003: P3 Certificate expiry in 7 days
│    Analyzed: cert for api.alpha.example.com expires Feb 16.
│    ✅ Added triage note: "Cert renewal needed by Feb 15. Contact: security-team."
│
│  ▸ [ASSISTANT #1] INC004: P4 Log rotation not configured on batch-worker
│    ✅ Added triage note: "Recommend adding logrotate config. Non-urgent."
│
│  ▸ [ASSISTANT #2] INC005: P2 Memory leak in notification-service
│    Fetched runbook "memory-leak-restart". Restarted service...
│    ✅ Memory usage dropped from 94% to 23%. Status changed to Resolved.
│
│  [MANAGER] REPORT
│  ## Iteration #1 Summary
│  - **5 incidents** processed in 1m 38s
│  - **3 remediated** (INC001, INC003-triage, INC005)
│  - **1 escalated** (INC002 — CPU still high after auto-scale)
│  - **1 triaged** (INC004 — low-priority log rotation)
│
│  **Recommendation:** INC002 may need manual investigation. The auto-scale
│  runbook didn't resolve the CPU spike — could be a code-level issue.
│
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**Step 5 — Rest & Repeat**

```
⏳ Next check in 4:22  [████████████░░░░░░░░░░░░░░░░]
```

The Manager rests for 5 minutes, then checks again. If no new incidents, it reports "All clear" and rests again.

---

## 13. Worked Example — Multi-Repository PR Review

### Scenario
You maintain 5 repositories under the "platform" GitHub organization. You want the Manager to check for new PRs every 10 minutes and run a code review on each.

**Master Prompt:**
```
Monitor all repositories under the "platform" organization on GitHub
for new pull requests every 10 minutes. For each new PR, run a thorough
code review focusing on security, performance, and code quality.
Post review comments on the PR.
```

**Iteration Flow:**

```
━━ Iteration #1 ━━━━━━━━━━━━━━━━━━━━━━━━━ 2:00 PM ━━ [▾]
│
│  [MANAGER] FETCHING
│  Querying github-mcp → list_pull_requests for 5 repos...
│  Found 3 new PRs across 2 repos.
│
│  [MANAGER] SCHEDULING
│  - PR #142 (platform-api) → Assistant #1
│  - PR #87 (platform-auth) → Assistant #2
│  - PR #201 (platform-api) → Assistant #3
│
│  ▸ [ASSISTANT #1] PR #142: "Add rate limiting middleware"
│    Reviewed 4 files, 287 lines changed.
│    - ⚠️ Security: Rate limit bypass possible via X-Forwarded-For header
│    - 💡 Performance: Consider using sliding window instead of fixed window
│    - ✅ Code quality: Clean implementation, good test coverage
│    Posted 3 review comments on PR.
│
│  ▸ [ASSISTANT #2] PR #87: "Update OAuth token refresh"
│    Reviewed 2 files, 94 lines changed.
│    - ⚠️ Security: Refresh token stored in localStorage (use httpOnly cookie)
│    - ✅ Performance: No issues
│    - ✅ Code quality: Good error handling
│    Posted 1 review comment on PR.
│
│  ▸ [ASSISTANT #3] PR #201: "Fix pagination in list endpoints"
│    Reviewed 3 files, 156 lines changed.
│    - ✅ Security: No issues
│    - ✅ Performance: Efficient cursor-based pagination
│    - 💡 Code quality: Missing null check on cursor parameter
│    Posted 1 review comment on PR.
│
│  [MANAGER] REPORT
│  ## Iteration #1 — PR Review Summary
│  - **3 PRs** reviewed across platform-api and platform-auth
│  - **2 security findings** (rate limit bypass, token storage)
│  - **5 total review comments** posted
│  - No blocking issues found.
│
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

---

## 14. Worked Example — Mid-Run Instruction Injection

### Scenario
The Manager is monitoring incidents. During Iteration #2, you decide to also monitor stale branches — but the instruction is ambiguous.

```
Chat Timeline:
──────────────────────────────────────────────────────

  ━━ Iteration #2 ━━━━━━━━━━━━━━━━━━━━━━ 10:37 AM ━━ [▾]
  │  [MANAGER] Found 3 incidents. Assigning...
  │  ▸ [ASSISTANT #1] INC006... (working)
  │  ▸ [ASSISTANT #2] INC007... (working)
  │

  [USER] 10:38 AM                              ← You type mid-execution
  Monitor the repos too

  [MANAGER] 10:38 AM — CLARIFICATION           ← Manager needs more info
  I'd like to help monitor repos. A few questions:
  1. Which repositories?
  2. What should I monitor for? (PRs, issues, commits, staleness?)

  │  ▸ [ASSISTANT #1] INC006: ✅ Done           ← Assistants keep working

  [USER] 10:39 AM
  All repos under "platform" org. Monitor for new PRs and stale branches.

  [MANAGER] 10:39 AM — CLARIFICATION
  One more: what defines "stale" — 30 days since last commit,
  or 30 days since last merge?

  │  ▸ [ASSISTANT #2] INC007: ✅ Done

  [USER] 10:39 AM
  30 days since last commit

  [SYSTEM] 📝 Refined instruction queued for next iteration:
  "Monitor all repos under 'platform' org for new PRs and stale branches
   (>30 days since last commit)"

  │  [MANAGER] REPORT — Iteration #2 Summary...
  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  ━━ Iteration #3 ━━━━━━━━━━━━━━━━━━━━━━ 10:42 AM ━━ [▾]
  │  [MANAGER] Found 2 incidents + 5 new PRs + 3 stale branches.
  │  ↑ New instruction incorporated!
```

**Key takeaway:** The clarification conversation happens inline without disrupting the running iteration. The refined instruction is cleanly queued and incorporated on the next cycle.

---

## 15. Best Practices & Tips

### Master Prompt Design

| Do | Don't |
|---|---|
| Be specific about event sources and MCP tools | Use vague language like "monitor stuff" |
| Include priority handling rules | Assume the Manager knows your team's conventions |
| Mention the desired check interval | Leave the interval ambiguous |
| Specify what "done" looks like for each category | Omit acceptance criteria |
| Reference specific repos, services, or endpoints | Say "all" when you mean a specific subset |

### Configuration Optimization

| Scenario | Pool Size | Interval | Timeout |
|---|---|---|---|
| Low-volume incident monitoring (1–5 events/check) | 3 | 5 min | 10 min |
| High-volume PR review (10–20 PRs/check) | 5 | 10 min | 15 min |
| Scheduled code audit (periodic, non-urgent) | 3 | 30 min | 20 min |
| Rapid incident response (P1 focus) | 5 | 2 min | 5 min |
| Multi-repo staleness scan | 3 | 60 min | 10 min |

### During Operation

- **Open the side panel** — the Live Commentary is invaluable for understanding what's happening
- **Use instruction injection** for scope changes — don't reset the session
- **Let the Manager learn** — it accumulates patterns across iterations; longer sessions are smarter sessions
- **Review iteration reports** — they contain recommendations that improve over time
- **Pause during low-activity windows** — saves API quota while maintaining context
- **Don't change pool size during execution** — wait for the current iteration to finish

### After Completion

- **Reset when the mission changes** — the Manager's context is optimized for the original prompt
- **Copy reports** for documentation or handoff to teammates
- **Check the event log** if something unexpected happened — every decision is recorded

---

## 16. How Agent Office Differs from Agent Team

| Aspect | Agent Team (👥) | Agent Office (🏢) |
|---|---|---|
| **Lifecycle** | One-shot: submit → plan → execute → done | Continuous loop: check → delegate → report → rest → repeat |
| **Manager Session** | Created per task, disposed after | Long-lived, persists across all iterations |
| **Task Source** | User provides the full task upfront | Manager discovers tasks from event sources |
| **Worker Lifetime** | Workers live for the batch duration | Assistants are ephemeral: spawn → work → report → dispose |
| **Scheduling** | All chunks dispatched at once (DAG stages) | Queue-based: if tasks > pool size, pending tasks wait |
| **User Interaction** | Approve plan once, optional injection | Ongoing: inject instructions, change interval, pause/resume |
| **Rest Period** | None | Configurable countdown between iterations |
| **Context Continuity** | Per-task only | Manager accumulates learnings across iterations |
| **State Machine Phases** | 7 phases | 11 phases (adds FetchingEvents, Scheduling, Resting, Stopped) |
| **Best For** | Complex one-time tasks (refactoring, audit, test generation) | Repeating periodic tasks (monitoring, triage, scheduled reviews) |

**Rule of thumb:**
- If you have a task with a clear start and end → use **Agent Team**
- If you need ongoing monitoring with periodic action → use **Agent Office**

---

## 17. Troubleshooting

### Common Issues

| Symptom | Likely Cause | Resolution |
|---|---|---|
| Start does nothing | No active Copilot session | Check session health in another tab; re-authenticate |
| Manager never plans | Master prompt too vague | Reset and rewrite with specific event sources and handling rules |
| Assistants all fail | Invalid working directory or MCP server | Verify MCP config; check that servicenow-mcp / github-mcp is running |
| Queue never drains | Assistants stuck or timed out | Check assistant timeout in config; increase if tasks are complex |
| "No events found" every iteration | Incorrect MCP query or filter | Review the plan; check that the MCP tool returns expected data |
| Countdown doesn't appear | Iteration completed with error | Check event log for errors; reset if needed |
| Injection not applied | Instruction injected during aggregating | It will be absorbed on the next iteration; this is by design |
| Side panel won't open | UI state conflict | Switch tabs and switch back; or restart the app |
| Health indicator is red | Network/auth issue | Check internet; re-authenticate with `copilot login` |

### Performance Considerations

- **High pool sizes** (5+) increase throughput but also increase API rate limit pressure
- **Short intervals** (1–2 min) with many events per iteration can cause sustained high load
- **Assistant timeout** should be generous enough for complex tasks but not so long that stuck assistants waste slots
- **Context accumulation** — very long sessions (100+ iterations) may cause the Manager's context to grow large; consider periodic reset

### Error Recovery

1. **Single assistant fails:** Retry policy handles this automatically. If retries are exhausted, the task is marked Failed and included in the report.
2. **All assistants fail in one iteration:** The Manager reports the failure and continues to the next iteration. Check the event log for the root cause.
3. **Manager session disconnects:** The service attempts to reconnect with context replay. If reconnection fails, the phase transitions to Error.
4. **App shutdown during execution:** Active assistants are disposed gracefully. On next launch, you start a new session.

---

## 18. Glossary

| Term | Definition |
|---|---|
| **Manager** | The central LLM-powered agent that runs continuously, fetching events, scheduling tasks, and aggregating results |
| **Assistant** | An ephemeral AI agent that executes a single task and is disposed |
| **Assistant Pool** | The managed set of concurrent assistant slots (size = Pool Size setting) with queue-based overflow |
| **Master Prompt** | The user's initial instruction that defines the Manager's ongoing mission |
| **Iteration** | One complete cycle: fetch events → schedule tasks → execute → aggregate → rest |
| **Iteration Report** | The Manager's consolidated narrative summary after each iteration |
| **Rest Period** | The countdown interval between iterations (default: 5 minutes) |
| **Instruction Injection** | Sending additional instructions to the Manager mid-run |
| **Clarification-Aware Injection** | When the Manager asks clarifying questions about an ambiguous injected instruction |
| **Event Source** | The system the Manager queries for new work (MCP server, file system, API) |
| **Scheduling Decision** | A logged entry recording why a task was assigned immediately, queued, retried, or cancelled |
| **Live Commentary** | Real-time natural-language stream showing what each agent is doing |
| **Phase** | A discrete stage in the Manager's lifecycle (Idle → Clarifying → Planning → AwaitingApproval → FetchingEvents → Scheduling → Executing → Aggregating → Resting) |
| **Context Continuity** | The Manager's ability to carry learnings and summaries from previous iterations into the next |

---

## 19. Frequently Asked Questions

**Q: Can I run multiple Office sessions at the same time?**
A: Each Office tab manages one session at a time. Complete or reset the current session before starting a new one.

**Q: Does the Manager remember things between iterations?**
A: Yes. The Manager carries a summary of the previous iteration's results, accumulated learnings, and all injected instructions into every subsequent iteration. This is **context continuity** — the Manager gets smarter over time within a session.

**Q: What happens if there are no events in an iteration?**
A: The Manager reports "No events found" and proceeds directly to the rest period. No assistants are spawned. This is normal for low-activity periods.

**Q: Can I use Agent Office without MCP servers?**
A: Yes. The Manager can use file system tools, Git commands, and the working directory as event sources. MCP servers enable richer integrations (ServiceNow, GitHub, Jira, etc.) but are not required.

**Q: What happens if I inject an instruction during the Executing phase?**
A: The Manager evaluates the instruction for clarity using its own session (which is idle during execution since assistants have their own sessions). If clear, it's queued. If ambiguous, inline clarification begins in the chat. Either way, assistants continue working uninterrupted.

**Q: How do I change what the Manager monitors?**
A: For minor scope changes, use **instruction injection** (e.g., "Also check for stale branches"). For fundamental mission changes, **reset** the session and start with a new master prompt.

**Q: Is there a limit on how long a session can run?**
A: There is no hard limit. Sessions can run for hours or days. However, very long sessions (hundreds of iterations) may accumulate large context; consider periodic reset for best performance.

**Q: Can I change the pool size while assistants are working?**
A: Pool size changes take effect on the **next iteration**. Active assistants are not interrupted.

**Q: How does billing work?**
A: Each Manager prompt and each Assistant prompt counts as a Copilot request against your premium quota. An iteration with 5 assistant tasks uses approximately 7 requests (1 fetch + 1 schedule/aggregate + 5 assistants). Factor this in when setting the check interval.

**Q: What's the difference between Stop and Reset?**
A: **Stop** waits for active assistants to finish, then halts the loop — no more iterations run. **Reset** cancels everything immediately, disposes all sessions, clears all context, and returns to Idle. Stop preserves state; Reset destroys it.

**Q: Can I use different models for the Manager and Assistants?**
A: Yes. In the side panel configuration, you can select different models for each. A common pattern is using a more capable model (e.g., GPT-4o) for the Manager and a faster/cheaper model for Assistants.

---

## 20. Appendix — Keyboard Shortcuts

| Shortcut | Context | Action |
|---|---|---|
| **Enter** | Input area focused | Send message / instruction |
| **Escape** | Side panel open | Close side panel |

---

<p align="center">
  <em>CopilotDesktop Agent Office — Your tireless AI operations center that never sleeps.</em><br/>
  <strong>© 2026 CopilotDesktop. All rights reserved.</strong>
</p>