# Configuration

## Storage Locations

| Path | Contents |
|------|----------|
| `%APPDATA%\CopilotAgent\settings.json` | Global application settings (includes Agent Team and Office defaults) |
| `%APPDATA%\CopilotAgent\tool-approval-rules.json` | Saved tool approval rules (session-level and global) |
| `%APPDATA%\CopilotAgent\sessions\*.json` | Individual session data (chat history, context, settings) |
| `~/.copilot/mcp-config.json` | MCP server configuration (Copilot CLI standard location) |

---

## Application Settings (`settings.json`)

The settings file is created automatically on first launch. You can edit it via the **Settings** dialog in the app or directly in JSON.

### Key Settings

| Setting | Description | Default |
|---------|-------------|---------|
| `DefaultModel` | Default LLM model for chat sessions | (Copilot default) |
| `DefaultWorkingDirectory` | Working directory for new sessions | (none) |
| `StreamingTimeoutSeconds` | Timeout for streaming responses | 120 |

### Agent Team Settings

| Setting | Description | Default |
|---------|-------------|---------|
| `MultiAgent.MaxParallelWorkers` | Maximum concurrent worker agents | 3 |
| `MultiAgent.WorkspaceStrategy` | Isolation strategy: `GitWorktree`, `FileLocking`, or `InMemory` | `FileLocking` |
| `MultiAgent.OrchestratorModel` | LLM model for the orchestrator agent | (default) |
| `MultiAgent.WorkerModel` | LLM model for worker agents | (default) |
| `MultiAgent.WorkingDirectory` | Working directory for orchestration | (session directory) |

### Agent Office Settings

| Setting | Description | Default |
|---------|-------------|---------|
| `Office.CheckIntervalSeconds` | Seconds between iteration cycles | 300 |
| `Office.AssistantPoolSize` | Maximum concurrent assistants | 3 |
| `Office.ManagerModel` | LLM model for the Manager agent | (default) |
| `Office.AssistantModel` | LLM model for Assistant agents | (default) |
| `Office.MaxIterations` | Maximum iterations (0 = unlimited) | 0 |

---

## Tool Approval Rules (`tool-approval-rules.json`)

Controls which tools are auto-approved, denied, or require user confirmation.

### Rule Structure

```json
{
  "rules": [
    {
      "toolName": "readFile",
      "action": "Allow",
      "scope": "Global"
    },
    {
      "toolName": "executeCommand",
      "action": "Prompt",
      "scope": "Session",
      "sessionId": "abc-123"
    }
  ]
}
```

### Actions

| Action | Behavior |
|--------|----------|
| `Allow` | Auto-approve without prompting |
| `Deny` | Automatically deny |
| `Prompt` | Show approval dialog to user |

### Scopes

| Scope | Behavior |
|-------|----------|
| `Global` | Applies to all sessions |
| `Session` | Applies only to the specific session |

### Built-in Auto-Approved Tools

Read-only operations and internal SDK tools are auto-approved by default (e.g., `readFile`, `listDirectory`, `getFileInfo`).

---

## MCP Server Configuration (`mcp-config.json`)

MCP (Model Context Protocol) servers extend agent capabilities with custom tools. Configuration follows the Copilot CLI standard.

**Location:** `~/.copilot/mcp-config.json`

### Format

```json
{
  "mcpServers": {
    "server-name": {
      "command": "node",
      "args": ["path/to/server.js"],
      "env": {
        "API_KEY": "your-key"
      }
    },
    "another-server": {
      "command": "python",
      "args": ["-m", "my_mcp_server"],
      "env": {}
    }
  }
}
```

### Fields

| Field | Required | Description |
|-------|----------|-------------|
| `command` | Yes | Executable to launch the MCP server |
| `args` | No | Command-line arguments |
| `env` | No | Environment variables passed to the server process |

### Managing MCP Servers in the App

- Navigate to the **MCP Servers** tab to view live servers from the active SDK session
- See server status (running, error, unknown)
- Browse available tools and their parameters
- Toggle servers on/off for the current session

---

## Session Data (`sessions/*.json`)

Each session is persisted as a separate JSON file containing:

- **Chat history** — All messages (user, assistant, tool, system)
- **Session settings** — Model, working directory, custom instructions
- **Tool call log** — Record of tool invocations and results
- **Metadata** — Creation date, last modified, session name

Sessions are saved automatically and can be resumed across app restarts.

---

## Skills Configuration

Skills (custom agent capabilities defined in `SKILL.md` files) are loaded from:

| Location | Scope |
|----------|-------|
| `%USERPROFILE%\CopilotAgent\Skills\` | Personal — available in all sessions |
| `<working-directory>\SKILL.md` | Repository-specific — available when session uses that directory |

See [Skills documentation](#) for authoring SKILL.md files.

---

## Environment Variables

The following environment variables are recognized for authentication:

| Variable | Purpose |
|----------|---------|
| `COPILOT_GITHUB_TOKEN` | GitHub token for Copilot SDK authentication |
| `GH_TOKEN` | GitHub CLI token (fallback) |
| `GITHUB_TOKEN` | Generic GitHub token (fallback) |

For BYOK (Bring Your Own Key) setup with custom LLM providers, see the [BYOK documentation](https://github.com/github/copilot-sdk/blob/main/docs/auth/byok.md).