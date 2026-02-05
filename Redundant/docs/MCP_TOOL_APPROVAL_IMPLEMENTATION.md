# MCP Tool Approval Implementation Plan

## Overview

This document outlines the implementation plan for MCP (Model Context Protocol) tool approval in CopilotDesktop. The goal is to provide a user-friendly tool approval experience similar to Claude Desktop and ChatGPT Desktop applications.

## Problem Statement

When using the Copilot CLI in non-interactive mode (`-p` flag), MCP tools fail with "permission denied" or "not in interactive mode" errors. This is because:

1. Non-interactive mode requires explicit tool permissions
2. Copilot CLI provides flags for tool management: `--allow-all-tools`, `--allow-tool`, `--deny-tool`
3. Without these flags, tools cannot execute in `-p` mode

## Quick Fix (Phase 0)

**Status**: ✅ COMPLETED

Implemented as a configurable autonomous mode with UI controls in the Session Info tab. Users can now toggle:
- **YOLO Mode** (`--allow-all`): Enables all permissions (tools, paths, URLs)
- **Allow All Tools** (`--allow-all-tools`): Just MCP tools
- **Allow All Paths** (`--allow-all-paths`): Just file system paths
- **Allow All URLs** (`--allow-all-urls`): Just web URLs

### Files to Modify
- `src/CopilotAgent.Core/Services/CopilotService.cs`

### Changes Required
Add `--allow-all-tools` to each argument string in `SendMessageStreamingAsync`:

```csharp
// Resume existing session
arguments = $"--resume {session.CopilotSessionId} --allow-all-tools -p \"{EscapeArgument(userMessage)}\" -s --stream on";

// Continue session (same app run)
arguments = $"--continue --allow-all-tools -p \"{EscapeArgument(userMessage)}\" -s --stream on";

// New session
arguments = $"--allow-all-tools -p \"{EscapeArgument(userMessage)}\" -s --stream on";

// Legacy mode
arguments = $"--allow-all-tools -p \"{EscapeArgument(userMessage)}\" -s --stream on";
```

---

## Production Implementation (5 Phases)

### Phase 1: Tool Permission Models & Storage

**Goal**: Define data structures for storing tool permissions

#### New Models

```csharp
// src/CopilotAgent.Core/Models/ToolPermission.cs
public enum ToolPermissionLevel
{
    Ask,           // Ask user each time (default)
    AllowOnce,     // Allowed for this single execution
    AllowSession,  // Allowed for current session only
    AllowAlways,   // Permanently allowed
    DenyAlways     // Permanently denied
}

public class ToolPermission
{
    public string ToolPattern { get; set; } = string.Empty;  // e.g., "filesystem(read_file)"
    public string ServerName { get; set; } = string.Empty;   // MCP server name
    public string? ToolName { get; set; }                    // Specific tool or null for all server tools
    public ToolPermissionLevel Level { get; set; } = ToolPermissionLevel.Ask;
    public DateTime? ExpiresAt { get; set; }                 // For session-scoped permissions
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
}
```

#### Storage Updates

- Add `ToolPermissions` collection to persistence
- Add `SessionToolPermissions` to `Session` model (for session-scoped permissions)

---

### Phase 2: Tool Permission Service

**Goal**: Create service for managing tool permissions

#### New Service Interface

```csharp
// src/CopilotAgent.Core/Services/IToolPermissionService.cs
public interface IToolPermissionService
{
    Task<ToolPermissionLevel> GetPermissionLevelAsync(string serverName, string? toolName);
    Task SetPermissionAsync(string serverName, string? toolName, ToolPermissionLevel level);
    Task<List<ToolPermission>> GetAllPermissionsAsync();
    Task ClearSessionPermissionsAsync(string sessionId);
    Task<bool> IsToolAllowedAsync(string serverName, string? toolName);
    Task<List<string>> GetAllowedToolPatternsAsync();
    Task<List<string>> GetDeniedToolPatternsAsync();
}
```

#### Implementation

- Load/save permissions via `IPersistenceService`
- Handle wildcard patterns: `server(*)` for all tools on a server
- Manage session-scoped vs permanent permissions
- Provide CLI argument generation: `--allow-tool`, `--deny-tool`

---

### Phase 3: Tool Approval Dialog UI

**Goal**: Create WPF dialog for tool approval requests

#### New Views

```
src/CopilotAgent.App/Views/ToolApprovalDialog.xaml
src/CopilotAgent.App/Views/ToolApprovalDialog.xaml.cs
src/CopilotAgent.App/ViewModels/ToolApprovalDialogViewModel.cs
```

#### Dialog Features

- Display tool name and MCP server
- Show tool description/purpose if available
- Four action buttons:
  - **Allow Once** - Execute this time only
  - **Allow for Session** - Allow for current session
  - **Allow Always** - Permanently allow
  - **Deny** - Reject this execution
