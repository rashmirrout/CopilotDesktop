# GitHub Copilot SDK Integration

This application is built on the **[GitHub Copilot SDK for .NET](https://github.com/github/copilot-sdk)** — the official SDK for building agent experiences powered by GitHub Copilot.

```xml
<PackageReference Include="GitHub.Copilot.SDK" Version="*" />
```

---

## Architecture Overview

```
User Interface (WPF)
       ↓
  CopilotSdkService (ICopilotService)
       ↓
  GitHub.Copilot.SDK Client
       ↓ JSON-RPC
  Copilot CLI (server mode)
       ↓
  GitHub Copilot API
```

---

## Key Integration Points

### 1. CopilotSdkService

The primary integration layer (`CopilotAgent.Core/Services/CopilotSdkService.cs`) wraps the SDK client and provides:

| Capability | Description |
|------------|-------------|
| **Session Lifecycle** | Create, resume, and abort agent sessions with isolated contexts |
| **OnPreToolUse Hooks** | Intercept tool invocations *before* execution for approval gating |
| **OnPostToolUse Hooks** | Process tool results *after* execution for logging and UI updates |
| **Streaming Responses** | Real-time token streaming to the UI with timeout management |
| **MCP Pass-through** | Forward MCP server configuration to the SDK for tool discovery |
| **Model Selection** | Runtime model switching per session (including BYOK providers) |

### 2. Multi-Agent SDK Usage

Both **Agent Team** and **Agent Office** create multiple concurrent SDK sessions:

- **Agent Team**: The orchestrator and each worker agent get their own `ICopilotService` session. Workers are ephemeral — created per work chunk and disposed after completion.
- **Agent Office**: The Manager holds a long-lived session. Assistants are ephemeral — spawned per task from the assistant pool, then disposed.

Each session maintains its own conversation history and tool context, enabling true parallel agent execution.

### 3. Model Configuration

The SDK supports all models available via Copilot CLI. Different components can use different models:

| Component | Setting | Use Case |
|-----------|---------|----------|
| Chat sessions | Per-session model selection | General coding tasks |
| Team Orchestrator | `MultiAgent.OrchestratorModel` | Task decomposition and planning |
| Team Workers | `MultiAgent.WorkerModel` | Focused execution tasks |
| Office Manager | `Office.ManagerModel` | Event analysis and delegation |
| Office Assistants | `Office.AssistantModel` | Individual task execution |

---

## Tool Approval System

Every tool invocation from the SDK passes through a multi-layer approval pipeline before execution.

### Approval Flow

```
Tool Invocation (from SDK)
       ↓
  ┌─ Built-in Auto-Approved? ──► YES ──► Execute
  │         NO
  │         ↓
  ├─ Session Rule Exists? ──► YES ──► Apply Rule (Allow/Deny)
  │         NO
  │         ↓
  ├─ Global Rule Exists? ──► YES ──► Apply Rule (Allow/Deny)
  │         NO
  │         ↓
  └─ Prompt User ──► Approve Once / Approve Session / Approve Global / Deny
```

### Built-in Auto-Approved Tools

Read-only and internal SDK operations are auto-approved by default:

- File reading operations (`readFile`, `listDirectory`, `getFileInfo`)
- Internal SDK tools (model listing, session management)
- Non-destructive query operations

### User Approval Options

When prompted, users can:

| Option | Behavior |
|--------|----------|
| **Approve Once** | Allow this specific invocation only |
| **Approve for Session** | Remember for the current session |
| **Approve Globally** | Remember across all sessions |
| **Deny** | Block the tool execution |

### Multi-Agent Approval

In Agent Team and Agent Office scenarios, tool approvals from concurrent workers are serialized through a centralized `IApprovalQueue` to prevent overlapping approval dialogs.

### Managing Rules

- **In-app**: Settings → Manage Tool Approvals
- **File**: `%APPDATA%\CopilotAgent\tool-approval-rules.json`

---

## MCP Server Integration

[Model Context Protocol (MCP)](https://modelcontextprotocol.io/) servers extend agent capabilities with custom tools and data sources.

### How It Works

1. MCP servers are configured in `~/.copilot/mcp-config.json`
2. The SDK discovers and launches configured servers on session creation
3. Tools provided by MCP servers become available to the agent alongside built-in tools
4. Tool invocations to MCP servers pass through the same approval pipeline

### Configuration

```json
{
  "mcpServers": {
    "weather-server": {
      "command": "node",
      "args": ["weather-mcp-server.js"]
    },
    "database-tools": {
      "command": "python",
      "args": ["-m", "db_mcp_server"],
      "env": {
        "DB_CONNECTION": "postgresql://localhost/mydb"
      }
    }
  }
}
```

### In-App MCP Management

The **MCP Servers** tab provides:

- **Live server status** — Running, error, or unknown state per server
- **Tool browser** — View all tools exposed by each server with parameter schemas
- **Toggle control** — Enable/disable servers for the current session
- **Diagnostic info** — Server process details and error messages

---

## Authentication Methods

The SDK supports multiple authentication strategies:

| Method | Description |
|--------|-------------|
| **GitHub signed-in user** | Uses stored OAuth credentials from `copilot login` |
| **OAuth GitHub App** | Pass user tokens from your GitHub OAuth app |
| **Environment variables** | `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_TOKEN` |
| **BYOK** | Bring Your Own Key — use API keys from OpenAI, Azure AI, Anthropic |

For BYOK setup, see the [BYOK documentation](https://github.com/github/copilot-sdk/blob/main/docs/auth/byok.md).

---

## References

- **[GitHub Copilot SDK](https://github.com/github/copilot-sdk)** — Official SDK repository
- **[Copilot SDK .NET Cookbook](https://github.com/github/awesome-copilot/blob/main/cookbook/copilot-sdk/dotnet/README.md)** — .NET examples and recipes
- **[Model Context Protocol](https://modelcontextprotocol.io/)** — MCP specification
- **[awesome-copilot](https://github.com/github/awesome-copilot)** — Additional resources and examples