# Release Notes

## v0.1.0 - Initial Release

**Release Date:** February 4, 2026

A production-grade Windows desktop application providing a Claude-like agent UI over the GitHub Copilot CLI.

---

### 🎯 New Features

#### Session Persistence Across Restarts
- **Conversation context is now preserved** when you close and reopen the application
- Uses Copilot CLI's native `--continue` and `--resume` flags for seamless session management
- Session IDs are captured and stored automatically for reliable reconnection
- See [Session Persistence Architecture](docs/SESSION_PERSISTENCE.md) for technical details

#### Copy to Clipboard
- **One-click copy** for assistant responses in the chat window
- Industry-standard copy icon (📋) in the bottom-right corner of each response
- **Visual feedback**: Icon changes to green checkmark (✓) for 1.5 seconds after copying
- Copies raw markdown content to preserve formatting

#### Self-Contained Executable
- **Portable deployment** - single `CopilotAgent.exe` file (~70 MB)
- No .NET runtime installation required on target machines
- Works on any Windows 10/11 x64 system

---

### 🐛 Bug Fixes

- **Fixed: Conversation context loss** - Messages no longer lose context between turns
  - Root cause: Each message was starting a new Copilot session
  - Solution: Implemented `--continue` flag for subsequent messages in same session

---

### 📚 Documentation

- **New:** [Session Persistence Architecture](docs/SESSION_PERSISTENCE.md) - Technical documentation covering:
  - Two-layer session management (App sessions + Copilot CLI sessions)
  - Session ID capture algorithm
  - Command decision matrix
  - Sequence diagrams

- **Updated:** [README.md](README.md) - Enhanced with:
  - Quick start guide
  - Development build instructions
  - Self-contained publishing options
  - Framework-dependent publishing
  - Publish options reference table

---

### 📦 Downloads

| File | Size | Description |
|------|------|-------------|
| `CopilotAgent.exe` | ~70 MB | Self-contained Windows x64 executable |
| `*.pdb` | ~0.2 MB | Debug symbols (optional) |

**Download location:** `publish/win-x64/`

---

### ⚙️ Requirements

**To run the published executable:**
- Windows 10/11 (x64)
- GitHub CLI (`gh`) installed and authenticated with Copilot access

**To build from source:**
- .NET 8.0 SDK
- Windows 10/11
- Visual Studio 2022 or VS Code (optional)

---

### 🔨 Technical Highlights

#### Session Management
```
First Message → gh copilot suggest -p "prompt" --stream on
             → Capture session ID from ~/.copilot/session-state/

Same Session → gh copilot suggest -p "prompt" --continue --stream on

After Restart → gh copilot suggest -p "prompt" --resume {sessionId} --stream on
```

#### Files Changed
- `src/CopilotAgent.Core/Models/Session.cs` - Added `CopilotSessionId` property
- `src/CopilotAgent.Core/Services/CopilotService.cs` - Session continuation logic
- `src/CopilotAgent.App/Views/ChatView.xaml` - Copy button UI
- `src/CopilotAgent.App/Views/ChatView.xaml.cs` - Copy button handler with visual feedback
- `src/CopilotAgent.App/ViewModels/ChatViewModel.cs` - Copy command

---

### 🚀 Quick Start

```bash
# Option 1: Run published executable
./publish/win-x64/CopilotAgent.exe

# Option 2: Build and run from source
dotnet restore
dotnet build
dotnet run --project src/CopilotAgent.App

# Option 3: Publish your own
dotnet publish src/CopilotAgent.App/CopilotAgent.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

---

### 🔮 What's Next

- [ ] Full GitHub Copilot SDK integration (when available)
- [ ] Iterative agent mode improvements
- [ ] MCP server integration enhancements
- [ ] Additional platform support considerations

---

### 🙏 Acknowledgments

Built with:
- .NET 8.0 & WPF
- CommunityToolkit.Mvvm
- MdXaml (Markdown rendering)
- Pty.Net (Terminal emulation)

---

**Full Changelog:** [View all commits](../../commits/main)