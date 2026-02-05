# Tool Approval Implementation Approaches

This document outlines two architectural approaches for implementing interactive tool approval dialogs in CopilotDesktop. The goal is to allow users to approve or deny tool executions (shell commands, file writes, MCP tools) before they run, with options for session-wide or global permissions.

## Current State Analysis

### Current Architecture
- **Process Model**: Uses `Process.Start()` with `-p` flag (non-interactive/piped mode)
- **Autonomous Mode**: Implemented in Phase 0 with CLI flags (`--allow-all`, `--yolo`, `--allow-all-tools`, `--allow-all-paths`, `--allow-all-urls`)
- **Permission Handling**: Binary (all-or-nothing via autonomous mode flags)
- **No Interception**: Tools execute directly without pre-approval hooks

### Relevant Files
| File | Purpose |
|------|---------|
| `src/CopilotAgent.Core/Services/CopilotService.cs` | Spawns Copilot CLI process |
| `src/CopilotAgent.Core/Models/Session.cs` | Contains `AutonomousModeSettings` |
| `src/CopilotAgent.App/Views/SessionInfoView.xaml` | Autonomous mode UI controls |

---

## Path A: SDK Migration

### Overview
Migrate from spawning Copilot CLI via `Process.Start()` to using the **GitHub Copilot SDK for .NET** (if available) or a native SDK integration. This provides programmatic hooks for tool execution events.

### Key SDK Events
Based on Copilot architecture patterns:

```csharp
// Event fired BEFORE tool execution
public event EventHandler<ToolExecutionStartEvent> OnPreToolUse;

// Event fired AFTER tool execution
public event EventHandler<ToolExecutionCompleteEvent> OnPostToolUse;

// Confirmation request from agent
public event EventHandler<ToolConfirmationRequestedEvent> OnConfirmationRequested;
```

### Implementation Pattern

```csharp
public class CopilotSdkService : ICopilotService
{
    private CopilotAgent _agent;
    
    public async Task InitializeAsync()
    {
        _agent = new CopilotAgent(config);
        
        // Register permission interception hook
        _agent.OnPreToolUse += HandlePreToolUse;
        _agent.OnConfirmationRequested += HandleConfirmationRequested;
    }
    
    private async Task<ToolApprovalResult> HandlePreToolUse(ToolExecutionStartEvent e)
    {
        var permission = new PermissionRequest
        {
            Type = e.ToolType,        // "shell", "write", "read", "url", "mcp"
            Command = e.Command,       // e.g., "git status"
            Path = e.TargetPath,       // e.g., "/path/to/file"
            McpServer = e.McpServer,   // e.g., "filesystem"
            McpTool = e.McpTool        // e.g., "read_file"
        };
        
        // Check existing permissions
        if (_permissionService.IsAllowed(permission))
            return ToolApprovalResult.Allow;
        
        if (_permissionService.IsDenied(permission))
            return ToolApprovalResult.Deny;
        
        // Show approval dialog (async/await - pauses execution)
        var result = await ShowApprovalDialogAsync(permission);
        
        // Store permission based on scope
        _permissionService.RecordDecision(permission, result);
        
        return result;
    }
}
```

### SDK Permission Format
Based on Copilot CLI documentation:
```
Permission String Format:
- 'shell'              → All shell commands
- 'shell(git)'         → Only git commands
- 'shell(git status)'  → Only "git status" command
- 'write'              → All file writes
- 'write(/path)'       → Writes to specific path
- 'read'               → All file reads
- 'url'                → All URL access
- 'MCP_SERVER(tool)'   → Specific MCP tool
```

### Pros
| Advantage | Description |
|-----------|-------------|
| ✅ Native Hooks | SDK provides `OnPreToolUse` for clean interception |
| ✅ Synchronous Approval | Dialog can pause execution until user responds |
| ✅ Rich Context | Full tool metadata (command, args, paths) available |
| ✅ Future-Proof | SDK updates automatically provide new features |
| ✅ No Parsing | No need to parse CLI output for errors |

