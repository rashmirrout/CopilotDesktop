Full Technical & Execution Plan
Multi‑Agent Panel Discussion System — Desktop Application
1. Vision & Objectives
1.1 Product Vision
Build a desktop‑native, in‑process, multi‑agent panel discussion system where a configurable number of AI agents collaborate, debate, and converge on complex analytical tasks — all orchestrated through a deterministic state machine and surfaced through a world‑class, live‑updating UI.

1.2 Core Principles
Principle	How We Apply It
KISS	Every component does one thing. No "god classes", no premature abstraction.
SOLID	Single responsibility per class. Open for extension (new tools, new agent roles). Depend on abstractions (IAgent, ITool).
Clean Architecture	Domain layer has zero external dependencies. Infrastructure is swappable. UI is a thin presentation layer.
Observable by Default	Every state transition, every agent action, every tool call emits a structured event.
Fail‑Safe	All LLM calls have timeouts, retries (Polly), and circuit breakers. The Moderator can kill a runaway discussion at any moment.
Memory‑Safe	Deterministic disposal of every agent, every tool handle, every chat history buffer when a discussion ends.
1.3 Key Differentiators
In‑Process Orchestration — No separate backend server. The entire agent pipeline runs inside the desktop process, making API calls directly to cloud LLMs. This means zero deployment friction, zero network hops for orchestration logic, and instant startup.
Full State Machine — Every discussion phase is a first‑class state with explicit transitions, guards, and side‑effects.
Live Commentary UI — Users see exactly what every agent is doing, thinking, and calling — in real time, with configurable verbosity.
Follow‑Up with Full Context — After a discussion concludes, the Head retains the entire synthesis and can answer user questions without re‑running the panel.
2. Architecture Overview
text

┌───────────────────────────────────────────────────────────────────┐
│                     HOST APPLICATION (existing)                   │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                 NEW TAB: Panel Discussion                   │ │
│  │  ┌──────────────────────────────────────────────────────┐  │ │
│  │  │              PRESENTATION LAYER                      │  │ │
│  │  │   WPF Shell  ◄───► Blazor Hybrid (MudBlazor)        │  │ │
│  │  │   • ChatPanel       • StatusDashboard                │  │ │
│  │  │   • ControlToolbar  • SettingsDialog                 │  │ │
│  │  │   • CommentaryView  • PanelIndicator                 │  │ │
│  │  └──────────────────┬───────────────────────────────────┘  │ │
│  │                     │ IDiscussionOrchestrator               │ │
│  │                     │ IEventStream<DiscussionEvent>         │ │
│  │  ┌──────────────────▼───────────────────────────────────┐  │ │
│  │  │              APPLICATION LAYER                       │  │ │
│  │  │   DiscussionOrchestrator                             │  │ │
│  │  │   DiscussionStateMachine (Stateless lib)             │  │ │
│  │  │   AgentSupervisor (lifecycle, pause, dispose)        │  │ │
│  │  │   ConversationMemoryManager                          │  │ │
│  │  │   ToolRouter                                         │  │ │
│  │  └──────────────────┬───────────────────────────────────┘  │ │
│  │                     │ IAgent, ITool, IAIProvider            │ │
│  │  ┌──────────────────▼───────────────────────────────────┐  │ │
│  │  │              DOMAIN LAYER                            │  │ │
│  │  │   Entities: Discussion, AgentInstance, Message,      │  │ │
│  │  │             ToolCall, GuardRailPolicy, Workspace     │  │ │
│  │  │   Value Objects: DiscussionId, ModelIdentifier,      │  │ │
│  │  │                  TurnNumber, TokenBudget             │  │ │
│  │  │   Enums: DiscussionState, AgentRole, Verbosity       │  │ │
│  │  │   Interfaces: IAgent, ITool, IAIProvider,            │  │ │
│  │  │               IEventBus, ISessionStore               │  │ │
│  │  └──────────────────────────────────────────────────────┘  │ │
│  │                     ▲                                       │ │
│  │  ┌──────────────────┴───────────────────────────────────┐  │ │
│  │  │              INFRASTRUCTURE LAYER                    │  │ │
│  │  │   SemanticKernelAIProvider                           │  │ │
│  │  │   McpClientAdapter                                   │  │ │
│  │  │   PlaywrightWebCrawlerTool                           │  │ │
│  │  │   RoslynCodeAnalyzerTool                             │  │ │
│  │  │   LiteDbSessionStore                                 │  │ │
│  │  │   LocalFileSettingsProvider                          │  │ │
│  │  └──────────────────────────────────────────────────────┘  │ │
│  └─────────────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────────────┘
         │                         │                      │
         ▼                         ▼                      ▼
   Azure OpenAI /          MCP Servers            Local File System
   OpenAI / Anthropic      (code repos,           (workspace repos,
   / Local LLMs            databases)             logs, settings)
Key Architectural Decisions
Decision	Rationale
In‑process, no backend	Desktop app calls LLM APIs directly. Zero deployment complexity. Orchestration latency is zero.
Blazor Hybrid for chat UI	MudBlazor gives us world‑class components (markdown, expansion panels, progress indicators) without building custom WPF controls from scratch. WPF hosts via BlazorWebView.
Reactive event stream	System.Reactive (IObservable<DiscussionEvent>) lets every UI component subscribe to exactly the events it cares about. No tight coupling.
Stateless (lib) state machine	Deterministic, testable, serializable state transitions.
LiteDB for local persistence	Embedded NoSQL database — zero‑config, single‑file, perfect for desktop session history.
Semantic Kernel as AI backbone	Official Microsoft SDK. Supports function calling (tools), chat history, multiple providers, prompt templates.
3. Technology Stack
3.1 Core Frameworks
Component	Technology	Version	NuGet Package
Runtime	.NET 8 LTS	8.0.x	—
Desktop Shell	WPF	.NET 8	Built‑in
Modern UI (Chat)	Blazor Hybrid	.NET 8	Microsoft.AspNetCore.Components.WebView.Wpf
UI Components	MudBlazor	7.x	MudBlazor
Markdown Rendering	Markdig + MudBlazor Markdown	—	Markdig, MudBlazor.Markdown
MVVM Toolkit	CommunityToolkit.Mvvm	8.x	CommunityToolkit.Mvvm
Dependency Injection	Microsoft.Extensions.Hosting	8.x	Microsoft.Extensions.Hosting
3.2 AI & Agent Frameworks
Component	Technology	NuGet Package
AI Orchestration	Microsoft Semantic Kernel	Microsoft.SemanticKernel
AI Abstraction	Microsoft.Extensions.AI	Microsoft.Extensions.AI
Azure OpenAI	Azure.AI.OpenAI	Microsoft.SemanticKernel.Connectors.AzureOpenAI
OpenAI	OpenAI .NET	Microsoft.SemanticKernel.Connectors.OpenAI
Anthropic	Community connector	Connectors.AI.Anthropic (or direct HTTP)
MCP Client	Model Context Protocol	ModelContextProtocol
Function Calling	SK Plugins	Microsoft.SemanticKernel.Plugins.Core
3.3 Infrastructure & Tooling
Component	Technology	NuGet Package
State Machine	Stateless	Stateless
Reactive Streams	System.Reactive	System.Reactive
Web Crawling	Playwright for .NET	Microsoft.Playwright
Code Analysis	Roslyn	Microsoft.CodeAnalysis.CSharp
Local DB	LiteDB	LiteDB
Resilience	Polly v8	Microsoft.Extensions.Http.Resilience
Logging	Serilog	Serilog.Sinks.File, Serilog.Sinks.Console
Telemetry	OpenTelemetry	OpenTelemetry.Extensions.Hosting
Testing	xUnit + FluentAssertions + NSubstitute	xunit, FluentAssertions, NSubstitute
UI Testing	Playwright (UI)	Microsoft.Playwright
3.4 Packaging
Component	Technology
Installer	MSIX (Windows Store + sideload)
Auto‑Update	MSIX auto‑update or Squirrel.Windows
Code Signing	Azure Trusted Signing (or DigiCert)
4. Solution & Project Structure
text