- Optional checkbox: "Remember this choice"
- Display pending tool parameters (read-only)

#### Dialog Mockup

```
┌─────────────────────────────────────────────────────┐
│ Tool Permission Request                         [X] │
├─────────────────────────────────────────────────────┤
│                                                     │
│  🔧 filesystem(read_file)                          │
│                                                     │
│  Server: @anthropic/filesystem                     │
│  Tool: read_file                                   │
│                                                     │
│  Parameters:                                        │
│  ┌─────────────────────────────────────────────┐   │
│  │ path: "C:\Users\...\config.json"            │   │
│  └─────────────────────────────────────────────┘   │
│                                                     │
│  ☐ Remember this choice for all filesystem tools   │
│                                                     │
├─────────────────────────────────────────────────────┤
│ [Allow Once] [Allow Session] [Allow Always] [Deny] │
└─────────────────────────────────────────────────────┘
```

---

### Phase 4: CopilotService Integration

**Goal**: Integrate permission service with CLI argument building

#### Modifications to CopilotService

1. Inject `IToolPermissionService`
2. Before building arguments, query allowed/denied tools
3. Generate appropriate `--allow-tool` and `--deny-tool` flags
4. Handle "Ask" level tools by showing dialog

#### Argument Building Logic

```csharp
private async Task<string> BuildToolPermissionArgsAsync()
{
    var allowed = await _toolPermissionService.GetAllowedToolPatternsAsync();
    var denied = await _toolPermissionService.GetDeniedToolPatternsAsync();
    
    var args = new StringBuilder();
    foreach (var pattern in allowed)
    {
        args.Append($"--allow-tool \"{pattern}\" ");
    }
    foreach (var pattern in denied)
    {
        args.Append($"--deny-tool \"{pattern}\" ");
    }
    
    return args.ToString().Trim();
}
```

#### Tool Request Detection

- Parse Copilot CLI output for tool execution requests
- When tool requires permission, pause streaming
- Show `ToolApprovalDialog`
- Based on user choice, update permissions and re-execute or abort

---

### Phase 5: Tool Permissions Management UI

**Goal**: Add UI for viewing/managing all tool permissions

#### New Views

```
src/CopilotAgent.App/Views/ToolPermissionsView.xaml
src/CopilotAgent.App/Views/ToolPermissionsView.xaml.cs
src/CopilotAgent.App/ViewModels/ToolPermissionsViewModel.cs
```

#### Features

- List all configured tool permissions
- Group by MCP server
- Edit permission levels
- Delete permissions
- Bulk actions: "Allow all from server", "Deny all from server"
- Import/export permission configurations

#### Integration

- Add "Tool Permissions" tab to main window (alongside MCP Config, Skills)
- Quick access from session settings

---

## Copilot CLI Tool Permission Patterns

The Copilot CLI uses this pattern for tool permissions:

```
<mcp-server-name>(tool-name?)
```

Examples:
- `filesystem(read_file)` - Specific tool
- `filesystem(*)` or `filesystem` - All tools on server
- `@anthropic/filesystem(read_file)` - Full server path

---

## Implementation Priority

1. **Phase 0** (Immediate): Quick fix with `--allow-all-tools` ✅
2. **Phase 1** (Week 1): Models and storage
3. **Phase 2** (Week 1-2): Permission service
4. **Phase 3** (Week 2): Approval dialog
5. **Phase 4** (Week 2-3): CopilotService integration
6. **Phase 5** (Week 3-4): Management UI

---

## Testing Plan

### Phase 0 Testing
- [ ] Build succeeds after adding `--allow-all-tools`
- [ ] MCP tools execute without permission errors
- [ ] Existing session functionality unchanged

### Phase 1-5 Testing
- [ ] Permissions persist across app restarts
- [ ] Session permissions clear on session end
- [ ] Dialog appears for "Ask" level tools
- [ ] "Allow Always" tools work without dialog
- [ ] "Deny Always" tools are blocked automatically
- [ ] Permission patterns match correctly
- [ ] Wildcard patterns work as expected

---

## Security Considerations

1. **Default to Ask**: New tools should require user approval
2. **Clear Permission Indicators**: User should always know what tools are allowed
3. **Audit Trail**: Log tool permission changes
4. **Session Isolation**: Session permissions don't leak across sessions
5. **No Silent Failures**: If tool is denied, show clear message

---

## Related Files

- `src/CopilotAgent.Core/Services/CopilotService.cs` - CLI integration
- `src/CopilotAgent.Core/Models/Session.cs` - Session model
- `src/CopilotAgent.Core/Services/IMcpService.cs` - MCP configuration
- `src/CopilotAgent.Core/Services/ICommandPolicyService.cs` - Similar pattern for command approval

---

## References

- Copilot CLI documentation
- Claude Desktop tool approval UX
- Existing `CommandPolicyService` implementation for patterns