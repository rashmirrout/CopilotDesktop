# Copilot Agent Desktop

> **What if your Copilot could do more than chat?**

Copilot Agent Desktop is a production-grade Windows application that turns the [GitHub Copilot SDK](https://github.com/github/copilot-sdk) into a full agentic platform — right on your desktop. Think of it as your personal AI command center: chat with Copilot, hand off complex tasks to a team of specialized agents, or spin up an autonomous office that monitors your repos around the clock.

### 💬 Chat — A Claude-like agent experience over Copilot
Multi-session, tool-enabled, terminal-embedded conversations with full MCP server support and fine-grained tool approval. Create sessions from GitHub issues, attach skills, and persist everything across restarts.

### 👥 Team — Divide and conquer with multi-agent orchestration
Got a task too big for one agent? The **Agent Team** breaks it down. An orchestrator decomposes your request into dependency-aware work chunks, spins up role-specialized workers (CodeAnalysis, Testing, Implementation, Synthesis…), runs them in parallel across isolated workspaces, and delivers a consolidated report. You review the plan, approve it, and watch the workers execute — or inject new instructions mid-flight.

### 🏢 Office — Your own AI operations center
Set up a small office for yourself. A long-running **Manager agent** periodically scans for events — incoming tickets, support requests, PR reviews, incident alerts — and delegates tasks to a pool of ephemeral **Assistant agents**. It clarifies ambiguous instructions, schedules work, aggregates results, rests, and repeats. Pause it, change the interval, inject new priorities, or just let it run. It's like having a tireless ops team that never sleeps.

### 🎙 Panel — Multi-expert AI debate for deep analysis
Assemble a panel of AI experts — each with unique expertise and personality — to analyze, debate, and synthesize insights on any topic. A **Head Agent** clarifies your question, selects 3–8 domain specialists (Security, Performance, Architecture, QA, DevOps, UX, Devil's Advocate…), and launches a moderated discussion. A **Moderator** enforces guard rails and tracks convergence. When the experts reach consensus, the Head delivers a comprehensive synthesis report with consensus points, dissenting views, and actionable recommendations. Ask follow-up questions with full debate context retained.

### 🔄 Iterative — Self-evaluating task loops
Define a goal and success criteria. The agent executes, evaluates its own output, and iterates until it succeeds or hits the limit. Perfect for tasks that need refinement — code generation, test fixing, quality gates.

Built with **.NET 8**, **WPF-UI (Fluent Design)**, and ships as a **single portable executable** — no installation required.

[![GitHub Copilot SDK](https://img.shields.io/nuget/v/GitHub.Copilot.SDK?label=GitHub.Copilot.SDK)](https://www.nuget.org/packages/GitHub.Copilot.SDK)

---

## Features

### 💬 Agent Chat
- 🎯 **Multi-Session Management** — Independent Copilot agent sessions with separate contexts
- 🌳 **Git Worktree Sessions** — Create sessions from GitHub issues with automatic worktree setup
- 💻 **Embedded Terminal** — Full PTY terminal with output capture using Pty.Net
- 🔒 **Tool Approval System** — Fine-grained approval dialogs for tool execution with session/global rules
- 🔌 **MCP Server Support** — Model Context Protocol integration with live session view
- 📚 **Skills/Plugins** — SKILL.md support for custom agent capabilities
- 🔄 **Iterative Agent Mode** — Self-evaluating task runner with success criteria
- 💾 **Session Persistence** — Full chat history and settings persistence

### 👥 Agent Team — Multi-Agent Orchestration
- 🧠 **Manager–Worker Pattern** — An orchestrator agent decomposes complex tasks into parallel work chunks delegated to specialized worker agents
- 📋 **Interactive Planning** — Human-in-the-loop workflow: the orchestrator clarifies requirements, generates a dependency-aware plan, and awaits your approval before executing
- ⚡ **Parallel Execution with DAG Scheduling** — Work chunks are topologically sorted by dependencies and executed in parallel stages (up to configurable concurrency)
- 🎭 **Role-Specialized Workers** — Workers are assigned roles (CodeAnalysis, MemoryDiagnostics, Testing, Implementation, Synthesis, etc.) with tailored system prompts and model overrides
- 🔀 **Workspace Isolation** — Git Worktree, File Locking, or In-Memory strategies keep concurrent workers from conflicting
- 💉 **Live Injection** — Inject instructions to the orchestrator mid-execution; workers absorb changes on the fly
- 🏷️ **Ephemeral Worker Status Bar** — Compact pills auto-appear during execution showing per-worker progress, then auto-hide on completion
- 📊 **Consolidated Reports** — A Synthesis agent aggregates all worker results into a cohesive summary with actionable recommendations
- ⚙️ **Side Panel Settings** — Slide-in panel with model selection, working directory, orchestration tuning, dirty-state tracking with Apply/Discard, and event log

### 🏢 Agent Office — Autonomous Operations Center
- 🔁 **Continuous Manager Loop** — A long-running Manager agent periodically checks for events, delegates to a finite pool of ephemeral Assistants, aggregates results, rests, and repeats
- 💬 **Rich Chat Interface** — Full-width scrollable chat plane with Markdown rendering, foldable iteration containers, color-coded Manager/Assistant messages, and inline plan approval
- 🎛️ **Dynamic Controls** — Change check interval, pause/resume, inject new instructions, or reset the session — all without stopping the loop
- 📡 **Live Commentary Side Panel** — Real-time auto-scrolling stream showing what the Manager and each Assistant are doing as it happens (🔵 Planning, 🟢 Discovery, 🟠 Working, ✅ Success, ❌ Error)
- ⏱️ **Rest Period Countdown** — Visual countdown timer between iterations with progress bar
- 🤖 **Clarification-Aware Injection** — When you inject an ambiguous instruction mid-run, the Manager asks clarifying questions inline in the chat, then queues the refined instruction for the next iteration
- 📈 **Iteration Statistics** — Track completed iterations, total tasks, success rate, and average duration
- 🗂️ **Event Log & Scheduling Decisions** — Structured log of every phase transition, task assignment, queue event, and assistant lifecycle change

### 🎙 Panel Discussion — Multi-Expert AI Debate
- 🎓 **Head Agent + Moderator + Panelists** — A Head Agent clarifies your topic and builds a discussion plan, a Moderator enforces guard rails and tracks convergence, and 3–8 AI panelists debate with distinct expertise and personalities
- 🗣️ **Three-Pane Layout** — Left (Head Agent chat), Center (live discussion stream with Markdown rendering), Right (Agent Inspector with per-agent stats, tool calls, and status)
- 🎯 **Convergence Detection** — Real-time convergence percentage (0–100%) measures how much experts agree; synthesis triggers automatically when the threshold is met
- 📊 **Synthesis Report** — Comprehensive report with executive summary, consensus points, dissenting views, and prioritized recommendations — copyable and Markdown-rendered
- ⚡ **Discussion Depth** — Auto-detected or manually set: Quick (10 turns), Standard (30 turns), or Deep (50 turns) with matching convergence thresholds
- 🛡️ **Guard Rails** — Turn limits, token budgets, duration caps, tool call limits, and content safety policies prevent runaway costs
- 🔌 **Tool-Enabled Panelists** — Experts can read files, browse code, and use MCP tools with sandboxed execution and circuit breakers
- 💬 **Follow-Up Q&A** — After synthesis, ask the Head Agent follow-up questions with full debate context retained
- 🎭 **8 Built-In Expert Profiles** — Security Expert, Performance Engineer, Software Architect, QA Specialist, DevOps Engineer, UX Advocate, Domain Expert, Devil's Advocate
- ⚙️ **Side Panel Settings** — Model selection (primary + panelist pool), panel configuration with dirty-state tracking, commentary mode (Detailed/Brief/Off), and event log

### 🎨 General
- 🎨 **Modern UI** — Fluent Design with WPF-UI (Windows 11 style)
- 📦 **Single Executable** — Self-contained deployment, no installation required

---

## How to Use

### Quick Start (5 Steps)

1. **Complete Prerequisites** — If you already use the Copilot CLI and are logged in, you're all set. Otherwise, follow the [installation guide](#installing-the-copilot-cli) below.

2. **Download Latest Release** — [![Download](https://img.shields.io/github/v/release/rashmirrout/CopilotDesktop?label=Latest%20Release)](https://github.com/rashmirrout/CopilotDesktop/releases)

3. **Authenticate with Copilot** — Run this once in your terminal:
   ```bash
   copilot login
   ```

4. **Start the Application** — Double-click `CopilotAgent.exe`

5. **Explore Possibilities** — Create sessions, chat with Copilot, orchestrate agent teams, run autonomous office loops, launch panel discussions, and more!

### Feature Quick Guide

| Tab | What It Does | When to Use |
|-----|-------------|-------------|
| **💬 Agent** | Single-session Copilot chat with tools, terminal, and MCP | Day-to-day coding tasks, file editing, debugging |
| **👥 Team** | Multi-agent orchestration with parallel workers | Complex tasks that benefit from decomposition — multi-file refactors, cross-module analysis, parallel code reviews |
| **🏢 Office** | Autonomous periodic manager with assistant pool | Long-running monitoring — incident management, scheduled audits, multi-repo PR reviews |
| **🎙 Panel** | Multi-expert AI debate with convergence & synthesis | Architecture decisions, security audits, technology evaluations, design reviews, deep research |
| **🔄 Iterative** | Self-evaluating task loop with success criteria | Tasks requiring iterative refinement until a goal is met |

---

### Screenshots
<img width="1184" height="790" alt="image" src="https://github.com/user-attachments/assets/bdbb3457-97d7-44ea-a9bc-dda50904650f" />
<img width="1774" height="1185" alt="image" src="https://github.com/user-attachments/assets/b3dfc4a0-fe3b-4daa-922d-92545dc0c1a2" />

---

## Architecture

```
CopilotAgent.sln
├── src/
│   ├── CopilotAgent.App/             # WPF application with MVVM
│   ├── CopilotAgent.Core/            # Core services, models, and shared interfaces
│   ├── CopilotAgent.MultiAgent/      # Agent Team orchestration engine
│   ├── CopilotAgent.Office/          # Agent Office manager loop engine
│   ├── CopilotAgent.Panel/           # Panel Discussion debate engine
│   └── CopilotAgent.Persistence/     # JSON file storage
└── tests/
    └── CopilotAgent.Tests/           # Unit tests (xUnit)
```

### Project Dependencies

```
CopilotAgent.App
├── CopilotAgent.Core
├── CopilotAgent.MultiAgent
├── CopilotAgent.Office
├── CopilotAgent.Panel
└── CopilotAgent.Persistence

CopilotAgent.MultiAgent ──► CopilotAgent.Core
CopilotAgent.Office ──► CopilotAgent.Core
CopilotAgent.Panel ──► CopilotAgent.Core
CopilotAgent.Persistence ──► CopilotAgent.Core
```

---

## Prerequisites

### Required

- **.NET 8.0 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Windows 10/11** — WPF desktop application
- **GitHub Copilot CLI** — Required for SDK communication
- **GitHub Copilot Subscription** — Required for API access (free tier available)

### Installing the Copilot CLI

Follow the [Copilot CLI installation guide](https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli) or ensure `copilot` is available in your PATH.

The SDK communicates with the Copilot CLI in server mode via JSON-RPC:

```
Your Application
       ↓
  SDK Client (GitHub.Copilot.SDK)
       ↓ JSON-RPC
  Copilot CLI (server mode)
```

### Authentication

The SDK supports multiple authentication methods:
- **GitHub signed-in user** — Uses stored OAuth credentials from `copilot` CLI login
- **OAuth GitHub App** — Pass user tokens from your GitHub OAuth app
- **Environment variables** — `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_TOKEN`
- **BYOK (Bring Your Own Key)** — Use your own API keys from supported LLM providers (OpenAI, Azure AI, Anthropic)

For BYOK setup, see the [BYOK documentation](https://github.com/github/copilot-sdk/blob/main/docs/auth/byok.md).

### Optional

- **Visual Studio 2022** or **VS Code** — For development
- **Git** — For worktree session support

---

## Building

### Quick Start

```bash
# Clone and build
git clone https://github.com/rashmirrout/CopilotDesktop.git
cd CopilotDesktop
dotnet restore
dotnet build
```

### Development Build

```bash
# Restore dependencies
dotnet restore

# Build solution (Debug)
dotnet build CopilotAgent.sln

# Run application
dotnet run --project src/CopilotAgent.App

# Or use the batch file
.\run-app.bat

# Run tests
dotnet test
```

### Visual Studio

1. Open `CopilotAgent.sln`
2. Build → Build Solution (Ctrl+Shift+B)
3. Debug → Start Debugging (F5)

---

## Publishing

### Self-Contained Single Executable (Recommended)

Creates a portable executable that includes the .NET runtime — no installation required on target machine.

```bash
# Publish for Windows x64
dotnet publish src/CopilotAgent.App/CopilotAgent.App.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -o publish/win-x64
```

**Output:** `publish/win-x64/CopilotAgent.exe` (~70 MB)

### Framework-Dependent (Smaller Size)

Requires .NET 8.0 runtime to be installed on target machine.

```bash
dotnet publish src/CopilotAgent.App/CopilotAgent.App.csproj \
    -c Release \
    -r win-x64 \
    --self-contained false \
    -o publish/win-x64-fd
```

**Output:** `publish/win-x64-fd/CopilotAgent.exe` (~5 MB)

---

## Documentation

| Document | Description |
|----------|-------------|
| 📐 **[Project Structure](docs/PROJECT_STRUCTURE.md)** | Solution layout, core models, services, multi-agent and office service interfaces, MVVM architecture |
| 🔗 **[SDK Integration](docs/SDK_INTEGRATION.md)** | GitHub Copilot SDK integration points, tool approval pipeline, MCP server setup, authentication methods |
| ⚙️ **[Configuration](docs/CONFIGURATION.md)** | Application settings, tool approval rules, MCP config, session data, environment variables |
| 🏢 **[Agent Office User Guide](docs/AGENT_OFFICE_USER_GUIDE.md)** | End-to-end guide for the autonomous office manager — setup, lifecycle, features, worked examples, troubleshooting |
| 🏢 **[Agent Office Design](docs/AGENT_OFFICE_DESIGN.md)** | Detailed design for the autonomous office manager loop |
| 👥 **[Agent Team User Guide](docs/AGENT_TEAMS_USER_GUIDE.md)** | End-to-end guide for multi-agent orchestration — planning, execution, injection, side panel, worked examples |
| 👥 **[Agent Team Design](docs/MULTI_AGENT_ORCHESTRATOR_DESIGN.md)** | Detailed design for the multi-agent orchestration engine |
| 🎙 **[Panel Discussion User Guide](docs/PANEL_DISCUSSION_USER_GUIDE.md)** | End-to-end guide for multi-expert AI debate — interface walkthrough, lifecycle, depth modes, worked examples |
| 🎙 **[Panel Architecture Design](docs/PANEL_ARCHITECTURE_DESIGN.md)** | Detailed design for the panel discussion engine — state machine, agents, convergence, guard rails |

---

## Usage

### Creating a Session

1. **New Session** — Blank session with optional working directory
2. **From Repository** — Session from existing local repository
3. **From GitHub Issue** — Creates worktree session from issue URL

### Tool Approval

When a tool is invoked, you can:
- **Approve Once** — Allow this specific invocation
- **Approve for Session** — Remember for current session
- **Approve Globally** — Remember across all sessions
- **Deny** — Block the tool execution

Manage saved rules in Settings → Manage Tool Approvals.

### Agent Team (👥)

1. **Submit a task** — Describe a complex task (e.g., "Debug memory leaks in the image pipeline and cache module")
2. **Answer clarifications** — The orchestrator may ask questions to refine the plan
3. **Review the plan** — See the decomposed work chunks, dependencies, stages, and assigned roles
4. **Approve & execute** — Workers run in parallel; watch progress via the ephemeral status bar
5. **Inject instructions** — Toggle injection mode (💉) to send live instructions to the orchestrator mid-execution
6. **Get the report** — A Synthesis agent consolidates all worker findings into an actionable summary
7. **Follow up** — Ask follow-up questions in the same context

**Side panel** (⚙️ gear icon) provides model selection, working directory, orchestration settings with dirty-state tracking, and an event log.

### Agent Office (🏢)

1. **Write a master prompt** — Describe what the Manager should monitor (e.g., "Analyze open incidents for Team Alpha every 5 minutes")
2. **Answer clarifications** — The Manager asks what it needs to understand your objective
3. **Approve the plan** — Review the Manager's execution strategy
4. **Watch the loop** — The Manager fetches events, schedules tasks to assistants, aggregates results, and rests on a countdown timer
5. **Inject instructions** — Send new instructions mid-run; the Manager may ask clarifying questions inline before queuing the refined instruction
6. **Use the side panel** — Live commentary stream, configuration controls (interval, pool size), event log, and iteration statistics

### Panel Discussion (🎙)

1. **Submit a topic** — Describe a decision, architecture question, or analysis task (e.g., "Should we migrate from REST to gRPC for our internal services?")
2. **Answer clarifications** — The Head Agent may ask questions to narrow scope
3. **Review the plan** — See selected panelists, discussion depth, focus areas, and estimated turns
4. **Approve & watch** — Panelists debate in real time in the center pane; convergence % rises in the header
5. **Read the synthesis** — When convergence meets the threshold, a comprehensive report is generated with consensus, dissenting views, and recommendations
6. **Ask follow-ups** — Continue the conversation with the Head Agent, who retains full debate context

**Side panel** (⚙️ gear icon) provides model selection (primary + panelist pool), panel configuration (depth, turns, convergence threshold, commentary mode), and an event log.

### MCP Servers Tab

- View **live MCP servers** from the active SDK session
- See server status (running, error, unknown)
- Browse available tools and their parameters
- Toggle servers on/off for session

### Skills (SKILL.md)

Place skill definitions in:
- `%USERPROFILE%\.CopilotDesktop\skills\` (personal)
- `%USERPROFILE%\.copilot\skills\` (SDK shared)
- `<working-directory>\SKILL.md` (repo-specific)

### Iterative Agent Mode

1. Define task description and success criteria
2. Set max iterations
3. Agent executes → evaluates → repeats until success or max iterations

---

## Technology Stack

| Component | Technology |
|-----------|------------|
| UI Framework | WPF + .NET 8 |
| Modern UI | WPF-UI (Fluent Design) |
| MVVM | CommunityToolkit.Mvvm |
| DI Container | Microsoft.Extensions.DependencyInjection |
| Copilot SDK | GitHub.Copilot.SDK |
| Terminal | Pty.Net |
| Markdown | Markdig.Wpf / MdXaml |
| Code Highlighting | AvalonEdit |
| Concurrency | SemaphoreSlim, ConcurrentDictionary, Task.WhenAll |
| Serialization | System.Text.Json |
| Logging | Serilog + Microsoft.Extensions.Logging |
| Testing | xUnit + Moq + FluentAssertions |

---

## FAQ

### Do I need a GitHub Copilot subscription?

Yes, unless using BYOK (Bring Your Own Key). With BYOK, you can use your own API keys from supported LLM providers. See [GitHub Copilot pricing](https://github.com/features/copilot#pricing) for free tier details.

### How does billing work?

Based on the same model as Copilot CLI, with each prompt counted towards your premium request quota. See [Requests in GitHub Copilot](https://docs.github.com/en/copilot/concepts/billing/copilot-requests).

### What tools are enabled by default?

All first-party Copilot tools are enabled, including file system operations, Git operations, and web requests. The app provides a tool approval layer to control execution.

### What models are supported?

All models available via Copilot CLI are supported. The SDK exposes a method to list available models at runtime. In Agent Team, Agent Office, and Panel Discussion, you can select different models for the manager/orchestrator and workers/assistants/panelists.

### What's the difference between Agent Team, Agent Office, and Panel Discussion?

| Aspect | Agent Team | Agent Office | Panel Discussion |
|--------|-----------|--------------|-----------------|
| Purpose | Parallel task execution | Continuous monitoring & delegation | Multi-expert debate & synthesis |
| Lifecycle | One-shot: submit → plan → execute → done | Continuous loop: check → delegate → report → rest → repeat | Single discussion: topic → debate → synthesize → done |
| Agents | Workers with specialized roles | Manager + ephemeral assistants | Head + Moderator + expert panelists |
| Agent Communication | Workers work independently | Assistants report to Manager only | Panelists see and critique each other |
| Output | Consolidated work product | Iteration reports with recommendations | Synthesis report with consensus + dissent |
| User Interaction | Approval before execution, optional injection | Ongoing: change prompt, interval, pause/resume mid-run | Submit topic, approve plan, read synthesis, follow-up Q&A |
| Best For | Multi-file refactors, parallel code changes | Incident monitoring, PR review, triage | Architecture decisions, security audits, research |
| Duration | 5–60 minutes | Hours to days | 2–30 minutes |
| Key Metric | Task completion % | Iteration count + success rate | Convergence % |

---

## License

See [LICENSE](LICENSE) file.

## Contributing

Contributions are welcome:

1. Fork the repository
2. Create a feature branch
3. Follow SOLID principles and clean architecture
4. Add tests for new functionality
5. Submit pull request

## References

- **[GitHub Copilot SDK](https://github.com/github/copilot-sdk)** — Official SDK repository
- **[Copilot SDK .NET Cookbook](https://github.com/github/awesome-copilot/blob/main/cookbook/copilot-sdk/dotnet/README.md)** — .NET examples and recipes
- **[Copilot CLI Installation](https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli)** — CLI setup guide
- **[Model Context Protocol (MCP)](https://modelcontextprotocol.io/)** — MCP specification
- **[awesome-copilot](https://github.com/github/awesome-copilot)** — Additional resources and examples