CopilotAgent.Panel.sln
│
├── src/
│   ├── CopilotAgent.Panel.Domain/
│   │   ├── Entities/
│   │   │   ├── Discussion.cs
│   │   │   ├── AgentInstance.cs
│   │   │   ├── Message.cs
│   │   │   ├── ToolCallRecord.cs
│   │   │   └── Workspace.cs
│   │   ├── ValueObjects/
│   │   │   ├── DiscussionId.cs
│   │   │   ├── ModelIdentifier.cs
│   │   │   ├── TurnNumber.cs
│   │   │   └── TokenBudget.cs
│   │   ├── Enums/
│   │   │   ├── DiscussionState.cs
│   │   │   ├── DiscussionTrigger.cs
│   │   │   ├── AgentRole.cs
│   │   │   ├── AgentStatus.cs
│   │   │   ├── VerbosityLevel.cs
│   │   │   └── MessageType.cs
│   │   ├── Interfaces/
│   │   │   ├── IAgent.cs
│   │   │   ├── IAgentFactory.cs
│   │   │   ├── ITool.cs
│   │   │   ├── IToolRegistry.cs
│   │   │   ├── IAIProvider.cs
│   │   │   ├── IEventBus.cs
│   │   │   ├── ISessionStore.cs
│   │   │   ├── ISettingsProvider.cs
│   │   │   └── IDiscussionOrchestrator.cs
│   │   ├── Events/
│   │   │   ├── DiscussionEvent.cs          // base
│   │   │   ├── StateChangedEvent.cs
│   │   │   ├── AgentMessageEvent.cs
│   │   │   ├── ToolCallEvent.cs
│   │   │   ├── ModerationEvent.cs
│   │   │   ├── CommentaryEvent.cs
│   │   │   └── ErrorEvent.cs
│   │   ├── Policies/
│   │   │   └── GuardRailPolicy.cs
│   │   └── CopilotAgent.Panel.Domain.csproj
│   │
│   ├── CopilotAgent.Panel.Application/
│   │   ├── StateMachine/
│   │   │   └── DiscussionStateMachine.cs
│   │   ├── Orchestration/
│   │   │   ├── DiscussionOrchestrator.cs
│   │   │   ├── AgentSupervisor.cs
│   │   │   ├── TurnManager.cs
│   │   │   └── DiscussionStrategy.cs
│   │   ├── Memory/
│   │   │   ├── ConversationMemoryManager.cs
│   │   │   └── DiscussionSummaryBuilder.cs
│   │   ├── Services/
│   │   │   ├── HeadService.cs
│   │   │   ├── ModerationService.cs
│   │   │   └── ToolRouter.cs
│   │   ├── DTOs/
│   │   │   ├── DiscussionRequest.cs
│   │   │   ├── DiscussionStatus.cs
│   │   │   ├── PanelistInfo.cs
│   │   │   └── SynthesisResult.cs
│   │   └── CopilotAgent.Panel.Application.csproj
│   │
│   ├── CopilotAgent.Panel.Agents/
│   │   ├── Head/
│   │   │   ├── HeadAgent.cs
│   │   │   └── HeadPrompts.cs
│   │   ├── Moderator/
│   │   │   ├── ModeratorAgent.cs
│   │   │   ├── ConvergenceDetector.cs
│   │   │   └── ModeratorPrompts.cs
│   │   ├── Panelist/
│   │   │   ├── PanelistAgent.cs
│   │   │   └── PanelistPrompts.cs
│   │   ├── Factory/
│   │   │   └── AgentFactory.cs
│   │   ├── Shared/
│   │   │   ├── AgentBase.cs
│   │   │   └── ChatHistoryExtensions.cs
│   │   └── CopilotAgent.Panel.Agents.csproj
│   │
│   ├── CopilotAgent.Panel.Tools/
│   │   ├── WebCrawler/
│   │   │   ├── PlaywrightWebCrawlerTool.cs
│   │   │   └── WebPageSummarizerTool.cs
│   │   ├── CodeAnalysis/
│   │   │   ├── RoslynCodeAnalyzerTool.cs
│   │   │   ├── EdgeCaseDetectorTool.cs
│   │   │   └── TestGeneratorTool.cs
│   │   ├── FileSystem/
│   │   │   ├── FileReaderTool.cs
│   │   │   └── DirectoryTreeTool.cs
│   │   ├── Search/
│   │   │   └── SemanticSearchTool.cs
│   │   ├── Registry/
│   │   │   └── ToolRegistry.cs
│   │   └── CopilotAgent.Panel.Tools.csproj
│   │
│   ├── CopilotAgent.Panel.Infrastructure/
│   │   ├── AI/
│   │   │   ├── SemanticKernelAIProvider.cs
│   │   │   ├── ModelResolver.cs
│   │   │   └── RetryPolicies.cs
│   │   ├── Mcp/
│   │   │   ├── McpClientAdapter.cs
│   │   │   └── McpToolBridge.cs
│   │   ├── Persistence/
│   │   │   ├── LiteDbSessionStore.cs
│   │   │   └── SessionDto.cs
│   │   ├── Settings/
│   │   │   └── LocalFileSettingsProvider.cs
│   │   ├── EventBus/
│   │   │   └── ReactiveEventBus.cs
│   │   ├── Logging/
│   │   │   └── SerilogConfiguration.cs
│   │   └── CopilotAgent.Panel.Infrastructure.csproj
│   │
│   └── CopilotAgent.Panel.UI/
│       ├── Hosting/
│       │   ├── PanelModule.cs            // composition root, DI registration
│       │   └── ServiceCollectionExtensions.cs
│       ├── WPF/
│       │   ├── PanelTabView.xaml         // the WPF UserControl that hosts BlazorWebView
│       │   ├── PanelTabView.xaml.cs
│       │   └── PanelTabViewModel.cs
│       ├── Blazor/
│       │   ├── wwwroot/
│       │   │   ├── css/
│       │   │   │   └── panel.css
│       │   │   └── js/
│       │   │       └── scrollHelper.js
│       │   ├── Shared/
│       │   │   ├── PanelLayout.razor
│       │   │   └── PanelLayout.razor.css
│       │   ├── Components/
│       │   │   ├── ChatPanel.razor
│       │   │   ├── ChatPanel.razor.cs
│       │   │   ├── ChatMessage.razor
│       │   │   ├── ChatMessage.razor.css
│       │   │   ├── ControlToolbar.razor
│       │   │   ├── StatusDashboard.razor
│       │   │   ├── StatusDashboard.razor.css
│       │   │   ├── PanelIndicator.razor
│       │   │   ├── CommentaryExpander.razor
│       │   │   ├── AgentAvatar.razor
│       │   │   ├── ToolCallCard.razor
│       │   │   ├── SettingsDialog.razor
│       │   │   ├── SettingsDialog.razor.cs
│       │   │   └── UserInputBar.razor
│       │   ├── ViewModels/
│       │   │   ├── ChatViewModel.cs
│       │   │   ├── StatusViewModel.cs
│       │   │   ├── SettingsViewModel.cs
│       │   │   └── ControlViewModel.cs
│       │   └── Services/
│       │       ├── UIEventSubscriber.cs
│       │       └── ThemeService.cs
│       └── CopilotAgent.Panel.UI.csproj
│
├── tests/
│   ├── CopilotAgent.Panel.Tests.Unit/
│   │   ├── Domain/
│   │   │   ├── DiscussionTests.cs
│   │   │   └── GuardRailPolicyTests.cs
│   │   ├── Application/
│   │   │   ├── StateMachineTests.cs
│   │   │   ├── OrchestratorTests.cs
│   │   │   ├── TurnManagerTests.cs
│   │   │   └── AgentSupervisorTests.cs
│   │   ├── Agents/
│   │   │   ├── HeadAgentTests.cs
│   │   │   ├── ModeratorAgentTests.cs
│   │   │   ├── PanelistAgentTests.cs
│   │   │   └── AgentFactoryTests.cs
│   │   ├── Tools/
│   │   │   ├── RoslynCodeAnalyzerToolTests.cs
│   │   │   └── ToolRegistryTests.cs
│   │   └── CopilotAgent.Panel.Tests.Unit.csproj
│   │
│   ├── CopilotAgent.Panel.Tests.Integration/
│   │   ├── FullDiscussionFlowTests.cs
│   │   ├── PersistenceTests.cs
│   │   ├── McpIntegrationTests.cs
│   │   └── CopilotAgent.Panel.Tests.Integration.csproj
│   │
│   └── CopilotAgent.Panel.Tests.UI/
│       ├── ChatPanelTests.cs
│       ├── ControlToolbarTests.cs
│       └── CopilotAgent.Panel.Tests.UI.csproj
│
└── docs/
    ├── architecture.md
    ├── state-machine.md
    ├── agent-prompts.md
    └── user-guide.md
