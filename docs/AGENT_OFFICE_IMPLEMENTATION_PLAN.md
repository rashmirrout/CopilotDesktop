# Agent Office — Implementation Plan

> **Version**: 1.0  
> **Status**: Pre-Implementation  
> **Design Reference**: [AGENT_OFFICE_DESIGN.md](./AGENT_OFFICE_DESIGN.md) v1.1  
> **Date**: February 2026

---

## Table of Contents

1. [Implementation Strategy](#1-implementation-strategy)
2. [Step-by-Step Implementation Order](#2-step-by-step-implementation-order)
3. [Step Details](#3-step-details)
4. [File Inventory](#4-file-inventory)
5. [Integration Points (Existing Files Modified)](#5-integration-points-existing-files-modified)
6. [Dependency Graph](#6-dependency-graph)
7. [Verification Gates](#7-verification-gates)
8. [Risk Register](#8-risk-register)

---

## 1. Implementation Strategy

### Principles

1. **Bottom-up, compile-after-every-step**: Models first, then interfaces, then implementations, then UI. The solution must compile after every step.
2. **One logical unit per step**: Each step produces a compilable, testable increment. No orphan files.
3. **Test early**: Write unit tests alongside core services, not as a separate phase.
4. **Existing pattern reuse**: Mirror `CopilotAgent.MultiAgent` project structure, naming, DI registration patterns, and MVVM conventions exactly.
5. **Minimal modification to existing files**: Touch `App.xaml.cs`, `MainWindowViewModel.cs`, `MainWindow.xaml`, `CopilotAgent.App.csproj`, `CopilotAgent.sln`, and `AppSettings.cs` only — no changes to any other existing code.

### Phasing

The design document (§9) defines 7 high-level phases. This implementation plan breaks those into **20 concrete steps**, each with:
- Exact files to create/modify
- What to implement in each file
- Dependencies (which steps must complete first)
- Acceptance criteria (how to verify the step is done)
- Estimated effort

---

## 2. Step-by-Step Implementation Order

| Step | Name | New Files | Modified Files | Depends On | Effort |
|------|------|-----------|---------------|------------|--------|
| 1 | Project scaffold | `CopilotAgent.Office.csproj` | `CopilotAgent.sln`, `CopilotAgent.App.csproj` | — | S |
| 2 | Enums & simple models | 4 model files | — | 1 | S |
| 3 | Complex models | 5 model files | — | 2 | S |
| 4 | Chat & UI models | 3 model files | — | 2 | S |
| 5 | Event types & hierarchy | 2 event files | — | 2, 3, 4 | M |
| 6 | Service interfaces | 5 interface files | — | 2, 3, 5 | M |
| 7 | Settings model in Core | 1 model file | `AppSettings.cs` | — | S |
| 8 | Event log implementation | 1 service file | — | 5, 6 | S |
| 9 | Iteration scheduler | 1 service file | — | 5, 6 | M |
| 10 | Assistant agent | 1 service file | — | 3, 5, 6 | M |
| 11 | Assistant pool | 1 service file | — | 5, 6, 10 | L |
| 12 | Manager service (core loop) | 1 service file | — | 6, 8, 9, 11 | XL |
| 13 | DI registration | — | `App.xaml.cs` | 7–12 | S |
| 14 | OfficeViewModel | 1 VM file | — | 5, 6, 12 | XL |
| 15 | OfficeView (chat plane) | 2 view files | — | 4, 14 | L |
| 16 | Side panel + live commentary | — (enhance OfficeView) | `OfficeView.xaml` | 14, 15 | L |
| 17 | MainWindow integration | — | `MainWindowViewModel.cs`, `MainWindow.xaml` | 14, 15 | M |
| 18 | Manager LLM intelligence | — (enhance ManagerService) | `OfficeManagerService.cs` | 12 | L |
| 19 | Clarification & instruction flow | — (enhance Manager + VM) | `OfficeManagerService.cs`, `OfficeViewModel.cs` | 14, 18 | L |
| 20 | Unit tests | test files | `CopilotAgent.Tests.csproj` | 8–12, 14 | L |

**Effort key**: S = < 1 hr, M = 1–2 hrs, L = 2–4 hrs, XL = 4–8 hrs

---

## 3. Step Details

### Step 1: Project Scaffold

**Goal**: Create the `CopilotAgent.Office` class library and wire it into the solution.

**New files**:
- `src/CopilotAgent.Office/CopilotAgent.Office.csproj`

**Modified files**:
- `CopilotAgent.sln` — add `CopilotAgent.Office` project
- `src/CopilotAgent.App/CopilotAgent.App.csproj` — add `<ProjectReference>` to `CopilotAgent.Office`

**Implementation**:
```xml
<!-- CopilotAgent.Office.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CopilotAgent.Office</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CopilotAgent.Core\CopilotAgent.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.2" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.2" />
    <PackageReference Include="System.Text.Json" Version="10.0.2" />
  </ItemGroup>
</Project>
```

**Acceptance**: `dotnet build CopilotAgent.sln` succeeds with new project included.

---

### Step 2: Enums & Simple Models

**Goal**: Create all enum types and simple value-type models.

**New files**:
- `src/CopilotAgent.Office/Models/ManagerPhase.cs` — `ManagerPhase` enum (Design §5.1.1)
- `src/CopilotAgent.Office/Models/AssistantTaskStatus.cs` — `AssistantTaskStatus` enum (Design §5.1.4, extracted)
- `src/CopilotAgent.Office/Models/SchedulingAction.cs` — `SchedulingAction` enum (Design §5.1.7)
- `src/CopilotAgent.Office/Models/CommentaryType.cs` — `CommentaryType` enum (Design §5.1.10)

**Acceptance**: Build succeeds. All enums have XML doc comments.

---

### Step 3: Complex Models

**Goal**: Create all non-trivial models used by services.

**New files**:
- `src/CopilotAgent.Office/Models/OfficeConfig.cs` — (Design §5.1.2)
- `src/CopilotAgent.Office/Models/ManagerContext.cs` — includes `ClarificationExchange` (Design §5.1.3)
- `src/CopilotAgent.Office/Models/AssistantTask.cs` — (Design §5.1.4)
- `src/CopilotAgent.Office/Models/AssistantResult.cs` — (Design §5.1.5)
- `src/CopilotAgent.Office/Models/IterationReport.cs` — includes `SchedulingDecision` (Design §5.1.6 + §5.1.7)

**Dependencies**: Step 2 (enums referenced by these models).

**Acceptance**: Build succeeds. All models have XML doc comments. No logic, pure data classes.

---

### Step 4: Chat & UI Models

**Goal**: Create UI-facing models.

**New files**:
- `src/CopilotAgent.Office/Models/OfficeChatMessage.cs` — includes `OfficeChatRole` enum (Design §5.1.8)
- `src/CopilotAgent.Office/Models/LiveCommentary.cs` — (Design §5.1.10, class only — enum already in Step 2)
- `src/CopilotAgent.Office/Models/OfficeColorScheme.cs` — static color helper (Design §5.1.9)

**Dependencies**: Step 2 (ManagerPhase, CommentaryType referenced).

**Acceptance**: Build succeeds.

---

### Step 5: Event Types & Hierarchy

**Goal**: Create the full event type system.

**New files**:
- `src/CopilotAgent.Office/Events/OfficeEventType.cs` — enum with all event types including `Commentary` (Design §5.2.1)
- `src/CopilotAgent.Office/Events/OfficeEvent.cs` — base class + all derived event classes: `PhaseChangedEvent`, `AssistantEvent`, `SchedulingEvent`, `IterationCompletedEvent`, `RestCountdownEvent`, `ChatMessageEvent`, `ClarificationEvent`, `CommentaryEvent` (Design §5.2.2)

**Dependencies**: Steps 2, 3, 4 (models referenced by events).

**Acceptance**: Build succeeds. Every event type maps to an `OfficeEventType` value.

---

### Step 6: Service Interfaces

**Goal**: Define all service contracts.

**New files**:
- `src/CopilotAgent.Office/Services/IOfficeManagerService.cs` — (Design §5.3.1)
- `src/CopilotAgent.Office/Services/IAssistantPool.cs` — (Design §5.3.2)
- `src/CopilotAgent.Office/Services/IAssistantAgent.cs` — (Design §5.3.3)
- `src/CopilotAgent.Office/Services/IIterationScheduler.cs` — (Design §5.3.4)
- `src/CopilotAgent.Office/Services/IOfficeEventLog.cs` — (Design §5.3.5)

**Dependencies**: Steps 2, 3, 5 (models + events referenced in signatures).

**Acceptance**: Build succeeds. Each interface method has XML doc.

---

### Step 7: Settings Model in Core

**Goal**: Add `OfficeSettings` to `CopilotAgent.Core` and wire into `AppSettings`.

**New files**:
- `src/CopilotAgent.Core/Models/OfficeSettings.cs` — (Design §10.2)

**Modified files**:
- `src/CopilotAgent.Core/Models/AppSettings.cs` — add `public OfficeSettings Office { get; set; } = new();`

**Acceptance**: Build succeeds. `AppSettings` serialization/deserialization works with new property (existing settings files without `Office` key will get defaults).

---

### Step 8: Event Log Implementation

**Goal**: In-memory event log with query support.

**New files**:
- `src/CopilotAgent.Office/Services/OfficeEventLog.cs` — implements `IOfficeEventLog` (Design §5.4)

**Implementation notes**:
- Internal `List<OfficeEvent>` with `lock` for thread safety
- `GetByIteration` filters by `IterationNumber`
- `GetByType` filters by `EventType`
- `GetSchedulingLog` casts to `SchedulingEvent` and extracts `Decision`

**Dependencies**: Steps 5, 6.

**Acceptance**: Build succeeds. Can write a quick smoke test.

---

### Step 9: Iteration Scheduler

**Goal**: Countdown timer with tick events and early cancellation.

**New files**:
- `src/CopilotAgent.Office/Services/IterationScheduler.cs` — implements `IIterationScheduler` (Design §5.4.4)

**Implementation notes**:
- Uses `PeriodicTimer(TimeSpan.FromSeconds(1))` for ticks
- `TaskCompletionSource` for early cancellation (`CancelRest()`)
- `OverrideRestDurationAsync` replaces current timer
- Raises `OnCountdownTick` event each second
- Respects `CancellationToken`

**Dependencies**: Steps 5, 6.

**Acceptance**: Build succeeds. Timer fires ticks, `CancelRest()` unblocks `WaitForNextIterationAsync`.

---

### Step 10: Assistant Agent

**Goal**: Ephemeral worker that creates a session, sends prompt, collects result.

**New files**:
- `src/CopilotAgent.Office/Services/AssistantAgent.cs` — implements `IAssistantAgent` (Design §5.4.3)

**Implementation notes**:
- Constructor takes `ICopilotService` + `int assistantIndex`
- `ExecuteAsync`: creates `Session` model → calls `ICopilotService.SendMessageAsync` → parses response → builds `AssistantResult` → disposes session
- System prompt from `BuildAssistantSystemPrompt(task)`
- Raises `OnProgress` during streaming
- Respects timeout via `CancellationToken`

**Dependencies**: Steps 3, 5, 6.

**Acceptance**: Build succeeds.

---

### Step 11: Assistant Pool

**Goal**: SemaphoreSlim-gated concurrent execution with scheduling decisions.

**New files**:
- `src/CopilotAgent.Office/Services/AssistantPool.cs` — implements `IAssistantPool` (Design §5.4.2)

**Implementation notes**:
- `SemaphoreSlim(_maxConcurrency, _maxConcurrency)` for gating
- Tasks sorted by `Priority` before dispatch
- For each task: `await _semaphore.WaitAsync(ct)` → spawn `AssistantAgent` → execute → release semaphore
- Raises `OnAssistantEvent` and `OnSchedulingEvent` at each lifecycle point
- `CancelAllAsync()` cancels per-task `CancellationTokenSource`s and releases semaphore
- Uses `ILogger` for structured logging

**Dependencies**: Steps 5, 6, 10.

**Acceptance**: Build succeeds. 5 tasks with pool=2 should serialize correctly.

---

### Step 12: Manager Service (Core Loop)

**Goal**: The heart — state machine, iteration loop, aggregation, instruction injection.

**New files**:
- `src/CopilotAgent.Office/Services/OfficeManagerService.cs` — implements `IOfficeManagerService` (Design §5.4.1)

**Implementation notes — Phase 1 (this step)**:
- State machine: `TransitionTo(phase)` helper that raises `PhaseChangedEvent`
- `StartAsync`: validate config → create manager session → transition to Planning → hardcode a test plan → transition to AwaitingApproval
- `ApprovePlanAsync` → start `Task.Run(RunIterationLoopAsync)`
- `RunIterationLoopAsync`:
  - `AbsorbInjectedInstructions()` — drain `ConcurrentBag<string>`
  - FetchingEvents — **stub**: return 2 hardcoded test tasks
  - Scheduling — create `AssistantTask` list, raise events
  - Executing — `await _assistantPool.ExecuteTasksAsync(tasks, config)`
  - Aggregating — **stub**: concatenate results into basic report
  - Resting — `await _scheduler.WaitForNextIterationAsync(interval)`
  - Loop
- `InjectInstructionAsync` → add to `ConcurrentBag`
- `StopAsync`, `ResetAsync`, `PauseAsync`, `ResumeAsync` — full lifecycle
- Owns `CancellationTokenSource` hierarchy (Design §6.5)
- Raises `OfficeEvent` via `OnEvent` for every decision

**Note**: LLM-driven clarification, event fetching, and aggregation are deferred to Step 18. This step uses hardcoded stubs to verify the loop mechanics.

**Dependencies**: Steps 6, 8, 9, 11.

**Acceptance**: Build succeeds. Manager can Start → plan (stub) → approve → iterate with stub tasks → rest → loop → stop. Debug output confirms full lifecycle.

---

### Step 13: DI Registration

**Goal**: Wire all Office services into the application DI container.

**Modified files**:
- `src/CopilotAgent.App/App.xaml.cs` — add Office service registrations after MultiAgent block

**Registrations to add**:
```csharp
// Agent Office Services
services.AddSingleton<IOfficeManagerService, OfficeManagerService>();
services.AddSingleton<IOfficeEventLog, OfficeEventLog>();
services.AddSingleton<IIterationScheduler, IterationScheduler>();
services.AddTransient<OfficeViewModel>();
```

**Note**: `IAssistantPool` and `IAssistantAgent` are NOT registered — they are created internally by `OfficeManagerService` and `AssistantPool` respectively (same pattern as `WorkerAgent` in MultiAgent).

**Dependencies**: Steps 7–12.

**Acceptance**: App starts without DI resolution errors. No UI visible yet for Office.

---

### Step 14: OfficeViewModel

**Goal**: Full MVVM ViewModel binding hub between Office services and UI.

**New files**:
- `src/CopilotAgent.App/ViewModels/OfficeViewModel.cs`

**Implementation notes**:
- Extends `ViewModelBase` (same base as `ChatViewModel`, `AgentTeamViewModel`)
- Constructor injects `IOfficeManagerService`, `IOfficeEventLog`, `AppSettings`
- Subscribes to `IOfficeManagerService.OnEvent` → `HandleEvent(OfficeEvent)` method
- `HandleEvent` uses `Application.Current.Dispatcher.Invoke()` to marshal all UI updates
- All properties from Design §7.7:
  - Chat: `ObservableCollection<OfficeChatMessage> Messages`
  - Status: `CurrentPhase`, `CurrentIteration`, `CompletedTasks`, `TotalTasks`, `QueueDepth`
  - Rest: `IsResting`, `RestProgressPercent`, `RestCountdownText`
  - Side panel: `IsSidePanelOpen`, `ObservableCollection<LiveCommentary> LiveCommentaries`, `AutoScrollCommentary`
  - Event log: `ObservableCollection<OfficeEvent> EventLog`
  - Stats: `TotalIterations`, `TotalTasksCompleted`, `SuccessRate`, `AverageDuration`
  - Clarification: `IsWaitingForClarification`, `IsPlanAwaitingApproval`
  - Config: `CheckIntervalMinutes`, `MaxAssistants`
- All commands as `[RelayCommand]`:
  - `Start`, `SendMessage`, `ApprovePlan`, `RejectPlan`
  - `Pause`, `Resume`, `Stop`, `Reset`
  - `ToggleSidePanel`, `ToggleCollapsed`, `ToggleIterationContainer`
  - `UpdateInterval`

**Dependencies**: Steps 5, 6, 12.

**Acceptance**: Build succeeds. ViewModel can be instantiated from DI.

---

### Step 15: OfficeView (Chat Plane)

**Goal**: The main Office tab XAML view with full-width chat, status bar, and input area.

**New files**:
- `src/CopilotAgent.App/Views/OfficeView.xaml` — main view
- `src/CopilotAgent.App/Views/OfficeView.xaml.cs` — minimal code-behind (DataContext wiring only)

**XAML structure** (Design §7.1):
```
Grid (3 rows: Status Bar | Chat ScrollViewer | Input Area)
├── Row 0: Status bar
│   ├── Phase pill (colored Border with TextBlock)
│   ├── Iteration counter
│   ├── Task progress (TextBlock + ProgressBar)
│   ├── Queue depth
│   ├── Timer
│   └── [📊] ToggleSidePanel button (right-aligned)
│
├── Row 1: ScrollViewer with ItemsControl
│   ├── ItemsSource="{Binding Messages}"
│   ├── ItemTemplateSelector (DataTemplateSelector for different message types)
│   ├── DataTemplates:
│   │   ├── UserMessage template (right-aligned, green accent)
│   │   ├── ManagerMessage template (left-aligned, blue accent, foldable)
│   │   ├── AssistantMessage template (indented, per-color accent, foldable)
│   │   ├── SystemMessage template (center, grey)
│   │   ├── IterationContainer template (separator header + child items)
│   │   └── RestCountdown template (progress bar + text)
│   └── Auto-scroll behavior (ScrollViewer attached behavior)
│
├── Row 2: Input area
│   ├── TextBox (message input)
│   ├── Send button
│   └── Visual indicator when IsWaitingForClarification (highlighted border)
│
└── Overlay layer (Grid on top of everything):
    ├── Backdrop (dimmed, click-to-close)
    └── Side panel placeholder (content in Step 16)
```

**DataTemplateSelector**:
- `OfficeChatMessageTemplateSelector.cs` — helper class that picks template based on `OfficeChatRole` and `IsIterationContainer`

**New files for this step**:
- `src/CopilotAgent.App/Helpers/OfficeChatMessageTemplateSelector.cs`

**Implementation notes**:
- Follow existing `ChatView.xaml` patterns for ScrollViewer auto-scroll
- Use existing converters: `BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`, `StringToBrushConverter`
- Plain text rendering in this step. Markdown rendering deferred to later enhancement.
- Iteration containers use an `Expander` or custom toggle control with `ContainerExpanded` binding

**Dependencies**: Steps 4, 14.

**Acceptance**: Build succeeds. Office view renders empty chat with status bar and input area. Can type and send (message appears in collection).

---

### Step 16: Side Panel + Live Commentary

**Goal**: Add the fly-in/fly-out animated side panel with all 4 sections.

**Modified files**:
- `src/CopilotAgent.App/Views/OfficeView.xaml` — add side panel overlay content

**Implementation**:
- Overlay `Border` (width=400, right-aligned) with `TranslateTransform`
- Storyboard animations in `OfficeView.Resources`:
  - `SlideIn`: X from 400→0, 300ms, `QuarticEase EaseOut`
  - `SlideOut`: X from 0→400, 250ms, `QuarticEase EaseIn`
- DataTrigger on `IsSidePanelOpen` starts/stops storyboards
- Backdrop `Border` with `#40000000` background, `MouseDown` closes panel
- Code-behind: `Backdrop_MouseDown` → sets `IsSidePanelOpen = false`, Escape key handler

**Side panel sections** (Design §7.6):
1. **💭 Live Commentary**: `ItemsControl` bound to `LiveCommentaries`, auto-scroll, monospace font, emoji+agent+message per line
2. **⚙️ Configuration**: Interval spinner, pool size spinner, model dropdown, Pause/Stop/Reset buttons
3. **📊 Event Log**: `ItemsControl` bound to `EventLog`, reverse-chronological, compact format
4. **📈 Iteration Stats**: Static labels bound to `TotalIterations`, `TotalTasksCompleted`, `SuccessRate`, `AverageDuration`

Each section wrapped in `Expander` for collapsibility.

**Dependencies**: Steps 14, 15.

**Acceptance**: Build succeeds. Clicking [📊] slides panel in. Clicking backdrop/Escape slides it out. Commentary entries appear when events fire.

---

### Step 17: MainWindow Integration

**Goal**: Add "🏢 Office" tab button and wire OfficeView into the main layout.

**Modified files**:
- `src/CopilotAgent.App/ViewModels/MainWindowViewModel.cs`:
  - Add `[ObservableProperty] private bool _showOffice;`
  - Add `[RelayCommand] private void ShowOfficeView()` method (sets `ShowOffice = true`, `ShowAgentTeam = false`)
  - Modify `ShowChat()` to also set `ShowOffice = false`
  - Modify `ShowAgentTeamView()` to also set `ShowOffice = false`

- `src/CopilotAgent.App/MainWindow.xaml`:
  - Add `🏢 Office` button next to `👥 Team` button in the tab bar
  - Add `<views:OfficeView>` with visibility bound to `ShowOffice` (same pattern as `AgentTeamView`)

**Dependencies**: Steps 14, 15.

**Acceptance**: Build succeeds. Office tab button appears. Clicking it shows OfficeView. Clicking Chat or Team hides Office. All three views are mutually exclusive.

---

### Step 18: Manager LLM Intelligence

**Goal**: Replace hardcoded stubs with real LLM-driven Manager behavior.

**Modified files**:
- `src/CopilotAgent.Office/Services/OfficeManagerService.cs`

**Implementation**:
- `EnsureManagerSessionAsync()`: create real Copilot session with system prompt (Design §6.1)
- `ClarifyAsync()`: send clarification prompt to LLM, parse response for questions vs `READY_TO_PLAN`
- `PlanAsync()`: send planning prompt, extract Markdown plan
- `FetchEventsAsync()`: send "CHECK FOR EVENTS" prompt, parse JSON response with events list
- `AggregateResultsAsync()`: send aggregation prompt with all `AssistantResult`s, parse Markdown report
- Error handling: retry on parse failure with rephrased prompt, reconnect on session loss
- LLM timeout respects `config.ManagerLlmTimeoutSeconds`

**Dependencies**: Step 12.

**Acceptance**: Manager makes real LLM calls. Clarification, planning, event fetching, and aggregation produce meaningful responses. Handles malformed LLM responses gracefully.

---

### Step 19: Clarification & Instruction Injection Flow

**Goal**: Full multi-turn clarification and smart instruction injection with Manager clarity evaluation.

**Modified files**:
- `src/CopilotAgent.Office/Services/OfficeManagerService.cs` — enhance `InjectInstructionAsync` with clarity evaluation (Design §8.3), add `RespondToClarificationAsync` multi-turn support
- `src/CopilotAgent.App/ViewModels/OfficeViewModel.cs` — enhance `SendMessage` command routing logic (Design §8.3 flow chart: Clarifying → mid-run clarification → straightforward instruction)

**Implementation** (Design §8.3 + §8.6):
- `InjectInstructionAsync(input)`:
  - If Manager session is idle (Executing/Resting): send clarity evaluation prompt to LLM
  - Parse "CLEAR" → queue instruction
  - Parse "CLARIFY: {question}" → set `IsWaitingForClarification`, raise `ClarificationEvent`, add Manager question to chat
  - If Manager session is busy (FetchingEvents/Aggregating): skip evaluation, queue directly
- `RespondToClarificationAsync(input)`:
  - Send response to Manager LLM with clarification context
  - If more questions → stay in clarification loop
  - If clear → build refined instruction → add to injected instructions → clear `IsWaitingForClarification`
- `OfficeViewModel.SendMessage` routing:
  - Phase == Clarifying → `RespondToClarificationAsync`
  - `IsWaitingForClarification == true` → `RespondToClarificationAsync`
  - Else → `InjectInstructionAsync`

**Dependencies**: Steps 14, 18.

**Acceptance**: Ambiguous instruction triggers Manager clarification question. User responds. Multi-turn Q&A completes. Refined instruction queued. Clear instructions queue immediately.

---

### Step 20: Unit Tests

**Goal**: Comprehensive test coverage for core services.

**Modified files**:
- `tests/CopilotAgent.Tests/CopilotAgent.Tests.csproj` — add reference to `CopilotAgent.Office`

**New files**:
- `tests/CopilotAgent.Tests/Office/ManagerPhaseTransitionTests.cs`
  - Test every valid transition in the state machine
  - Test invalid transitions throw
- `tests/CopilotAgent.Tests/Office/AssistantPoolTests.cs`
  - 3 tasks, pool=3 → all start immediately
  - 5 tasks, pool=2 → 2 immediate, 3 queued, drain correctly
  - CancelAll cancels active + queued
  - Priority ordering respected
- `tests/CopilotAgent.Tests/Office/IterationSchedulerTests.cs`
  - Timer fires correct number of ticks
  - `CancelRest()` unblocks immediately
  - `OverrideRestDurationAsync` changes remaining time
- `tests/CopilotAgent.Tests/Office/OfficeEventLogTests.cs`
  - Log, GetAll, GetByIteration, GetByType, Clear
- `tests/CopilotAgent.Tests/Office/OfficeManagerServiceTests.cs`
  - Full lifecycle: Start → Approve → 1 iteration → Stop
  - InjectInstruction queues correctly
  - Reset cancels everything
  - Pause/Resume works

**Test approach**: Mock `ICopilotService` with Moq or NSubstitute (whichever the project already uses). Stub LLM responses.

**Dependencies**: Steps 8–12, 14.

**Acceptance**: All tests pass. `dotnet test` succeeds.

---

## 4. File Inventory

### New Files (CopilotAgent.Office — 19 files)

| # | Path | Step |
|---|------|------|
| 1 | `src/CopilotAgent.Office/CopilotAgent.Office.csproj` | 1 |
| 2 | `src/CopilotAgent.Office/Models/ManagerPhase.cs` | 2 |
| 3 | `src/CopilotAgent.Office/Models/AssistantTaskStatus.cs` | 2 |
| 4 | `src/CopilotAgent.Office/Models/SchedulingAction.cs` | 2 |
| 5 | `src/CopilotAgent.Office/Models/CommentaryType.cs` | 2 |
| 6 | `src/CopilotAgent.Office/Models/OfficeConfig.cs` | 3 |
| 7 | `src/CopilotAgent.Office/Models/ManagerContext.cs` | 3 |
| 8 | `src/CopilotAgent.Office/Models/AssistantTask.cs` | 3 |
| 9 | `src/CopilotAgent.Office/Models/AssistantResult.cs` | 3 |
| 10 | `src/CopilotAgent.Office/Models/IterationReport.cs` | 3 |
| 11 | `src/CopilotAgent.Office/Models/OfficeChatMessage.cs` | 4 |
| 12 | `src/CopilotAgent.Office/Models/LiveCommentary.cs` | 4 |
| 13 | `src/CopilotAgent.Office/Models/OfficeColorScheme.cs` | 4 |
| 14 | `src/CopilotAgent.Office/Events/OfficeEventType.cs` | 5 |
| 15 | `src/CopilotAgent.Office/Events/OfficeEvent.cs` | 5 |
| 16 | `src/CopilotAgent.Office/Services/IOfficeManagerService.cs` | 6 |
| 17 | `src/CopilotAgent.Office/Services/IAssistantPool.cs` | 6 |
| 18 | `src/CopilotAgent.Office/Services/IAssistantAgent.cs` | 6 |
| 19 | `src/CopilotAgent.Office/Services/IIterationScheduler.cs` | 6 |
| 20 | `src/CopilotAgent.Office/Services/IOfficeEventLog.cs` | 6 |
| 21 | `src/CopilotAgent.Office/Services/OfficeEventLog.cs` | 8 |
| 22 | `src/CopilotAgent.Office/Services/IterationScheduler.cs` | 9 |
| 23 | `src/CopilotAgent.Office/Services/AssistantAgent.cs` | 10 |
| 24 | `src/CopilotAgent.Office/Services/AssistantPool.cs` | 11 |
| 25 | `src/CopilotAgent.Office/Services/OfficeManagerService.cs` | 12 |

### New Files (CopilotAgent.Core — 1 file)

| # | Path | Step |
|---|------|------|
| 26 | `src/CopilotAgent.Core/Models/OfficeSettings.cs` | 7 |

### New Files (CopilotAgent.App — 4 files)

| # | Path | Step |
|---|------|------|
| 27 | `src/CopilotAgent.App/ViewModels/OfficeViewModel.cs` | 14 |
| 28 | `src/CopilotAgent.App/Views/OfficeView.xaml` | 15 |
| 29 | `src/CopilotAgent.App/Views/OfficeView.xaml.cs` | 15 |
| 30 | `src/CopilotAgent.App/Helpers/OfficeChatMessageTemplateSelector.cs` | 15 |

### New Files (Tests — 5 files)

| # | Path | Step |
|---|------|------|
| 31 | `tests/CopilotAgent.Tests/Office/ManagerPhaseTransitionTests.cs` | 20 |
| 32 | `tests/CopilotAgent.Tests/Office/AssistantPoolTests.cs` | 20 |
| 33 | `tests/CopilotAgent.Tests/Office/IterationSchedulerTests.cs` | 20 |
| 34 | `tests/CopilotAgent.Tests/Office/OfficeEventLogTests.cs` | 20 |
| 35 | `tests/CopilotAgent.Tests/Office/OfficeManagerServiceTests.cs` | 20 |

**Total new files: 35**

---

## 5. Integration Points (Existing Files Modified)

| File | Step | Change |
|------|------|--------|
| `CopilotAgent.sln` | 1 | Add `CopilotAgent.Office` project reference |
| `src/CopilotAgent.App/CopilotAgent.App.csproj` | 1 | Add `<ProjectReference>` to Office |
| `src/CopilotAgent.Core/Models/AppSettings.cs` | 7 | Add `public OfficeSettings Office { get; set; } = new();` |
| `src/CopilotAgent.App/App.xaml.cs` | 13 | Add DI registrations for Office services + OfficeViewModel |
| `src/CopilotAgent.App/ViewModels/MainWindowViewModel.cs` | 17 | Add `ShowOffice` property + `ShowOfficeView` command + update `ShowChat`/`ShowAgentTeamView` |
| `src/CopilotAgent.App/MainWindow.xaml` | 17 | Add Office tab button + OfficeView with visibility binding |
| `tests/CopilotAgent.Tests/CopilotAgent.Tests.csproj` | 20 | Add `<ProjectReference>` to Office |

**Total modified existing files: 7**

---

## 6. Dependency Graph

```
Step 1 (scaffold)
├── Step 2 (enums)
│   ├── Step 3 (complex models)
│   │   ├── Step 5 (events) ←── also depends on Step 4
│   │   │   ├── Step 6 (interfaces)
│   │   │   │   ├── Step 8  (event log)
│   │   │   │   ├── Step 9  (scheduler)
│   │   │   │   ├── Step 10 (assistant agent)
│   │   │   │   │   └── Step 11 (assistant pool)
│   │   │   │   │       └── Step 12 (manager service) ←── also depends on 8, 9
│   │   │   │   │           ├── Step 13 (DI) ←── also depends on 7
│   │   │   │   │           ├── Step 14 (ViewModel)
│   │   │   │   │           │   ├── Step 15 (OfficeView)
│   │   │   │   │           │   │   ├── Step 16 (side panel)
│   │   │   │   │           │   │   └── Step 17 (MainWindow)
│   │   │   │   │           │   └── Step 19 (clarification)
│   │   │   │   │           ├── Step 18 (LLM intelligence)
│   │   │   │   │           │   └── Step 19 (clarification)
│   │   │   │   │           └── Step 20 (tests)
│   │   │   │   └── ...
│   │   │   └── ...
│   │   └── Step 10 (assistant agent)
│   └── Step 4 (chat/UI models)
└── Step 7 (settings) ←── independent, can run in parallel with 2-6

Parallel tracks possible:
- Track A: Steps 1 → 2 → 3 → 5 → 6 → 8/9/10 → 11 → 12 → 13
- Track B: Steps 1 → 2 → 4 (can overlap with Track A)
- Track C: Step 7 (independent of Track A/B)
- Track D: Steps 14 → 15 → 16 → 17 (after 12+13)
- Track E: Steps 18 → 19 (after 12+14)
- Track F: Step 20 (after 8-12, 14)
```

---

## 7. Verification Gates

After each group of steps, run these checks before proceeding:

### Gate 1: After Steps 1–6 (Models + Interfaces)
- [ ] `dotnet build src/CopilotAgent.Office/CopilotAgent.Office.csproj` succeeds
- [ ] All files have `namespace CopilotAgent.Office.Models/Events/Services`
- [ ] All public types have XML doc comments
- [ ] No compiler warnings

### Gate 2: After Steps 7–12 (All Services)
- [ ] `dotnet build CopilotAgent.sln` succeeds
- [ ] `OfficeManagerService` can be manually instantiated with mocks (mental check)
- [ ] State machine transitions compile: Idle→Clarifying→Planning→AwaitingApproval→FetchingEvents→Scheduling→Executing→Aggregating→Resting→loop

### Gate 3: After Step 13 (DI)
- [ ] App starts without DI resolution errors
- [ ] `IOfficeManagerService` resolves from container
- [ ] No visible UI changes yet (Office tab not wired)

### Gate 4: After Steps 14–17 (UI)
- [ ] Office tab visible in MainWindow
- [ ] Clicking "🏢 Office" shows OfficeView
- [ ] Status bar renders
- [ ] Chat plane renders messages from `Messages` collection
- [ ] Side panel slides in/out on [📊] click
- [ ] Start → stub plan → approve → iteration visible in chat → rest countdown → loop
- [ ] Stop/Reset clears state

### Gate 5: After Steps 18–19 (LLM Intelligence)
- [ ] Manager asks clarification questions from LLM
- [ ] User responds → Manager generates plan → approval works
- [ ] Event fetching via LLM returns structured events
- [ ] Aggregation produces readable Markdown report
- [ ] Instruction injection triggers clarity evaluation
- [ ] Multi-turn clarification works inline

### Gate 6: After Step 20 (Tests)
- [ ] `dotnet test` passes all Office tests
- [ ] State machine coverage ≥ 90%
- [ ] Pool concurrency tests pass deterministically
- [ ] Scheduler tick count is accurate

---

## 8. Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| LLM returns unparseable JSON for events | High | Medium | Retry with rephrased prompt + fallback to regex extraction. Design §6.3 covers this. |
| Manager session disconnects mid-iteration | Medium | High | Implement session reconnect with context replay (same pattern as `OrchestratorService`). |
| `SemaphoreSlim` deadlock in AssistantPool if assistant throws before `Release()` | Medium | High | Use `try/finally` around semaphore acquire/release. Always release in finally block. |
| UI thread marshalling missed for one collection | Medium | Medium | Code review checklist: every `ObservableCollection.Add/Remove/Clear` must be in `Dispatcher.Invoke`. |
| Side panel animation jitters on slow machines | Low | Low | Use hardware-accelerated `RenderTransform` (already in design). Avoid layout-affecting animations. |
| Large number of commentary entries (>1000) causes UI lag | Medium | Medium | Cap `LiveCommentaries` at 200 entries, trim oldest on overflow. |
| Manager LLM call during Executing phase blocks if session is unexpectedly busy | Low | Medium | Use separate `SemaphoreSlim(1)` for Manager session access. Check phase before clarity evaluation. |
| Iteration containers in chat don't fold correctly with nested messages | Medium | Medium | Use `ItemsControl` with `GroupStyle` or custom panel. Test with 5+ iterations early. |

---

*End of Implementation Plan*