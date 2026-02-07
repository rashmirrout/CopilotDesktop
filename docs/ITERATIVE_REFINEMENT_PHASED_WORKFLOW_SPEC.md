# Iterative Refinement & Phased Workflow Feature Specification

**Version:** 1.0  
**Date:** February 7, 2026  
**Author:** CopilotDesktop Engineering Team  
**Status:** Draft for Review

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Architecture](#2-architecture)
3. [High-Level Design](#3-high-level-design)
4. [Low-Level Design](#4-low-level-design)
5. [Technical Specification](#5-technical-specification)
6. [Use Cases and User Scenarios](#6-use-cases-and-user-scenarios)
7. [Implementation Checklist](#7-implementation-checklist)

---

## 1. Executive Summary

### 1.1 Feature Overview

This specification documents the implementation of two advanced agent execution modes for CopilotDesktop:

1. **Iterative Refinement Mode** (Previously "Ralph Mode")
   - Autonomous loop-based execution until task completion
   - Self-evaluation against user-defined success criteria
   - Progress tracking with state persistence
   - Configurable context management

2. **Phased Workflow Mode** (Previously "Lisa Mode")
   - Structured multi-phase execution with human review gates
   - Plan → Review → Execute → Review → Validate → Final Review
   - Evidence-based validation (screenshots, test results)
   - Phase-by-phase approval/rejection logic

### 1.2 Business Value

| Benefit | Description |
|---------|-------------|
| **Quality Assurance** | Built-in review gates and evidence collection ensure production-ready output |
| **Developer Productivity** | Autonomous iterations reduce manual back-and-forth prompting |
| **Audit Trail** | Complete history of agent decisions, tool calls, and human approvals |
| **Risk Mitigation** | Human checkpoints prevent costly mistakes in high-stakes tasks |
| **Flexibility** | Users choose execution mode based on task complexity and risk tolerance |

### 1.3 Design Principles

1. **Production-Grade Quality** - No shortcuts; designed for enterprise reliability
2. **Evidence-Driven** - Claims backed by screenshots, test results, artifacts
3. **Human-in-the-Loop** - Strategic review gates without excessive friction
4. **Graceful Degradation** - Failures are contained and recoverable
5. **Separation of Concerns** - Clean MVVM architecture with testable services

---

## 2. Architecture

### 2.1 System Context Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CopilotDesktop Application                        │
│                                                                             │
│  ┌─────────────┐    ┌─────────────────────────────────────────────────────┐│
│  │   WPF UI    │    │                   Core Services                      ││
│  │  (XAML/C#)  │◄──►│                                                      ││
│  │             │    │  ┌─────────────────┐  ┌────────────────────────────┐││
│  │ • ChatView  │    │  │ AgentExecution  │  │      CopilotSdkService     │││
│  │ • Workflow  │    │  │    Orchestrator │◄►│   (GitHub.Copilot.SDK)     │││
│  │   Panels    │    │  └────────┬────────┘  └────────────┬───────────────┘││
│  │ • Settings  │    │           │                        │                ││
│  └─────────────┘    │           ▼                        ▼                ││
│                     │  ┌─────────────────┐  ┌────────────────────────────┐││
│                     │  │  State Machine  │  │   Persistence Service      │││
│                     │  │   (Phases/      │  │   (JSON file-based)        │││
│                     │  │    Iterations)  │  └────────────────────────────┘││
│                     │  └─────────────────┘                                ││
│                     └─────────────────────────────────────────────────────┘│
└───────────────────────────────────────────────────────────────────────────┬┘
                                                                            │
                                    ┌───────────────────────────────────────▼┐
                                    │         Copilot CLI (Server Mode)      │
                                    │                                        │
                                    │  ┌────────────────────────────────────┐│
                                    │  │    Agent Runtime (Planning, Tools) ││
                                    │  └────────────────────────────────────┘│
                                    │                    │                   │
                                    │                    ▼                   │
                                    │  ┌────────────────────────────────────┐│
                                    │  │      LLM Provider (GitHub AI)      ││
                                    │  └────────────────────────────────────┘│
                                    └────────────────────────────────────────┘
```

### 2.2 Component Architecture

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                              CopilotAgent.App                                │
├──────────────────────────────────────────────────────────────────────────────┤
│  ViewModels                                                                  │
│  ├── ChatViewModel.cs              - Main chat orchestration                 │
│  ├── IterativeTaskViewModel.cs     - Iterative refinement UI logic          │
│  ├── PhasedWorkflowViewModel.cs    - [NEW] Phased workflow UI logic         │
│  └── AgentModeSelectionViewModel.cs- [NEW] Mode selection UI                │
│                                                                              │
│  Views                                                                       │
│  ├── ChatView.xaml                 - Chat interface                          │
│  ├── IterativeTaskView.xaml        - Iteration progress display              │
│  ├── PhasedWorkflowView.xaml       - [NEW] Phase progress & review UI        │
│  └── AgentModePanel.xaml           - [NEW] Mode toggle & configuration       │
└──────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                              CopilotAgent.Core                               │
├──────────────────────────────────────────────────────────────────────────────┤
│  Models                                                                      │
│  ├── AgentExecutionMode.cs         - [NEW] Mode enumeration                  │
│  ├── IterativeRefinementConfig.cs  - [EXTEND] Loop execution config          │
│  ├── PhasedWorkflowConfig.cs       - [NEW] Phase-based config                │
│  ├── WorkflowPhase.cs              - [NEW] Phase state & transitions         │
│  ├── ReviewDecision.cs             - [NEW] Approve/Reject logic              │
│  └── EvidenceCollection.cs         - [NEW] Screenshots, test results         │
│                                                                              │
│  Services                                                                    │
│  ├── IAgentExecutionOrchestrator.cs- [NEW] Main orchestration interface      │
│  ├── AgentExecutionOrchestrator.cs - [NEW] Coordinates execution modes       │
│  ├── IIterativeRefinementService.cs- [EXTEND] Loop execution                 │
│  ├── IterativeRefinementService.cs - [EXTEND] Implementation                 │
│  ├── IPhasedWorkflowService.cs     - [NEW] Phase execution interface         │
│  ├── PhasedWorkflowService.cs      - [NEW] Implementation                    │
│  ├── ICompletionSignalDetector.cs  - [NEW] Signal parsing                    │
│  ├── CompletionSignalDetector.cs   - [NEW] Implementation                    │
│  ├── IEvidenceService.cs           - [NEW] Evidence collection               │
│  └── EvidenceService.cs            - [NEW] Screenshots, reports              │
└──────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                           CopilotAgent.Persistence                           │
├──────────────────────────────────────────────────────────────────────────────┤
│  ├── WorkflowStatePersistence.cs   - [NEW] Save/restore workflow state       │
│  └── EvidenceStorage.cs            - [NEW] File-based evidence storage       │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 2.3 Data Flow Architecture

```
                     User Input (Prompt + Mode Selection)
                                    │
                                    ▼
┌───────────────────────────────────────────────────────────────────────────┐
│                    AgentExecutionOrchestrator                             │
│  ┌─────────────────────┐                    ┌───────────────────────────┐ │
│  │  Mode: Standard     │                    │  Mode: IterativeRefinement│ │
│  │  ─────────────────  │                    │  ───────────────────────  │ │
│  │  Single turn        │                    │  Loop until complete      │ │
│  │  Direct to SDK      │                    │  State persistence        │ │
│  └─────────┬───────────┘                    │  Self-evaluation          │ │
│            │                                └────────────┬──────────────┘ │
│            │      ┌───────────────────────────────────┐  │                │
│            │      │  Mode: PhasedWorkflow             │  │                │
│            │      │  ─────────────────────            │  │                │
│            │      │  Plan → Execute → Validate        │  │                │
│            │      │  Human review gates               │  │                │
│            │      │  Evidence collection              │  │                │
│            │      └──────────────┬────────────────────┘  │                │
└────────────┼─────────────────────┼───────────────────────┼────────────────┘
             │                     │                       │
             ▼                     ▼                       ▼
┌───────────────────────────────────────────────────────────────────────────┐
│                         CopilotSdkService                                 │
│                                                                           │
│   SDK Events:                                                             │
│   ├── SessionIdleEvent         → Trigger next iteration/phase            │
│   ├── AssistantMessageEvent    → Parse completion signals                │
│   ├── AssistantMessageDeltaEvent → Stream UI updates                     │
│   ├── ToolExecutionStartEvent  → Track tool progress                     │
│   ├── ToolExecutionCompleteEvent → Record tool results                   │
│   └── AssistantReasoningEvent  → Capture agent reasoning                 │
│                                                                           │
│   SDK API:                                                                │
│   ├── CreateSessionAsync()     → Initialize session                      │
│   ├── SendAndWaitAsync()       → Send prompt, wait for idle              │
│   ├── Session.On()             → Subscribe to events                     │
│   └── Session.AbortAsync()     → Cancel execution                        │
└───────────────────────────────────────────────────────────────────────────┘
```

### 2.4 Reference: copilot-ui Implementation Pattern (Best Practices)

From analyzing the copilot-ui project:

**Strengths to Adopt:**
1. **State Persistence** - Progress files and state files for resumability
2. **Completion Signal Detection** - Agent emits explicit signals (`[TASK_COMPLETE]`)
3. **Context Management** - Option to clear context between iterations
4. **Evidence Requirements** - Screenshots and test results for validation
5. **Phase History Tracking** - Complete audit trail of phase transitions

**Improvements for CopilotDesktop:**
1. **Typed State Machine** - Use C# pattern matching for phase transitions (vs JS switch)
2. **MVVM Separation** - Clean separation between UI and business logic
3. **Cancellation Tokens** - Proper async cancellation throughout
4. **DI-Based Services** - Testable, mockable service dependencies
5. **Persistence Abstraction** - Interface-based storage (vs hard-coded file paths)

---

## 3. High-Level Design

### 3.1 Agent Execution Modes

```csharp
/// <summary>
/// Available agent execution modes
/// </summary>
public enum AgentExecutionMode
{
    /// <summary>Single-turn: send prompt, receive response</summary>
    Standard = 0,
    
    /// <summary>Loop until task complete (autonomous)</summary>
    IterativeRefinement = 1,
    
    /// <summary>Multi-phase with human review gates</summary>
    PhasedWorkflow = 2
}
```

### 3.2 Iterative Refinement Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    Iterative Refinement Mode                            │
└─────────────────────────────────────────────────────────────────────────┘

User: "Build a todo app that passes all unit tests"
      + MaxIterations: 10
      + RequireEvidence: true
      + ClearContextBetweenIterations: false
                │
                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ ITERATION 1                                                             │
│ ┌─────────────────────────────────────────────────────────────────────┐ │
│ │ Agent works: Create project structure, initial files               │ │
│ │                                                                     │ │
│ │ Agent self-evaluates:                                               │ │
│ │   "Project structure created, but no tests yet."                    │ │
│ │   Criteria met: NO                                                  │ │
│ │   → Continue to next iteration                                      │ │
│ └─────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────┬───────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ ITERATION 2                                                             │
│ ┌─────────────────────────────────────────────────────────────────────┐ │
│ │ Agent works: Add unit tests, implement features                     │ │
│ │                                                                     │ │
│ │ Agent runs tests: 3/5 passing                                       │ │
│ │                                                                     │ │
│ │ Agent self-evaluates:                                               │ │
│ │   "Tests not all passing yet."                                      │ │
│ │   Criteria met: NO                                                  │ │
│ │   → Continue to next iteration                                      │ │
│ └─────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────┬───────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ ITERATION 3                                                             │
│ ┌─────────────────────────────────────────────────────────────────────┐ │
│ │ Agent works: Fix failing tests                                      │ │
│ │                                                                     │ │
│ │ Agent runs tests: 5/5 passing                                       │ │
│ │                                                                     │ │
│ │ Agent self-evaluates:                                               │ │
│ │   "All tests passing. Todo app complete."                           │ │
│ │   Criteria met: YES                                                 │ │
│ │   Signal: [ITERATIVE_TASK_COMPLETE]                                 │ │
│ │   → Task complete                                                   │ │
│ └─────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
```

### 3.3 Phased Workflow Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                       Phased Workflow Mode                              │
└─────────────────────────────────────────────────────────────────────────┘

User: "Refactor authentication to use OAuth2"
                │
                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ PHASE 1: PLAN                                                           │
│ ┌─────────────────────────────────────────────────────────────────────┐ │
│ │ Agent creates detailed implementation plan:                         │ │
│ │   1. Analyze current auth system                                    │ │
│ │   2. Design OAuth2 integration                                      │ │
│ │   3. List files to modify                                           │ │
│ │   4. Define migration strategy                                      │ │
│ │                                                                     │ │
│ │ Signal: [WORKFLOW_PHASE_COMPLETE]                                   │ │
│ └─────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────┬───────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ PHASE 2: PLAN REVIEW (Human Gate)                                       │
│ ┌─────────────────────────────────────────────────────────────────────┐ │
│ │ UI displays plan for user review                                    │ │
│ │                                                                     │ │
│ │ User options:                                                       │ │
│ │   [✓ Approve] → Proceed to Execute                                  │ │
│ │   [✗ Reject]  → Return to Plan with feedback                        │ │
│ │                                                                     │ │
│ │ User: "Approve" [WORKFLOW_REVIEW_APPROVE]                           │ │
│ └─────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────┬───────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ PHASE 3: EXECUTE                                                        │
│ ┌─────────────────────────────────────────────────────────────────────┐ │
│ │ Agent implements the approved plan:                                 │ │
│ │   • Modify AuthService.cs                                           │ │
│ │   • Add OAuth2Provider.cs                                           │ │
│ │   • Update configuration                                            │ │
│ │   • Add migration scripts                                           │ │
│ │                                                                     │ │
│ │ Signal: [WORKFLOW_PHASE_COMPLETE]                                   │ │
│ └─────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────┬───────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ PHASE 4: CODE REVIEW (Human Gate)                                       │
│ ┌─────────────────────────────────────────────────────────────────────┐ │
│ │ UI displays changes for review                                      │ │
│ │   • Diff view of modified files                                     │ │
│ │   • Summary of changes                                              │ │
│ │                                                                     │ │
│ │ User: "Approve" [WORKFLOW_REVIEW_APPROVE]                           │ │
│ └─────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────┬───────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ PHASE 5: VALIDATE                                                       │
│ ┌─────────────────────────────────────────────────────────────────────┐ │
│ │ Agent generates evidence:                                           │ │
│ │   • Run unit tests → Capture results                                │ │
│ │   • Take screenshots of OAuth flow                                  │ │
│ │   • Generate HTML summary report                                    │ │
│ │                                                                     │ │
│ │ Evidence stored in: {workspace}/evidence/                           │ │
│ │   ├── test-results.txt                                              │ │
│ │   ├── oauth-flow-screenshot.png                                     │ │
│ │   └── validation-summary.html                                       │ │
│ │                                                                     │ │
│ │ Signal: [WORKFLOW_PHASE_COMPLETE]                                   │ │
│ └─────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────┬───────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ PHASE 6: FINAL REVIEW (Human Gate)                                      │
│ ┌─────────────────────────────────────────────────────────────────────┐ │
│ │ UI displays evidence and summary:                                   │ │
│ │   • Test results: 45/45 passing                                     │ │
│ │   • Screenshots of working OAuth flow                               │ │
│ │   • Summary of all changes made                                     │ │
│ │                                                                     │ │
│ │ User: "Approve" [WORKFLOW_REVIEW_APPROVE]                           │ │
│ │                                                                     │ │
│ │ → WORKFLOW COMPLETE                                                 │ │
│ └─────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
```

### 3.4 State Transition Diagrams

#### 3.4.1 Iterative Refinement States

```
                     ┌──────────────┐
                     │  NotStarted  │
                     └──────┬───────┘
                            │ Start()
                            ▼
              ┌─────────────────────────────┐
              │         Running             │◄─────────────┐
              │   (executing iteration)     │              │
              └──────┬─────┬────────────────┘              │
                     │     │                               │
    SessionIdleEvent │     │ Self-evaluation:              │
    + Criteria Met   │     │ NOT complete                  │
                     │     │                               │
                     │     └───────────────────────────────┘
                     │         Next iteration (if < max)
                     ▼
              ┌──────────────┐                    ┌──────────────┐
              │  Completed   │                    │    Failed    │
              │              │                    │              │
              └──────────────┘                    └──────────────┘
                     ▲                                   ▲
                     │                                   │
    Signal: [TASK_COMPLETE]                      Error/Exception
    OR Criteria fully met
    
              ┌──────────────┐                    ┌──────────────┐
              │   Stopped    │                    │MaxIterations │
              │  (by user)   │                    │   Reached    │
              └──────────────┘                    └──────────────┘
```

#### 3.4.2 Phased Workflow States

```
                                    ┌──────────────┐
                                    │  NotStarted  │
                                    └──────┬───────┘
                                           │ Start()
                                           ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                                                                            │
│    ┌──────────┐    ┌─────────────┐    ┌──────────┐    ┌──────────────┐    │
│    │   Plan   │───►│ PlanReview  │───►│ Execute  │───►│  CodeReview  │    │
│    └────┬─────┘    └──────┬──────┘    └────┬─────┘    └───────┬──────┘    │
│         │                 │                │                   │          │
│         │                 │ Reject         │                   │ Reject   │
│         │                 └───────┐        │                   └─────┐    │
│         │                         │        │                         │    │
│         │◄────────────────────────┘        │◄────────────────────────┘    │
│         │                                  │                              │
│    Phase                              Phase                               │
│    Complete                           Complete                            │
│                                                                           │
│    ┌──────────────┐    ┌─────────────────┐                               │
│    │   Validate   │───►│   FinalReview   │                               │
│    └──────┬───────┘    └────────┬────────┘                               │
│           │                     │                                         │
│           │                     │ Reject                                  │
│           │                     └──────────────────┐                      │
│           │                                        │                      │
│           │◄───────────────────────────────────────┘                      │
│           │                                                               │
│      Phase                                                                │
│      Complete                                                             │
│                                                                           │
└───────────────────────────────────┬────────────────────────────────────────┘
                                    │
                                    │ Approve (FinalReview)
                                    ▼
                             ┌──────────────┐
                             │  Completed   │
                             └──────────────┘
```

---

## 4. Low-Level Design

### 4.1 Data Models

#### 4.1.1 Core Configuration Models

```csharp
// File: src/CopilotAgent.Core/Models/AgentExecutionConfig.cs

namespace CopilotAgent.Core.Models;

/// <summary>
/// Configuration for agent execution, encapsulating all mode-specific settings
/// </summary>
public class AgentExecutionConfig
{
    /// <summary>Selected execution mode</summary>
    public AgentExecutionMode Mode { get; set; } = AgentExecutionMode.Standard;
    
    /// <summary>Configuration for Iterative Refinement mode</summary>
    public IterativeRefinementConfig? IterativeRefinement { get; set; }
    
    /// <summary>Configuration for Phased Workflow mode</summary>
    public PhasedWorkflowConfig? PhasedWorkflow { get; set; }
}

/// <summary>
/// Configuration for Iterative Refinement execution mode
/// </summary>
public class IterativeRefinementConfig
{
    /// <summary>Original user prompt that initiated the task</summary>
    public string OriginalPrompt { get; set; } = string.Empty;
    
    /// <summary>Success criteria for task completion</summary>
    public string SuccessCriteria { get; set; } = string.Empty;
    
    /// <summary>Maximum number of iterations before stopping</summary>
    public int MaxIterations { get; set; } = 10;
    
    /// <summary>Current iteration number (1-based)</summary>
    public int CurrentIteration { get; set; } = 1;
    
    /// <summary>Whether the iterative mode is currently active</summary>
    public bool IsActive { get; set; }
    
    /// <summary>Whether to require evidence (screenshots) after each iteration</summary>
    public bool RequireEvidence { get; set; }
    
    /// <summary>Clear conversation context between iterations (Gemini-style)</summary>
    public bool ClearContextBetweenIterations { get; set; }
    
    /// <summary>When the task was started</summary>
    public DateTime StartedAt { get; set; }
    
    /// <summary>Path to progress tracking file</summary>
    public string? ProgressFilePath { get; set; }
    
    /// <summary>Path to state persistence file</summary>
    public string? StateFilePath { get; set; }
    
    /// <summary>Current execution state</summary>
    public IterativeRefinementState State { get; set; } = new();
}

/// <summary>
/// Runtime state for iterative refinement
/// </summary>
public class IterativeRefinementState
{
    /// <summary>Current status</summary>
    public IterativeTaskStatus Status { get; set; } = IterativeTaskStatus.NotStarted;
    
    /// <summary>History of all iterations</summary>
    public List<IterationResult> Iterations { get; set; } = new();
    
    /// <summary>Reason for completion or stoppage</summary>
    public string? CompletionReason { get; set; }
    
    /// <summary>When the task completed</summary>
    public DateTime? CompletedAt { get; set; }
}
```

#### 4.1.2 Phased Workflow Models

```csharp
// File: src/CopilotAgent.Core/Models/PhasedWorkflowConfig.cs

namespace CopilotAgent.Core.Models;

/// <summary>
/// Configuration for Phased Workflow execution mode
/// </summary>
public class PhasedWorkflowConfig
{
    /// <summary>Original user prompt that initiated the workflow</summary>
    public string OriginalPrompt { get; set; } = string.Empty;
    
    /// <summary>Current workflow phase</summary>
    public WorkflowPhase CurrentPhase { get; set; } = WorkflowPhase.Plan;
    
    /// <summary>Track iterations per phase (for re-attempts after rejection)</summary>
    public Dictionary<WorkflowPhase, int> PhaseIterations { get; set; } = new()
    {
        { WorkflowPhase.Plan, 0 },
        { WorkflowPhase.PlanReview, 0 },
        { WorkflowPhase.Execute, 0 },
        { WorkflowPhase.CodeReview, 0 },
        { WorkflowPhase.Validate, 0 },
        { WorkflowPhase.FinalReview, 0 }
    };
    
    /// <summary>Whether the workflow is currently active</summary>
    public bool IsActive { get; set; }
    
    /// <summary>Complete history of phase transitions</summary>
    public List<PhaseHistoryEntry> PhaseHistory { get; set; } = new();
    
    /// <summary>Path to evidence folder</summary>
    public string EvidenceFolderPath { get; set; } = "evidence";
    
    /// <summary>When the workflow was started</summary>
    public DateTime StartedAt { get; set; }
    
    /// <summary>Current execution state</summary>
    public PhasedWorkflowState State { get; set; } = new();
}

/// <summary>
/// Workflow phases in order of execution
/// </summary>
public enum WorkflowPhase
{
    /// <summary>Agent creates implementation plan</summary>
    Plan = 0,
    
    /// <summary>Human reviews and approves/rejects plan</summary>
    PlanReview = 1,
    
    /// <summary>Agent executes the approved plan</summary>
    Execute = 2,
    
    /// <summary>Human reviews implementation</summary>
    CodeReview = 3,
    
    /// <summary>Agent generates evidence (tests, screenshots)</summary>
    Validate = 4,
    
    /// <summary>Final human review before completion</summary>
    FinalReview = 5,
    
    /// <summary>Workflow completed successfully</summary>
    Completed = 6
}

/// <summary>
/// Entry in phase transition history
/// </summary>
public class PhaseHistoryEntry
{
    public WorkflowPhase Phase { get; set; }
    public int Iteration { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Runtime state for phased workflow
/// </summary>
public class PhasedWorkflowState
{
    /// <summary>Overall workflow status</summary>
    public WorkflowStatus Status { get; set; } = WorkflowStatus.NotStarted;
    
    /// <summary>Content produced in Plan phase</summary>
    public string? PlanContent { get; set; }
    
    /// <summary>Content produced in Execute phase</summary>
    public string? ExecutionSummary { get; set; }
    
    /// <summary>Evidence collected in Validate phase</summary>
    public EvidenceCollection? Evidence { get; set; }
    
    /// <summary>History of review decisions</summary>
    public List<ReviewDecision> ReviewHistory { get; set; } = new();
    
    /// <summary>When the workflow completed</summary>
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Workflow execution status
/// </summary>
public enum WorkflowStatus
{
    NotStarted,
    Running,
    AwaitingReview,
    Completed,
    Failed,
    Cancelled
}
```

#### 4.1.3 Review Decision Models

```csharp
// File: src/CopilotAgent.Core/Models/ReviewDecision.cs

namespace CopilotAgent.Core.Models;

/// <summary>
/// A decision made during a review phase
/// </summary>
public class ReviewDecision
{
    /// <summary>Which phase was being reviewed</summary>
    public WorkflowPhase Phase { get; set; }
    
    /// <summary>The decision made</summary>
    public ReviewDecisionType Decision { get; set; }
    
    /// <summary>User-provided feedback (especially for rejections)</summary>
    public string? Feedback { get; set; }
    
    /// <summary>When the decision was made</summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>Who made the decision (for audit)</summary>
    public string? ReviewedBy { get; set; }
}

/// <summary>
/// Types of review decisions
/// </summary>
public enum ReviewDecisionType
{
    /// <summary>Work approved, proceed to next phase</summary>
    Approve,
    
    /// <summary>Work rejected, return to previous phase with feedback</summary>
    Reject,
    
    /// <summary>Review skipped (auto-approve mode)</summary>
    Skipped
}
```

#### 4.1.4 Evidence Collection Models

```csharp
// File: src/CopilotAgent.Core/Models/EvidenceCollection.cs

namespace CopilotAgent.Core.Models;

/// <summary>
/// Collection of evidence artifacts for validation
/// </summary>
public class EvidenceCollection
{
    /// <summary>Path to evidence folder</summary>
    public string FolderPath { get; set; } = string.Empty;
    
    /// <summary>Screenshot files</summary>
    public List<EvidenceFile> Screenshots { get; set; } = new();
    
    /// <summary>Test result files</summary>
    public List<EvidenceFile> TestResults { get; set; } = new();
    
    /// <summary>Generated reports</summary>
    public List<EvidenceFile> Reports { get; set; } = new();
    
    /// <summary>HTML summary report path</summary>
    public string? SummaryReportPath { get; set; }
    
    /// <summary>When evidence was collected</summary>
    public DateTime CollectedAt { get; set; }
}

/// <summary>
/// A single evidence file
/// </summary>
public class EvidenceFile
{
    /// <summary>File path relative to evidence folder</summary>
    public string Path { get; set; } = string.Empty;
    
    /// <summary>Human-readable description</summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>File type</summary>
    public EvidenceFileType Type { get; set; }
    
    /// <summary>When file was created</summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>File size in bytes</summary>
    public long SizeBytes { get; set; }
}

/// <summary>
/// Types of evidence files
/// </summary>
public enum EvidenceFileType
{
    Screenshot,
    TestResult,
    LogFile,
    HtmlReport,
    Other
}
```

### 4.2 Service Interfaces

#### 4.2.1 Agent Execution Orchestrator

```csharp
// File: src/CopilotAgent.Core/Services/IAgentExecutionOrchestrator.cs

namespace CopilotAgent.Core.Services;

/// <summary>
/// Orchestrates agent execution across different modes
/// </summary>
public interface IAgentExecutionOrchestrator
{
    /// <summary>Current execution configuration</summary>
    AgentExecutionConfig? CurrentConfig { get; }
    
    /// <summary>Whether an execution is currently in progress</summary>
    bool IsExecuting { get; }
    
    /// <summary>Starts execution with the specified configuration</summary>
    Task StartExecutionAsync(
        string prompt, 
        AgentExecutionConfig config, 
        CancellationToken cancellationToken = default);
    
    /// <summary>Stops the current execution</summary>
    Task StopExecutionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>Submits a review decision (for Phased Workflow)</summary>
    Task SubmitReviewDecisionAsync(
        ReviewDecisionType decision, 
        string? feedback = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>Event raised when execution state changes</summary>
    event EventHandler<ExecutionStateChangedEventArgs>? StateChanged;
    
    /// <summary>Event raised when a review is required</summary>
    event EventHandler<ReviewRequiredEventArgs>? ReviewRequired;
    
    /// <summary>Event raised when execution completes</summary>
    event EventHandler<ExecutionCompletedEventArgs>? ExecutionCompleted;
}
```

#### 4.2.2 Completion Signal Detector

```csharp
// File: src/CopilotAgent.Core/Services/ICompletionSignalDetector.cs

namespace CopilotAgent.Core.Services;

/// <summary>
/// Detects completion signals in agent output
/// </summary>
public interface ICompletionSignalDetector
{
    /// <summary>Checks if content contains task completion signal</summary>
    bool ContainsTaskCompleteSignal(string content);
    
    /// <summary>Checks if content contains phase completion signal</summary>
    bool ContainsPhaseCompleteSignal(string content);
    
    /// <summary>Checks if content contains review approval signal</summary>
    bool ContainsReviewApproveSignal(string content);
    
    /// <summary>Extracts rejection feedback if present</summary>
    (bool IsRejection, string? Feedback) TryExtractRejection(string content);
    
    /// <summary>Parses self-evaluation from agent output</summary>
    SelfEvaluation? ParseSelfEvaluation(string content);
}

/// <summary>
/// Result of agent self-evaluation
/// </summary>
public class SelfEvaluation
{
    public bool IsComplete { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public double? ConfidenceScore { get; set; }
}
```

#### 4.2.3 Evidence Service

```csharp
// File: src/CopilotAgent.Core/Services/IEvidenceService.cs

namespace CopilotAgent.Core.Services;

/// <summary>
/// Manages evidence collection and storage
/// </summary>
public interface IEvidenceService
{
    /// <summary>Initializes evidence folder for a workflow</summary>
    Task<string> InitializeEvidenceFolderAsync(
        string workspacePath, 
        CancellationToken cancellationToken = default);
    
    /// <summary>Captures a screenshot</summary>
    Task<EvidenceFile> CaptureScreenshotAsync(
        string evidenceFolderPath,
        string description,
        CancellationToken cancellationToken = default);
    
    /// <summary>Records test results</summary>
    Task<EvidenceFile> RecordTestResultsAsync(
        string evidenceFolderPath,
        string testOutput,
        string description,
        CancellationToken cancellationToken = default);
    
    /// <summary>Generates HTML summary report</summary>
    Task<string> GenerateSummaryReportAsync(
        EvidenceCollection evidence,
        string workflowSummary,
        CancellationToken cancellationToken = default);
    
    /// <summary>Loads existing evidence collection</summary>
    Task<EvidenceCollection?> LoadEvidenceCollectionAsync(
        string evidenceFolderPath,
        CancellationToken cancellationToken = default);
}
```

### 4.3 Completion Signal Constants

```csharp
// File: src/CopilotAgent.Core/Constants/CompletionSignals.cs

namespace CopilotAgent.Core.Constants;

/// <summary>
/// Signal constants for agent communication
/// </summary>
public static class CompletionSignals
{
    // Iterative Refinement signals
    public const string TaskComplete = "[ITERATIVE_TASK_COMPLETE]";
    public const string TaskContinue = "[ITERATIVE_CONTINUE]";
    
    // Phased Workflow signals
    public const string PhaseComplete = "[WORKFLOW_PHASE_COMPLETE]";
    public const string ReviewApprove = "[WORKFLOW_REVIEW_APPROVE]";
    public const string ReviewRejectPrefix = "<workflow-review>reject:";
    public const string ReviewRejectSuffix = "</workflow-review>";
    
    // File names
    public const string ProgressFileName = "iterative-progress.md";
    public const string StateFileName = ".iterative-state.json";
    public const string EvidenceFolderName = "evidence";
    public const string SummaryReportFileName = "validation-summary.html";
}
```

---

## 5. Technical Specification

### 5.1 SDK Capabilities (GitHub.Copilot.SDK)

Based on analysis of the official .NET SDK:

#### 5.1.1 Core API

```csharp
// Session Creation
await using var session = await client.CreateSessionAsync(new SessionConfig
{
    SessionId = "unique-session-id",      // Required for resumability
    Model = "gpt-4.1",
    Streaming = true,
    WorkingDirectory = "/path/to/workspace",
    Tools = [customTool1, customTool2],   // Custom tools
    Hooks = new SessionHooks { ... }      // Hook handlers
});

// Send and Wait
var response = await session.SendAndWaitAsync(
    new MessageOptions { Prompt = "Your prompt here" },
    timeout: TimeSpan.FromMinutes(5));

// Event Subscription
using var subscription = session.On(evt =>
{
    switch (evt)
    {
        case AssistantMessageDeltaEvent delta:
            // Handle streaming content
            break;
        case SessionIdleEvent:
            // Agent finished turn - trigger next iteration
            break;
        case ToolExecutionStartEvent toolStart:
            // Tool began executing
            break;
        case ToolExecutionCompleteEvent toolComplete:
            // Tool finished
            break;
        case AssistantReasoningEvent reasoning:
            // Capture agent reasoning
            break;
    }
});

// Session Control
await session.AbortAsync();       // Cancel current operation
await session.DisposeAsync();     // End session
```

#### 5.1.2 Key Session Events for Iteration Control

| Event | Type | Use Case |
|-------|------|----------|
| `SessionIdleEvent` | Trigger | Agent finished turn → check completion → trigger next iteration |
| `AssistantMessageEvent` | Content | Full message → parse for completion signals |
| `AssistantMessageDeltaEvent` | Streaming | Incremental content → UI updates |
| `ToolExecutionStartEvent` | Progress | Tool starting → show progress indicator |
| `ToolExecutionCompleteEvent` | Result | Tool finished → record result |
| `AssistantReasoningEvent` | Reasoning | Agent thinking → capture for audit |
| `SessionErrorEvent` | Error | Handle errors gracefully |

#### 5.1.3 Session Hooks for Control

```csharp
var hooks = new SessionHooks
{
    OnPreToolUse = async (input, invocation) =>
    {
        // Intercept before tool execution
        // Can approve, deny, or modify
        return new PreToolUseHookOutput 
        { 
            PermissionDecision = "allow" 
        };
    },
    
    OnPostToolUse = async (input, invocation) =>
    {
        // Process after tool execution
        // Can transform results
        return null;
    },
    
    OnSessionStart = async (input, invocation) =>
    {
        // Add context at session start
        return new SessionStartHookOutput
        {
            AdditionalContext = "Execution mode context here"
        };
    }
};
```

### 5.2 Code Flow Diagrams

#### 5.2.1 Iterative Refinement Code Flow

```
User clicks "Start Iterative Task"
          │
          ▼
┌─────────────────────────────────────────────────────────────────────┐
│ ChatViewModel.StartIterativeRefinementCommand                       │
│   │                                                                 │
│   ├─► Validate inputs (prompt, criteria, maxIterations)            │
│   │                                                                 │
│   ├─► Create IterativeRefinementConfig                             │
│   │   {                                                             │
│   │     OriginalPrompt: userPrompt,                                 │
│   │     SuccessCriteria: criteria,                                  │
│   │     MaxIterations: 10,                                          │
│   │     CurrentIteration: 1                                         │
│   │   }                                                             │
│   │                                                                 │
│   └─► _orchestrator.StartExecutionAsync(prompt, config)            │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ AgentExecutionOrchestrator.StartExecutionAsync()                   │
│   │                                                                 │
│   ├─► BuildIterativePrompt(originalPrompt, criteria, iteration)    │
│   │   """                                                           │
│   │   Task: {originalPrompt}                                        │
│   │                                                                 │
│   │   Success Criteria: {criteria}                                  │
│   │                                                                 │
│   │   Instructions: Work on this task. After completing your work, │
│   │   evaluate whether the success criteria are met.               │
│   │                                                                 │
│   │   If criteria ARE met: emit [ITERATIVE_TASK_COMPLETE]          │
│   │   If criteria NOT met: describe what remains to be done        │
│   │   """                                                           │
│   │                                                                 │
│   ├─► Subscribe to SDK events                                       │
│   │                                                                 │
│   └─► _copilotService.SendMessageAsync(builtPrompt)                │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ SDK Events Processing Loop                                          │
│                                                                     │
│   AssistantMessageDeltaEvent ─► Update UI with streaming content   │
│                                                                     │
│   ToolExecutionStartEvent ───► Update iteration.CurrentToolName     │
│                                                                     │
│   ToolExecutionCompleteEvent ► Record in iteration.ToolExecutions  │
│                                                                     │
│   AssistantReasoningEvent ───► Capture in iteration.AgentReasoning │
│                                                                     │
│   SessionIdleEvent ──────────► TRIGGER ITERATION CHECK             │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ OnSessionIdle() - Iteration Check                                   │
│   │                                                                 │
│   ├─► Get accumulated message content                               │
│   │                                                                 │
│   ├─► _signalDetector.ContainsTaskCompleteSignal(content)?         │
│   │   │                                                             │
│   │   ├─► YES: Mark task complete, raise ExecutionCompleted        │
│   │   │                                                             │
│   │   └─► NO: Check iteration count                                │
│   │       │                                                         │
│   │       ├─► currentIteration >= maxIterations?                   │
│   │       │   │                                                     │
│   │       │   ├─► YES: Mark MaxIterationsReached                   │
│   │       │   │                                                     │
│   │       │   └─► NO: StartNextIteration()                         │
│   │       │       │                                                 │
│   │       │       ├─► Increment currentIteration                   │
│   │       │       ├─► Persist state                                │
│   │       │       ├─► (Optional) Clear context                     │
│   │       │       └─► Send continuation prompt                     │
│   │       │                                                         │
│   └───────┴──► Loop continues                                       │
└─────────────────────────────────────────────────────────────────────┘
```

#### 5.2.2 Phased Workflow Code Flow

```
User clicks "Start Guided Workflow"
          │
          ▼
┌─────────────────────────────────────────────────────────────────────┐
│ ChatViewModel.StartPhasedWorkflowCommand                           │
│   │                                                                 │
│   ├─► Create PhasedWorkflowConfig                                   │
│   │   {                                                             │
│   │     OriginalPrompt: userPrompt,                                 │
│   │     CurrentPhase: Plan,                                         │
│   │     EvidenceFolderPath: "{workspace}/evidence"                  │
│   │   }                                                             │
│   │                                                                 │
│   └─► _orchestrator.StartExecutionAsync(prompt, config)            │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Phase: PLAN                                                         │
│   │                                                                 │
│   ├─► BuildPhasePrompt(Plan, originalPrompt)                        │
│   │   """                                                           │
│   │   Task: {originalPrompt}                                        │
│   │                                                                 │
│   │   Phase: PLANNING                                               │
│   │                                                                 │
│   │   Create a detailed implementation plan including:              │
│   │   1. Analysis of current state                                  │
│   │   2. Step-by-step implementation approach                      │
│   │   3. Files to create/modify                                     │
│   │   4. Potential risks and mitigations                           │
│   │                                                                 │
│   │   When plan is complete: emit [WORKFLOW_PHASE_COMPLETE]         │
│   │   """                                                           │
│   │                                                                 │
│   └─► Send to SDK, wait for SessionIdleEvent                       │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ OnSessionIdle() - Phase Complete Detection                          │
│   │                                                                 │
│   ├─► _signalDetector.ContainsPhaseCompleteSignal(content)?        │
│   │                                                                 │
│   ├─► YES: Transition to review phase                               │
│   │   │                                                             │
│   │   ├─► Store plan content                                       │
│   │   ├─► Update state: CurrentPhase = PlanReview                  │
│   │   ├─► Update status: AwaitingReview                            │
│   │   └─► Raise ReviewRequired event                               │
│   │                                                                 │
│   └─► NO: Agent still working (or failed)                          │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ UI: Review Dialog                                                   │
│   │                                                                 │
│   ├─► Display plan content to user                                  │
│   │                                                                 │
│   ├─► User clicks [Approve] or [Reject]                            │
│   │                                                                 │
│   └─► _orchestrator.SubmitReviewDecisionAsync(decision, feedback)  │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ AgentExecutionOrchestrator.SubmitReviewDecisionAsync()             │
│   │                                                                 │
│   ├─► decision == Approve?                                          │
│   │   │                                                             │
│   │   ├─► YES: TransitionToNextPhase()                             │
│   │   │   │                                                         │
│   │   │   └─► PlanReview → Execute                                 │
│   │   │                                                             │
│   │   └─► NO (Reject): ReturnToPreviousPhase(feedback)             │
│   │       │                                                         │
│   │       └─► PlanReview → Plan (with feedback injected)           │
│   │                                                                 │
│   └─► Continue workflow loop                                        │
└─────────────────────────────────────────────────────────────────────┘
                                │
            ┌───────────────────┴───────────────────┐
            │                                       │
            ▼                                       ▼
┌───────────────────────────┐         ┌───────────────────────────┐
│ Phase: EXECUTE            │         │ Phase: VALIDATE           │
│                           │         │                           │
│ Agent implements plan     │         │ Agent generates evidence: │
│ Creates/modifies files    │   ...   │ • Run tests               │
│ Signal: PHASE_COMPLETE    │         │ • Capture screenshots     │
│         ▼                 │         │ • Generate report         │
│ → CodeReview              │         │ Signal: PHASE_COMPLETE    │
└───────────────────────────┘         │         ▼                 │
                                      │ → FinalReview             │
                                      └───────────────────────────┘
                                                  │
                                                  ▼
                                      ┌───────────────────────────┐
                                      │ Phase: FINAL REVIEW       │
                                      │                           │
                                      │ User reviews evidence     │
                                      │ Approves final result     │
                                      │                           │
                                      │ → WORKFLOW COMPLETE       │
                                      └───────────────────────────┘
```

### 5.3 UI Enhancements

#### 5.3.1 Mode Selection Panel

```xml
<!-- File: src/CopilotAgent.App/Views/AgentModePanel.xaml -->
<UserControl>
    <StackPanel>
        <TextBlock Text="Agent Execution Mode" Style="{StaticResource HeaderStyle}"/>
        
        <RadioButton x:Name="StandardMode" 
                     Content="Standard Mode"
                     IsChecked="{Binding IsStandardMode}"
                     GroupName="ExecutionMode">
            <RadioButton.ToolTip>
                Single response per prompt. Best for quick questions.
            </RadioButton.ToolTip>
        </RadioButton>
        
        <RadioButton x:Name="IterativeMode"
                     Content="Auto-Complete Mode"
                     IsChecked="{Binding IsIterativeMode}"
                     GroupName="ExecutionMode">
            <RadioButton.ToolTip>
                Agent works in a loop until task is complete.
                Best for tasks with clear success criteria.
            </RadioButton.ToolTip>
        </RadioButton>
        
        <RadioButton x:Name="PhasedMode"
                     Content="Guided Workflow Mode"
                     IsChecked="{Binding IsPhasedMode}"
                     GroupName="ExecutionMode">
            <RadioButton.ToolTip>
                Step-by-step execution with review checkpoints.
                Best for complex, high-stakes tasks.
            </RadioButton.ToolTip>
        </RadioButton>
        
        <!-- Iterative Mode Options (visible when selected) -->
        <StackPanel Visibility="{Binding IsIterativeMode, Converter={StaticResource BoolToVisibility}}">
            <TextBox Text="{Binding SuccessCriteria}"
                     PlaceholderText="Enter success criteria..."/>
            <Slider Value="{Binding MaxIterations}" Minimum="1" Maximum="20"/>
            <CheckBox Content="Clear context between iterations"
                      IsChecked="{Binding ClearContextBetweenIterations}"/>
            <CheckBox Content="Require evidence"
                      IsChecked="{Binding RequireEvidence}"/>
        </StackPanel>
    </StackPanel>
</UserControl>
```

#### 5.3.2 Workflow Progress Panel

```xml
<!-- File: src/CopilotAgent.App/Views/PhasedWorkflowView.xaml -->
<UserControl>
    <Grid>
        <!-- Phase Progress Indicator -->
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
            <local:PhaseIndicator Phase="Plan" 
                                  Status="{Binding PlanPhaseStatus}"/>
            <Path Data="→" />
            <local:PhaseIndicator Phase="PlanReview" 
                                  Status="{Binding PlanReviewStatus}"/>
            <Path Data="→" />
            <local:PhaseIndicator Phase="Execute" 
                                  Status="{Binding ExecutePhaseStatus}"/>
            <Path Data="→" />
            <local:PhaseIndicator Phase="CodeReview" 
                                  Status="{Binding CodeReviewStatus}"/>
            <Path Data="→" />
            <local:PhaseIndicator Phase="Validate" 
                                  Status="{Binding ValidatePhaseStatus}"/>
            <Path Data="→" />
            <local:PhaseIndicator Phase="FinalReview" 
                                  Status="{Binding FinalReviewStatus}"/>
        </StackPanel>
        
        <!-- Review Panel (visible during review phases) -->
        <Border Visibility="{Binding IsAwaitingReview, Converter={StaticResource BoolToVisibility}}"
                Background="{StaticResource ReviewPanelBackground}">
            <StackPanel>
                <TextBlock Text="{Binding ReviewTitle}" Style="{StaticResource TitleStyle}"/>
                <ScrollViewer>
                    <TextBlock Text="{Binding ContentToReview}" TextWrapping="Wrap"/>
                </ScrollViewer>
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                    <Button Content="✓ Approve" Command="{Binding ApproveCommand}"
                            Style="{StaticResource ApproveButtonStyle}"/>
                    <Button Content="✗ Reject" Command="{Binding RejectCommand}"
                            Style="{StaticResource RejectButtonStyle}"/>
                </StackPanel>
                <TextBox Text="{Binding RejectionFeedback}"
                         Visibility="{Binding ShowFeedbackBox}"
                         PlaceholderText="Enter feedback for rejection..."/>
            </StackPanel>
        </Border>
        
        <!-- Evidence Viewer (visible during final review) -->
        <local:EvidenceViewer Evidence="{Binding EvidenceCollection}"
                              Visibility="{Binding ShowEvidenceViewer}"/>
    </Grid>
</UserControl>
```

### 5.4 Threading Model

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Threading Architecture                            │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   UI Thread     │    │  Task Thread    │    │ Background Pool │
│   (STA)         │    │  (Worker)       │    │  (Thread Pool)  │
└────────┬────────┘    └────────┬────────┘    └────────┬────────┘
         │                      │                      │
         │ User Input           │                      │
         ├──────────────────────►                      │
         │                      │                      │
         │                      │ SDK Calls           │
         │                      ├──────────────────────►
         │                      │                      │
         │                      │                      │ JSON-RPC to CLI
         │                      │                      │
         │                      │◄─────────────────────┤ SDK Events
         │                      │                      │
         │◄─────────────────────┤                      │
         │ Dispatcher.Invoke    │                      │
         │ (UI Updates)         │                      │
         │                      │                      │
         │                      │ Next Iteration      │
         │                      ├──────────────────────►
         │                      │                      │
         ▼                      ▼                      ▼

Key Synchronization Points:
─────────────────────────────
1. UI updates via Dispatcher.InvokeAsync()
2. State changes via SemaphoreSlim for concurrency control
3. CancellationToken propagation for clean shutdown
4. ObservableCollection updates via synchronization context
```

**Threading Rules:**

1. **UI Thread (STA)**
   - All ViewModel property changes
   - ObservableCollection modifications
   - INotifyPropertyChanged notifications

2. **Task Thread**
   - SDK `SendAndWaitAsync` calls
   - State machine transitions
   - File I/O for persistence

3. **Event Handlers**
   - SDK events arrive on background threads
   - Must marshal to UI thread for ViewModel updates
   - Use `Dispatcher.InvokeAsync()` for WPF

```csharp
// Example: Handling SDK events with proper threading
session.On(evt =>
{
    switch (evt)
    {
        case AssistantMessageDeltaEvent delta:
            // Marshal to UI thread
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _viewModel.AppendContent(delta.Data.DeltaContent);
            });
            break;
            
        case SessionIdleEvent:
            // Iteration logic can run on background thread
            Task.Run(async () =>
            {
                await ProcessIterationCompleteAsync();
            });
            break;
    }
});
```

### 5.5 Backend Services Implementation

#### 5.5.1 Agent Execution Orchestrator Implementation

```csharp
// File: src/CopilotAgent.Core/Services/AgentExecutionOrchestrator.cs

namespace CopilotAgent.Core.Services;

public class AgentExecutionOrchestrator : IAgentExecutionOrchestrator
{
    private readonly ICopilotService _copilotService;
    private readonly ICompletionSignalDetector _signalDetector;
    private readonly IEvidenceService _evidenceService;
    private readonly IPersistenceService _persistenceService;
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    
    private AgentExecutionConfig? _currentConfig;
    private CancellationTokenSource? _executionCts;
    private StringBuilder _accumulatedContent = new();
    
    public AgentExecutionConfig? CurrentConfig => _currentConfig;
    public bool IsExecuting => _executionCts != null && !_executionCts.IsCancellationRequested;
    
    public event EventHandler<ExecutionStateChangedEventArgs>? StateChanged;
    public event EventHandler<ReviewRequiredEventArgs>? ReviewRequired;
    public event EventHandler<ExecutionCompletedEventArgs>? ExecutionCompleted;
    
    public async Task StartExecutionAsync(
        string prompt,
        AgentExecutionConfig config,
        CancellationToken cancellationToken = default)
    {
        await _executionLock.WaitAsync(cancellationToken);
        try
        {
            _currentConfig = config;
            _executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _accumulatedContent.Clear();
            
            switch (config.Mode)
            {
                case AgentExecutionMode.Standard:
                    await ExecuteStandardModeAsync(prompt, _executionCts.Token);
                    break;
                    
                case AgentExecutionMode.IterativeRefinement:
                    await ExecuteIterativeRefinementAsync(prompt, config.IterativeRefinement!, _executionCts.Token);
                    break;
                    
                case AgentExecutionMode.PhasedWorkflow:
                    await ExecutePhasedWorkflowAsync(prompt, config.PhasedWorkflow!, _executionCts.Token);
                    break;
            }
        }
        finally
        {
            _executionLock.Release();
        }
    }
    
    private async Task ExecuteIterativeRefinementAsync(
        string prompt,
        IterativeRefinementConfig config,
        CancellationToken cancellationToken)
    {
        config.IsActive = true;
        config.State.Status = IterativeTaskStatus.Running;
        config.StartedAt = DateTime.UtcNow;
        
        // Subscribe to SDK events
        var session = _copilotService.CurrentSession;
        using var subscription = session!.On(evt => HandleIterativeEvent(evt, config));
        
        while (config.IsActive && 
               config.CurrentIteration <= config.MaxIterations &&
               !cancellationToken.IsCancellationRequested)
        {
            // Build iteration prompt
            var iterationPrompt = BuildIterativePrompt(prompt, config);
            
            // Create iteration record
            var iteration = new IterationResult
            {
                IterationNumber = config.CurrentIteration,
                StartedAt = DateTime.UtcNow,
                Status = IterationStatus.Running
            };
            config.State.Iterations.Add(iteration);
            
            RaiseStateChanged(new ExecutionStateChangedEventArgs
            {
                State = ExecutionState.IterationStarted,
                IterationNumber = config.CurrentIteration
            });
            
            // Send to agent and wait for idle
            _accumulatedContent.Clear();
            await _copilotService.SendMessageAsync(iterationPrompt, cancellationToken);
            
            // Wait for processing (SessionIdleEvent triggers completion check)
            await WaitForIterationCompleteAsync(cancellationToken);
            
            // Check for completion
            if (_signalDetector.ContainsTaskCompleteSignal(_accumulatedContent.ToString()))
            {
                config.State.Status = IterativeTaskStatus.Completed;
                config.State.CompletionReason = "Success criteria met";
                config.State.CompletedAt = DateTime.UtcNow;
                config.IsActive = false;
                
                RaiseExecutionCompleted(new ExecutionCompletedEventArgs
                {
                    Success = true,
                    CompletionReason = "Task completed successfully"
                });
                return;
            }
            
            // Prepare for next iteration
            config.CurrentIteration++;
            
            if (config.ClearContextBetweenIterations)
            {
                await _copilotService.ClearContextAsync(cancellationToken);
            }
            
            // Persist state
            await _persistenceService.SaveIterativeStateAsync(config, cancellationToken);
        }
        
        // Max iterations reached
        if (config.CurrentIteration > config.MaxIterations)
        {
            config.State.Status = IterativeTaskStatus.MaxIterationsReached;
            config.State.CompletionReason = $"Reached maximum {config.MaxIterations} iterations";
            config.IsActive = false;
            
            RaiseExecutionCompleted(new ExecutionCompletedEventArgs
            {
                Success = false,
                CompletionReason = config.State.CompletionReason
            });
        }
    }
    
    private string BuildIterativePrompt(string originalPrompt, IterativeRefinementConfig config)
    {
        if (config.CurrentIteration == 1)
        {
            return $"""
                Task: {originalPrompt}
                
                Success Criteria: {config.SuccessCriteria}
                
                This is an iterative task. You will work on it step by step.
                
                Instructions:
                1. Work on the task to make progress toward the success criteria
                2. After completing your work, evaluate whether the success criteria are fully met
                3. If the criteria ARE fully met, emit the signal: {CompletionSignals.TaskComplete}
                4. If the criteria are NOT yet met, describe what remains to be done
                
                Begin working on the task now.
                """;
        }
        else
        {
            var previousIteration = config.State.Iterations.LastOrDefault();
            return $"""
                Continue working on the task.
                
                Task: {originalPrompt}
                Success Criteria: {config.SuccessCriteria}
                
                Iteration: {config.CurrentIteration} of {config.MaxIterations}
                
                Previous iteration summary: {previousIteration?.Summary ?? "N/A"}
                
                Continue making progress. When the success criteria are fully met,
                emit the signal: {CompletionSignals.TaskComplete}
                """;
        }
    }
    
    // ... Additional implementation methods
}
```

---

## 6. Use Cases and User Scenarios

### 6.1 Iterative Refinement Scenarios

#### Scenario 1: Building a Feature with Tests

**User Story:** As a developer, I want to build a complete feature including tests without manually prompting multiple times.

**Flow:**
1. User enters: "Create a REST API endpoint for user registration with email validation and unit tests"
2. User sets success criteria: "Endpoint works, validation prevents invalid emails, all tests pass"
3. User enables Iterative Refinement mode, max 10 iterations
4. Agent:
   - Iteration 1: Creates API endpoint structure
   - Iteration 2: Adds email validation logic
   - Iteration 3: Creates unit tests
   - Iteration 4: Runs tests, fixes failing tests
   - Iteration 5: Confirms all tests pass → emits `[ITERATIVE_TASK_COMPLETE]`
5. Task completes automatically

#### Scenario 2: Debugging Complex Issue

**User Story:** As a developer, I want the agent to debug an issue autonomously until it's resolved.

**Flow:**
1. User enters: "Fix the memory leak in the WebSocket handler"
2. User sets criteria: "Memory usage stabilizes after 1000 connections, no heap growth"
3. Agent iterates:
   - Analyzes code → identifies suspect areas
   - Adds diagnostics → confirms leak location
   - Implements fix → re-tests
   - Validates fix → emits completion signal

### 6.2 Phased Workflow Scenarios

#### Scenario 1: Major Refactoring

**User Story:** As a tech lead, I want oversight during a major refactoring to prevent costly mistakes.

**Flow:**
1. User enters: "Refactor the authentication system from session-based to JWT"
2. User enables Phased Workflow mode

**Plan Phase:**
- Agent creates detailed migration plan
- Lists all files to change
- Identifies breaking changes

**Plan Review:**
- User reviews plan
- Notices missing backward compatibility
- Rejects with feedback: "Need migration path for existing sessions"
- Agent revises plan

**Plan Review (2nd attempt):**
- Updated plan includes migration strategy
- User approves

**Execute Phase:**
- Agent implements changes per approved plan
- Creates new JWT service
- Modifies authentication middleware
- Updates tests

**Code Review:**
- User reviews implementation
- Approves changes

**Validate Phase:**
- Agent runs test suite → captures results
- Tests JWT flow → takes screenshots
- Generates validation report

**Final Review:**
- User reviews evidence
- Confirms all tests pass
- Approves → Workflow complete

#### Scenario 2: Production Deployment Preparation

**User Story:** As a DevOps engineer, I want structured preparation for a production deployment with checkpoints.

**Flow:**
1. User enters: "Prepare production deployment for v2.5.0 release"
2. Phased workflow with review gates at each step
3. Evidence includes deployment scripts, rollback procedures, health checks

### 6.3 Mode Selection Decision Tree

```
                    Start
                      │
                      ▼
            ┌─────────────────┐
            │  Is task quick  │
            │  and simple?    │
            └────────┬────────┘
                     │
         ┌───────────┴───────────┐
         │ YES                   │ NO
         ▼                       ▼
    ┌─────────┐         ┌─────────────────┐
    │Standard │         │ Are there clear │
    │  Mode   │         │ success criteria│
    └─────────┘         │ to evaluate?    │
                        └────────┬────────┘
                                 │
                    ┌────────────┴────────────┐
                    │ YES                     │ NO
                    ▼                         ▼
         ┌───────────────────┐      ┌─────────────────┐
         │ Is human oversight│      │ Is the task     │
         │ critical for this │      │ high-risk or    │
         │ task?             │      │ complex?        │
         └─────────┬─────────┘      └────────┬────────┘
                   │                         │
         ┌─────────┴─────────┐      ┌────────┴────────┐
         │ NO           YES  │      │ YES        NO   │
         ▼                   ▼      ▼                 ▼
    ┌──────────────┐  ┌────────────────┐        ┌─────────┐
    │  Iterative   │  │    Phased      │        │Standard │
    │  Refinement  │  │   Workflow     │        │  Mode   │
    │    Mode      │  │    Mode        │        └─────────┘
    └──────────────┘  └────────────────┘
```

---

## 7. Implementation Checklist

### 7.1 Phase 1: Core Models (Week 1)

- [ ] Create `AgentExecutionMode.cs` enumeration
- [ ] Create `AgentExecutionConfig.cs` (umbrella config)
- [ ] Extend `IterativeRefinementConfig.cs` with new properties
- [ ] Create `PhasedWorkflowConfig.cs`
- [ ] Create `WorkflowPhase.cs` enumeration
- [ ] Create `PhasedWorkflowState.cs`
- [ ] Create `ReviewDecision.cs` and `ReviewDecisionType.cs`
- [ ] Create `EvidenceCollection.cs` and `EvidenceFile.cs`
- [ ] Create `CompletionSignals.cs` constants

### 7.2 Phase 2: Core Services (Week 2)

- [ ] Create `IAgentExecutionOrchestrator.cs` interface
- [ ] Implement `AgentExecutionOrchestrator.cs`
- [ ] Create `ICompletionSignalDetector.cs` interface
- [ ] Implement `CompletionSignalDetector.cs`
- [ ] Create `IIterativeRefinementService.cs` (extend existing)
- [ ] Implement iterative loop logic
- [ ] Create `IPhasedWorkflowService.cs` interface
- [ ] Implement `PhasedWorkflowService.cs` state machine

### 7.3 Phase 3: Evidence & Persistence (Week 3)

- [ ] Create `IEvidenceService.cs` interface
- [ ] Implement `EvidenceService.cs`
  - [ ] Screenshot capture integration
  - [ ] Test result recording
  - [ ] HTML report generation
- [ ] Extend `IPersistenceService.cs` for workflow state
- [ ] Implement state persistence for both modes
- [ ] Add state restoration on app restart

### 7.4 Phase 4: SDK Integration (Week 3-4)

- [ ] Update `CopilotSdkService.cs` event handling
- [ ] Implement `SessionIdleEvent` iteration trigger
- [ ] Add completion signal parsing to message handler
- [ ] Add context clearing support
- [ ] Integrate hooks for tool approval during iterations
- [ ] Add session persistence/resume support

### 7.5 Phase 5: ViewModels (Week 4)

- [ ] Create `AgentModeSelectionViewModel.cs`
- [ ] Create `PhasedWorkflowViewModel.cs`
- [ ] Extend `IterativeTaskViewModel.cs` for new features
- [ ] Update `ChatViewModel.cs` to coordinate modes
- [ ] Add review submission commands
- [ ] Add evidence viewing commands

### 7.6 Phase 6: Views & UI (Week 5)

- [ ] Create `AgentModePanel.xaml` for mode selection
- [ ] Create `PhasedWorkflowView.xaml` for phase progress
- [ ] Create `ReviewDialog.xaml` for review decisions
- [ ] Create `EvidenceViewer.xaml` for evidence display
- [ ] Update `IterativeTaskView.xaml` for new iteration display
- [ ] Add phase indicator component
- [ ] Add iteration progress component
- [ ] Style approve/reject buttons

### 7.7 Phase 7: Settings Integration (Week 5)

- [ ] Add mode settings to `AppSettings.cs`
- [ ] Update `SettingsDialog.xaml` with mode defaults
- [ ] Add default max iterations setting
- [ ] Add default evidence requirement setting
- [ ] Add auto-approve mode setting

### 7.8 Phase 8: Testing & Documentation (Week 6)

- [ ] Unit tests for `CompletionSignalDetector`
- [ ] Unit tests for `AgentExecutionOrchestrator`
- [ ] Unit tests for `PhasedWorkflowService`
- [ ] Integration tests for full iteration cycle
- [ ] Integration tests for full workflow cycle
- [ ] Update README with feature documentation
- [ ] Add user guide for each mode
- [ ] Update RELEASE_NOTES.md

### 7.9 Quality Checklist

- [ ] **Correctness**: All state transitions verified
- [ ] **Safety**: No race conditions in concurrent access
- [ ] **Cancellation**: Clean shutdown on cancel
- [ ] **Recovery**: State restoration after crash
- [ ] **Performance**: No memory leaks in long iterations
- [ ] **Accessibility**: Keyboard navigation for review dialogs
- [ ] **Localization**: All strings externalized

---

## Appendix A: Comparison with copilot-ui

| Aspect | copilot-ui | CopilotDesktop (Planned) |
|--------|------------|--------------------------|
| Language | TypeScript/React | C#/WPF |
| Architecture | Single 13K+ line file | MVVM with separated services |
| State Machine | JavaScript switch | C# pattern matching |
| Persistence | Direct file I/O | Abstracted interface |
| Threading | Single-threaded (Node) | Multi-threaded (Task/async) |
| Testing | Manual | Unit tests with mocks |
| Cancellation | Basic | CancellationToken throughout |

**Improvements in CopilotDesktop:**
1. Type-safe state machine with enums
2. Testable service abstractions
3. Proper async/await with cancellation
4. Clean MVVM separation
5. Enterprise-grade logging

---

## Appendix B: Prompt Templates

### B.1 Iterative Refinement Prompts

```csharp
public static class IterativePromptTemplates
{
    public const string FirstIteration = """
        Task: {0}
        
        Success Criteria: {1}
        
        This is an iterative task (iteration 1 of {2}).
        
        Instructions:
        1. Analyze the task and create a plan
        2. Execute the first meaningful step toward completion
        3. After your work, evaluate if success criteria are met
        4. If fully complete: emit {3}
        5. If not complete: describe remaining work
        
        Begin now.
        """;
    
    public const string ContinuationIteration = """
        Task: {0}
        Success Criteria: {1}
        
        Iteration: {2} of {3}
        Previous work: {4}
        
        Continue making progress toward the success criteria.
        
        If criteria are now fully met: emit {5}
        If not yet complete: continue working and describe progress.
        """;
}
```

### B.2 Phased Workflow Prompts

```csharp
public static class PhasedPromptTemplates
{
    public const string PlanPhase = """
        Task: {0}
        
        Phase: PLANNING
        
        Create a detailed implementation plan including:
        1. Analysis of current state
        2. Step-by-step implementation approach
        3. Files to create or modify
        4. Potential risks and mitigations
        5. Testing strategy
        
        When your plan is complete, emit: {1}
        """;
    
    public const string ExecutePhase = """
        Task: {0}
        
        Phase: EXECUTION
        
        Approved Plan:
        {1}
        
        Implement the approved plan. Follow each step carefully.
        Create files, modify code, and make all necessary changes.
        
        When implementation is complete, emit: {2}
        """;
    
    public const string ValidatePhase = """
        Task: {0}
        
        Phase: VALIDATION
        
        Implementation Summary:
        {1}
        
        Generate evidence to validate the implementation:
        1. Run all tests and capture results
        2. Take screenshots of key functionality
        3. Create a summary report
        
        Store evidence in: {2}
        
        When validation is complete, emit: {3}
        """;
}
```

---

## Document Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-02-07 | CopilotDesktop Team | Initial specification |

---

*End of Specification Document*