4.1 Dependency Graph
text

Domain ──────────────────── (zero dependencies)
   ▲
   │
Application ─────────────── depends on: Domain, Stateless, System.Reactive
   ▲
   │
Agents ──────────────────── depends on: Application, Domain, SemanticKernel
   ▲
   │
Tools ───────────────────── depends on: Application, Domain, Playwright, Roslyn
   ▲
   │
Infrastructure ──────────── depends on: Application, Domain, LiteDB, SK Connectors, MCP, Polly, Serilog
   ▲
   │
UI (Composition Root) ───── depends on: Application, Domain + registers Agents, Tools, Infrastructure
5. Domain Model
5.1 Entities
csharp

// === Discussion.cs ===
namespace CopilotAgent.Panel.Domain.Entities;

public sealed class Discussion
{
    public DiscussionId Id { get; }
    public DiscussionState CurrentState { get; private set; }
    public string OriginalUserPrompt { get; }
    public string? RefinedTopicOfDiscussion { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int MaxPanelists { get; }
    public VerbosityLevel Verbosity { get; }

    private readonly List<Message> _messages = [];
    public IReadOnlyList<Message> Messages => _messages.AsReadOnly();

    private readonly List<AgentInstance> _agents = [];
    public IReadOnlyList<AgentInstance> Agents => _agents.AsReadOnly();

    public Discussion(
        DiscussionId id,
        string userPrompt,
        int maxPanelists,
        VerbosityLevel verbosity)
    {
        Id = id;
        OriginalUserPrompt = userPrompt ?? throw new ArgumentNullException(nameof(userPrompt));
        MaxPanelists = maxPanelists > 0
            ? maxPanelists
            : throw new ArgumentOutOfRangeException(nameof(maxPanelists));
        Verbosity = verbosity;
        CurrentState = DiscussionState.Idle;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void TransitionTo(DiscussionState newState)
    {
        CurrentState = newState;
        if (newState is DiscussionState.Completed or DiscussionState.Cancelled)
            CompletedAt = DateTimeOffset.UtcNow;
    }

    public void SetRefinedTopic(string topic) =>
        RefinedTopicOfDiscussion = topic ?? throw new ArgumentNullException(nameof(topic));

    public void AddMessage(Message message) => _messages.Add(message);
    public void RegisterAgent(AgentInstance agent) => _agents.Add(agent);
    public void UnregisterAgent(AgentInstance agent) => _agents.Remove(agent);
}
csharp

// === AgentInstance.cs ===
namespace CopilotAgent.Panel.Domain.Entities;

public sealed class AgentInstance
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public AgentRole Role { get; }
    public ModelIdentifier Model { get; }
    public AgentStatus Status { get; private set; }
    public int TurnsCompleted { get; private set; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    public AgentInstance(string name, AgentRole role, ModelIdentifier model)
    {
        Name = name;
        Role = role;
        Model = model;
        Status = AgentStatus.Created;
    }

    public void Activate() => Status = AgentStatus.Active;
    public void SetThinking() => Status = AgentStatus.Thinking;
    public void SetIdle() => Status = AgentStatus.Idle;
    public void IncrementTurn() => TurnsCompleted++;
    public void Dispose() => Status = AgentStatus.Disposed;
}
csharp

// === Message.cs ===
namespace CopilotAgent.Panel.Domain.Entities;

public sealed record Message(
    Guid Id,
    DiscussionId DiscussionId,
    Guid AuthorAgentId,
    string AuthorName,
    AgentRole AuthorRole,
    string Content,
    string? MarkdownHtml,
    MessageType Type,
    Guid? InReplyTo,
    IReadOnlyList<ToolCallRecord>? ToolCalls,
    DateTimeOffset Timestamp)
{
    public static Message Create(
        DiscussionId discussionId,
        Guid authorId,
        string authorName,
        AgentRole role,
        string content,
        MessageType type,
        Guid? inReplyTo = null,
        IReadOnlyList<ToolCallRecord>? toolCalls = null)
    {
        return new Message(
            Guid.NewGuid(),
            discussionId,
            authorId,
            authorName,
            role,
            content,
            null, // HTML rendered later
            type,
            inReplyTo,
            toolCalls,
            DateTimeOffset.UtcNow);
    }
}
5.2 Value Objects
csharp

namespace CopilotAgent.Panel.Domain.ValueObjects;

public readonly record struct DiscussionId(Guid Value)
{
    public static DiscussionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N")[..8]; // short display
}

public readonly record struct ModelIdentifier(string Provider, string ModelName)
{
    // e.g. ("AzureOpenAI", "gpt-4o"), ("Anthropic", "claude-3-sonnet")
    public override string ToString() => $"{Provider}/{ModelName}";
}

public readonly record struct TurnNumber(int Value)
{
    public TurnNumber Increment() => new(Value + 1);
    public bool Exceeds(int max) => Value >= max;
}

public readonly record struct TokenBudget(int MaxTokensPerTurn, int MaxTotalTokens)
{
    public bool IsExceeded(int currentTurnTokens, int totalTokens) =>
        currentTurnTokens > MaxTokensPerTurn || totalTokens > MaxTotalTokens;
}
5.3 Enums
csharp

namespace CopilotAgent.Panel.Domain.Enums;

public enum DiscussionState
{
    Idle,
    GatheringClarifications,
    AwaitingUserApproval,
    Initializing,        // Head is spinning up panelists
    Running,
    Paused,
    Converging,          // Moderator detected convergence, wrapping up
    Synthesizing,        // Head is aggregating
    Completed,
    Cancelled
}

public enum DiscussionTrigger
{
    UserSubmitted,
    ClarificationsComplete,
    UserApproved,
    PanelistsReady,
    TurnCompleted,
    ConvergenceDetected,
    SynthesisComplete,
    UserPaused,
    UserResumed,
    UserCancelled,
    Timeout,
    Error,
    Reset
}

public enum AgentRole { Head, Moderator, Panelist, User }

public enum AgentStatus { Created, Active, Thinking, Idle, Disposed }

public enum VerbosityLevel { Brief, Detailed, FullReasoning }

public enum MessageType
{
    UserMessage,
    Clarification,
    TopicOfDiscussion,
    PanelistArgument,
    ModerationNote,
    ToolCallResult,
    Commentary,          // internal reasoning
    Synthesis,
    SystemNotification,
    Error
}
5.4 Events
csharp

namespace CopilotAgent.Panel.Domain.Events;

public abstract record DiscussionEvent(
    DiscussionId DiscussionId,
    DateTimeOffset Timestamp);

public sealed record StateChangedEvent(
    DiscussionId DiscussionId,
    DiscussionState OldState,
    DiscussionState NewState,
    DateTimeOffset Timestamp) : DiscussionEvent(DiscussionId, Timestamp);

public sealed record AgentMessageEvent(
    DiscussionId DiscussionId,
    Message Message,
    DateTimeOffset Timestamp) : DiscussionEvent(DiscussionId, Timestamp);

public sealed record AgentStatusChangedEvent(
    DiscussionId DiscussionId,
    Guid AgentId,
    string AgentName,
    AgentRole Role,
    AgentStatus NewStatus,
    DateTimeOffset Timestamp) : DiscussionEvent(DiscussionId, Timestamp);

public sealed record ToolCallEvent(
    DiscussionId DiscussionId,
    Guid AgentId,
    string ToolName,
    string Input,
    string? Output,
    bool Succeeded,
    TimeSpan Duration,
    DateTimeOffset Timestamp) : DiscussionEvent(DiscussionId, Timestamp);

public sealed record ModerationEvent(
    DiscussionId DiscussionId,
    string Action,       // "Redirected", "Blocked", "ConvergenceCalled"
    string Reason,
    DateTimeOffset Timestamp) : DiscussionEvent(DiscussionId, Timestamp);

public sealed record CommentaryEvent(
    DiscussionId DiscussionId,
    Guid AgentId,
    string AgentName,
    AgentRole Role,
    string Commentary,   // internal reasoning text
    VerbosityLevel MinimumLevel,
    DateTimeOffset Timestamp) : DiscussionEvent(DiscussionId, Timestamp);

public sealed record ProgressEvent(
    DiscussionId DiscussionId,
    int CompletedTurns,
    int EstimatedTotalTurns,
    int ActivePanelists,
    int DonePanelists,
    DateTimeOffset Timestamp) : DiscussionEvent(DiscussionId, Timestamp);

public sealed record ErrorEvent(
    DiscussionId DiscussionId,
    string Source,
    string ErrorMessage,
    Exception? Exception,
    DateTimeOffset Timestamp) : DiscussionEvent(DiscussionId, Timestamp);
5.5 Core Interfaces
csharp

namespace CopilotAgent.Panel.Domain.Interfaces;

public interface IAgent : IAsyncDisposable
{
    Guid Id { get; }
    string Name { get; }
    AgentRole Role { get; }
    ModelIdentifier Model { get; }
    AgentStatus Status { get; }

    Task<AgentResponse> ProcessAsync(
        AgentRequest request,
        CancellationToken ct = default);

    Task PauseAsync();
    Task ResumeAsync();
}

public record AgentRequest(
    DiscussionId DiscussionId,
    IReadOnlyList<Message> ConversationHistory,
    string SystemPrompt,
    TurnNumber CurrentTurn,
    CancellationToken CancellationToken);

public record AgentResponse(
    Message Message,
    IReadOnlyList<ToolCallRecord>? ToolCalls,
    bool RequestsMoreTurns,
    string? InternalReasoning);

public interface IAgentFactory
{
    IAgent CreateHead(DiscussionId discussionId, ModelIdentifier model);
    IAgent CreateModerator(DiscussionId discussionId, ModelIdentifier model, GuardRailPolicy policy);
    IAgent CreatePanelist(DiscussionId discussionId, string name, ModelIdentifier model);
}

public interface ITool
{
    string Name { get; }
    string Description { get; }
    Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default);
}

public record ToolResult(bool Success, string Output, TimeSpan Duration);

public interface IToolRegistry
{
    void Register(ITool tool);
    ITool? Get(string name);
    IReadOnlyList<ITool> GetAll();
}

public interface IEventBus
{
    IObservable<T> Observe<T>() where T : DiscussionEvent;
    void Publish<T>(T @event) where T : DiscussionEvent;
}

public interface ISessionStore
{
    Task SaveAsync(Discussion discussion, CancellationToken ct = default);
    Task<Discussion?> LoadAsync(DiscussionId id, CancellationToken ct = default);
    Task<IReadOnlyList<Discussion>> ListRecentAsync(int count, CancellationToken ct = default);
    Task DeleteAsync(DiscussionId id, CancellationToken ct = default);
}

public interface ISettingsProvider
{
    PanelSettings Load();
    void Save(PanelSettings settings);
}

public interface IDiscussionOrchestrator
{
    DiscussionId? ActiveDiscussionId { get; }
    DiscussionState CurrentState { get; }

