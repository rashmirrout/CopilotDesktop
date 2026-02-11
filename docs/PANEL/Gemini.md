This is a comprehensive, enterprise-grade execution plan for building **CopilotAgent.Panel**. This plan assumes a target audience of millions, requiring 99.9% reliability, strict memory management, and an architecture that scales with complexity.

We will use **Clean Architecture** principles, enforcing separation of concerns between the UI, the Orchestration Engine, and the AI Core.

---

### 1. The Technology Stack (The "No Compromise" List)

We choose technologies that offer strong typing, high performance, and long-term support.

*   **Application Framework:** **WinUI 3 (Windows App SDK 1.5+)**.
    *   *Reason:* The native, modern UI stack for Windows. Supports hardware acceleration, Mica materials, and the latest Fluent Design controls.
*   **Language:** **C# 12 / .NET 8 (LTS)**.
    *   *Reason:* Stability and performance.
*   **AI Orchestration:** **Microsoft Semantic Kernel (SK)**.
    *   *Reason:* The enterprise standard for .NET. It abstracts LLMs, handles "Tool Calling" (MCP), and manages memory contexts better than raw API calls.
*   **State Management:** **Stateless (NuGet)**.
    *   *Reason:* We need a rigid Finite State Machine (FSM). `if/else` statements are insufficient for a complex multi-agent lifecycle.
*   **Concurrency & Data Flow:** **System.Reactive (Rx.NET)** & **System.Threading.Channels**.
    *   *Reason:* To handle high-throughput token streams from multiple agents without freezing the UI thread.
*   **Dependency Injection:** **Microsoft.Extensions.DependencyInjection**.
    *   *Reason:* Essential for modularity and unit testing.
*   **UI Logic:** **CommunityToolkit.Mvvm**.
    *   *Reason:* Source generators reduce boilerplate; standard for modern .NET apps.

---

### 2. Architectural Design (The "Module" Concept)

We will build this as a standalone, loosely coupled module that plugs into your main application shell.

#### **Project Structure**
1.  **`Copilot.Panel.Core` (Net Standard 2.1)**
    *   Contains Interfaces, Enums (`PanelState`), Domain Objects (`Transcript`, `AgentProfile`), and Exceptions. Pure C#, no dependencies on UI or AI libraries.
2.  **`Copilot.Panel.Engine` (.NET 8)**
    *   The "Brain." Contains the `SemanticKernel` logic, `Agent` implementations, `Moderator` logic, MCP Client wrappers, and the `Orchestrator` (State Machine).
3.  **`Copilot.Panel.UI` (WinUI 3)**
    *   The "Face." Contains Views, ViewModels, Custom Controls, and Converters.

---

### 3. Execution Phase 1: The Core & Engine (Backend)

**Goal:** Build a robust, testable state machine that runs the debate loop in a background thread.

#### A. The State Machine Definition
We define the lifecycle strictly to prevent bugs like "Starting a debate before clarification is done."

```csharp
public enum PanelState
{
    Idle,           // Waiting for user
    Clarifying,     // Head <-> User (Refining task)
    Forming,        // Hiring Panelists based on task
    Debating,       // Panelists talking
    Moderating,     // Moderator evaluating consensus
    Synthesizing,   // Head writing final report
    Paused,         // User paused execution
    Disposing       // Cleanup
}
```

#### B. The Orchestrator (The Controller)
This class manages the `IServiceScope`. Every time a new session starts, a new Scope is created. When the session ends, the Scope is disposed, guaranteeing all AI services and HTTP clients are killed (No Memory Leaks).

*   **Concurrency:** Use `SemaphoreSlim(0)` for the **Pause** functionality. When paused, the loop awaits the semaphore. When resumed, we `Release()`.
*   **Cancellation:** Use `CancellationTokenSource` linked to the UI "Stop" button.

#### C. The Agents (Head, Moderator, Panelists)
*   **Head Agent:** Configured with a "Manager" persona. Capabilities: Interaction, Summary, Task Decomposition.
*   **Panelist Factory:** A service that analyzes the Task and instantiates specific agents (e.g., "Code Security Expert", "Performance Architect"). It injects specific **MCP Tools** (e.g., `FileFinder`, `WebCrawler`) into their Kernel.
*   **Moderator:** A specialized agent. It does not output content; it outputs *Decisions* (JSON).
    *   *Decision:* `{"NextSpeaker": "AgentA", "ConvergenceScore": 85, "StopDiscussion": false}`.

---

### 4. Execution Phase 2: The UI (Presentation)

**Goal:** An "Ultra Modern," reactive interface that visualizes the "Black Box" of AI thinking.

#### A. The Layout (Dashboard Style)
*   **Left Pane (The Interface):** A chat timeline where the **Head** talks to the User.
*   **Center Pane (The Boardroom):** A visual representation of the agents.
    *   *Tech:* Use a `Canvas` or `Grid`.
    *   *Visuals:* Circular Avatars.
    *   *States:*
        *   *Thinking:* Avatar has a spinning indeterminate ring.
        *   *Speaking:* Avatar scales up slightly, glows, and a connector line draws to the center.
        *   *Idle:* Avatar is semi-transparent.
*   **Right Pane (The Stream):** A collapsible "Live Log."
    *   Shows raw reasoning chains ("I am reading file X...").
    *   Uses **Markdown** rendering for code blocks.

#### B. The ViewModel (The Glue)
The ViewModel does **not** run the logic. It subscribes to the Engine.

