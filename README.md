# Copilot Agent Desktop

A production-grade Windows desktop application providing a Claude-like agent UI over the [GitHub Copilot SDK](https://github.com/github/copilot-sdk) for .NET.

[![GitHub Copilot SDK](https://img.shields.io/nuget/v/GitHub.Copilot.SDK?label=GitHub.Copilot.SDK)](https://www.nuget.org/packages/GitHub.Copilot.SDK)

## Features

- 🎯 **Multi-Session Management** - Independent Copilot agent sessions with separate contexts
- 🌳 **Git Worktree Sessions** - Create sessions from GitHub issues with automatic worktree setup
- 💻 **Embedded Terminal** - Full PTY terminal with output capture using Pty.Net
- 🔒 **Tool Approval System** - Fine-grained approval dialogs for tool execution with session/global rules
- 🔌 **MCP Server Support** - Model Context Protocol integration with live session view
- 📚 **Skills/Plugins** - SKILL.md support for custom agent capabilities
- 🔄 **Iterative Agent Mode** - Self-evaluating task runner with success criteria
- 🎨 **Modern UI** - Fluent Design with WPF-UI (Windows 11 style)
- 💾 **Session Persistence** - Full chat history and settings persistence
- 📦 **Single Executable** - Self-contained deployment

<img width="1184" height="790" alt="image" src="https://github.com/user-attachments/assets/bdbb3457-97d7-44ea-a9bc-dda50904650f" />

## How to Use

### Quick Start (5 Steps)

1. **Complete Prerequisites** - Ensure you have the [Copilot CLI installed](#installing-the-copilot-cli)

2. **Download Latest Release** - [⬇️ Download CopilotAgent.exe](https://github.com/rashmirrout/CopilotDesktop/releases/download/Beta_0/CopilotAgent.exe)

3. **Authenticate with Copilot** - Run this in terminal:
   ```bash
   copilot login
   ```

4. **Start the Application** - Double-click `CopilotAgent.exe`

5. **Explore Possibilities** - Create sessions, chat with Copilot, configure MCP servers, and more!

## Architecture

```
CopilotAgent.sln
├── src/
│   ├── CopilotAgent.App/          # WPF application with MVVM
│   ├── CopilotAgent.Core/         # Core services and models
│   └── CopilotAgent.Persistence/  # JSON file storage
└── tests/
    └── CopilotAgent.Tests/        # Unit tests (xUnit)
```

## Prerequisites

### Required

- **.NET 8.0 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Windows 10/11** - WPF desktop application
- **GitHub Copilot CLI** - Required for SDK communication
- **GitHub Copilot Subscription** - Required for API access (free tier available)

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
- **GitHub signed-in user** - Uses stored OAuth credentials from `copilot` CLI login
- **OAuth GitHub App** - Pass user tokens from your GitHub OAuth app
- **Environment variables** - `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_TOKEN`
- **BYOK (Bring Your Own Key)** - Use your own API keys from supported LLM providers (OpenAI, Azure AI, Anthropic)

For BYOK setup, see the [BYOK documentation](https://github.com/github/copilot-sdk/blob/main/docs/auth/byok.md).

### Optional

- **Visual Studio 2022** or **VS Code** - For development
- **Git** - For worktree session support

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

## Publishing

### Self-Contained Single Executable (Recommended)

Creates a portable executable that includes the .NET runtime - no installation required on target machine.

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

## Project Structure

### Core Models

- **Session** - Agent session with history, settings, and context
- **ChatMessage** - Message in conversation (User, Assistant, Tool, System)
- **ToolCall/ToolResult** - Tool invocation tracking
- **ToolApprovalRequest/Response** - Tool approval gating models
- **McpServerConfig** - MCP server configuration
- **SkillDefinition** - Skill/plugin definition
- **IterativeTaskConfig** - Iterative agent mode configuration

### Core Services

- **ICopilotService** - Wrapper around GitHub Copilot SDK with tool approval hooks
- **IToolApprovalService** - Tool approval logic and rule management
- **ISessionManager** - Session lifecycle management
- **IMcpService** - MCP server configuration and live session querying
- **ISkillsService** - Skills loading and management
- **IPersistenceService** - Session and settings persistence

## GitHub Copilot SDK Integration

This application uses the **[GitHub Copilot SDK for .NET](https://github.com/github/copilot-sdk)**.

```xml
<PackageReference Include="GitHub.Copilot.SDK" Version="*" />
```

### Key Integration Points

1. **CopilotSdkService** - Main SDK integration with:
   - `OnPreToolUse` hooks for tool approval gating
   - `OnPostToolUse` hooks for result processing
   - Session lifecycle management (create, resume, abort)
   - MCP server configuration pass-through

2. **Tool Approval System** - Every tool invocation goes through:
   - Auto-approved list (read operations, internal SDK tools)
   - Session-level rules
   - Global rules
   - User prompt for unknown tools

3. **MCP Servers** - Configure in `~/.copilot/mcp-config.json`:
   ```json
   {
     "mcpServers": {
       "weather-server": {
         "command": "node",
         "args": ["weather-mcp-server.js"]
       }
     }
   }
   ```

## Configuration

Settings are stored in: `%APPDATA%\CopilotAgent\`

- `settings.json` - Global application settings
- `tool-approval-rules.json` - Tool approval rules
- `sessions\*.json` - Individual session data

MCP configuration uses the Copilot CLI standard location: `~/.copilot/mcp-config.json`

## Usage

### Creating a Session

1. **New Session** - Blank session with optional working directory
2. **From Repository** - Session from existing local repository
3. **From GitHub Issue** - Creates worktree session from issue URL

### Tool Approval

When a tool is invoked, you can:
- **Approve Once** - Allow this specific invocation
- **Approve for Session** - Remember for current session
- **Approve Globally** - Remember across all sessions
- **Deny** - Block the tool execution

Manage saved rules in Settings → Manage Tool Approvals.

### MCP Servers Tab

- View **live MCP servers** from the active SDK session
- See server status (running, error, unknown)
- Browse available tools and their parameters
- Toggle servers on/off for session

### Skills (SKILL.md)

Place skill definitions in:
- `%USERPROFILE%\CopilotAgent\Skills\` (personal)
- `<working-directory>\SKILL.md` (repo-specific)

### Iterative Agent Mode

1. Define task description and success criteria
2. Set max iterations
3. Agent executes → evaluates → repeats until success or max iterations

## Technology Stack

| Component | Technology |
|-----------|------------|
| UI Framework | WPF + .NET 8 |
| Modern UI | WPF-UI (Fluent Design) |
| MVVM | CommunityToolkit.Mvvm |
| DI Container | Microsoft.Extensions.DependencyInjection |
| Copilot SDK | GitHub.Copilot.SDK |
| Terminal | Pty.Net |
| Markdown | Markdig.Wpf |
| Code Highlighting | AvalonEdit |
| Logging | Serilog + Microsoft.Extensions.Logging |
| Testing | xUnit + Moq + FluentAssertions |

## FAQ

### Do I need a GitHub Copilot subscription?

Yes, unless using BYOK (Bring Your Own Key). With BYOK, you can use your own API keys from supported LLM providers. See [GitHub Copilot pricing](https://github.com/features/copilot#pricing) for free tier details.

### How does billing work?

Based on the same model as Copilot CLI, with each prompt counted towards your premium request quota. See [Requests in GitHub Copilot](https://docs.github.com/en/copilot/concepts/billing/copilot-requests).

### What tools are enabled by default?

All first-party Copilot tools are enabled, including file system operations, Git operations, and web requests. The app provides a tool approval layer to control execution.

### What models are supported?

All models available via Copilot CLI are supported. The SDK exposes a method to list available models at runtime.

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

- **[GitHub Copilot SDK](https://github.com/github/copilot-sdk)** - Official SDK repository
- **[Copilot SDK .NET Cookbook](https://github.com/github/awesome-copilot/blob/main/cookbook/copilot-sdk/dotnet/README.md)** - .NET examples and recipes
- **[Copilot CLI Installation](https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli)** - CLI setup guide
- **[Model Context Protocol (MCP)](https://modelcontextprotocol.io/)** - MCP specification
- **[awesome-copilot](https://github.com/github/awesome-copilot)** - Additional resources and examples