    Task<DiscussionId> StartAsync(string userPrompt, CancellationToken ct = default);
    Task SendUserMessageAsync(string message, CancellationToken ct = default);
    Task ApproveAndProceedAsync(CancellationToken ct = default);
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
    Task ResetAsync();

    IObservable<DiscussionEvent> Events { get; }
}
5.6 Guard‑Rail Policy
csharp

namespace CopilotAgent.Panel.Domain.Policies;

public sealed class GuardRailPolicy
{
    public int MaxTurnsPerDiscussion { get; init; } = 30;
    public int MaxTokensPerTurn { get; init; } = 4000;
    public int MaxTotalTokens { get; init; } = 100_000;
    public int MaxToolCallsPerTurn { get; init; } = 5;
    public int MaxToolCallsPerDiscussion { get; init; } = 50;
    public TimeSpan MaxDiscussionDuration { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan MaxSingleTurnDuration { get; init; } = TimeSpan.FromMinutes(3);
    public IReadOnlyList<string> ProhibitedContentPatterns { get; init; } = [];
    public IReadOnlyList<string> AllowedDomains { get; init; } = [];  // for web crawling
    public bool AllowFileSystemAccess { get; init; } = true;
    public IReadOnlyList<string> AllowedFilePaths { get; init; } = [];
}
6. State Machine Design
6.1 State Diagram
text

                                    ┌─────────┐
                                    │  Idle    │◄──────────────────────────────────┐
                                    └────┬─────┘                                   │
                                         │ UserSubmitted                            │
                                         ▼                                         │
                              ┌─────────────────────┐                              │
                              │ GatheringClarific.   │ ◄──┐                        │
                              └──────────┬──────────┘    │                        │
                                         │               │ (more questions)        │
                                         │ ClarificationsComplete                  │
                                         ▼                                         │
                              ┌─────────────────────┐                              │
                              │ AwaitingUserApproval │                              │
                              └──────────┬──────────┘                              │
                                         │ UserApproved                            │
                                         ▼                                         │
                              ┌─────────────────────┐                              │
                              │   Initializing       │ (Head spins up panelists)   │
                              └──────────┬──────────┘                              │
                                         │ PanelistsReady                          │
                                         ▼                                         │
                    ┌──────── ┌─────────────────────┐ ──────────┐                  │
                    │         │     Running          │           │                  │
     UserPaused     │         └──────────┬──────────┘           │ UserCancelled    │
                    │                    │                       │ / Timeout        │
                    ▼                    │ ConvergenceDetected   │ / Error          │
              ┌───────────┐              ▼                       │                  │
              │  Paused   │   ┌─────────────────────┐           ▼                  │
              └─────┬─────┘   │    Converging        │   ┌──────────────┐          │
                    │         └──────────┬──────────┘   │  Cancelled   │          │
       UserResumed  │                    │               └──────────────┘          │
                    │                    │ (all panelists                           │
                    └──────►  Running    │  final position)                        │
                                         ▼                                         │
                              ┌─────────────────────┐                              │
                              │   Synthesizing       │ (Head aggregates)           │
                              └──────────┬──────────┘                              │
                                         │ SynthesisComplete                       │
                                         ▼                                         │
                              ┌─────────────────────┐      Reset                   │
                              │    Completed         │─────────────────────────────►│
                              └─────────────────────┘                              │
                                                                                   │
      Any state ──── UserCancelled / Error ───► Cancelled ──── Reset ──────────────┘
6.2 Implementation
csharp

namespace CopilotAgent.Panel.Application.StateMachine;

public sealed class DiscussionStateMachine
{
    private readonly StateMachine<DiscussionState, DiscussionTrigger> _machine;
    private readonly Discussion _discussion;
    private readonly IEventBus _eventBus;
    private readonly ILogger<DiscussionStateMachine> _logger;

    // Parameterized triggers
    private readonly StateMachine<DiscussionState, DiscussionTrigger>
        .TriggerWithParameters<string> _errorTrigger;

    public DiscussionState CurrentState => _machine.State;
    public bool CanFire(DiscussionTrigger trigger) => _machine.CanFire(trigger);

