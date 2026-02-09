# Project Structure

## Solution Layout

```
CopilotAgent.sln
├── src/
│   ├── CopilotAgent.App/             # WPF application with MVVM
│   ├── CopilotAgent.Core/            # Core services, models, and shared interfaces
│   ├── CopilotAgent.MultiAgent/      # Agent Team orchestration engine
│   ├── CopilotAgent.Office/          # Agent Office manager loop engine
│   └── CopilotAgent.Persistence/     # JSON file storage
└── tests/
    └── CopilotAgent.Tests/           # Unit tests (xUnit)
```

## Project Dependencies

```
CopilotAgent.App
├── CopilotAgent.Core
├── CopilotAgent.MultiAgent
├── CopilotAgent.Office
└── CopilotAgent.Persistence

CopilotAgent.MultiAgent ──► CopilotAgent.Core
CopilotAgent.Office ──► CopilotAgent.Core
CopilotAgent.Persistence ──► CopilotAgent.Core
```

---

## Core Models

| Model | Description |
|-------|-------------|
| **Session** | Agent session with history, settings, and context |
| **ChatMessage** | Message in conversation (User, Assistant, Tool, System) |
| **ToolCall / ToolResult** | Tool invocation tracking |
| **ToolApprovalRequest / Response** | Tool approval gating models |
| **McpServerConfig** | MCP server configuration |
| **SkillDefinition** | Skill/plugin definition |
| **IterativeTaskConfig** | Iterative agent mode configuration |
| **MultiAgentSettings** | Agent Team orchestration settings (parallel workers, strategy, models, working directory) |
| **OfficeSettings** | Agent Office settings (interval, pool size, models, timeouts) |

---

## Core Services

| Service | Responsibility |
|---------|---------------|
| **ICopilotService** | Wrapper around GitHub Copilot SDK with tool approval hooks |
| **IToolApprovalService** | Tool approval logic and rule management |
| **ISessionManager** | Session lifecycle management |
| **IMcpService** | MCP server configuration and live session querying |
| **ISkillsService** | Skills loading and management |
| **IPersistenceService** | Session and settings persistence |

---

## Multi-Agent Services (Agent Team)

| Service | Responsibility |
|---------|---------------|
| **IOrchestratorService** | Task submission, planning, approval, execution, and follow-up |
| **ITaskDecomposer** | LLM-driven task decomposition with JSON schema validation |
| **IDependencyScheduler** | DAG-based topological sort for parallel stage scheduling |
| **IAgentPool** | Concurrency-limited worker dispatch with retry |
| **IWorkerAgent** | Ephemeral Copilot session per work chunk |
| **IAgentRoleProvider** | Role-specialized session creation (CodeAnalysis, Synthesis, etc.) |
| **IWorkspaceStrategy** | Git Worktree / File Locking / In-Memory workspace isolation |
| **IResultAggregator** | LLM-driven consolidation of worker results |
| **IApprovalQueue** | Centralized tool approval queue for multi-worker scenarios |

For detailed design, see [Agent Team Orchestrator Design](MULTI_AGENT_ORCHESTRATOR_DESIGN.md).

---

## Office Services (Agent Office)

| Service | Responsibility |
|---------|---------------|
| **IOfficeManagerService** | Manager state machine: start → clarify → plan → approve → loop (fetch → schedule → execute → aggregate → rest) |
| **IAssistantPool** | Finite pool with queue-based overflow for ephemeral assistants |
| **IAssistantAgent** | Ephemeral Copilot session per task (spawn → work → report → dispose) |
| **IIterationScheduler** | Rest period countdown with dynamic interval changes |
| **IOfficeEventLog** | Structured event log for scheduling decisions and lifecycle events |

For detailed design, see [Agent Office Design](AGENT_OFFICE_DESIGN.md).

---

## Application Layer (`CopilotAgent.App`)

### ViewModels (MVVM)

| ViewModel | View |
|-----------|------|
| `ChatViewModel` | Agent Chat tab |
| `AgentTeamViewModel` | Agent Team tab |
| `OfficeViewModel` | Agent Office tab |
| `IterativeTaskViewModel` | Iterative Agent tab |
| `McpConfigViewModel` | MCP Servers tab |
| `SkillsViewModel` | Skills tab |
| `SettingsDialogViewModel` | Settings dialog |
| `ToolApprovalDialogViewModel` | Tool approval prompts |
| `SessionInfoViewModel` | Session info panel |
| `TerminalViewModel` | Embedded terminal |

### Key Converters

| Converter | Purpose |
|-----------|---------|
| `BoolToVisibilityConverter` | Standard bool → Visibility |
| `InverseBoolToVisibilityConverter` | Inverted bool → Visibility |
| `StringToVisibilityConverter` | Non-empty string → Visible |
| `UtcToLocalTimeConverter` | UTC timestamps → local display |
| `StringToBrushConverter` | Color string → WPF Brush |

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