### Cons
| Disadvantage | Description |
|--------------|-------------|
| ❌ SDK Availability | GitHub Copilot .NET SDK may not be publicly available |
| ❌ Major Refactor | Requires rewriting `CopilotService` entirely |
| ❌ API Stability | SDK APIs may change between versions |
| ❌ Authentication | May require different auth flow than CLI |
| ❌ Testing Complexity | Need to mock SDK for unit tests |

### Effort Estimate
- **High**: 3-5 days for full migration
- **Risk**: SDK availability uncertain

---

## Path B: CLI Output Parsing

### Overview
Keep the current `Process.Start()` architecture but enhance it to:
1. Parse CLI output for permission errors
2. Prompt user when permission denied
3. Retry command with appropriate `--allow` flags

### Permission Error Detection
When tools fail due to missing permissions, Copilot CLI outputs identifiable error patterns:

```
# Shell command denied
Error: Permission denied for shell command: git push
Add '--allow shell(git push)' to allow this command.

# File write denied
Error: Permission denied for write operation: /path/to/file.txt
Add '--allow write(/path/to/file.txt)' to allow this operation.

# MCP tool denied  
Error: Permission denied for MCP tool: filesystem(read_file)
Add '--allow MCP_filesystem(read_file)' to allow this tool.
```

### Implementation Pattern

```csharp
public class CopilotService : ICopilotService
{
    private readonly IPermissionService _permissionService;
    private readonly IApprovalDialogService _dialogService;
    
    public async Task<string> SendPromptAsync(Session session, string prompt)
    {
        var result = await ExecuteWithPermissionsAsync(session, prompt);
        return result;
    }
    
    private async Task<string> ExecuteWithPermissionsAsync(Session session, string prompt)
    {
        // Build initial args with known permissions
        var args = BuildArgsWithPermissions(session);
        
        while (true)
        {
            var output = await ExecuteCopilotAsync(args, prompt);
            
            // Check for permission errors
            var permissionError = ParsePermissionError(output);
            if (permissionError == null)
                return output; // Success
            
            // Show approval dialog
            var decision = await _dialogService.ShowApprovalAsync(permissionError);
            
            if (decision.Approved)
            {
                // Add permission and retry
                _permissionService.Grant(permissionError, decision.Scope);
                args = BuildArgsWithPermissions(session);
                // Loop continues to retry
            }
            else
            {
                // User denied - return error message
                return $"Tool execution denied by user: {permissionError.Description}";
            }
        }
    }
    
    private PermissionError? ParsePermissionError(string output)
    {
        // Regex patterns for different permission types
        var patterns = new[]
        {
            (@"Permission denied for shell command: (.+)", PermissionType.Shell),
            (@"Permission denied for write operation: (.+)", PermissionType.Write),
            (@"Permission denied for read operation: (.+)", PermissionType.Read),
            (@"Permission denied for MCP tool: (.+)", PermissionType.Mcp),
            (@"Permission denied for URL access: (.+)", PermissionType.Url)
        };
        
        foreach (var (pattern, type) in patterns)
        {
            var match = Regex.Match(output, pattern);
            if (match.Success)
            {
                return new PermissionError
                {
                    Type = type,
                    Target = match.Groups[1].Value
                };
            }
        }
        
        return null;
    }
    
    private string BuildArgsWithPermissions(Session session)
    {
        var args = new List<string> { "-p" };
        
        // Add autonomous mode flags
        args.AddRange(session.AutonomousMode.GetCliArguments());
        
        // Add granted permissions
        foreach (var permission in _permissionService.GetGrantedPermissions())
        {
            args.Add($"--allow {permission.ToCliString()}");
        }
        
        return string.Join(" ", args);
    }
}
```

### Pros
| Advantage | Description |
|-----------|-------------|
| ✅ Minimal Changes | Extends existing architecture |
| ✅ No SDK Dependency | Works with current CLI-based approach |
| ✅ Immediate Start | Can begin implementation now |
| ✅ Known Patterns | Uses familiar Process/stdout handling |
| ✅ Easy Rollback | Can disable without major refactor |