    public DiscussionStateMachine(
        Discussion discussion,
        IEventBus eventBus,
        ILogger<DiscussionStateMachine> logger)
    {
        _discussion = discussion;
        _eventBus = eventBus;
        _logger = logger;

        _machine = new StateMachine<DiscussionState, DiscussionTrigger>(
            () => _discussion.CurrentState,
            s => _discussion.TransitionTo(s));

        _errorTrigger = _machine.SetTriggerParameters<string>(DiscussionTrigger.Error);

        ConfigureStates();
    }

    private void ConfigureStates()
    {
        // ─── IDLE ───
        _machine.Configure(DiscussionState.Idle)
            .Permit(DiscussionTrigger.UserSubmitted, DiscussionState.GatheringClarifications)
            .OnEntry(() => PublishStateChange(DiscussionState.Idle));

        // ─── GATHERING CLARIFICATIONS ───
        _machine.Configure(DiscussionState.GatheringClarifications)
            .Permit(DiscussionTrigger.ClarificationsComplete, DiscussionState.AwaitingUserApproval)
            .Permit(DiscussionTrigger.UserCancelled, DiscussionState.Cancelled)
            .PermitReentry(DiscussionTrigger.TurnCompleted)  // more clarification Q&A
            .OnEntry(() => PublishStateChange(DiscussionState.GatheringClarifications));

        // ─── AWAITING USER APPROVAL ───
        _machine.Configure(DiscussionState.AwaitingUserApproval)
            .Permit(DiscussionTrigger.UserApproved, DiscussionState.Initializing)
            .Permit(DiscussionTrigger.UserCancelled, DiscussionState.Cancelled)
            .OnEntry(() => PublishStateChange(DiscussionState.AwaitingUserApproval));

        // ─── INITIALIZING ───
        _machine.Configure(DiscussionState.Initializing)
            .Permit(DiscussionTrigger.PanelistsReady, DiscussionState.Running)
            .Permit(DiscussionTrigger.Error, DiscussionState.Cancelled)
            .Permit(DiscussionTrigger.UserCancelled, DiscussionState.Cancelled)
            .OnEntry(() => PublishStateChange(DiscussionState.Initializing));

        // ─── RUNNING ───
        _machine.Configure(DiscussionState.Running)
            .Permit(DiscussionTrigger.ConvergenceDetected, DiscussionState.Converging)
            .Permit(DiscussionTrigger.UserPaused, DiscussionState.Paused)
            .Permit(DiscussionTrigger.UserCancelled, DiscussionState.Cancelled)
            .Permit(DiscussionTrigger.Timeout, DiscussionState.Converging) // force converge
            .Permit(DiscussionTrigger.Error, DiscussionState.Cancelled)
            .PermitReentry(DiscussionTrigger.TurnCompleted) // normal turn cycle
            .OnEntry(() => PublishStateChange(DiscussionState.Running));

        // ─── PAUSED ───
        _machine.Configure(DiscussionState.Paused)
            .Permit(DiscussionTrigger.UserResumed, DiscussionState.Running)
            .Permit(DiscussionTrigger.UserCancelled, DiscussionState.Cancelled)
            .OnEntry(() => PublishStateChange(DiscussionState.Paused));

        // ─── CONVERGING ───
        _machine.Configure(DiscussionState.Converging)
            .Permit(DiscussionTrigger.TurnCompleted, DiscussionState.Synthesizing)
            .Permit(DiscussionTrigger.UserCancelled, DiscussionState.Cancelled)
            .Permit(DiscussionTrigger.Error, DiscussionState.Cancelled)
            .OnEntry(() => PublishStateChange(DiscussionState.Converging));

        // ─── SYNTHESIZING ───
        _machine.Configure(DiscussionState.Synthesizing)
            .Permit(DiscussionTrigger.SynthesisComplete, DiscussionState.Completed)
            .Permit(DiscussionTrigger.Error, DiscussionState.Cancelled)
            .Permit(DiscussionTrigger.UserCancelled, DiscussionState.Cancelled)
            .OnEntry(() => PublishStateChange(DiscussionState.Synthesizing));

        // ─── COMPLETED ───
        _machine.Configure(DiscussionState.Completed)
            .Permit(DiscussionTrigger.UserSubmitted, DiscussionState.GatheringClarifications) // follow-up
            .Permit(DiscussionTrigger.Reset, DiscussionState.Idle)
            .OnEntry(() => PublishStateChange(DiscussionState.Completed));

        // ─── CANCELLED ───
        _machine.Configure(DiscussionState.Cancelled)
            .Permit(DiscussionTrigger.Reset, DiscussionState.Idle)
            .OnEntry(() => PublishStateChange(DiscussionState.Cancelled));

        // Global unhandled trigger handler
        _machine.OnUnhandledTrigger((state, trigger) =>
        {
            _logger.LogWarning(
                "Unhandled trigger {Trigger} in state {State} for discussion {Id}",
                trigger, state, _discussion.Id);
        });
    }

    public async Task FireAsync(DiscussionTrigger trigger)
    {
        _logger.LogInformation(
            "Discussion {Id}: {OldState} ──[{Trigger}]──► ...",
            _discussion.Id, _machine.State, trigger);

        await _machine.FireAsync(trigger);
    }

    public async Task FireErrorAsync(string reason)
    {
        _logger.LogError("Discussion {Id}: Error trigger: {Reason}", _discussion.Id, reason);
        await _machine.FireAsync(_errorTrigger, reason);
    }

    private void PublishStateChange(DiscussionState newState)
    {
        _eventBus.Publish(new StateChangedEvent(
            _discussion.Id,
            _machine.State,
            newState,
            DateTimeOffset.UtcNow));
    }

    // Serializable snapshot for persistence
    public string ToDotGraph() => UmlDotGraph.Format(_machine.GetInfo());
}
7. Agent System Architecture
7.1 AgentBase — Common Foundation
csharp

namespace CopilotAgent.Panel.Agents.Shared;

public abstract class AgentBase : IAgent
{
    public Guid Id { get; } = Guid.NewGuid();
    public abstract string Name { get; }
    public abstract AgentRole Role { get; }
    public ModelIdentifier Model { get; }
    public AgentStatus Status { get; private set; } = AgentStatus.Created;

    protected readonly Kernel Kernel;  // Semantic Kernel
    protected readonly IEventBus EventBus;
    protected readonly IToolRegistry ToolRegistry;
    protected readonly ILogger Logger;
    protected ChatHistory ChatHistory = [];
    protected readonly CancellationTokenSource InternalCts = new();
    private bool _paused;

    protected AgentBase(
        ModelIdentifier model,
        Kernel kernel,
        IEventBus eventBus,
        IToolRegistry toolRegistry,
        ILogger logger)
    {
        Model = model;
        Kernel = kernel;
        EventBus = eventBus;
        ToolRegistry = toolRegistry;
        Logger = logger;
    }

    public async Task<AgentResponse> ProcessAsync(AgentRequest request, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, InternalCts.Token);

        while (_paused)
        {
            await Task.Delay(500, linked.Token);
        }

        Status = AgentStatus.Thinking;
        PublishStatusChange();

        try
        {
            PublishCommentary($"Processing turn {request.CurrentTurn.Value}...");
            var response = await ProcessCoreAsync(request, linked.Token);
            Status = AgentStatus.Idle;
            PublishStatusChange();
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Agent {Name} error during processing", Name);
            Status = AgentStatus.Idle;
            PublishStatusChange();
            throw;
        }
    }

    protected abstract Task<AgentResponse> ProcessCoreAsync(
        AgentRequest request, CancellationToken ct);

    public Task PauseAsync()
    {
        _paused = true;
        PublishCommentary("Paused.");
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        _paused = false;
        PublishCommentary("Resumed.");
        return Task.CompletedTask;
    }

    protected void PublishCommentary(string text)
    {
        EventBus.Publish(new CommentaryEvent(
            default, Id, Name, Role, text,
            VerbosityLevel.Detailed,
            DateTimeOffset.UtcNow));
    }

    protected void PublishStatusChange()
    {
        EventBus.Publish(new AgentStatusChangedEvent(
            default, Id, Name, Role, Status,
            DateTimeOffset.UtcNow));
    }

