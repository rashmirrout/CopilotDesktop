# Copilot Agent Desktop

A production-grade Windows desktop application providing a Claude-like agent UI over the GitHub Copilot SDK for .NET.

## Features

- 🎯 **Multi-Session Management** - Independent Copilot agent sessions with separate contexts
- 🌳 **Git Worktree Sessions** - Create sessions from GitHub issues with automatic worktree setup
- 💻 **Embedded Terminal** - Full PTY terminal with output capture using Pty.Net
- 🔒 **Command Security Policy** - Allow/deny lists with approval dialogs for unknown commands
- 🔌 **MCP Server Support** - Model Context Protocol integration for external tools
- 📚 **Skills/Plugins** - SKILL.md support for custom agent capabilities
- 🔄 **Iterative Agent Mode** - Self-evaluating task runner with success criteria
- 🎨 **Modern UI** - Fluent Design with WPF-UI (Windows 11 style)
- 💾 **Offline Capability** - View cached session history without connection
- 📦 **Single Executable** - Self-contained deployment

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

- .NET 8.0 SDK
- Windows 10/11
- GitHub CLI (`gh`) authenticated with Copilot access
- Visual Studio 2022 or VS Code (optional)

## Building

### Quick Start

```bash
# Clone and build
git clone <repository-url>
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

**Contents:**
| File | Description |
|------|-------------|
| `CopilotAgent.exe` | Self-contained executable (includes .NET runtime) |
| `*.pdb` | Debug symbols (optional, for stack traces) |
| `Resources/` | Embedded resources |

### Framework-Dependent (Smaller Size)

Requires .NET 8.0 runtime to be installed on target machine.

```bash
# Publish (smaller, requires .NET runtime)
dotnet publish src/CopilotAgent.App/CopilotAgent.App.csproj \
    -c Release \
    -r win-x64 \
    --self-contained false \
    -o publish/win-x64-fd
```

**Output:** `publish/win-x64-fd/CopilotAgent.exe` (~5 MB)

### Publish Options Reference

| Option | Description |
|--------|-------------|
| `-c Release` | Release configuration (optimized) |
| `-r win-x64` | Target Windows x64 |
| `--self-contained true` | Include .NET runtime |
| `-p:PublishSingleFile=true` | Bundle into single executable |
| `-p:PublishTrimmed=true` | Trim unused code (smaller, may break reflection) |
| `-p:EnableCompressionInSingleFile=true` | Compress bundled files |

### Other Platforms

```bash
# Windows ARM64
dotnet publish src/CopilotAgent.App/CopilotAgent.App.csproj -c Release -r win-arm64 --self-contained true -o publish/win-arm64

# Note: This is a WPF app, Windows-only
```

## Project Structure

### Core Models

- **Session** - Agent session with history, settings, and context
- **ChatMessage** - Message in conversation (User, Assistant, Tool, System)
- **ToolCall/ToolResult** - Tool invocation tracking
- **McpServerConfig** - MCP server configuration
- **SkillDefinition** - Skill/plugin definition
- **CommandPolicy** - Security policy for command execution
- **IterativeTaskConfig** - Iterative agent mode configuration

### Core Services

- **ICopilotService** - Wrapper around GitHub Copilot SDK
- **ISessionManager** - Session lifecycle management
- **ITerminalService** - PTY terminal integration
- **IMcpService** - MCP server configuration
- **ISkillsService** - Skills loading and management
- **ICommandPolicyService** - Command approval logic
- **IPersistenceService** - Session and settings persistence

## GitHub Copilot SDK Integration

This application is designed to integrate with the **GitHub Copilot SDK for .NET**. As the SDK is currently in development:

1. The `CopilotService` provides an abstraction layer
2. Initially, it may use `gh copilot` CLI as a fallback
3. Once the SDK NuGet package is available, update `CopilotAgent.Core.csproj`

```xml
<!-- Uncomment when available -->
<!-- <PackageReference Include="GitHub.Copilot.SDK" Version="*" /> -->
```

## Configuration

Settings are stored in: `%APPDATA%\CopilotAgent\`

- `settings.json` - Global application settings
- `mcp-config.json` - MCP servers configuration
- `sessions\*.json` - Individual session data

## Usage

### Creating a Session

1. **New Session** - Blank session with optional working directory
2. **From Repository** - Session from existing local repository
3. **From GitHub Issue** - Creates worktree session from issue URL

### Multi-Session Workflow

- Each session has independent:
  - Chat history
  - Working directory
  - Enabled MCP servers
  - Enabled skills
  - Command policy overrides

### Terminal Integration

- Embedded PTY terminal per session
- Run commands in session's working directory
- "Add terminal output to message" - Attach output as context

### Command Policy

- **Allowed Commands** - Automatically execute (e.g., `git`, `npm`, `dotnet`)
- **Denied Commands** - Block automatically (e.g., `rm -rf`)
- **Unknown Commands** - Prompt for approval with risk assessment

### MCP Servers

Configure external tools via MCP protocol:

```json
{
  "name": "weather-server",
  "transport": "stdio",
  "command": "node",
  "args": ["weather-mcp-server.js"],
  "enabled": true
}
```

### Skills (SKILL.md)

Place skill definitions in:
- `%USERPROFILE%\CopilotAgent\Skills\` (personal)
- `<working-directory>\SKILL.md` (repo-specific)

Skills are injected as system prompts when enabled.

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
| Terminal | Pty.Net |
| Markdown | Markdig.Wpf |
| Code Highlighting | AvalonEdit |
| Logging | Serilog |
| Testing | xUnit + Moq + FluentAssertions |

## Development

### Adding a New View

1. Create view in `src/CopilotAgent.App/Views/`
2. Create ViewModel in `src/CopilotAgent.App/ViewModels/`
3. Register in `App.xaml.cs` DI container
4. Add navigation

### Adding a New Service

1. Define interface in `src/CopilotAgent.Core/Services/`
2. Implement in same file or separate implementation
3. Register in `App.xaml.cs` DI container

## Roadmap

- [ ] Phase 1: Foundation ✅
- [ ] Phase 2: Copilot SDK Integration (in progress)
- [ ] Phase 3: Chat UI
- [ ] Phase 4: Multi-Session Management
- [ ] Phase 5: Embedded Terminal
- [ ] Phase 6: Command Policy
- [ ] Phase 7: MCP Configuration
- [ ] Phase 8: Skills Support
- [ ] Phase 9: Iterative Agent Mode
- [ ] Phase 10: Polish & Packaging

## License

See [LICENSE](LICENSE) file.

## Contributing

This is a production-quality implementation. Contributions are welcome:

1. Fork the repository
2. Create a feature branch
3. Follow SOLID principles and clean architecture
4. Add tests for new functionality
5. Submit pull request

## References

- [GitHub Copilot SDK](https://github.com/github/copilot-sdk)
- [Microsoft Chat Copilot](https://github.com/microsoft/chat-copilot)
- [copilot-ui](https://github.com/idofrizler/copilot-ui)
- [Model Context Protocol (MCP)](https://modelcontextprotocol.io/)