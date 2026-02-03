# Copilot Agent Desktop - Complete Implementation Guide

This document provides a comprehensive guide to implementing all phases of the Copilot Agent Desktop application.

## Current Status (As of 2026-02-03)

### ✅ Completed
- Project structure and solution setup
- All 11 core data models
- NuGet package configuration
- Basic WPF application shell
- ViewModelBase (MVVM infrastructure)
- MainWindowViewModel
- IPersistenceService interface
- JsonPersistenceService implementation
- ICopilotService interface
- Build system configured and working

### 🔨 Next Critical Steps for MVP

## Phase 1: Complete Core Services (2-3 hours)

### 1.1 CopilotService Implementation
Create `src/CopilotAgent.Core/Services/CopilotService.cs`:

```csharp
public class CopilotService : ICopilotService
{
    private readonly ILogger<CopilotService> _logger;
    
    public async Task<bool> IsCopilotAvailableAsync()
    {
        // Execute: gh copilot --version
        // Return true if exit code 0
    }
    
    public async IAsyncEnumerable<ChatMessage> SendMessageStreamingAsync(...)
    {
        // Execute: gh copilot suggest --stream
        // Parse streaming JSON responses
        // Yield ChatMessage objects
    }
    
    public async Task<ToolResult> ExecuteCommandAsync(...)
    {
        // Use Process.Start to execute command
        // Capture stdout/stderr
        // Return ToolResult
    }
}
```

**Key Implementation Notes:**
- Use `System.Diagnostics.Process` for CLI interaction
- Parse JSON responses from `gh copilot` commands
- Handle authentication via existing `gh auth` token
- Implement timeout and cancellation support

### 1.2 SessionManager
Create `src/CopilotAgent.Core/Services/ISessionManager.cs`:

```csharp
public interface ISessionManager
{
    Task<Session> CreateSessionAsync(string? workingDirectory = null);
    Task<Session> CreateWorktreeSessionAsync(string issueUrl);
    Task<List<Session>> GetAllSessionsAsync();
    Task<Session?> GetSessionAsync(string sessionId);
    Task SaveSessionAsync(Session session);
    Task DeleteSessionAsync(string sessionId);
    Session? ActiveSession { get; set; }
}
```

Implementation should:
- Use IPersistenceService for storage
- Manage active session state
- Handle worktree creation via git CLI

### 1.3 Configure Dependency Injection

Update `src/CopilotAgent.App/App.xaml.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

public partial class App : Application
{
    private IHost? _host;
    
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File("logs/copilot-agent-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();
        
        // Build host with DI
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Core Services
                services.AddSingleton<IPersistenceService, JsonPersistenceService>();
                services.AddSingleton<ICopilotService, CopilotService>();
                services.AddSingleton<ISessionManager, SessionManager>();
                
                // ViewModels
                services.AddTransient<MainWindowViewModel>();
                services.AddTransient<ChatViewModel>();
                
                // Main Window
                services.AddSingleton<MainWindow>();
            })
            .UseSerilog()
            .Build();
        
        await _host.StartAsync();
        
        // Show main window
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = _host.Services.GetRequiredService<MainWindowViewModel>();
        mainWindow.Show();
    }
    
    protected override async void OnExit(ExitEventArgs e)
    {
        await _host?.StopAsync()!;
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
```

## Phase 2: Basic Chat UI (3-4 hours)

### 2.1 ChatViewModel
Create `src/CopilotAgent.App/ViewModels/ChatViewModel.cs`:

```csharp
public partial class ChatViewModel : ViewModelBase
{
    private readonly ICopilotService _copilotService;
    private readonly ISessionManager _sessionManager;
    private string _inputMessage = string.Empty;
    private ObservableCollection<ChatMessage> _messages = new();
    
    public ChatViewModel(ICopilotService copilotService, ISessionManager sessionManager)
    {
        _copilotService = copilotService;
        _sessionManager = sessionManager;
    }
    
    [RelayCommand]
    private async Task SendMessageAsync()
    {
        // Add user message to UI
        // Call _copilotService.SendMessageStreamingAsync
        // Update UI with streaming responses
    }
}
```

### 2.2 ChatView XAML
Create `src/CopilotAgent.App/Views/ChatView.xaml`:

```xml
<UserControl>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        
        <!-- Messages List -->
        <ScrollViewer Grid.Row="0">
            <ItemsControl ItemsSource="{Binding Messages}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <!-- Message display with Markdown rendering -->
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
        
        <!-- Input Area -->
        <Grid Grid.Row="1">
            <TextBox Text="{Binding InputMessage}" />
            <Button Command="{Binding SendMessageCommand}" Content="Send"/>
        </Grid>
    </Grid>
</UserControl>
```

### 2.3 Markdown Rendering
Use Markdig to convert markdown to XAML:

```csharp
using Markdig;

public static class MarkdownHelper
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();
    
    public static string ToHtml(string markdown)
    {
        return Markdown.ToHtml(markdown, Pipeline);
    }
}
```

## Phase 3: Multi-Session Management (2-3 hours)

### 3.1 Session Tabs UI
Add tab control to MainWindow.xaml:

```xml
<TabControl ItemsSource="{Binding Sessions}">
    <TabControl.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding DisplayName}"/>
        </DataTemplate>
    </TabControl.ItemTemplate>
    <TabControl.ContentTemplate>
        <DataTemplate>
            <views:ChatView DataContext="{Binding ChatViewModel}"/>
        </DataTemplate>
    </TabControl.ContentTemplate>
</TabControl>
```

### 3.2 Worktree Session Creation
Implement in SessionManager:

```csharp
public async Task<Session> CreateWorktreeSessionAsync(string issueUrl)
{
    // 1. Parse issue URL (owner/repo/issues/123)
    // 2. Call: gh issue view 123 --repo owner/repo --json title,body
    // 3. Create worktree: git worktree add path branch
    // 4. Create session with worktree info
    // 5. Add issue context to system prompt
}
```

## Phase 4: Terminal Integration (3-4 hours)

### 4.1 Terminal Service
Create `src/CopilotAgent.Core/Services/ITerminalService.cs`:

```csharp
public interface ITerminalService
{
    Task<string> StartTerminalAsync(string workingDirectory);
    Task SendCommandAsync(string terminalId, string command);
    Task<string> GetOutputAsync(string terminalId);
    IObservable<string> ObserveOutput(string terminalId);
}
```

### 4.2 Pty.Net Integration
```csharp
using Pty.Net;

public class TerminalService : ITerminalService
{
    public async Task<string> StartTerminalAsync(string workingDirectory)
    {
        var pty = await PtyProvider.SpawnAsync(new PtyOptions
        {
            Name = "cmd.exe",
            Cwd = workingDirectory
        });
        // Store pty instance and return ID
    }
}
```

## Phase 5: Command Policy (1-2 hours)

### 5.1 Command Policy Service
```csharp
public class CommandPolicyService : ICommandPolicyService
{
    public Task<bool> IsCommandAllowedAsync(string command, Session session)
    {
        // Check against global + session policies
        // Return true if allowed
    }
    
    public async Task<CommandDecision> RequestApprovalAsync(string command)
    {
        // Show approval dialog
        // Return user decision
    }
}
```

### 5.2 Approval Dialog
Create WPF dialog with:
- Command display
- Risk level indicator
- Options: Allow Once, Always Allow, Deny

## Phase 6-9: Advanced Features (4-6 hours each)

### Phase 6: MCP Configuration
- Create McpService for managing server configs
- UI for adding/editing/enabling MCP servers
- Integration with Copilot SDK when available

### Phase 7: Skills Support
- SkillsService to load SKILL.md files
- Parse markdown and extract skills
- Inject into system prompts

### Phase 8: Iterative Agent Mode
- State machine for task iterations
- Self-evaluation prompts
- Progress tracking UI

### Phase 9: Context Summarization
- Token counting
- Summarization when threshold exceeded
- Store compressed history

## Testing Strategy

### Unit Tests
Create in `tests/CopilotAgent.Tests/`:
- `SessionManagerTests.cs`
- `PersistenceServiceTests.cs`
- `CommandPolicyTests.cs`
- `IterativeTaskStateTests.cs`

### Integration Tests
- Test Copilot CLI integration
- Test session persistence
- Test worktree creation

## Deployment

### Single-File Publish
```bash
dotnet publish src/CopilotAgent.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Output: `publish/CopilotAgent.exe` (~100-150MB)

### Installer (Optional)
Use WiX Toolset or Inno Setup to create installer:
- Install to Program Files
- Create desktop shortcut
- Add to Start Menu
- Register file associations (optional)

## Performance Considerations

1. **Async/Await**: All IO operations must be async
2. **Streaming**: Use IAsyncEnumerable for Copilot responses
3. **Memory**: Implement message history limits
4. **UI Threading**: Use Dispatcher for UI updates from background threads

## Security Considerations

1. **Command Execution**: Always validate via CommandPolicy
2. **File Access**: Restrict to session working directory
3. **Secrets**: Use DPAPI for any stored credentials
4. **Updates**: Implement secure update mechanism

## Estimated Total Implementation Time

| Phase | Time Estimate |
|-------|---------------|
| Phase 1: Core Services | 2-3 hours |
| Phase 2: Basic Chat UI | 3-4 hours |
| Phase 3: Multi-Session | 2-3 hours |
| Phase 4: Terminal | 3-4 hours |
| Phase 5: Command Policy | 1-2 hours |
| Phase 6: MCP | 4-6 hours |
| Phase 7: Skills | 3-4 hours |
| Phase 8: Iterative Mode | 4-6 hours |
| Phase 9: Polish | 2-3 hours |
| Testing | 4-6 hours |
| **Total** | **28-41 hours** |

## MVP (Minimum Viable Product)

For a working MVP, focus on:
1. ✅ Core services (Copilot, Persistence, Session)
2. ✅ Basic chat UI with markdown
3. ✅ Single session management
4. ⏳ Command execution via gh CLI

This provides a functional chat interface with Copilot in ~8-10 hours of development.

## References

- [GitHub CLI Copilot Extension](https://github.com/github/gh-copilot)
- [GitHub Copilot SDK](https://github.com/github/copilot-sdk)
- [WPF-UI Documentation](https://wpfui.lepo.co/)
- [Pty.Net GitHub](https://github.com/microsoft/Pty.Net)
- [Model Context Protocol](https://modelcontextprotocol.io/)