    public async ValueTask DisposeAsync()
    {
        Status = AgentStatus.Disposed;
        PublishStatusChange();
        await InternalCts.CancelAsync();
        InternalCts.Dispose();
        ChatHistory.Clear();
        Logger.LogInformation("Agent {Name} ({Role}) disposed", Name, Role);
    }
}
7.2 HeadAgent
csharp

namespace CopilotAgent.Panel.Agents.Head;

public sealed class HeadAgent : AgentBase
{
    public override string Name => "Head";
    public override AgentRole Role => AgentRole.Head;

    private readonly IChatCompletionService _chatService;
    private SynthesisResult? _lastSynthesis;

    public HeadAgent(
        ModelIdentifier model,
        Kernel kernel,
        IEventBus eventBus,
        IToolRegistry toolRegistry,
        ILogger<HeadAgent> logger)
        : base(model, kernel, eventBus, toolRegistry, logger)
    {
        _chatService = kernel.GetRequiredService<IChatCompletionService>();

        ChatHistory.AddSystemMessage(HeadPrompts.SystemPrompt);
    }

    protected override async Task<AgentResponse> ProcessCoreAsync(
        AgentRequest request, CancellationToken ct)
    {
        // Build context from conversation history
        foreach (var msg in request.ConversationHistory)
        {
            ChatHistory.AddMessage(
                msg.AuthorRole == AgentRole.User
                    ? AuthorRole.User
                    : AuthorRole.Assistant,
                msg.Content);
        }

        var result = await _chatService.GetChatMessageContentAsync(
            ChatHistory,
            executionSettings: new OpenAIPromptExecutionSettings
            {
                MaxTokens = 4000,
                Temperature = 0.3,
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            },
            kernel: Kernel,
            cancellationToken: ct);

        var responseText = result.Content ?? string.Empty;

        var message = Message.Create(
            request.DiscussionId,
            Id, Name, Role,
            responseText,
            MessageType.Clarification);

        return new AgentResponse(message, null, RequestsMoreTurns: false, responseText);
    }

    // ─── Clarification Phase ───
    public async Task<string> ElaborateAndClarifyAsync(
        string userPrompt, CancellationToken ct)
    {
        PublishCommentary("Analyzing user request to identify ambiguities...");

        ChatHistory.AddUserMessage(userPrompt);
        ChatHistory.AddSystemMessage(HeadPrompts.ClarificationInstruction);

        var result = await _chatService.GetChatMessageContentAsync(
            ChatHistory, cancellationToken: ct);

        PublishCommentary($"Generated clarification questions.");
        return result.Content ?? "No further clarification needed.";
    }

    // ─── Build Topic of Discussion ───
    public async Task<string> BuildTopicOfDiscussionAsync(
        IReadOnlyList<Message> clarificationExchange, CancellationToken ct)
    {
        PublishCommentary("Synthesizing all clarifications into a comprehensive discussion prompt...");

        var prompt = HeadPrompts.BuildTopicPrompt(clarificationExchange);
        ChatHistory.AddUserMessage(prompt);

        var result = await _chatService.GetChatMessageContentAsync(
            ChatHistory, cancellationToken: ct);

        var topic = result.Content ?? string.Empty;
        PublishCommentary($"Topic of Discussion ready ({topic.Length} chars).");
        return topic;
    }

    // ─── Synthesis Phase ───
    public async Task<SynthesisResult> SynthesizeAsync(
        IReadOnlyList<Message> panelMessages,
        CancellationToken ct)
    {
        PublishCommentary(
            $"Synthesizing {panelMessages.Count} messages from panel discussion...");

        var synthesisPrompt = HeadPrompts.BuildSynthesisPrompt(panelMessages);
        ChatHistory.AddUserMessage(synthesisPrompt);

        var result = await _chatService.GetChatMessageContentAsync(
            ChatHistory,
            executionSettings: new OpenAIPromptExecutionSettings
            {
                MaxTokens = 8000,
                Temperature = 0.2
            },
            cancellationToken: ct);

        _lastSynthesis = new SynthesisResult(
            result.Content ?? string.Empty,
            panelMessages.Count,
            DateTimeOffset.UtcNow);

        PublishCommentary("Synthesis complete.");
        return _lastSynthesis;
    }

    // ─── Post‑Discussion Q&A ───
    public async Task<string> AnswerFollowUpAsync(
        string userQuestion, CancellationToken ct)
    {
        PublishCommentary($"Answering follow-up: \"{userQuestion[..Math.Min(50, userQuestion.Length)]}...\"");

        ChatHistory.AddUserMessage(
            $"The user has a follow-up question about the completed discussion:\n\n{userQuestion}");

        var result = await _chatService.GetChatMessageContentAsync(
            ChatHistory, cancellationToken: ct);

        return result.Content ?? string.Empty;
    }

    // ─── Meta‑Questions ───
    public string EstimateTimeRemaining(int currentTurn, int maxTurns, int panelistCount)
    {
        var remainingTurns = maxTurns - currentTurn;
        var estimatedSecondsPerTurn = 15 * panelistCount; // rough estimate
        var remaining = TimeSpan.FromSeconds(remainingTurns * estimatedSecondsPerTurn);
        return $"Estimated time remaining: ~{remaining.TotalMinutes:F0} minutes " +
               $"({remainingTurns} turns × {panelistCount} panelists).";
    }
}
7.3 HeadPrompts (Prompt Templates)
csharp

namespace CopilotAgent.Panel.Agents.Head;

internal static class HeadPrompts
{
    public const string SystemPrompt = """
        You are the HEAD of a multi-agent panel discussion system.
        Your responsibilities:
        1. Understand the user's complex request thoroughly.
        2. Ask targeted clarification questions to eliminate ambiguity.
        3. Compose a comprehensive "Topic of Discussion" prompt for panelists.
        4. After the panel concludes, synthesize all findings into a comprehensive report.
        5. Remain available for follow-up questions with full context.

        Always be professional, precise, and thorough.
        Use markdown formatting in all responses.
        """;

    public const string ClarificationInstruction = """
        Based on the user's request above, generate 2-5 specific clarification questions
        that would help you create the most effective analysis prompt for a panel of experts.

        Focus on:
        - Scope boundaries (what's in/out of scope)
        - Priority areas
        - Specific concerns or known issues
        - Expected output format
        - Any constraints (time, budget, technology)

        If the request is already crystal clear, respond with:
        "CLEAR: No further clarification needed."

        Format each question on a new line with a number.
        """;

    public static string BuildTopicPrompt(IReadOnlyList<Message> exchange) => $"""
        Based on the original request and the following clarification exchange:

        {string.Join("\n\n", exchange.Select(m => $"**{m.AuthorName}**: {m.Content}"))}

        Compose a comprehensive "Topic of Discussion" that will be given to a panel of
        expert AI analysts. The topic should:
        1. State the exact analysis goal
        2. List specific areas to investigate
        3. Define success criteria
        4. Specify any constraints or boundaries
        5. Indicate what tools/data sources are available
        6. Define the expected output format

        Be thorough but concise. This prompt will guide the entire panel discussion.
        """;

    public static string BuildSynthesisPrompt(IReadOnlyList<Message> panelMessages) => $"""
        The panel discussion has concluded. Below are all contributions from the panelists:

        {string.Join("\n\n---\n\n", panelMessages.Select(m =>
            $"**{m.AuthorName}** (Turn {m.Timestamp:HH:mm:ss}):\n{m.Content}"))}

        Synthesize all findings into a comprehensive final report with:
        1. **Executive Summary** — Key findings in 3-5 bullet points
        2. **Detailed Analysis** — Organized by topic/area
        3. **Agreements** — Points all panelists agreed on
        4. **Disagreements** — Points of contention and different perspectives
        5. **Recommendations** — Concrete, actionable next steps
        6. **Risk Assessment** — Potential risks and mitigations
        7. **Appendix** — Raw data, tool outputs, or evidence cited

        Use rich markdown formatting. Be as comprehensive and elaborative as possible.
        """;
}
7.4 ModeratorAgent
csharp

namespace CopilotAgent.Panel.Agents.Moderator;

public sealed class ModeratorAgent : AgentBase
{
    public override string Name => "Moderator";
    public override AgentRole Role => AgentRole.Moderator;