### Cons
| Disadvantage | Description |
|--------------|-------------|
| ❌ Fragile Parsing | Relies on CLI output format (may change) |
| ❌ Retry Overhead | Must re-execute command after approval |
| ❌ Delayed Feedback | User sees error before approval prompt |
| ❌ Incomplete Context | May miss some metadata in error message |
| ❌ Race Conditions | Multi-tool chains may have complex flow |

### Effort Estimate
- **Medium**: 2-3 days for basic implementation
- **Risk**: CLI output format changes could break parsing

---

## Comparison Matrix

| Criteria | Path A (SDK) | Path B (CLI Parsing) |
|----------|-------------|---------------------|
| **Implementation Effort** | High (3-5 days) | Medium (2-3 days) |
| **Maintenance Burden** | Low (SDK handles changes) | High (parsing may break) |
| **User Experience** | Better (pre-approval) | Acceptable (post-error) |
| **Reliability** | High (native hooks) | Medium (regex fragility) |
| **SDK Dependency** | Required (availability uncertain) | None |
| **Refactor Scope** | Major (`CopilotService` rewrite) | Minor (additive changes) |
| **Testing** | Complex (SDK mocking) | Simple (output fixtures) |
| **Future Features** | Automatic via SDK | Manual implementation |

---

## Recommended Approach

### Short-Term: Path B (CLI Parsing)
Given the current constraints:
1. **SDK Availability**: GitHub Copilot .NET SDK availability is uncertain
2. **Working System**: Current CLI-based approach is functional with autonomous mode
3. **Incremental Value**: CLI parsing can be shipped quickly and improved iteratively

### Long-Term: Path A (SDK Migration)
When SDK becomes available:
1. **Better UX**: Pre-approval dialogs before tool execution
2. **Cleaner Code**: Remove parsing logic in favor of event handlers
3. **Richer Features**: Access to full tool metadata and streaming

### Hybrid Approach (Recommended)
Implement Path B now with **abstraction layer** that allows Path A migration later:

```csharp
// Abstract permission interception
public interface IToolPermissionInterceptor
{
    Task<PermissionDecision> InterceptAsync(ToolExecutionContext context);
}

// Path B implementation
public class CliOutputPermissionInterceptor : IToolPermissionInterceptor
{
    // Parses CLI output for permission errors
}

// Future Path A implementation
public class SdkPermissionInterceptor : IToolPermissionInterceptor
{
    // Uses SDK OnPreToolUse events
}
```

---

## Implementation Roadmap

### Phase 1: Foundation (Path B)
- [ ] Create `IToolPermissionInterceptor` interface
- [ ] Create `PermissionRequest` and `PermissionDecision` models
- [ ] Implement permission error regex parsing
- [ ] Add permission storage (session/global)

### Phase 2: UI Integration
- [ ] Create `ToolApprovalDialog.xaml` (approval popup)
- [ ] Integrate dialog with chat view
- [ ] Add "Remember" checkbox (Once/Session/Always)
- [ ] Display pending approvals in chat

### Phase 3: Permission Management
- [ ] Create `PermissionsView.xaml` for managing saved permissions
- [ ] Add "Allowed Commands" and "Blocked Commands" lists
- [ ] Persist permissions to JSON/SQLite
- [ ] Add import/export for permission profiles

### Phase 4: SDK Migration (Future)
- [ ] Evaluate SDK availability
- [ ] Create `SdkPermissionInterceptor` implementation
- [ ] Add feature flag for SDK vs CLI mode
- [ ] Migrate incrementally

---

## Related Documentation
- [MCP Tool Approval Implementation](./MCP_TOOL_APPROVAL_IMPLEMENTATION.md) - Phase 0 details
- [Copilot CLI Documentation](https://docs.github.com/en/copilot/using-github-copilot/using-github-copilot-in-the-command-line) - Official CLI reference

---

*Last Updated: February 2026*