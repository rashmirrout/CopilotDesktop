# Session Persistence Architecture

This document describes how conversation context is maintained with GitHub Copilot CLI across app restarts.

## Overview

The Copilot Desktop app maintains conversation context with GitHub Copilot CLI using a two-layer session management approach:

1. **App-level sessions** - Managed by our app, persisted as JSON files
2. **Copilot CLI sessions** - Managed by Copilot CLI, stored at `~/.copilot/session-state/`

The key challenge is bridging these two layers so that conversation context survives app restarts.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Copilot Desktop App                       │
├─────────────────────────────────────────────────────────────────┤
│  Session (our app)                                              │
│  ├─ SessionId: "abc123"           (our internal ID)             │
│  ├─ CopilotSessionId: "e7194601-..." (Copilot CLI's GUID)       │
│  ├─ MessageHistory: [...]         (persisted in JSON)           │
│  └─ WorkingDirectory: "C:\..."                                  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Copilot CLI Session                         │
│  Location: ~/.copilot/session-state/{CopilotSessionId}/         │
│  ├─ workspace.yaml    (cwd, created_at, summary)                │
│  ├─ events.jsonl      (conversation events)                     │
│  ├─ checkpoints/      (context checkpoints)                     │
│  └─ files/            (referenced file snapshots)               │
└─────────────────────────────────────────────────────────────────┘
```

## Copilot CLI Session Commands

The Copilot CLI provides these flags for session management:

| Flag | Purpose |
|------|---------|
| `-p "message"` | Non-interactive prompt mode |
| `--continue` | Resume the most recent session (global) |
| `--resume [sessionId]` | Resume a specific session by GUID |
| `--stream on` | Enable streaming output |
| `-s` / `--silent` | Output only agent response |

## Session Lifecycle

### Phase 1: First Message (New Session)

```
User sends first message
        │
        ▼
┌───────────────────────────────────────┐
│ CopilotService.SendMessageStreamingAsync()
│                                       │
│ 1. Check: session.CopilotSessionId    │
│    → null (first message)             │
│                                       │
│ 2. Check: _sessionHasStarted dict     │
│    → false (app just started)         │
│                                       │
│ 3. Build command:                     │
│    copilot -p "message" -s --stream on│
│                                       │
│ 4. Mark shouldCaptureSessionId = true │
└───────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────┐
│ Copilot CLI creates new session       │
│ → ~/.copilot/session-state/{GUID}/    │
└───────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────┐
│ TryCaptureCopilotSessionIdAsync()     │
│                                       │
│ 1. Scan session-state directories     │
│ 2. Filter: GUID-named folders only    │
│ 3. Sort by LastWriteTime (recent first)│
│ 4. For each folder:                   │
│    - Parse workspace.yaml             │
│    - Match cwd with session.WorkingDir│
│    - Check created_at < 60 seconds ago│
│ 5. Return matched GUID                │
│                                       │
│ → "e7194601-209d-4bfc-b89f-a6f8912cdc04"
└───────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────┐
│ Store captured ID:                    │
│ session.CopilotSessionId = GUID       │
│ (persisted to JSON on save)           │
└───────────────────────────────────────┘
```

### Phase 2: Subsequent Messages (Same App Run)

```
User sends another message
        │
        ▼
┌───────────────────────────────────────┐
│ CopilotService.SendMessageStreamingAsync()
│                                       │
│ 1. Check: session.CopilotSessionId    │
│    → "e7194601-..." (captured earlier)│
│                                       │
│ 2. Build command:                     │
│    copilot --resume e7194601-...      │
│            -p "message" -s --stream on│
└───────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────┐
│ Copilot CLI resumes specific session  │
│ → Full conversation context available │
└───────────────────────────────────────┘
```

### Phase 3: After App Restart

```
App restarts, loads session from JSON
        │
        ▼
┌───────────────────────────────────────┐
│ Session loaded:                       │
│ ├─ SessionId: "abc123"                │
│ ├─ CopilotSessionId: "e7194601-..."   │ ← Persisted!
│ └─ MessageHistory: [previous msgs]    │
└───────────────────────────────────────┘
        │
        ▼
User sends new message
        │
        ▼
┌───────────────────────────────────────┐
│ CopilotService.SendMessageStreamingAsync()
│                                       │
│ 1. Check: session.CopilotSessionId    │
│    → "e7194601-..." (from JSON)       │
│                                       │
│ 2. Build command:                     │
│    copilot --resume e7194601-...      │
│            -p "message" -s --stream on│
│                                       │
│ Note: Uses --resume NOT --continue    │
│ because --continue picks "most recent"│
│ which may be wrong session            │
└───────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────┐
│ Copilot CLI resumes EXACT session     │
│ → Conversation context restored!      │
└───────────────────────────────────────┘
```

## Data Structures

### Session Model (Session.cs)

```csharp
public class Session
{
    // Our internal session ID
    public string SessionId { get; set; }
    
    // Copilot CLI's session GUID (persisted for cross-restart reconnection)
    public string? CopilotSessionId { get; set; }
    
    // Working directory - used to match Copilot sessions
    public string? WorkingDirectory { get; set; }
    
    // UI message history (separate from Copilot's internal context)
    public List<ChatMessage> MessageHistory { get; set; }
}
```

### Copilot Session Storage (~/.copilot/session-state/{GUID}/)

```yaml
# workspace.yaml
id: e7194601-209d-4bfc-b89f-a6f8912cdc04
cwd: C:\WorkSpace\Project
git_root: C:\WorkSpace\Project
repository: user/repo
branch: main
summary: Working on feature X
created_at: 2026-02-03T22:26:45.448Z
updated_at: 2026-02-03T22:29:26.529Z
```

## Session ID Capture Algorithm

```csharp
async Task<string?> TryCaptureCopilotSessionIdAsync(string workingDirectory)
{
    // 1. Get all GUID-named directories in session-state
    var sessionDirs = Directory.GetDirectories(_copilotSessionStatePath)
        .Where(d => Guid.TryParse(Path.GetFileName(d), out _))
        .Select(d => new DirectoryInfo(d))
        .OrderByDescending(d => d.LastWriteTime)
        .Take(10);

    foreach (var sessionDir in sessionDirs)
    {
        // 2. Parse workspace.yaml
        var yamlContent = await File.ReadAllTextAsync(
            Path.Combine(sessionDir.FullName, "workspace.yaml"));
        
        var cwd = ParseYamlValue(yamlContent, "cwd");
        var createdAt = ParseYamlValue(yamlContent, "created_at");

        // 3. Match working directory
        if (PathsMatch(cwd, workingDirectory))
        {
            // 4. Verify recently created (within 60 seconds)
            if (WasCreatedRecently(createdAt, maxAge: 60))
            {
                return sessionDir.Name; // The GUID
            }
        }
    }
    
    return null;
}
```

## Command Decision Matrix

| Condition | Command Used |
|-----------|--------------|
| First message, no stored CopilotSessionId | `copilot -p "msg" -s --stream on` |
| Subsequent message, same app run, no stored ID | `copilot --continue -p "msg" -s --stream on` |
| Any message with stored CopilotSessionId | `copilot --resume {id} -p "msg" -s --stream on` |
| Legacy mode (USE_SESSION_CONTINUATION=false) | `copilot -p "msg" -s --stream on` |

## Edge Cases

### Session ID Capture Failure

If the session ID cannot be captured (e.g., Copilot session-state not accessible):
- Within same app run: Falls back to `--continue` (works but less precise)
- After restart: Starts new Copilot session (context lost, but UI history remains)

### Working Directory Mismatch

If user changes working directory:
- Session ID may not match new directory
- May need to start new session for new directory

### Copilot Session Expiry

Copilot CLI may clean up old sessions. If the stored CopilotSessionId no longer exists:
- `--resume` will fail
- App should handle error and start new session (TODO: not yet implemented)

## Feature Flag

```csharp
// CopilotService.cs
private const bool USE_SESSION_CONTINUATION = true;

// Set to false to disable session persistence and use legacy per-message mode
```

## File Locations

| Data | Location |
|------|----------|
| App sessions | `%APPDATA%\CopilotAgent\sessions\{SessionId}.json` |
| Copilot sessions | `%USERPROFILE%\.copilot\session-state\{GUID}\` |
| Session workspace | `%USERPROFILE%\.copilot\session-state\{GUID}\workspace.yaml` |

## Sequence Diagram

```
┌──────┐          ┌────────────┐          ┌─────────────┐          ┌─────────────────┐
│ User │          │ ChatView   │          │CopilotService│          │ Copilot CLI     │
└──┬───┘          └─────┬──────┘          └──────┬──────┘          └────────┬────────┘
   │                    │                        │                          │
   │ Send "Hello"       │                        │                          │
   │───────────────────>│                        │                          │
   │                    │ SendMessageStreaming() │                          │
   │                    │───────────────────────>│                          │
   │                    │                        │                          │
   │                    │                        │ [No CopilotSessionId]    │
   │                    │                        │ copilot -p "Hello" ...   │
   │                    │                        │─────────────────────────>│
   │                    │                        │                          │
   │                    │                        │        Response          │
   │                    │<───────────────────────│<─────────────────────────│
   │                    │                        │                          │
   │                    │                        │ Capture session ID       │
   │                    │                        │ from ~/.copilot/...      │
   │                    │                        │──────────┐               │
   │                    │                        │          │               │
   │                    │                        │<─────────┘               │
   │                    │                        │ CopilotSessionId = GUID  │
   │                    │                        │                          │
   │ Send "What's 2+2?" │                        │                          │
   │───────────────────>│                        │                          │
   │                    │ SendMessageStreaming() │                          │
   │                    │───────────────────────>│                          │
   │                    │                        │                          │
   │                    │                        │ [Has CopilotSessionId]   │
   │                    │                        │ copilot --resume GUID... │
   │                    │                        │─────────────────────────>│
   │                    │                        │                          │
   │                    │                        │   Response with context  │
   │<───────────────────│<───────────────────────│<─────────────────────────│
   │                    │                        │                          │