    private readonly GuardRailPolicy _policy;
    private readonly ConvergenceDetector _convergenceDetector;
    private readonly IChatCompletionService _chatService;
    private int _totalTokensConsumed;
    private int _totalToolCalls;
    private readonly Stopwatch _discussionTimer = new();
    private readonly List<string> _violations = [];

    public ModeratorAgent(
        ModelIdentifier model,
        Kernel kernel,
        GuardRailPolicy policy,
        IEventBus eventBus,
        IToolRegistry toolRegistry,
        ILogger<ModeratorAgent> logger)
        : base(model, kernel, eventBus, toolRegistry, logger)
    {
        _policy = policy;
        _convergenceDetector = new ConvergenceDetector(kernel, logger);
        _chatService = kernel.GetRequiredService<IChatCompletionService>();

        ChatHistory.AddSystemMessage(ModeratorPrompts.SystemPrompt);
    }

    public void StartTimer() => _discussionTimer.Start();
    public void StopTimer() => _discussionTimer.Stop();

    // ─── Validate a single panelist message before it enters the shared board ───
    public async Task<ModerationResult> ValidateMessageAsync(
        Message message, TurnNumber turn, CancellationToken ct)
    {
        // 1. Turn limit
        if (turn.Exceeds(_policy.MaxTurnsPerDiscussion))
        {
            RecordViolation("MaxTurnsExceeded");
            return ModerationResult.ForceConverge(
                $"Maximum turns ({_policy.MaxTurnsPerDiscussion}) reached.");
        }

        // 2. Time limit
        if (_discussionTimer.Elapsed > _policy.MaxDiscussionDuration)
        {
            RecordViolation("TimeoutExceeded");
            return ModerationResult.ForceConverge(
                $"Maximum duration ({_policy.MaxDiscussionDuration.TotalMinutes:F0} min) exceeded.");
        }

        // 3. Prohibited content
        foreach (var pattern in _policy.ProhibitedContentPatterns)
        {
            if (Regex.IsMatch(message.Content, pattern, RegexOptions.IgnoreCase))
            {
                RecordViolation($"ProhibitedContent: {pattern}");
                return ModerationResult.Block(
                    $"Message blocked: contains prohibited content.");
            }
        }

        // 4. Token budget
        var messageTokens = EstimateTokens(message.Content);
        _totalTokensConsumed += messageTokens;
        if (messageTokens > _policy.MaxTokensPerTurn)
        {
            RecordViolation("TokensPerTurnExceeded");
            return ModerationResult.Redirect(
                "Your response exceeded the token limit. Please be more concise.");
        }
        if (_totalTokensConsumed > _policy.MaxTotalTokens)
        {
            RecordViolation("TotalTokensExceeded");
            return ModerationResult.ForceConverge("Total token budget exhausted.");
        }

        // 5. Tool call limits
        if (message.ToolCalls is { Count: > 0 })
        {
            _totalToolCalls += message.ToolCalls.Count;
            if (message.ToolCalls.Count > _policy.MaxToolCallsPerTurn)
                return ModerationResult.Redirect("Too many tool calls in one turn.");
            if (_totalToolCalls > _policy.MaxToolCallsPerDiscussion)
                return ModerationResult.ForceConverge("Total tool call budget exhausted.");
        }

        // 6. Convergence detection (AI‑based)
        if (turn.Value > 5 && turn.Value % 3 == 0) // check every 3 turns after turn 5
        {
            var converged = await _convergenceDetector.CheckAsync(
                message, ChatHistory, ct);
            if (converged)
            {
                PublishCommentary("Convergence detected — panelists are aligning on conclusions.");
                EventBus.Publish(new ModerationEvent(
                    default, "ConvergenceDetected",
                    "Panelists have converged on key points.",
                    DateTimeOffset.UtcNow));
                return ModerationResult.ConvergenceDetected();
            }
        }

        PublishCommentary($"Message from {message.AuthorName} passed moderation (turn {turn.Value}).");
        return ModerationResult.Approved();
    }

    // ─── Generate a redirect prompt to keep discussion focused ───
    public async Task<string> GenerateRedirectAsync(
        IReadOnlyList<Message> recentMessages, string issue, CancellationToken ct)
    {
        var prompt = ModeratorPrompts.BuildRedirectPrompt(recentMessages, issue);
        ChatHistory.AddUserMessage(prompt);
        var result = await _chatService.GetChatMessageContentAsync(
            ChatHistory, cancellationToken: ct);
        return result.Content ?? "Please refocus on the topic.";
    }

    private void RecordViolation(string violation)
    {
        _violations.Add($"[{DateTimeOffset.UtcNow:HH:mm:ss}] {violation}");
        Logger.LogWarning("Guard-rail violation: {Violation}", violation);
        EventBus.Publish(new ModerationEvent(
            default, "Violation", violation, DateTimeOffset.UtcNow));
    }

    private static int EstimateTokens(string text) =>
        (int)(text.Length / 3.5); // rough approximation
}

public sealed record ModerationResult(
    ModerationAction Action,
    string? Reason)
{
    public static ModerationResult Approved() => new(ModerationAction.Approved, null);
    public static ModerationResult Block(string reason) => new(ModerationAction.Blocked, reason);
    public static ModerationResult Redirect(string reason) => new(ModerationAction.Redirect, reason);
    public static ModerationResult ForceConverge(string reason) => new(ModerationAction.ForceConverge, reason);
    public static ModerationResult ConvergenceDetected() => new(ModerationAction.ConvergenceDetected, null);
}

public enum ModerationAction { Approved, Blocked, Redirect, ForceConverge, ConvergenceDetected }
7.5 PanelistAgent
csharp

namespace CopilotAgent.Panel.Agents.Panelist;

public sealed class PanelistAgent : AgentBase
{
    public override string Name { get; }
    public override AgentRole Role => AgentRole.Panelist;

    private readonly IChatCompletionService _chatService;

    public PanelistAgent(
        string name,
        ModelIdentifier model,
        Kernel kernel,
        IEventBus eventBus,
        IToolRegistry toolRegistry,
        ILogger<PanelistAgent> logger)
        : base(model, kernel, eventBus, toolRegistry, logger)
    {
        Name = name;
        _chatService = kernel.GetRequiredService<IChatCompletionService>();

        ChatHistory.AddSystemMessage(PanelistPrompts.BuildSystemPrompt(name));
    }

    protected override async Task<AgentResponse> ProcessCoreAsync(
        AgentRequest request, CancellationToken ct)
    {
        // Inject the shared discussion board into this agent's context
        foreach (var msg in request.ConversationHistory.TakeLast(20)) // sliding window
        {
            var role = msg.AuthorAgentId == Id ? AuthorRole.Assistant : AuthorRole.User;
            ChatHistory.AddMessage(role, $"[{msg.AuthorName}]: {msg.Content}");
        }

        // Register all tools as SK functions
        RegisterToolsAsKernelFunctions();

        PublishCommentary($"Analyzing topic... (turn {request.CurrentTurn.Value})");

        var result = await _chatService.GetChatMessageContentAsync(
            ChatHistory,
            executionSettings: new OpenAIPromptExecutionSettings
            {
                MaxTokens = 3000,
                Temperature = 0.7, // slightly creative for diverse perspectives
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            },
            kernel: Kernel,
            cancellationToken: ct);

        var content = result.Content ?? string.Empty;

        // Extract tool calls from metadata
        var toolCalls = ExtractToolCalls(result);

        var message = Message.Create(
            request.DiscussionId,
            Id, Name, Role,
            content,
            MessageType.PanelistArgument,
            toolCalls: toolCalls);

        PublishCommentary($"Completed turn. Response: {content.Length} chars, " +
                         $"{toolCalls?.Count ?? 0} tool calls.");

        return new AgentResponse(
            message,
            toolCalls,
            RequestsMoreTurns: content.Contains("[MORE_ANALYSIS_NEEDED]"),
            InternalReasoning: content);
    }

