Here is a concrete, production-grade plan for your new multi-agent panel `.csproj` from architecture to execution.

***

## 1. High-level architecture

Create a new bounded context:

- New project: `CopilotAgent.Panel` (class lib) and `CopilotAgent.Panel.Web` (UI host; could be part of your existing web front-end).
- New tab in UI (like “Multi Agent Teams” / “Office”): “Panel Discussion”.
- Core layers:
  - **Domain**: agents, panel session, state machine, settings.
  - **Orchestration**: Microsoft Agent Framework + Copilot SDK multi-agent workflows. [learn.microsoft](https://learn.microsoft.com/en-us/agent-framework/overview/agent-framework-overview)
  - **Infrastructure**: MCP servers, tools, persistence, telemetry.
  - **UI**: modern chat UI (Blazor or Avalonia/MAUI with web-hosted front-end). [syncfusion](https://www.syncfusion.com/blogs/post/cross-platform-frameworks-comparison)

Pattern-wise:

- Clean architecture: UI → Application (orchestration) → Domain → Infrastructure.
- Strict separation and loose coupling (DI everywhere, interface boundaries for agent providers, UI adapters for streaming, etc.).
- State machine per panel session; agents are stateless except for thread/context backing storage.

***

## 2. Core technologies and packages

### Agentic & LLM layer

- **Microsoft Agent Framework** (preview, near-GA; pin versions):
  - `Microsoft.Agents.Abstractions`
  - `Microsoft.Agents.AI`
  - `Microsoft.Agents.Hosting`
  - `Microsoft.Agents.AI.GitHub.Copilot` (for Copilot integration). [devblogs.microsoft](https://devblogs.microsoft.com/semantic-kernel/build-ai-agents-with-github-copilot-sdk-and-microsoft-agent-framework/)
- **Microsoft.Extensions.AI** for provider-agnostic chat clients; GA and stable core. [dev](https://dev.to/mashrulhaque/build-ai-agents-with-microsoft-agent-framework-in-c-46h0)
- **GitHub Copilot SDK** for:
  - LLM access
  - MCP servers
  - File system, repo and shell tools. [infoworld](https://www.infoworld.com/article/4125776/building-ai-agents-with-the-github-copilot-sdk.html)

### UI stack

For a world-class, ultra-modern chat UI in C#:

- If web-first: **Blazor (Server or WASM)** + component library:
  - Radzen Blazor, MudBlazor, or Syncfusion Blazor for chat bubbles, tabs, timelines, accordions, status chips, etc. [freshroastedhosting](https://freshroastedhosting.com/choosing-the-right-net-mobile-framework-in-2026/)
- If desktop first: **Avalonia UI** for rich desktop experience, but still host a web-based chat surface in a WebView for maximal reuse. [syncfusion](https://www.syncfusion.com/blogs/post/cross-platform-frameworks-comparison)
- Markdown rendering:
  - Use a rich markdown renderer (e.g., `Markdig` on server, plus custom components to render tables, code blocks, callouts).

### Storage, messaging, telemetry

- Distributed cache / store for session and state machine:
  - `IMemoryCache` + Redis or Postgres depending on existing infra.
- Logging & tracing:
  - OpenTelemetry for traces/metrics; enrich with agent name, state transition, tool invocations. [dev](https://dev.to/mashrulhaque/build-ai-agents-with-microsoft-agent-framework-in-c-46h0)
- Background coordination:
  - Hosted services (`IHostedService`) to run panels even when the user is on a different tab.

***

## 3. Domain model and state machine

### Key domain objects

- `PanelSessionId`
- `PanelSession`
  - `HeadAgentState`
  - `ModeratorAgentState`
  - `List<PanelistAgentState>`
  - `PanelSessionSettings` (primary workspace, secondary workspaces, model selection, commentary verbosity).
  - `PanelSessionLifecycleState` (enum).
  - `List<PanelEvent>` (for full audit/visibility).
- `PanelEvent`
  - Types: `UserMessage`, `HeadClarificationQuestion`, `PanelistMessage`, `ModeratorIntervention`, `ToolCall`, `StateChanged`, `Pause`, `Resume`, `Stop`, `Reset`, `SummaryProduced`.
- `PanelAgentRole` enum: `Head`, `Moderator`, `Panelist`.

### State machine (PanelSessionLifecycleState)

Define a deterministic state machine:

1. `Created`
2. `AwaitingClarification`
3. `AwaitingUserAgreement`
4. `PreparingPanel` (spawning panelists, validating models, tools)
5. `Running` (panel discussion ongoing)
6. `Paused`
7. `Synthesizing` (Head generating final answer)
8. `Completed`
9. `Stopped` (user stop)
10. `Resetting` (cleanup and fresh start)
11. `Failed` (error, with reason)

Transitions:

- `Created → AwaitingClarification`: after Head first expansion of user task.
- `AwaitingClarification ↔ AwaitingUserAgreement`: Head asks clarifications; when user confirms, go to agreement, then `PreparingPanel`.
- `PreparingPanel → Running`: once panelists and moderator are initialized.
- `Running ↔ Paused`: user pause / resume.
- `Running → Synthesizing`: Moderator converged, signals Head to synthesize.
- `Synthesizing → Completed`.
- `Running → Stopped`: user stop; panelists & moderator disposed.
- Any state → `Resetting → Created`: reset action.
- Any state → `Failed`: fatal error, with recovery path; allow reset.

Represent this with a state machine library (e.g., `Stateless`) or custom pattern with explicit transition methods to keep it transparent and testable.

***

## 4. Agent roles and orchestration

Use Microsoft Agent Framework multi-agent workflows (group chat / debate orchestrators) with Copilot agents. [infoq](https://www.infoq.com/news/2025/10/microsoft-agent-framework/)

### Head agent

Responsibilities:

- Interacts directly with the user.
- Elaborates the task, asks clarifying questions, and generates the **“topic of discussion”** prompt.
- Always listening to the user on that tab, regardless of panel state.
- Answers questions such as:
  - “How long will the discussion take?”
  - “Please stop now.”
  - “Think more on edge cases.”
- Schedules panel:
  - Decides number of panelists and models per panelist.
  - Decides phases of discussion.
- After panel convergence, synthesizes and presents a **comprehensive, elaborative** final response, with clear structure and reasoning trace.
- Maintains a “knowledge brief” about the recently finished discussion for follow-up questions.

Implementation:

- Head is an Agent Framework agent with:
  - System prompt specifying responsibilities, guardrails, and explicit state-machine awareness.
  - Access to tools: panel manager tool, “ask moderator”, “get panel status”, “get discussion timeline”.
  - Chat handler that transforms user intents into state transitions and orchestrator calls.

### Moderator agent

Responsibilities:

- Enforces guardrails (safety, security, non-infinite discussion).
- Ensures convergence:
  - Recognizes when enough perspectives are collected.
  - Triggers Head to synthesize.
- Controls panel lifespan:
  - Ensures no indefinite loops.
  - Takes into account user commands: stop, pause, resume, “think deeper” (expands exploration depth).
- Oversees tools usage:
  - Nudges panelists to use MCP tools, tests, repo scans where appropriate.
  - Avoids redundant or wasteful calls.

Implementation:

- Agent Framework “orchestrator” agent, using group chat/debate pattern. [infoq](https://www.infoq.com/news/2025/10/microsoft-agent-framework/)
- A2A communication: moderator reads panelists’ messages, sets instructions for next round.

### Panelist agents

Responsibilities:

- Each panelist is a Copilot-based agent tuned for a perspective:
  - e.g., “Static code analysis panelist”, “Runtime / load perspective”, “Security perspective”, “Edge-case / fuzzing perspective”, “Architecture & design panelist”.
- All have access to:
  - MCP servers (codebase access, file browsing, tests, telemetry).
  - Web tools for external info.
  - Domain-specific tools (e.g., test runners, static analyzers, log scrapers).

Implementation:

- Use Agent Framework group chat / debate orchestrator with N panelists (configurable).
- Each panelist uses a model selected from the **panelist models list** in Settings.
- Head randomly picks a model per panelist instance but can apply heuristics (e.g., heavier model for “architect”, faster for others).

***

## 5. Settings and configuration

Settings model:

```csharp
public sealed class PanelSettings
{
    public List<WorkspaceConfig> Workspaces { get; init; } // Primary + secondary
    public string PrimaryWorkspaceId { get; init; }
    public string PrimaryModel { get; init; }              // Head & Moderator
    public List<string> PanelistModels { get; init; }      // Pool for panelists
    public int MaxPanelists { get; init; }
    public TimeSpan MaxDiscussionDuration { get; init; }
    public int MaxRounds { get; init; }
    public CommentaryMode CommentaryMode { get; init; }    // Detailed / Brief / Off
}
```

Workspaces might map one-to-one to MCP server endpoints, repo roots, or environment configurations via Copilot SDK and Agent Framework’s MCP integration. [devblogs.microsoft](https://devblogs.microsoft.com/semantic-kernel/build-ai-agents-with-github-copilot-sdk-and-microsoft-agent-framework/)

UI for settings:

- Tab-level settings drawer:
  - Primary workspace selector.
  - Secondary workspace list (add/remove).
  - Primary model dropdown.
  - Panelist model list with priority ordering.
  - Commentary verbosity (live full trace vs. brief vs. minimal).

***

## 6. UI design: world-class panel experience

Use a modern chat-oriented design with clear visualization of state.

### Layout

- Left: conversation with Head (user <→ Head).
  - Messages render rich markdown: headings, code, tables, callouts.
  - Final responses from Head show a collapsed “commentary” section below (accordion) with:
    - How panel was scheduled.
    - Number of panelists, models used.
    - High-level phases of discussion.
- Right: panel viewer:
  - Timeline / “multi-stream chat” of panel discussion.
  - Filters:
    - All messages
    - By role (Head, Moderator, specific panelist)
    - Tool calls only
  - Foldable view:
    - Show/hide detailed panelist reasoning, tool traces.
- Top bar:
  - **Play / Pause / Stop** button for panel:
    - Play = Running
    - Pause = Paused
    - Stop = Stopped
  - State badges:
    - Manager/Head state (Created, Clarifying, Running, Synthesizing, etc.).
    - Moderator state (Monitoring, Intervening, Converging).
    - Number of active panelists vs. done.
  - Time-based indicator: elapsed time, soft ETA based on heuristics from settings.

### UX flows

- User types initial task.
- Head elaborates, asks clarifications (Head messages appear in main chat).
- Once ready, Head proposes “topic of discussion” and asks for confirmation; user agrees → panel starts.
- Panel discussion view goes live:
  - Animated “live” badge.
  - Per-agent message streams with avatars.
- User can:
  - Ask Head meta-questions at any time (even while panel runs).
  - Pause/resume/stop.
  - After completion, ask follow-up questions on same context or hit “Reset” to clear everything.

### Technical UI details

- Use SignalR for streaming updates to the UI so panel continues while user is on other tabs; reconnection logic to resync state.
- Virtualized list components for the panel discussion; avoid memory bloat for long conversations.
- Use responsive design; adapt to narrow screens without losing state indicators.

***

## 7. Execution flow and lifecycle

### 1. Start session

- User opens Panel tab → `PanelSession` created with default `PanelSettings`.
- Head agent attaches to this session.

### 2. Clarification phase

- User enters high-level request.
- Head expands, asks clarifications until it can formulate a crisp “topic of discussion”.
- State: `AwaitingClarification`.

### 3. Agreement

- Head presents “topic of discussion”, proposed number of panelists, estimated duration and depth, maybe cost indicator.
- User clicks “Start Panel”.
- State: `AwaitingUserAgreement → PreparingPanel`.

### 4. Panel creation

- Panel manager spins up:
  - Moderator agent using primary model.
  - N panelist agents with models selected from `PanelistModels`.
- Wire them into an Agent Framework group-chat/debate orchestrator, with:
  - Moderator-driven rounds.
  - Structured instructions about:
    - Tools allowed
    - Focus on codebase for this workspace
    - Termination conditions (max rounds, convergence).
- State: `PreparingPanel → Running`.

### 5. Discussion

- Panelists:
  - Analyze codebase via MCP tools, run tests, scan logs, etc.
  - Post findings into group chat.
- Moderator:
  - Summarizes, calls for more detail in specific areas, prevents drift, and monitors constraints.
- UI:
  - Streams messages into panel viewer.
  - Head’s commentary area keeps meta-updates like “Moderator requested one more round focusing on failure modes in async pipeline.”

### 6. Convergence and synthesis

- Moderator decides convergence:
  - Enough consensus or identified trade-offs.
  - Signals to Head (via A2A message).
- Head enters `Synthesizing` state:
  - Produces final answer to user, structured, referencing main panel findings, proposed changes, risks, and follow-up tasks.
- Panelists and moderator are disposed; only Head retains summarized context.
- State: `Synthesizing → Completed`.

### 7. Follow-up and reset

- Follow-up discussion:
  - User can ask more questions within same `PanelSession`.
  - Head answers using stored “discussion knowledge brief” and transcripts.
- Reset:
  - “Reset” button moves session to `Resetting`:
    - Hard-dispose all agents, clear memory, cancel any long-running tasks, clear caches.
    - Create a fresh `PanelSession`, Head gets reinitialized with no previous context.

***

## 8. Resource management and cleanup

To avoid memory bloat:

- Use per-session scoped services; ensure disposable resources are properly released:
  - Agents, tool handlers, and MCP connections implement `IAsyncDisposable`.
- After `Completed`, `Stopped`, or `Failed`:
  - Persist only:
    - Compressed summary of discussion.
    - Minimal timeline for audit (optionally).
  - Drop full conversation context from hot memory.
- Implement background cleanup job:
  - Periodically scans expired sessions and terminates any forgotten workflows, clears caches.
- UI:
  - Uses streaming with limited buffer; old panel messages can be paged and loaded on demand.

***

## 9. API and integration boundaries

Define a dedicated application service interface for the UI:

```csharp
public interface IPanelSessionService
{
    Task<PanelSessionDto> CreateSessionAsync(CreatePanelSessionRequest request);
    Task<PanelSessionDto> GetSessionAsync(PanelSessionId id);
    IAsyncEnumerable<PanelEventDto> SubscribeEventsAsync(PanelSessionId id, CancellationToken ct);

    Task SendUserMessageAsync(PanelSessionId id, string message);
    Task PauseAsync(PanelSessionId id);
    Task ResumeAsync(PanelSessionId id);
    Task StopAsync(PanelSessionId id);
    Task ResetAsync(PanelSessionId id);

    Task<PanelStatusDto> GetStatusAsync(PanelSessionId id);
    Task UpdateSettingsAsync(PanelSessionId id, PanelSettingsUpdateRequest settings);
}
```

Use this interface from your Blazor / front-end controllers to keep the rest of the project isolated.

***

## 10. Testing strategy

### Unit tests

- State machine:
  - Tests for all transitions and invalid transitions (e.g., cannot `Pause` when `Created`).
- Head logic:
  - Clarification and agreement flows (mock LLM with deterministic responses).
  - “Stop now”, “how long will it take”, “think more” commands.
- Moderator:
  - Convergence logic (simulate panelist outputs).
  - Guardrail enforcement (max rounds, duration).
- Panel manager:
  - Correct creation and disposal of agents.
  - Model selection for panelists.
- Settings:
  - Ensure sane defaults and validation (e.g., primary workspace must be in list).

### Integration tests

- End-to-end flows using test doubles for LLMs:
  - From initial user question to completed panel and synthesized answer.
  - Pause/resume, stop, reset.
- UI-level tests (Playwright or bUnit for Blazor) to verify:
  - State indicators.
  - “Play/Pause/Stop/Reset” behavior.
  - Commentary expansion/collapse.

### Non-regression

- All panel-related services behind new interfaces and routes to avoid touching existing office/multi-agent modules.
- Add regression tests to verify existing tabs & functionality still behave identically.

***

## 11. Step-by-step execution plan

1. **Skeleton & boundaries**
   - Create `CopilotAgent.Panel` and `CopilotAgent.Panel.Web`.
   - Define domain models, state machine, and `IPanelSessionService`.
2. **Head & state machine**
   - Implement Head with mocked LLM; wire clarification → agreement → synthesized answer without real panel.
   - Implement full state machine and transitions and unit tests.
3. **UI first iteration**
   - Add Panel tab.
   - Implement chat with Head, basic state indicators, Play button stub.
4. **Integrate Agent Framework + Copilot SDK**
   - Add packages, define base agent types for Head, Moderator, Panelist.
   - Implement simple multi-agent workflow with 2 panelists and moderator.
5. **Panel orchestration**
   - Build panel manager, connect to state machine.
   - Implement Running → Synthesizing flow.
6. **MCP and tools integration**
   - Connect to your codebase/workspaces through Copilot SDK MCP servers.
   - Implement example scenario: “find edge cases that can crash system”.
7. **UI refinement**
   - Full live panel viewer, commentary toggles, detailed status chips, markdown rendering.
   - Pause/resume/stop/reset wiring.
8. **Resource management & cleanup**
   - Implement disposable patterns, background cleanup, load tests to validate no leaks.
9. **Settings and multi-workspace**
   - UI to manage primary/secondary workspaces, models, commentary modes.
10. **Hardening & polish**
   - Add telemetry, monitoring dashboards, structured logs.
   - Tune prompts and defaults for quality.
   - Final regression suite and staging rollout.

***

If you want, next step I can draft:

- The concrete state machine enums and transition methods.
- Sample C# interfaces/classes for Head, Moderator, Panelist agents using Microsoft Agent Framework + Copilot SDK.
- A Blazor component skeleton showing how the UI binds states, events, and commentary.