```csharp
// Reactive Subscription pattern
_orchestrator.ActivityStream
    .ObserveOn(RxApp.MainThreadScheduler) // Marshal to UI Thread
    .Subscribe(activity => {
        if (activity.Type == ActivityType.Thinking)
             GetAgent(activity.AgentId).SetStatus("Thinking...");
        else if (activity.Type == ActivityType.NewMessage)
             Transcript.Add(activity.Message);
    });
```

---

### 5. Execution Phase 3: Critical Functionality Implementation

#### 1. The "Pause" & "Resume" Mechanism
Real-time control is vital for cost and UX.
*   **UI:** User clicks "Pause".
*   **ViewModel:** Calls `_orchestrator.PauseAsync()`.
*   **Engine:** The loop reaches a safe stopping point (end of current agent's turn) and awaits the `Semaphore`. Status changes to `Paused`.
*   **UI:** User clicks "Play". `_orchestrator.Resume()` releases the Semaphore.

#### 2. Clarification Loop (The Head)
Before the panel starts:
1.  User types: "Check my code."
2.  Head (internal thought): "Too vague."
3.  Head (response): "Which repository? Are we looking for security bugs or performance issues?"
4.  User answers.
5.  Head triggers `State.Forming`.

#### 3. Deep Cleanup (Anti-Memory Bloat)
For a desktop app running 24/7:
*   Implement `IAsyncDisposable` on the Orchestrator.
*   When a session finishes or is reset:
    1.  Cancel all processing tokens.
    2.  Dispose the `SemanticKernel` object (clears internal buffers).
    3.  Dispose `HttpClient` instances (socket cleanup).
    4.  Force a generic `GC.Collect()` only if memory pressure is detected (optional/advanced).

---

### 6. Development Roadmap (The Plan)

#### **Sprint 1: The Core Foundation**
*   **Day 1-2:** Solution setup. Define `IPanelAgent`, `IOrchestrator`, and `PanelState` enum.
*   **Day 3-5:** Implement the **State Machine** using the `Stateless` library. Write Unit Tests ensuring valid transitions only.
*   **Day 6-7:** Implement the `PanelistFactory`. It should accept a topic string and return a list of Agents with defined personas.

#### **Sprint 2: The Agentic Engine (Backend)**
*   **Day 8-10:** Integrate **Semantic Kernel**. Connect the Head Agent to the LLM. Implement the "Clarification Loop."
*   **Day 11-13:** Implement the **Debate Loop**.
    *   Logic: Moderator selects speaker -> Speaker runs -> Moderator checks convergence -> Repeat.
*   **Day 14:** Implement MCP Tool integration (Basic file reading capability).

#### **Sprint 3: The Modern UI (Frontend)**
*   **Day 15-17:** Build the WinUI 3 Layout. Implement `MarkdownTextBlock`.
*   **Day 18-20:** Create the **Visualizer Control**. Bind it to the ViewModel. Add animations using WinUI Composition API.
*   **Day 21:** Connect Engine events to ViewModel (Rx.NET).

#### **Sprint 4: Polish & Resilience**
*   **Day 22-23:** Implement **Pause/Resume** and **Stop**.
*   **Day 24:** **Settings Manager**. Allow user to select Primary Model (Head) vs Cheap Model (Panelists).
*   **Day 25:** **Memory Profiling**. Run `dotMemory`. Ensure creating/destroying 50 sessions results in flat memory usage.
*   **Day 26:** **Edge Case Handling**. (Network disconnect, API rate limits).

---

### 7. Code Example: The Orchestrator Loop

This is the "Secret Sauce" ensuring reliability.

```csharp
public async Task RunDebateAsync(CancellationToken token)
{
    try 
    {
        while (_fsm.State == PanelState.Debating)
        {
            token.ThrowIfCancellationRequested();

            // 1. Handle Pause Check (Thread Safe)
            await _pauseSemaphore.WaitAsync(token); 
            _pauseSemaphore.Release(); 

            // 2. Moderator Planning Step
            NotifyUI("Moderator is deciding next speaker...");
            var plan = await _moderator.PlanNextTurnAsync(_context);

            if (plan.ShouldStop)
            {
                _fsm.Fire(Trigger.ConsensusReached);
                break;
            }

            // 3. Execution Step
            var agent = _panelists[plan.NextSpeakerId];
            NotifyUI($"{agent.Name} is thinking...", agent.Id);
            
            var contribution = await agent.GenerateContributionAsync(_context, token);
            
            // 4. Update Context
            _context.AppendToTranscript(agent, contribution);
            NotifyUI(contribution, agent.Id);
        }

        if (_fsm.State == PanelState.Synthesizing)
        {
            await _head.SynthesizeFinalResultAsync(_context);
        }
    }
    catch (OperationCanceledException)
    {
        // Graceful shutdown
    }
    catch (Exception ex)
    {
        // Log and transition to Error state
        _logger.LogError(ex, "Debate crashed");
        _fsm.Fire(Trigger.Error);
    }
}
```

### 8. Final Deliverable Checklist

*   [ ] **Unit Tests:** 90% coverage on `Copilot.Panel.Engine`.
*   [ ] **Responsiveness:** UI never freezes, even during heavy LLM generation.
*   [ ] **Visualization:** Users can visually see who is talking to whom.
*   [ ] **Control:** Users can Pause, Stop, and Restart at will.
*   [ ] **Context:** Users can ask follow-up questions to the Head after the panel is dismissed.

This plan moves beyond a "demo" and provides the architectural rigor required for a commercial application serving millions of users.