    private void RegisterToolsAsKernelFunctions()
    {
        foreach (var tool in ToolRegistry.GetAll())
        {
            if (Kernel.Plugins.Any(p => p.Name == tool.Name)) continue;

            var function = KernelFunctionFactory.CreateFromMethod(
                async (string input) =>
                {
                    PublishCommentary($"Calling tool: {tool.Name}...");

                    var sw = Stopwatch.StartNew();
                    var result = await tool.ExecuteAsync(input, InternalCts.Token);
                    sw.Stop();

                    EventBus.Publish(new ToolCallEvent(
                        default, Id, tool.Name, input, result.Output,
                        result.Success, sw.Elapsed, DateTimeOffset.UtcNow));

                    return result.Output;
                },
                tool.Name,
                tool.Description);

            Kernel.Plugins.AddFromFunctions(tool.Name, [function]);
        }
    }

    private static IReadOnlyList<ToolCallRecord>? ExtractToolCalls(
        ChatMessageContent result)
    {
        // Extract from SK metadata if available
        if (result.Metadata?.TryGetValue("ToolCalls", out var calls) == true
            && calls is List<ToolCallRecord> records)
        {
            return records;
        }
        return null;
    }
}
7.6 ConvergenceDetector
csharp

namespace CopilotAgent.Panel.Agents.Moderator;

public sealed class ConvergenceDetector
{
    private readonly Kernel _kernel;
    private readonly ILogger _logger;

    public ConvergenceDetector(Kernel kernel, ILogger logger)
    {
        _kernel = kernel;
        _logger = logger;
    }

    public async Task<bool> CheckAsync(
        Message latestMessage,
        ChatHistory moderatorHistory,
        CancellationToken ct)
    {
        var prompt = """
            Based on the recent discussion messages, determine if the panelists have
            substantially converged on their conclusions. Convergence means:
            1. Key findings are consistent across panelists
            2. No major new points are being raised
            3. Disagreements have been addressed or acknowledged

            Respond with ONLY "CONVERGED" or "NOT_CONVERGED" followed by a brief reason.
            """;

        moderatorHistory.AddUserMessage(
            $"Latest message from {latestMessage.AuthorName}:\n{latestMessage.Content}\n\n{prompt}");

        var chatService = _kernel.GetRequiredService<IChatCompletionService>();
        var result = await chatService.GetChatMessageContentAsync(
            moderatorHistory,
            executionSettings: new OpenAIPromptExecutionSettings
            {
                MaxTokens = 100,
                Temperature = 0.0
            },
            cancellationToken: ct);

        var response = result.Content?.Trim() ?? string.Empty;
        var converged = response.StartsWith("CONVERGED", StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation("Convergence check: {Result}", response);
        return converged;
    }
}
7.7 AgentFactory
csharp

namespace CopilotAgent.Panel.Agents.Factory;

public sealed class AgentFactory : IAgentFactory
{
    private readonly IServiceProvider _sp;
    private readonly IEventBus _eventBus;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Random _random = new();

    public AgentFactory(
        IServiceProvider sp,
        IEventBus eventBus,
        IToolRegistry toolRegistry,
        ILoggerFactory loggerFactory)
    {
        _sp = sp;
        _eventBus = eventBus;
        _toolRegistry = toolRegistry;
        _loggerFactory = loggerFactory;
    }

    public IAgent CreateHead(DiscussionId discussionId, ModelIdentifier model)
    {
        var kernel = BuildKernel(model);
        return new HeadAgent(
            model, kernel, _eventBus, _toolRegistry,
            _loggerFactory.CreateLogger<HeadAgent>());
    }

    public IAgent CreateModerator(
        DiscussionId discussionId, ModelIdentifier model, GuardRailPolicy policy)
    {
        var kernel = BuildKernel(model);
        return new ModeratorAgent(
            model, kernel, policy, _eventBus, _toolRegistry,
            _loggerFactory.CreateLogger<ModeratorAgent>());
    }

    public IAgent CreatePanelist(
        DiscussionId discussionId, string name, ModelIdentifier model)
    {
        var kernel = BuildKernel(model);
        return new PanelistAgent(
            name, model, kernel, _eventBus, _toolRegistry,
            _loggerFactory.CreateLogger<PanelistAgent>());
    }

    public IAgent CreateRandomPanelist(
        DiscussionId discussionId,
        string name,
        IReadOnlyList<ModelIdentifier> modelPool)
    {
        var model = modelPool[_random.Next(modelPool.Count)];
        return CreatePanelist(discussionId, name, model);
    }

    private Kernel BuildKernel(ModelIdentifier model)
    {
        var builder = Kernel.CreateBuilder();
        var settings = _sp.GetRequiredService<IOptions<PanelSettings>>().Value;

        switch (model.Provider.ToLowerInvariant())
        {
            case "azureopenai":
                builder.AddAzureOpenAIChatCompletion(
                    model.ModelName,
                    settings.AzureOpenAI.Endpoint,
                    settings.AzureOpenAI.ApiKey);
                break;
            case "openai":
                builder.AddOpenAIChatCompletion(
                    model.ModelName,
                    settings.OpenAI.ApiKey);
                break;
            // Add more providers as needed
        }

        builder.Services.AddSingleton(_eventBus);
        builder.Services.AddSingleton(_toolRegistry);
        builder.Services.AddLogging(lb => lb.AddSerilog());

        return builder.Build();
    }
}
8. Discussion Orchestration Engine
8.1 DiscussionOrchestrator — The Central Conductor
csharp

namespace CopilotAgent.Panel.Application.Orchestration;

public sealed class DiscussionOrchestrator : IDiscussionOrchestrator, IAsyncDisposable
{
    private readonly IAgentFactory _agentFactory;
    private readonly IEventBus _eventBus;
    private readonly ISessionStore _sessionStore;
    private readonly ISettingsProvider _settingsProvider;
    private readonly ILogger<DiscussionOrchestrator> _logger;

    private Discussion? _discussion;
    private DiscussionStateMachine? _stateMachine;
    private AgentSupervisor? _supervisor;
    private TurnManager? _turnManager;
    private HeadAgent? _head;
    private ModeratorAgent? _moderator;
    private CancellationTokenSource? _discussionCts;

    public DiscussionId? ActiveDiscussionId => _discussion?.Id;
    public DiscussionState CurrentState =>
        _stateMachine?.CurrentState ?? DiscussionState.Idle;

    public IObservable<DiscussionEvent> Events => _eventBus.Observe<DiscussionEvent>();

    public DiscussionOrchestrator(
        IAgentFactory agentFactory,
        IEventBus eventBus,
        ISessionStore sessionStore,
        ISettingsProvider settingsProvider,
        ILogger<DiscussionOrchestrator> logger)
    {
        _agentFactory = agentFactory;
        _eventBus = eventBus;
        _sessionStore = sessionStore;
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    // ═══════════════════════════════════════════
    //  1. START — User submits a task
    // ═══════════════════════════════════════════
    public async Task<DiscussionId> StartAsync(string userPrompt, CancellationToken ct = default)
    {
        var settings = _settingsProvider.Load();
        _discussionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Create discussion entity
        _discussion = new Discussion(
            DiscussionId.New(),
            userPrompt,
            settings.PanelistCount,
            settings.Verbosity);

        // Create state machine
        _stateMachine = new DiscussionStateMachine(
            _discussion, _eventBus,
            _logger as ILogger<DiscussionStateMachine>
                ?? LoggerFactory.Create(b => b.AddConsole())
                    .CreateLogger<DiscussionStateMachine>());

        // Create Head agent
        _head = (HeadAgent)_agentFactory.CreateHead(
            _discussion.Id, settings.PrimaryModel);
        _discussion.RegisterAgent(
            new AgentInstance("Head", AgentRole.Head, settings.PrimaryModel));

        _logger.LogInformation("Discussion {Id} started with prompt: {Prompt}",
            _discussion.Id, userPrompt[..Math.Min(100, userPrompt.Length)]);

        // Transition to 