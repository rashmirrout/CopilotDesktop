<p align="center">
  <img src="../src/CopilotAgent.App/Resources/app.png" alt="CopilotDesktop Logo" width="80" />
</p>

<h1 align="center">Panel Discussion — User Guide</h1>

<p align="center">
  <strong>CopilotDesktop v2.0</strong> · Multi-Expert AI Debate System for Deep Analysis<br/>
  <em>Assemble a panel of AI experts — each with unique expertise and personality — to analyze, debate, and synthesize insights on any topic you bring to the table.</em>
</p>

---

## Table of Contents

1. [What is Panel Discussion?](#1-what-is-panel-discussion)
2. [Why Use a Panel? — Value Proposition](#2-why-use-a-panel--value-proposition)
3. [Quick Start — Your First Panel in 2 Minutes](#3-quick-start--your-first-panel-in-2-minutes)
4. [Understanding the Interface](#4-understanding-the-interface)
   - 4.1 [Three-Pane Layout Overview](#41-three-pane-layout-overview)
   - 4.2 [Header Bar — Your Command Center](#42-header-bar--your-command-center)
   - 4.3 [Execution Bar — Live Pulse](#43-execution-bar--live-pulse)
   - 4.4 [Left Pane — Head Agent Chat](#44-left-pane--head-agent-chat)
   - 4.5 [Center Pane — Discussion Stream](#45-center-pane--discussion-stream)
   - 4.6 [Right Pane — Agent Inspector](#46-right-pane--agent-inspector)
   - 4.7 [Bottom Input Bar](#47-bottom-input-bar)
   - 4.8 [Side Panel — Settings & Event Log](#48-side-panel--settings--event-log)
   - 4.9 [Synthesis Report Overlay](#49-synthesis-report-overlay)
5. [The Discussion Lifecycle — From Topic to Synthesis](#5-the-discussion-lifecycle--from-topic-to-synthesis)
6. [Discussion Depth — Controlling Analysis Intensity](#6-discussion-depth--controlling-analysis-intensity)
7. [Commentary Mode — Controlling Verbosity](#7-commentary-mode--controlling-verbosity)
8. [Meet the Panelists — Default Expert Profiles](#8-meet-the-panelists--default-expert-profiles)
9. [Preset Panels — Ready-Made Expert Teams](#9-preset-panels--ready-made-expert-teams)
10. [Convergence — How Consensus Emerges](#10-convergence--how-consensus-emerges)
11. [Guard Rails — Safety & Resource Limits](#11-guard-rails--safety--resource-limits)
12. [Settings Reference](#12-settings-reference)
13. [Phase Reference](#13-phase-reference)
14. [Agent Role Reference](#14-agent-role-reference)
15. [Worked Example — Architecture Review](#15-worked-example--architecture-review)
16. [Worked Example — Security Audit](#16-worked-example--security-audit)
17. [Worked Example — Deep Research Question](#17-worked-example--deep-research-question)
18. [Best Practices & Tips](#18-best-practices--tips)
19. [How Panel Discussion Differs from Agent Team & Agent Office](#19-how-panel-discussion-differs-from-agent-team--agent-office)
20. [Troubleshooting](#20-troubleshooting)
21. [Glossary](#21-glossary)
22. [Frequently Asked Questions](#22-frequently-asked-questions)
23. [Appendix — Keyboard Shortcuts](#23-appendix--keyboard-shortcuts)

---

## 1. What is Panel Discussion?

**Panel Discussion** is a structured multi-expert AI debate system built into CopilotDesktop. You pose a topic or question, and a team of AI panelists — each with distinct expertise, personality, and analysis style — engages in a moderated discussion to produce a comprehensive synthesis.

It's like convening a panel of senior engineers, architects, and specialists in a conference room. Each expert examines your topic through their unique lens (security, performance, architecture, UX, QA, DevOps), challenges each other's positions, and ultimately converges on actionable, multi-perspective insights.

### The Cast of Characters

| Agent | Role | What They Do |
|---|---|---|
| 🎓 **Head Agent** | Discussion Director | Your personal interface. Clarifies your topic, builds a discussion plan, selects panelists, initiates debate, and delivers the final synthesis. |
| 🛡️ **Moderator** | Quality Guardian | Works behind the scenes. Enforces guard rails, detects convergence, manages turn order, and prevents runaway discussions. |
| 🏗️⚡🧪🔧🎨😈... **Panelists** | Domain Experts | 3–8 AI experts, each with unique expertise and persona. They analyze, debate, critique, and refine positions until consensus emerges. |

### What Makes It Special

- **Multi-perspective analysis** — no single-agent blind spots
- **Structured debate** — not just multiple responses, but genuine back-and-forth critique
- **Convergence detection** — the system knows when experts agree and triggers synthesis automatically
- **Guard rails** — resource limits, time caps, and safety policies prevent runaway costs
- **Rich UI** — watch the debate unfold in real time across a three-pane layout
- **Follow-up Q&A** — ask questions after the synthesis to dig deeper

---

## 2. Why Use a Panel? — Value Proposition

### The Problem with Single-Agent Analysis

When you ask a single AI agent to review your architecture, audit your security, or analyze a complex topic, you get **one perspective**. That agent might be great at code review but miss the DevOps implications, or excellent at performance analysis but overlook UX concerns.

### The Panel Advantage

| Single Agent | Panel Discussion |
|---|---|
| One perspective, one pass | 3–8 experts, multiple rounds of analysis |
| No self-challenge | Experts critique each other's positions |
| May miss cross-domain impacts | Every domain is represented |
| Fixed depth of analysis | Depth adapts to complexity (Quick → Standard → Deep) |
| No convergence signal | You know when experts agree (convergence %) |
| One-shot output | Iterative refinement through structured debate |
| Flat response | Rich synthesis report with multi-perspective consensus |

### When to Use Panel Discussion

| ✅ Great For | ❌ Not Ideal For |
|---|---|
| Architecture decisions with multiple trade-offs | Simple coding questions |
| Security audits needing multi-angle analysis | Quick one-off tasks |
| Technology selection evaluations | File editing or code generation |
| Design reviews (code, UX, API) | Repetitive monitoring (use Agent Office) |
| Complex research questions | Large multi-file refactoring (use Agent Team) |
| Risk assessment and mitigation planning | Real-time incident response |
| Post-mortem analysis | Tasks requiring tool-heavy execution |
| Compliance and regulatory review | |

---

## 3. Quick Start — Your First Panel in 2 Minutes

### Step 1 — Open the Panel Tab

Click the **🎙 Panel** tab in the main CopilotDesktop navigation bar.

### Step 2 — Enter Your Topic

Type your topic in the input box at the bottom:

```
Should we migrate our monolithic REST API to a microservices 
architecture using gRPC? Consider our team of 8 engineers, 
current 50ms p99 latency requirement, and 3-month timeline.
```

Click **🎙 Start Panel** (or press Enter).

### Step 3 — Interact with the Head Agent

The Head Agent may ask clarifying questions in the **left pane**:

> 🎓 **Head Agent:** "Before I assemble the panel, I'd like to clarify:
> 1. What's your current tech stack?
> 2. What's driving the migration — performance, scalability, or team autonomy?
> 3. Are there any regulatory constraints on data flow between services?"

Answer naturally. The Head builds context before the debate begins.

### Step 4 — Review the Plan & Approve

The Head presents a discussion plan with selected panelists:

> **Discussion Plan:**
> - Depth: Standard (30 turns, 80% convergence)
> - Panelists: 🏗️ Software Architect, ⚡ Performance Engineer, 🔧 DevOps Engineer, 🧪 QA Specialist, 😈 Devil's Advocate
> - Focus areas: Service boundaries, data consistency, deployment complexity, testing strategy, risk assessment

Click **✅ Approve & Start Panel** to launch the discussion.

### Step 5 — Watch the Debate Unfold

The **center pane** fills with expert analysis:

> 🏗️ **Software Architect:** "Given the 8-person team and 3-month timeline, I'd recommend a modular monolith as a stepping stone..."
>
> ⚡ **Performance Engineer:** "The 50ms p99 requirement is tight. gRPC's binary protocol helps, but network hops between services add latency..."
>
> 😈 **Devil's Advocate:** "Let's challenge the premise — is the monolith actually the bottleneck, or is this a Conway's Law reflex?"

### Step 6 — Read the Synthesis

When convergence reaches the threshold (default 80%), the system automatically produces a **Synthesis Report** — a comprehensive, multi-perspective conclusion.

### Step 7 — Ask Follow-Up Questions

After synthesis, you can ask follow-up questions:

```
What would the first 3 microservices be if we decided to go ahead?
```

The Head Agent answers with the full context of the panel debate.

---

## 4. Understanding the Interface

### 4.1 Three-Pane Layout Overview

The Panel Discussion interface uses a purpose-built three-pane layout:

```
┌──────────────────────────────────────────────────────────────────────────┐
│  🎙 Panel Discussion  [Phase]  [Depth]   Status   🎯 75%  🔄 12/30  💰 ~$0.42  [⏸][▶][⏹][🔄] [⚙]  │
├──────────────────────────────────────────────────────────────────────────┤
│  🟢 ● Panelists are debating service boundaries...  ⚡ Quick  🏗️ Architect  ⚙ Parallel           │
├───────────────────┬──────────────────────────────────┬───────────────────┤
│                   │                                  │                   │
│  🎓 Head Agent    │  🗣 Discussion Stream  [12 msgs] │  🔍 Agent Inspector│
│                   │                                  │                   │
│  ┌─────────────┐  │  ┌─────────────────────────────┐ │  ┌─────────────┐ │
│  │ [Head] I've │  │  │ 🏗️ Software Architect       │ │  │ 🏗️ Architect│ │
│  │ assembled a │  │  │ Given the team size...      │ │  │   Active    │ │
│  │ panel of 5  │  │  ├─────────────────────────────┤ │  ├─────────────┤ │
│  │ experts...  │  │  │ ⚡ Performance Engineer     │ │  │ ⚡ Perf Eng │ │
│  ├─────────────┤  │  │ The 50ms p99 constraint...  │ │  │   Idle      │ │
│  │ [You] Our   │  │  ├─────────────────────────────┤ │  ├─────────────┤ │
│  │ stack is    │  │  │ 😈 Devil's Advocate         │ │  │ 😈 Devil's  │ │
│  │ .NET 8 +    │  │  │ Before we accept that...    │ │  │   Idle      │ │
│  │ PostgreSQL  │  │  │                             │ │  ├─────────────┤ │
│  └─────────────┘  │  └─────────────────────────────┘ │  │  [Details]  │ │
│                   │                                  │  │  Msgs: 4    │ │
│  ┌─────────────┐  │                                  │  │  Tools: 2   │ │
│  │ 📋 Review   │  │                                  │  │  Last: ...  │ │
│  │ [✅ Approve]│  │                                  │  └─────────────┘ │
│  │ [❌ Reject] │  │                                  │                   │
│  └─────────────┘  │                                  │                   │
├───────────────────┴──────────────────────────────────┴───────────────────┤
│  ▶ Enter a topic to start a panel discussion...                 [Send]  │
└──────────────────────────────────────────────────────────────────────────┘
```

| Pane | Purpose | What to Watch For |
|---|---|---|
| **Left** (280px) | Your 1:1 chat with the Head Agent | Clarification Q&A, plan review, approval buttons |
| **Center** (flexible) | The live discussion stream — all panelists debating | Expert analysis, cross-critiques, commentary notes |
| **Right** (240px) | Agent Inspector — select any agent to see stats | Status, message count, tool calls, last activity |

All three panes are **resizable** — drag the splitters between them.

### 4.2 Header Bar — Your Command Center

The header bar is the nerve center, packed with real-time indicators and controls:

```
┌──────────────────────────────────────────────────────────────────────────┐
│  🎙 Panel Discussion  [Phase Badge] [Depth Badge]   🔍 Status Text      │
│                                       🎯 75%  🔄 12/30  💰 ~$0.42      │
│                                       [⏸ Pause] [▶ Resume] [⏹ Stop] [🔄 Reset]  [⚙]  │
└──────────────────────────────────────────────────────────────────────────┘
```

| Element | Description |
|---|---|
| **Phase Badge** | Shows current lifecycle phase (e.g., `Clarifying`, `Running`, `Synthesizing`) with color coding |
| **Depth Badge** | Appears after the Head Agent detects discussion depth: `⚡ Quick`, `📐 Standard`, `🔬 Deep` |
| **Status Icon + Text** | Center-aligned status description (e.g., `🔍 Analyzing topic complexity...`) |
| **🎯 Convergence %** | How close the panelists are to consensus (teal badge) |
| **🔄 Turns Display** | Current turn / max turns (blue badge) |
| **💰 Cost Display** | Estimated API cost for the session (amber badge) |
| **Action Buttons** | ⏸ Pause, ▶ Resume, ⏹ Stop, 🔄 Reset |
| **⚙ Settings Gear** | Opens the side panel (settings + event log). Red badge shows pending unsaved changes |

### 4.3 Execution Bar — Live Pulse

When the discussion is active, a green pulsing bar appears below the header showing real-time execution state:

```
┌──────────────────────────────────────────────────────────────────────┐
│  ● Panelists are debating...  │  ⚡ Quick  │  🏗️ Architect  │  ⚙ Parallel  │
└──────────────────────────────────────────────────────────────────────┘
```

| Column | What It Shows |
|---|---|
| **Status Text** | Green dot + animated description of current activity |
| **Discussion Mode Badge** | Color-coded depth indicator — see [Discussion Depth](#6-discussion-depth--controlling-analysis-intensity) |
| **Active Agent** | Which panelist is currently speaking (green pill with name/icon) |
| **Parallel Indicator** | Shows `⚙ Parallel` when multiple agents are active simultaneously |

**Discussion Mode Badge Colors:**

| Depth | Badge Text | Background | Border/Text Color |
|---|---|---|---|
| ⚡ Quick | `⚡ Quick` | Amber `#FFF8E1` | `#F57F17` |
| 📐 Standard | `📐 Standard` | Blue `#E3F2FD` | `#1565C0` |
| 🔬 Deep | `🔬 Deep` | Purple `#F3E5F5` | `#7B1FA2` |

### 4.4 Left Pane — Head Agent Chat

This is your private channel with the Head Agent. The Head is your representative on the panel — it:

1. **Clarifies** your topic (asks questions if needed)
2. **Builds** the discussion plan (selects panelists, sets parameters)
3. **Presents** the plan for your approval
4. **Manages** the discussion on your behalf
5. **Delivers** the final synthesis
6. **Answers** follow-up questions after completion

**Message Styles:**

| Sender | Background | Alignment |
|---|---|---|
| Head Agent | Blue `#E3F2FD` | Left |
| You | Indigo `#E8EAF6` | Right |

**Approval Banner:**

When the Head presents a plan, an amber banner appears at the bottom of the left pane:

```
┌──────────────────────────────┐
│ 📋 Review the panel plan above │
│ [✅ Approve & Start Panel]     │
│ [❌ Reject]                    │
└──────────────────────────────┘
```

- **✅ Approve & Start Panel** — Launches the discussion immediately
- **❌ Reject** — Rejects the plan; type feedback in the input box and the Head will revise

### 4.5 Center Pane — Discussion Stream

The main event. All panelist messages appear here in chronological order.

**Message Format:**

Each message shows:
```
┌──────────────────────────────────────────────────────┐
│  [🏗️]  Software Architect  · Panelist      10:34:22  │
│                                                      │
│  Given the team size of 8 engineers and the          │
│  3-month timeline, I'd strongly recommend...         │
│  (Markdown rendered content)                         │
└──────────────────────────────────────────────────────┘
```

| Element | Description |
|---|---|
| **Role Icon** | Colored circle with emoji (matches the panelist's assigned icon) |
| **Author Name** | Panelist's display name (colored to match their role) |
| **Role Label** | `Head`, `Moderator`, `Panelist` in gray |
| **Timestamp** | HH:mm:ss format |
| **Content** | Full Markdown rendering (code blocks, lists, tables, emphasis) |

**Commentary Messages** (from the Moderator) are distinguished with a purple left border and lilac background (`#F3E5F5`).

**Pane Header Features:**
- 🗣 "Discussion Stream" title with message count badge
- **Agent pill indicators** — small colored dots showing each agent's status (green = active, gray = idle)
- Click any agent pill to select it in the Agent Inspector

### 4.6 Right Pane — Agent Inspector

A live dashboard for monitoring individual agents.

**Agent List** (scrollable, max 200px height):
Each agent appears as a clickable card showing:
- Role icon + name
- Role label + message count
- Status dot (colored by current status)

**Selected Agent Detail Panel:**
When you click an agent, the detail section shows:
- **Header:** Icon, name, status badge, role
- **Stats Grid:**
  - Messages sent
  - Tool calls made
  - Last tool used
- **Last Message:** Most recent content from this agent

**How to Select an Agent:**
- Click an agent card in the right pane, or
- Click an agent pill in the center pane header

### 4.7 Bottom Input Bar

A unified input area that adapts based on the current phase:

| Phase | Placeholder Text | Button | Action |
|---|---|---|---|
| **Idle** | "Enter a topic to start a panel discussion..." | 🎙 Start Panel | Submits topic to Head Agent |
| **Clarifying** | "Send a message to the Head agent..." | Send | Sends answer to Head |
| **Running** | "Send a message to the Head agent..." | Send | Communicates with Head mid-discussion |
| **Completed** | "Ask a follow-up question..." | Send | Sends follow-up to Head for contextual answer |

The input supports **multi-line text** (press Shift+Enter for new lines, Enter to send).

### 4.8 Side Panel — Settings & Event Log

Click the **⚙** gear icon in the header to slide open the settings panel from the right.

```
┌─── ⚙ Panel Settings ──────────────── [✕] ──┐
│                                              │
│  🧠 Model Selection                         │
│  ├─ Primary Model (Head/Moderator): [▾]     │
│  └─ 🤖 Panelist Model Pool: [☑] [☑] [☐]   │
│                                              │
│  ─────────────────────────────────           │
│                                              │
│  🎙 Panel Configuration   [↩ Defaults]      │
│  ├─ Max Panelists: [5]   Max Turns: [30]    │
│  ├─ Duration (min): [30] Convergence: [80]% │
│  ├─ Commentary: [Brief▾] Depth: [Auto▾]     │
│  ├─ ☑ Allow file system access               │
│  └─ 📂 Working Directory: [path] [📁]       │
│                                              │
│  [⚠ 3 unsaved changes]  [✓ Apply] [✕ Disc] │
│                                              │
│  ─────────────────────────────────           │
│                                              │
│  📝 Event Log                                │
│  ├─ 10:34:12 PhaseChanged → Running         │
│  ├─ 10:34:10 AgentMessage Architect          │
│  ├─ 10:33:58 ConvergenceUpdate 45%          │
│  └─ ...                                     │
│                                              │
└──────────────────────────────────────────────┘
```

**Model Selection:**

| Control | Description |
|---|---|
| **Primary Model** | Dropdown for Head + Moderator agents. Use your most capable model here. |
| **Panelist Model Pool** | Multi-select checkbox list. Each panelist is randomly assigned from this pool. Empty = all panelists use the primary model. |
| **🔄 Refresh** | Re-fetches available models from the Copilot SDK |

**Panel Configuration:**

| Setting | Default | Range | Description |
|---|---|---|---|
| **Max Panelists** | 5 | Dropdown | Maximum number of expert panelists in the discussion |
| **Max Turns** | 30 | Dropdown | Total turns across all panelists before forced convergence |
| **Max Duration** | 30 min | Dropdown | Wall-clock time limit for the entire discussion |
| **Convergence %** | 80 | 0–100 | Agreement threshold that triggers automatic synthesis |
| **Commentary Mode** | Brief | Detailed / Brief / Off | How much reasoning the moderator shares (see [Section 7](#7-commentary-mode--controlling-verbosity)) |
| **Discussion Depth** | Auto | Auto / Quick / Standard / Deep | Analysis intensity (see [Section 6](#6-discussion-depth--controlling-analysis-intensity)) |
| **File System Access** | ✅ | Checkbox | Whether panelists can use file system tools |
| **Working Directory** | (empty) | Path | Root directory for file system tools. Empty = default. |

**Pending Changes System:**
- Changes are tracked but **not applied immediately**
- An amber badge appears: `⚠ 3 unsaved change(s)`
- Click **✓ Apply** to save, or **✕ Discard** to revert
- Some settings require a **Reset** to take effect (indicated by a yellow warning)
- The header gear icon shows a red badge with the count of pending changes

**Event Log:**
A reverse-chronological, monospace-formatted log of every significant event:
- Phase transitions
- Agent messages
- Convergence updates
- Tool calls
- Moderation decisions
- Errors

### 4.9 Synthesis Report Overlay

When the discussion converges, a modal overlay presents the synthesis:

```
┌────────────────────────────────────────────────────┐
│  📊 Panel Synthesis Report                    [✕]  │
│                                                    │
│  ## Executive Summary                              │
│  Based on the panel's analysis...                  │
│                                                    │
│  ## Key Findings                                   │
│  1. Architecture: Modular monolith recommended...  │
│  2. Performance: gRPC viable within latency...     │
│  3. Risk: Timeline too tight for full micro...     │
│                                                    │
│  ## Consensus Points                               │
│  - All panelists agree on phased migration...      │
│                                                    │
│  ## Dissenting Views                               │
│  - Devil's Advocate: Consider the null hypothesis  │
│                                                    │
│  ## Recommendations                                │
│  1. Start with modular monolith...                 │
│  2. Extract first service after 6 months...        │
│                                                    │
│                        [📋 Copy]  [✕ Close]        │
└────────────────────────────────────────────────────┘
```

- **Markdown rendered** — full formatting with headers, lists, code blocks, tables
- **📋 Copy** — copies the full synthesis text to clipboard
- **✕ Close** — dismisses the overlay (you can still access the synthesis via follow-up questions)
- **Click backdrop** to dismiss

---

## 5. The Discussion Lifecycle — From Topic to Synthesis

Every panel discussion follows an 11-phase lifecycle:

### Visual Flow

```
                    ┌──────────┐
                    │   IDLE   │ ◄── Ready for a topic
                    └────┬─────┘
                         │ User submits topic
                         ▼
                  ┌──────────────┐
                  │  CLARIFYING  │ ◄── Head asks questions (optional)
                  └──────┬───────┘
                         │ Topic is clear
                         ▼
             ┌───────────────────────┐
             │  AWAITING APPROVAL    │ ◄── Plan ready for review
             └───────────┬───────────┘
                         │ User approves
                         ▼
                   ┌───────────┐
                   │ PREPARING │ ◄── Creating agent sessions
                   └─────┬─────┘
                         │ All agents ready
                         ▼
                   ┌───────────┐
                   │  RUNNING  │ ◄── Panelists debating
                   └─────┬─────┘
                         │                    ┌──────────┐
                         ├───── User pauses ──►│ PAUSED  │
                         │                    └────┬─────┘
                         │                         │ User resumes
                         │◄────────────────────────┘
                         │
                         │ Convergence threshold met
                         ▼
                  ┌──────────────┐
                  │  CONVERGING  │ ◄── Final round of refinement
                  └──────┬───────┘
                         │
                         ▼
                ┌────────────────┐
                │  SYNTHESIZING  │ ◄── Head compiles the report
                └────────┬───────┘
                         │
                         ▼
                   ┌───────────┐
                   │ COMPLETED │ ◄── Synthesis available + follow-up Q&A
                   └───────────┘

    At any point:
    ┌───────────┐       ┌──────────┐
    │  STOPPED  │       │  FAILED  │
    └───────────┘       └──────────┘
    (user clicked Stop)  (unrecoverable error)
```

### Phase-by-Phase Walkthrough

| # | Phase | What Happens | Your Role |
|---|---|---|---|
| 1 | **Idle** | Panel is ready. No active discussion. | Enter a topic in the input box. |
| 2 | **Clarifying** | Head Agent analyzes your topic and may ask questions to narrow scope. | Answer the Head's questions in the left pane. |
| 3 | **AwaitingApproval** | Head presents a discussion plan with selected panelists, depth, and focus areas. | Review and click ✅ Approve or ❌ Reject. |
| 4 | **Preparing** | System creates Copilot sessions for each panelist. Agents initialize. | Wait — this takes 5–15 seconds. |
| 5 | **Running** | Panelists analyze and debate. Messages stream into the center pane. Convergence rises. | Watch, learn, and optionally send messages to the Head. |
| 6 | **Paused** | Discussion frozen. No new turns. | Click ▶ Resume when ready. |
| 7 | **Converging** | Moderator detected sufficient agreement. Final refinement round. | Observe — this is brief. |
| 8 | **Synthesizing** | Head Agent compiles all perspectives into a comprehensive report. | Wait — synthesis takes 10–30 seconds. |
| 9 | **Completed** | Synthesis report displayed. Follow-up Q&A available. | Read the report. Ask follow-up questions. |
| 10 | **Stopped** | User manually stopped the discussion. | Click 🔄 Reset to start fresh. |
| 11 | **Failed** | An unrecoverable error occurred. | Check the event log. Click 🔄 Reset. |

---

## 6. Discussion Depth — Controlling Analysis Intensity

Discussion Depth determines how thoroughly the panel analyzes your topic. It controls the number of debate turns, the convergence threshold, and the overall analysis intensity.

### The Four Depth Levels

| Depth | Icon | Max Turns | Convergence Threshold | Best For |
|---|---|---|---|---|
| **Auto** | 🤖 | (detected) | (detected) | Most topics — let the Head Agent decide |
| **Quick** | ⚡ | 10 | 60% | Simple trade-off questions, quick reviews, time-sensitive decisions |
| **Standard** | 📐 | 30 | 80% | Architecture reviews, design decisions, most analysis tasks |
| **Deep** | 🔬 | 50 | 90% | Complex research, compliance audits, critical architecture decisions |

### How Auto Detection Works

When set to **Auto** (default), the Head Agent analyzes your topic's complexity and selects the appropriate depth. The detected depth appears as a badge in the header bar.

**Factors the Head considers:**
- Topic complexity and scope
- Number of competing trade-offs
- Presence of safety/compliance implications
- Whether the topic spans multiple domains
- Explicit user cues ("quick review" → Quick, "deep analysis" → Deep)

### How to Override

1. Open the **⚙ Side Panel**
2. Under **Panel Configuration**, find **Discussion Depth**
3. Select `Quick`, `Standard`, or `Deep` from the dropdown
4. Click **✓ Apply**

> **Tip:** Use `Auto` for most topics. Override to `Quick` when you're time-pressed, or to `Deep` when the decision is critical and you want maximum rigor.

---

## 7. Commentary Mode — Controlling Verbosity

Commentary Mode controls how much behind-the-scenes reasoning the Moderator agent shares in the discussion stream.

| Mode | What You See | Best For |
|---|---|---|
| **Detailed** | All Moderator reasoning: convergence calculations, turn-order decisions, guard rail checks | Learning how the system works; debugging unexpected behavior |
| **Brief** | Key decisions only: convergence milestones, phase transitions, notable moderation actions | Normal usage — stays informed without noise |
| **Off** | No commentary. Only panelist contributions appear. | Clean reading experience; maximum focus on content |

**Commentary messages** appear in the center pane with a distinctive purple left border and lilac background, making them easy to distinguish from panelist analysis.

### How to Change

1. Open the **⚙ Side Panel**
2. Under **Panel Configuration**, find **Commentary Mode**
3. Select `Detailed`, `Brief`, or `Off`
4. Click **✓ Apply**

---

## 8. Meet the Panelists — Default Expert Profiles

CopilotDesktop includes 8 pre-built expert profiles. The Head Agent selects the most relevant subset for each discussion.

| # | Icon | Name | Expertise | Persona | Priority | Tools |
|---|---|---|---|---|---|---|
| 1 | 🛡️ | **Security Expert** | Identifies vulnerabilities, auth/authz issues, data exposure risks, and compliance gaps | Thorough and cautious — flags risks others overlook | 1 (highest) | ✅ |
| 2 | ⚡ | **Performance Engineer** | Analyzes latency, throughput, memory, algorithmic complexity, and scalability bottlenecks | Data-driven — demands benchmarks and evidence | 2 | ✅ |
| 3 | 🏗️ | **Software Architect** | Evaluates system design, patterns, modularity, extensibility, and technical debt | Strategic thinker — balances ideals with pragmatism | 3 | ✅ |
| 4 | 🧪 | **QA Specialist** | Focuses on testability, edge cases, regression risks, and quality metrics | Detail-oriented — asks "what could go wrong?" | 4 | ✅ |
| 5 | 🔧 | **DevOps Engineer** | Covers deployment, CI/CD, observability, infrastructure, and operational readiness | Practical — cares about what happens at 3 AM | 5 | ✅ |
| 6 | 🎨 | **UX Advocate** | Evaluates user experience, API ergonomics, developer experience, and accessibility | Empathetic — represents the end user's perspective | 6 | ✅ |
| 7 | 📋 | **Domain Expert** | Provides business context, regulatory knowledge, and domain-specific constraints | Knowledgeable — bridges tech and business | 7 | ✅ |
| 8 | 😈 | **Devil's Advocate** | Challenges assumptions, questions premises, and stress-tests conclusions | Contrarian — ensures the panel doesn't fall into groupthink | 8 | ✅ |

### Panelist Colors

Each panelist is assigned a unique color for visual identification in the discussion stream:

| Panelist | Color |
|---|---|
| 🛡️ Security Expert | Red `#D32F2F` |
| ⚡ Performance Engineer | Amber `#F57F17` |
| 🏗️ Software Architect | Indigo `#303F9F` |
| 🧪 QA Specialist | Green `#388E3C` |
| 🔧 DevOps Engineer | Blue-Gray `#455A64` |
| 🎨 UX Advocate | Pink `#C2185B` |
| 📋 Domain Expert | Teal `#00796B` |
| 😈 Devil's Advocate | Deep Purple `#512DA8` |

### How Panelists Are Selected

The Head Agent selects panelists based on:
1. **Topic relevance** — a security audit will prioritize the Security Expert
2. **Diversity** — ensures multiple perspectives are represented
3. **Max panelists setting** — respects the configured limit (default 5)
4. **Priority order** — when all else is equal, lower priority number = selected first

---

## 9. Preset Panels — Ready-Made Expert Teams

For common use cases, CopilotDesktop includes preset panel configurations:

### ⚡ QuickPanel (3 experts)

| Panelist | Why Included |
|---|---|
| 🏗️ Software Architect | Core design perspective |
| 🛡️ Security Expert | Critical safety coverage |
| ⚡ Performance Engineer | Non-functional requirements |

**Best for:** Quick design reviews, focused technical decisions, time-boxed analysis.

### 📐 BalancedPanel (5 experts)

| Panelist | Why Included |
|---|---|
| 🏗️ Software Architect | Design leadership |
| 🛡️ Security Expert | Security coverage |
| ⚡ Performance Engineer | Performance analysis |
| 🧪 QA Specialist | Quality and testability |
| 😈 Devil's Advocate | Assumption-challenging |

**Best for:** Architecture reviews, technology evaluations, comprehensive design decisions.

---

## 10. Convergence — How Consensus Emerges

Convergence is the panel's measure of how much the experts agree. It's displayed as a percentage in the header bar (🎯 badge).

### How It Works

The **Convergence Detector** service analyzes panelist messages and scores agreement:

| Convergence % | Meaning | Visual |
|---|---|---|
| 0–30% | **Divergent** — experts have fundamentally different views | 🎯 Red/low |
| 30–60% | **Emerging** — some common ground, but significant disagreements | 🎯 Amber |
| 60–80% | **Aligning** — broad agreement with minor differences | 🎯 Green |
| 80–100% | **Converged** — strong consensus reached | 🎯 Bright green |

### What Triggers Synthesis

Synthesis is automatically triggered when **either** of these conditions is met:

1. **Convergence threshold reached** — the convergence % meets or exceeds your configured threshold (default 80%)
2. **Max turns exhausted** — the discussion hits the maximum turn count (forced convergence)
3. **Max duration exceeded** — the wall-clock time limit is reached

The Moderator agent monitors these conditions on every turn.

### Adjusting the Threshold

- **Lower threshold (60–70%)** — faster synthesis, but may include more unresolved disagreements
- **Default (80%)** — good balance of thoroughness and efficiency
- **Higher threshold (90%+)** — very thorough, but discussions may run longer

Change it in **⚙ Side Panel → Convergence (%)**.

---

## 11. Guard Rails — Safety & Resource Limits

Every panel discussion is governed by a **Guard Rail Policy** that prevents runaway costs, infinite loops, and unsafe content.

### Default Limits

| Guard Rail | Default | Range | Description |
|---|---|---|---|
| **Max Turns** | 30 | 5–100 | Total turns across all panelists |
| **Max Tokens/Turn** | 4,000 | — | Maximum tokens a single panelist can produce per turn |
| **Max Total Tokens** | 100,000 | 10K–500K | Token budget for the entire discussion |
| **Max Tool Calls/Turn** | 5 | — | Tool calls per panelist per turn |
| **Max Tool Calls Total** | 50 | 10–200 | Tool call budget for the entire discussion |
| **Max Duration** | 30 min | 5–120 min | Wall-clock time limit |
| **Max Single Turn Duration** | 3 min | — | Timeout for any single agent's turn |
| **Max Critique Rounds** | 2 | — | Maximum refine-critique iterations per topic before force-accept |

### What Happens When Limits Are Hit

1. **The Moderator detects the limit violation**
2. **Commentary message** appears in the discussion stream (if Commentary Mode is Brief or Detailed)
3. **Depending on severity:**
   - **Turn limit / duration** → Moderator forces convergence, discussion moves to Synthesizing
   - **Token budget** → Current turn is truncated; discussion forced to converge
   - **Tool call limit** → Further tool calls are blocked for the current turn/discussion
   - **Prohibited content** → Message is blocked and not shown to other panelists

### Resilience Features

| Feature | Description |
|---|---|
| **Tool Circuit Breaker** | If a tool fails repeatedly, it's temporarily disabled to prevent cascading failures |
| **Retry Policy** | Transient failures are retried with exponential backoff |
| **Sandboxed Tool Executor** | Tool calls run in isolation to prevent cross-contamination between agents |
| **Cost Estimation** | Real-time cost tracking displayed in the header (💰 badge) |

---

## 12. Settings Reference

Complete reference for all configurable settings:

### Model Settings

| Setting | Where | Default | Description |
|---|---|---|---|
| Primary Model | Side Panel → Model Selection | (first available) | Model for Head + Moderator agents. Use your best model. |
| Panelist Models | Side Panel → Model Pool | (empty = primary) | Pool of models for panelists. Random assignment. Empty means all use primary. |

### Discussion Settings

| Setting | Where | Default | Range | Description |
|---|---|---|---|---|
| Max Panelists | Side Panel → Panel Config | 5 | Dropdown | Maximum number of expert panelists |
| Max Turns | Side Panel → Panel Config | 30 | Dropdown | Total turn budget |
| Max Duration | Side Panel → Panel Config | 30 min | Dropdown | Wall-clock time limit |
| Convergence Threshold | Side Panel → Panel Config | 80% | 0–100 | Agreement % that triggers synthesis |
| Commentary Mode | Side Panel → Panel Config | Brief | Detailed/Brief/Off | Moderator verbosity |
| Discussion Depth | Side Panel → Panel Config | Auto | Auto/Quick/Standard/Deep | Analysis intensity |
| File System Access | Side Panel → Panel Config | ✅ | Checkbox | Whether agents can use file system tools |
| Working Directory | Side Panel → Panel Config | (empty) | Path | Root for file system tools |

### How Settings Are Applied

1. **Change settings** in the side panel
2. **Pending changes indicator** appears with count
3. Click **✓ Apply** to save
4. Some settings (model, panelists) require a **🔄 Reset** to take effect on the next discussion
5. Click **↩ Defaults** to restore all settings to factory defaults

---

## 13. Phase Reference

| Phase | Badge Color | Status Icon | Description |
|---|---|---|---|
| **Idle** | Gray | 💤 | No active discussion. Ready for input. |
| **Clarifying** | Amber | ❓ | Head Agent is asking you questions about the topic. |
| **AwaitingApproval** | Blue | 📋 | Discussion plan ready for your review. |
| **Preparing** | Purple | ⏳ | Creating agent sessions and initializing panelists. |
| **Running** | Green | 🟢 | Panelists are actively debating. Convergence is rising. |
| **Paused** | Orange | ⏸ | Discussion frozen by user. No turns being processed. |
| **Converging** | Teal | 🎯 | Final refinement round after convergence threshold met. |
| **Synthesizing** | Purple | 📊 | Head Agent is compiling the synthesis report. |
| **Completed** | Green | ✅ | Synthesis available. Follow-up Q&A enabled. |
| **Stopped** | Gray | ⏹ | User manually stopped the discussion. |
| **Failed** | Red | 🔴 | Unrecoverable error. Check event log. |

---

## 14. Agent Role Reference

| Role | Icon | Count | Lifecycle | Responsibilities |
|---|---|---|---|---|
| **Head** | 🎓 | 1 per discussion | Lives for entire discussion | User interaction, clarification, plan building, panelist selection, synthesis, follow-up Q&A |
| **Moderator** | 🛡️ | 1 per discussion | Lives for entire discussion | Guard rail enforcement, convergence detection, turn management, safety filtering |
| **Panelist** | Various | 3–8 per discussion | Lives for entire discussion | Domain-specific analysis, cross-critique, evidence gathering, position refinement |
| **User** | 👤 | 1 | — | Submits topics, answers clarifications, approves plans, asks follow-ups |

### Agent Status States

| Status | Color | Meaning |
|---|---|---|
| **Idle** | Gray | Agent created but not currently active |
| **Active** | Green | Agent is currently producing a response |
| **WaitingForTurn** | Blue | Agent is queued and waiting for its turn |
| **Completed** | Dark Green | Agent has finished all contributions |
| **Failed** | Red | Agent encountered an error |
| **Disposed** | Gray | Agent session has been cleaned up |

---

## 15. Worked Example — Architecture Review

### Scenario
You're leading a team deciding whether to use event sourcing for a new order management system.

---

**Step 1 — Enter the Topic**

```
Should we use event sourcing for our new order management system? 
We expect 10K orders/day, need audit trails, and have a team of 
6 engineers with no prior event sourcing experience.
```

**Step 2 — Head Clarification**

> 🎓 **Head Agent:** "Before I assemble the panel, a few questions:
> 1. What's your current data store? (SQL, NoSQL, mixed?)
> 2. Do you need to support CQRS as well, or just event sourcing?
> 3. What's your deployment target? (Cloud provider, Kubernetes, bare metal?)
> 4. Any existing event infrastructure? (Kafka, RabbitMQ, EventStoreDB?)"

> **You:** "PostgreSQL. Open to CQRS if the panel recommends it. AWS EKS. 
> We have Kafka for messaging but haven't used it for event sourcing."

**Step 3 — Plan & Approve**

> 🎓 **Head Agent:**
> ## Discussion Plan
> **Depth:** Standard (📐 30 turns, 80% convergence)
> 
> **Panelists Selected:**
> | # | Expert | Focus Area |
> |---|---|---|
> | 1 | 🏗️ Software Architect | Event sourcing patterns, CQRS design, aggregate boundaries |
> | 2 | ⚡ Performance Engineer | 10K orders/day throughput, read-model projection latency |
> | 3 | 🔧 DevOps Engineer | Kafka + EventStoreDB ops, EKS deployment, schema evolution |
> | 4 | 🧪 QA Specialist | Testing event-sourced systems, replay testing, snapshot validation |
> | 5 | 😈 Devil's Advocate | Challenge the event sourcing premise, simpler alternatives |

You click **✅ Approve & Start Panel**.

**Step 4 — Watch the Debate**

```
🏗️ Software Architect                                         10:34:22
  Event sourcing is a natural fit for order management — orders have 
  a clear lifecycle (created → confirmed → shipped → delivered) that 
  maps perfectly to an event stream. With 10K orders/day (~0.12/sec),
  the write throughput is modest. I'd recommend...

⚡ Performance Engineer                                        10:34:45
  Agreed on throughput being manageable. But let's talk about read-side
  projections. If you need real-time dashboards, projection lag becomes
  critical. With Kafka + PostgreSQL read models, expect 50-200ms lag...

😈 Devil's Advocate                                            10:35:08
  Before we deep-dive event sourcing, has anyone considered that a 
  simple audit_log table with triggers on a standard CRUD model gives 
  you 90% of the audit trail benefit with 10% of the complexity? 
  The team has zero ES experience and a deadline...

🔧 DevOps Engineer                                             10:35:32
  The Devil raises a good point about operational complexity. Running
  EventStoreDB on EKS adds a stateful workload. Kafka you already have,
  but schema evolution with Avro/Protobuf needs governance from day 1...

🧪 QA Specialist                                               10:35:55
  Testing is my biggest concern. Event-sourced systems need:
  1. Event schema compatibility tests
  2. Projection correctness tests  
  3. Snapshot + replay verification
  None of these exist in your current test suite...

  [Moderator Commentary] 🎯 Convergence: 35% — significant disagreement
  between event sourcing advocates and the simpler-alternatives camp.

🏗️ Software Architect                                         10:36:18
  Fair points from everyone. Let me propose a middle ground: start with
  a hybrid approach. Use standard CRUD with an append-only event log 
  table for audit. This gives you the audit trail immediately. Then 
  migrate to full event sourcing in phase 2 once the team has learned...

😈 Devil's Advocate                                            10:36:42
  Now that's pragmatic. The hybrid approach addresses the team 
  experience gap while preserving optionality. I withdraw my objection
  if we frame this as a phased adoption...

  [Moderator Commentary] 🎯 Convergence: 72% — rapid alignment around 
  the phased approach. Continuing for refinement...
```

**Step 5 — Synthesis Report**

> ## 📊 Panel Synthesis Report
> 
> ### Executive Summary
> The panel recommends a **phased approach** to event sourcing for the order management system, starting with a CRUD + audit trail hybrid and evolving toward full event sourcing.
> 
> ### Consensus Points (92% agreement)
> 1. **Phase 1 (Months 1–3):** Standard CRUD with PostgreSQL + append-only event log table for audit trails
> 2. **Phase 2 (Months 4–6):** Introduce CQRS read models using Kafka projections from the event log
> 3. **Phase 3 (Month 7+):** Full event sourcing with EventStoreDB if Phase 2 validates the pattern
> 
> ### Key Trade-offs
> | Factor | Full ES Now | Phased Approach |
> |---|---|---|
> | Team risk | High (no experience) | Low (learn incrementally) |
> | Audit trail | Immediate | Immediate |
> | Complexity | High from day 1 | Incremental |
> | Time to production | 4+ months | 2 months for Phase 1 |
> 
> ### Dissenting Views
> - ⚡ Performance Engineer notes that the phased approach may require data migration between phases
> - 🏗️ Architect recommends designing Phase 1 tables with ES migration in mind (append-only, immutable events)
> 
> ### Recommendations
> 1. Design the event log table as if it were an event store (immutable, append-only, with version numbers)
> 2. Invest in Kafka consumer literacy during Phase 1
> 3. Budget 2 sprints for the Phase 1 → Phase 2 migration
> 4. Add event schema compatibility testing from Phase 1

**Step 6 — Follow-Up**

```
You: "What should the event log table schema look like for Phase 1?"
```

The Head Agent answers with full context from the entire panel discussion.

---

## 16. Worked Example — Security Audit

### Scenario
You want the panel to audit a REST API's authentication and authorization implementation.

**Topic:**
```
Audit the authentication and authorization implementation in our 
REST API. We use JWT tokens with RS256, role-based access control, 
and API key authentication for service-to-service calls. The API 
handles PII data and must comply with SOC 2.
```

**Panel Selected:** 🛡️ Security Expert (lead), 🏗️ Architect, 📋 Domain Expert (compliance), 🧪 QA Specialist, 😈 Devil's Advocate

**Discussion Highlights:**

```
🛡️ Security Expert                                            14:02:10
  Let me start with the JWT implementation. Key concerns:
  1. Token lifetime — what's the expiry? Anything > 15 min needs refresh
  2. RS256 key rotation — how often? Where are keys stored?
  3. Token revocation — JWTs are stateless; how do you invalidate?
  4. Are you checking 'aud' and 'iss' claims?

📋 Domain Expert                                               14:02:35
  For SOC 2 compliance, we need to verify:
  - All PII access is logged with immutable audit trails
  - Service-to-service API keys have rotation schedules
  - There's a formal access review process for RBAC roles
  - Encryption at rest and in transit for all PII fields

😈 Devil's Advocate                                            14:03:01
  Has anyone verified that the "role-based" access control is actually
  role-based and not just permission flags masquerading as RBAC? I've
  seen many APIs claim RBAC but implement flat permission checks...
```

**Synthesis Output:** A structured audit report with findings categorized by severity (Critical / High / Medium / Low), SOC 2 compliance gaps, and a prioritized remediation plan.

---

## 17. Worked Example — Deep Research Question

### Scenario
You're evaluating database technologies for a time-series IoT platform.

**Topic:**
```
Deep analysis: Which time-series database should we use for an IoT 
platform ingesting 1M data points/second from 100K sensors? Evaluate 
TimescaleDB, InfluxDB, QuestDB, and ClickHouse. We need 90-day hot 
storage, 2-year cold storage, and sub-second query latency for 
dashboards. Budget: $5K/month infrastructure.
```

**Depth Override:** 🔬 Deep (50 turns, 90% convergence)

**Panel Selected:** ⚡ Performance Engineer (lead), 🏗️ Architect, 🔧 DevOps Engineer, 💰 Domain Expert (cost analysis), 😈 Devil's Advocate

This discussion runs longer, with multiple rounds of analysis, benchmark comparisons, and cost modeling. The synthesis includes a detailed comparison matrix and a recommended architecture with specific configuration guidance.

---

## 18. Best Practices & Tips

### Writing Effective Topics

| ✅ Do | ❌ Don't |
|---|---|
| Be specific about constraints (timeline, team size, budget) | Submit vague topics ("review my code") |
| Mention relevant technologies and versions | Assume the panel knows your stack |
| State the decision you need to make | Ask open-ended questions with no focus |
| Include success criteria or non-functional requirements | Omit critical constraints |
| Mention compliance/regulatory requirements if relevant | Forget to mention security sensitivity |

**Good topics:**
- ✅ "Should we migrate from REST to gRPC for our internal services? Team of 12, .NET 8, latency budget is 20ms p99."
- ✅ "Review our caching strategy: Redis for sessions, Memcached for API responses, CDN for static assets. 50K concurrent users."
- ✅ "Evaluate authentication approaches for our mobile app: OAuth2 + PKCE vs. magic links vs. passkeys. SOC 2 required."

**Weak topics:**
- ❌ "Is microservices good?" (too vague)
- ❌ "Fix my code" (not a discussion topic — use the Chat tab)
- ❌ "Everything about Kubernetes" (no decision to make, no constraints)

### Optimizing Discussion Settings

| Goal | Depth | Panelists | Convergence | Commentary |
|---|---|---|---|---|
| Quick decision gut-check | Quick | 3 | 60% | Off |
| Standard architecture review | Standard | 5 | 80% | Brief |
| Critical security/compliance audit | Deep | 5–7 | 90% | Detailed |
| Exploratory research | Deep | 5 | 70% | Brief |
| Time-boxed meeting replacement | Quick | 3–4 | 60% | Off |

### During the Discussion

- **Watch the convergence indicator** — it tells you how close the panel is to consensus
- **Use the Agent Inspector** — click on the most active agent to understand their reasoning
- **Commentary mode = Brief** is the sweet spot — you see key decisions without noise
- **Don't interrupt unless needed** — the panel self-manages through the Moderator
- **Send messages to the Head** if you want to redirect the discussion

### After the Discussion

- **Copy the synthesis** — it's the most valuable artifact
- **Ask follow-up questions** — the Head retains full context
- **Start a new panel** for a follow-up topic — the panels are independent
- **Check the event log** if anything seemed unexpected

### Model Selection Strategy

| Agent | Recommended Model Tier | Why |
|---|---|---|
| Head + Moderator (Primary) | Best available (GPT-4o, Claude 3.5 Sonnet) | They orchestrate, synthesize, and make meta-decisions |
| Panelists | Good tier (GPT-4o-mini, Claude Haiku) | They analyze and debate; quantity matters more than individual brilliance |

**Cost optimization:** Using a cheaper model for the panelist pool and a premium model for the Primary can reduce costs by 50–70% with minimal quality impact.

---

## 19. How Panel Discussion Differs from Agent Team & Agent Office

| Aspect | 💬 Panel Discussion | 👥 Agent Team | 🏢 Agent Office |
|---|---|---|---|
| **Purpose** | Multi-expert debate & synthesis | Parallel task execution | Continuous monitoring & delegation |
| **Agents** | Experts with personas + opinions | Workers with specialized roles | Manager + ephemeral assistants |
| **Interaction Pattern** | Debate → converge → synthesize | Plan → execute → consolidate | Loop: fetch → delegate → report → rest |
| **Agent Communication** | Agents see and critique each other | Agents work independently | Assistants report to Manager only |
| **Output** | Synthesis report with consensus + dissent | Consolidated work product | Iteration reports with recommendations |
| **Lifecycle** | Single discussion → completion | Single batch → completion | Continuous loop until stopped |
| **User Role** | Submit topic, approve, read synthesis | Submit task, approve plan, review | Define mission, approve, inject mid-run |
| **Best For** | Analysis, decisions, reviews, research | Refactoring, test gen, code changes | Incident monitoring, PR review, triage |
| **Duration** | 2–30 minutes | 5–60 minutes | Hours to days |
| **Key Metric** | Convergence % | Task completion % | Iteration count + success rate |

**Decision Guide:**
- 🤔 **"I need multiple perspectives on a decision"** → **Panel Discussion**
- 🔨 **"I need to execute a complex multi-step task"** → **Agent Team**
- 🔄 **"I need ongoing, periodic monitoring"** → **Agent Office**

---

## 20. Troubleshooting

### Common Issues

| Symptom | Likely Cause | Resolution |
|---|---|---|
| Start button does nothing | No active Copilot session | Check health indicator; re-authenticate |
| Head never asks questions, goes straight to plan | Topic was clear enough | This is normal — clarification is optional |
| Discussion feels shallow | Depth set to Quick or Auto-detected as simple | Override to Standard or Deep in settings |
| Convergence stuck at 0% | Panelists fundamentally disagree | Wait for more turns; the Moderator will guide convergence. Or stop and narrow the topic. |
| Convergence stuck at 100% | Topic too simple for multi-agent debate | Expected for straightforward questions. Use Chat tab instead. |
| "PENDING — Reset to apply" won't go away | Changed settings that require a session restart | Click 🔄 Reset, then start a new discussion |
| Side panel won't open | UI state conflict | Click the ⚙ gear icon again; switch tabs and back if needed |
| Agent Inspector shows no agents | Discussion hasn't started yet | Agents appear after the Preparing phase completes |
| Synthesis overlay is empty | Synthesis still generating | Wait for the Synthesizing phase to complete |
| Discussion stops unexpectedly | Guard rail limit hit (turns, duration, tokens) | Check event log for the specific limit. Increase limits in settings. |
| Panelists all say the same thing | Topic has clear consensus | This is valid — the panel agrees. Check dissenting views in synthesis. |
| Cost seems high | Many turns + expensive model | Use cheaper models in panelist pool. Reduce depth. Lower max turns. |

### Performance Considerations

- **5+ panelists** with Deep depth can generate 50+ LLM calls — factor in API quota
- **File system tools** add latency if agents are reading large files
- **Commentary Mode = Detailed** increases moderator output but adds informational value
- **Very long discussions** (50+ turns) produce large context — synthesis quality may degrade slightly

### Error Recovery

1. **Single agent failure:** The Moderator skips the failed agent's turn and continues. The agent may recover on later turns.
2. **Head Agent failure:** The phase transitions to Failed. Check event log and Reset.
3. **Network disconnection:** The system attempts reconnection with context replay. If it fails, the phase transitions to Failed.
4. **Tool circuit breaker open:** A frequently-failing tool is temporarily disabled. Other tools continue working. The circuit breaker resets after a cooldown period.

---

## 21. Glossary

| Term | Definition |
|---|---|
| **Panel Discussion** | A structured multi-expert AI debate on a user-submitted topic |
| **Head Agent** | The primary agent that manages user interaction, builds discussion plans, and produces the final synthesis |
| **Moderator** | The behind-the-scenes agent that enforces guard rails, detects convergence, and manages turn order |
| **Panelist** | An AI agent with a specific expertise persona (e.g., Security Expert, Performance Engineer) |
| **Discussion Depth** | The configured intensity level: Quick (10 turns), Standard (30 turns), or Deep (50 turns) |
| **Commentary Mode** | How much Moderator reasoning is visible: Detailed, Brief, or Off |
| **Convergence** | A percentage (0–100%) measuring how much the panelists agree on key points |
| **Convergence Threshold** | The agreement percentage that triggers automatic synthesis (default 80%) |
| **Synthesis** | The Head Agent's comprehensive report combining all panel perspectives into consensus findings, dissenting views, and recommendations |
| **Guard Rail Policy** | Safety and resource limits that prevent runaway discussions (turn limits, token budgets, time caps) |
| **Tool Circuit Breaker** | Resilience pattern that temporarily disables a frequently-failing tool |
| **Panelist Profile** | A predefined expert persona with name, expertise area, personality, icon, and color |
| **Preset Panel** | A pre-configured combination of panelists (e.g., QuickPanel, BalancedPanel) |
| **Follow-Up Q&A** | The ability to ask the Head Agent additional questions after synthesis, with full debate context retained |
| **Three-Pane Layout** | The UI layout: Left (Head Chat), Center (Discussion Stream), Right (Agent Inspector) |
| **Execution Bar** | The pulsing status strip below the header showing real-time execution status, discussion mode badge, active agent, and parallel indicator |
| **Side Panel** | Fly-in settings panel with model selection, configuration, and event log |
| **Phase** | A discrete stage in the discussion lifecycle (Idle, Clarifying, Running, Synthesizing, etc.) |
| **Turn** | One panelist's contribution to the discussion (a single message in the debate) |
| **Agent Inspector** | The right pane that shows per-agent details: status, message count, tool calls, last message |

---

## 22. Frequently Asked Questions

**Q: How many panelists should I use?**
A: For most topics, 4–5 is optimal. Three feels thin; more than 6 can be noisy without proportional insight gain. Start with the BalancedPanel preset (5) and adjust from there.

**Q: Does the panel remember previous discussions?**
A: No. Each panel discussion is independent. Reset clears all context. If you need continuity, reference previous findings in your new topic description.

**Q: Can I change panelists mid-discussion?**
A: Not during a running discussion. You'd need to Stop or Reset and start a new discussion with different panelist preferences.

**Q: What happens if I reject the plan?**
A: The Head Agent receives your rejection and any feedback you type. It revises the plan and presents a new one for approval. You can reject as many times as needed.

**Q: Can panelists use tools (file reading, web search, etc.)?**
A: Yes, if enabled in settings. All 8 default profiles have tools enabled. The Guard Rail Policy limits tool calls per turn (5) and per discussion (50) to prevent abuse.

**Q: How is cost calculated?**
A: The 💰 badge shows an estimate based on token usage across all agents. Actual cost depends on your Copilot plan. A typical Standard-depth discussion with 5 panelists costs roughly the equivalent of 15–30 individual chat messages.

**Q: Can I use Panel Discussion for code review?**
A: Absolutely. Paste the code or reference a file path in your topic. Enable file system access so panelists can read the actual code. Example: "Review the authentication middleware in `src/middleware/auth.ts` — focus on security, error handling, and testability."

**Q: Why does the Head Agent sometimes skip clarification?**
A: If your topic is specific enough, the Head determines that clarification is unnecessary and proceeds directly to planning. This is intentional and saves time.

**Q: What's the difference between Pause and Stop?**
A: **Pause** freezes the discussion — you can Resume to continue from where you left off. **Stop** terminates the discussion permanently — to continue, you must Reset and start over.

**Q: Can I send messages to the Head during the discussion?**
A: Yes. Use the input bar to send messages while the discussion is Running. The Head Agent processes your message and may adjust the discussion direction.

**Q: Why would I set Commentary Mode to Detailed?**
A: To understand the Moderator's reasoning — useful when debugging why convergence isn't rising, or when learning how the system works. For everyday use, Brief is sufficient.

**Q: What if I want more than 8 panelists?**
A: The default Max Panelists setting caps at 8 (matching the 8 built-in profiles). In practice, 5–6 is the sweet spot for quality vs. cost.

**Q: Can I use different models for different panelists?**
A: Yes. The Panelist Model Pool supports multi-select. Each panelist is randomly assigned from the selected pool. This lets you mix premium and economy models.

---

## 23. Appendix — Keyboard Shortcuts

| Shortcut | Context | Action |
|---|---|---|
| **Enter** | Input focused, no Shift | Send message / start panel |
| **Shift+Enter** | Input focused | New line (multi-line input) |
| **Escape** | Side panel open | Close side panel |
| **Escape** | Synthesis overlay open | Dismiss synthesis |

---

<p align="center">
  <em>CopilotDesktop Panel Discussion — Assemble the experts. Debate the hard questions. Ship with confidence.</em><br/>
  <strong>© 2026 CopilotDesktop. All rights reserved.</strong>
</p>