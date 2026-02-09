using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.Persistence;
using CopilotAgent.App.ViewModels;
using CopilotAgent.App.Services;
using CopilotAgent.MultiAgent.Models;
using CopilotAgent.MultiAgent.Services;
using CopilotAgent.Office.Services;

namespace CopilotAgent.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <summary>
    /// Gets the service provider for resolving dependencies
    /// </summary>
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not initialized");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure Serilog — logs go to ~/.CopilotDesktop/Logs/
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var logsPath = Path.Combine(userProfile, ".CopilotDesktop", "Logs");
        Directory.CreateDirectory(logsPath);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logsPath, "copilot-agent-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        // ── Global exception handlers ────────────────────────────
        // These catch any unhandled exception that would otherwise silently kill the process.

        // 1. UI thread exceptions (WPF dispatcher)
        DispatcherUnhandledException += (sender, args) =>
        {
            // Classify the exception: some WPF framework bugs are benign and
            // should NOT kill the app — just log and swallow.
            var isBenignWpfBug = args.Exception is InvalidOperationException ioe
                && (ioe.Message.Contains("is not a Visual or Visual3D", StringComparison.OrdinalIgnoreCase)
                    || ioe.Message.Contains("This Visual is not connected to a PresentationSource", StringComparison.OrdinalIgnoreCase));

            if (isBenignWpfBug)
            {
                Log.Error(args.Exception,
                    "[WPF_BUG] Known WPF framework exception (swallowed, non-fatal): {Message}",
                    args.Exception.Message);
                args.Handled = true;
                return; // Do NOT crash the app
            }

            Log.Fatal(args.Exception,
                "[CRASH] Unhandled exception on UI thread: {Message}",
                args.Exception.Message);
            WriteCrashLog(args.Exception, "DispatcherUnhandledException", logsPath);

            // Mark handled to prevent immediate process termination —
            // gives Serilog time to flush. The app may be in a bad state,
            // so we still shut down gracefully.
            args.Handled = true;

            try
            {
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{args.Exception.Message}\n\nThe application will close. A crash log has been saved.",
                    "Copilot Agent — Fatal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* UI may already be broken */ }

            Log.CloseAndFlush();
            Environment.Exit(1);
        };

        // 2. Background thread / async task exceptions that were never observed
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Log.Error(args.Exception,
                "[UNOBSERVED_TASK] Unobserved task exception (NOT crashing): {Message}",
                args.Exception?.InnerException?.Message ?? args.Exception?.Message);
            WriteCrashLog(args.Exception?.Flatten() ?? args.Exception!, "UnobservedTaskException", logsPath);

            // Observe it to prevent process termination.
            // This is deliberately non-fatal: unobserved tasks are often
            // fire-and-forget background work that shouldn't kill the app.
            args.SetObserved();
        };

        // 3. Any other unhandled exception (native, finalizer, etc.)
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Log.Fatal(ex,
                "[CRASH] AppDomain unhandled exception (isTerminating={IsTerminating}): {Message}",
                args.IsTerminating,
                ex?.Message ?? args.ExceptionObject?.ToString());
            WriteCrashLog(ex ?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown"),
                "AppDomainUnhandledException", logsPath);
            Log.CloseAndFlush();
        };

        try
        {
            // Pre-load settings to avoid deadlock during DI resolution
            // Using Task.Run to avoid blocking on the UI synchronization context
            Log.Debug("Pre-loading application settings...");
            var persistenceService = new JsonPersistenceService(
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger<JsonPersistenceService>());
            var appSettings = await Task.Run(() => persistenceService.LoadSettingsAsync());
            Log.Debug("Settings loaded");

            // Build host with DI
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // App Settings - already loaded, register as singleton instance
                    services.AddSingleton(appSettings);

                    // Core Services
                    services.AddSingleton<IPersistenceService, JsonPersistenceService>();
                    
                    // Tool Approval Service
                    services.AddSingleton<IToolApprovalService, ToolApprovalService>();
                    
                    // Register Copilot SDK service (CLI service deprecated)
                    services.AddSingleton<CopilotSdkService>();
                    services.AddSingleton<ICopilotService>(sp => sp.GetRequiredService<CopilotSdkService>());
                    Log.Information("Using SDK mode for Copilot communication");
                    
                    services.AddSingleton<ISessionManager, SessionManager>();
                    services.AddSingleton<ICommandPolicyService, CommandPolicyService>();
                    services.AddSingleton<IMcpService, McpService>();
                    services.AddSingleton<ISkillsService, SkillsService>();
                    services.AddSingleton<IIterativeTaskService, IterativeTaskService>();
                    
                    // Streaming Message Manager - manages streaming operations independently of UI
                    // Must be singleton to track streaming state across session switches
                    services.AddSingleton<IStreamingMessageManager, StreamingMessageManager>();

                    // Multi-Agent Orchestration Services
                    services.AddSingleton<IDependencyScheduler, DependencyScheduler>();
                    services.AddSingleton<IAgentRoleProvider, AgentRoleProvider>();
                    services.AddSingleton<IApprovalQueue, ApprovalQueue>();
                    services.AddSingleton<ITaskLogStore, JsonTaskLogStore>();
                    services.AddSingleton<ITaskDecomposer, LlmTaskDecomposer>();
                    services.AddSingleton<IResultAggregator, ResultAggregator>();

                    // Workspace strategies
                    services.AddSingleton<GitWorktreeStrategy>();
                    services.AddSingleton<FileLockingStrategy>();
                    services.AddSingleton<InMemoryStrategy>();

                    // Factory delegate: maps WorkspaceStrategyType enum → IWorkspaceStrategy
                    services.AddSingleton<Func<WorkspaceStrategyType, IWorkspaceStrategy>>(sp =>
                    {
                        var git = sp.GetRequiredService<GitWorktreeStrategy>();
                        var fileLock = sp.GetRequiredService<FileLockingStrategy>();
                        var inMemory = sp.GetRequiredService<InMemoryStrategy>();
                        return strategyType => strategyType switch
                        {
                            WorkspaceStrategyType.GitWorktree => git,
                            WorkspaceStrategyType.FileLocking => fileLock,
                            WorkspaceStrategyType.InMemory => inMemory,
                            _ => throw new ArgumentOutOfRangeException(
                                nameof(strategyType), strategyType,
                                $"Unknown workspace strategy: {strategyType}")
                        };
                    });

                    services.AddSingleton<IAgentPool, AgentPool>();
                    services.AddSingleton<IOrchestratorService, OrchestratorService>();

                    // Agent Office Services
                    services.AddSingleton<IReasoningStream, ReasoningStream>();
                    services.AddSingleton<IOfficeManagerService, OfficeManagerService>();
                    services.AddSingleton<IOfficeEventLog, OfficeEventLog>();
                    services.AddSingleton<IIterationScheduler, IterationScheduler>();
                    
                    // Browser Automation Service (for OAuth/SAML authentication)
                    services.AddSingleton<IBrowserAutomationService>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<PlaywrightBrowserService>>();
                        var settings = sp.GetRequiredService<AppSettings>();
                        return new PlaywrightBrowserService(logger, settings.BrowserAutomation);
                    });

                    // UI Services
                    services.AddSingleton<ToolApprovalUIService>();

                    // ViewModels
                    services.AddTransient<MainWindowViewModel>();
                    services.AddTransient<ChatViewModel>();
                    services.AddTransient<NewWorktreeSessionDialogViewModel>();
                    services.AddTransient<TerminalViewModel>();
                    services.AddTransient<McpConfigViewModel>();
                    services.AddTransient<SkillsViewModel>();
                    services.AddTransient<IterativeTaskViewModel>();
                    services.AddTransient<SessionInfoViewModel>();
                    services.AddTransient<AddSkillDialogViewModel>();
                    services.AddTransient<AgentTeamViewModel>();
                    services.AddTransient<OfficeViewModel>();

                    // Main Window
                    services.AddSingleton<MainWindow>();
                })
                .UseSerilog()
                .Build();

            await _host.StartAsync();

            Log.Information("Copilot Agent Desktop starting...");

            // Initialize services
            var sessionManager = _host.Services.GetRequiredService<ISessionManager>();
            await sessionManager.LoadSessionsAsync();

            var commandPolicyService = _host.Services.GetRequiredService<ICommandPolicyService>();
            await commandPolicyService.LoadPolicyAsync();

            var mcpService = _host.Services.GetRequiredService<IMcpService>();
            await mcpService.LoadServersAsync();

            var skillsService = _host.Services.GetRequiredService<ISkillsService>();
            await skillsService.ScanSkillsAsync();
            
            Log.Debug("Initializing tool approval UI service...");
            // Initialize tool approval UI service
            var toolApprovalUIService = _host.Services.GetRequiredService<ToolApprovalUIService>();
            toolApprovalUIService.Initialize();
            Log.Debug("Tool approval UI service initialized");

            // If no sessions exist, create a default one
            if (!sessionManager.Sessions.Any())
            {
                Log.Debug("No sessions exist, creating default session...");
                await sessionManager.CreateSessionAsync();
                Log.Debug("Default session created");
            }

            Log.Debug("Creating main window...");
            // Show main window
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            Log.Debug("Getting main view model...");
            var mainViewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
            Log.Debug("Setting DataContext...");
            mainWindow.DataContext = mainViewModel;
            Log.Debug("Initializing main view model...");
            await mainViewModel.InitializeAsync();
            Log.Debug("Showing main window...");
            mainWindow.Show();
            mainWindow.Activate();
            mainWindow.Focus();
            
            // Ensure window is visible and in foreground
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Topmost = true;
            mainWindow.Topmost = false;

            Log.Information("Copilot Agent Desktop started successfully");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application failed to start");
            MessageBox.Show(
                $"Failed to start application: {ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>
    /// Writes exception details to a dedicated crash log file using synchronous I/O.
    /// This is intentionally independent of Serilog so it works even if the logging
    /// pipeline is broken or hasn't flushed yet.
    /// </summary>
    private static void WriteCrashLog(Exception ex, string source, string logsPath)
    {
        try
        {
            var crashFile = Path.Combine(logsPath, "crash-log.txt");
            var entry = $"""
                ────────────────────────────────────────
                Timestamp : {DateTime.UtcNow:O}
                Source    : {source}
                Type      : {ex.GetType().FullName}
                Message   : {ex.Message}
                Stack     :
                {ex.StackTrace}
                Inner     : {ex.InnerException?.Message ?? "(none)"}
                ────────────────────────────────────────

                """;
            File.AppendAllText(crashFile, entry);
        }
        catch
        {
            // Never throw from the crash logger itself.
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host != null)
            {
                // Save active session before exit
                var sessionManager = _host.Services.GetRequiredService<ISessionManager>();
                await sessionManager.SaveActiveSessionAsync();

                // Save command policy
                var commandPolicyService = _host.Services.GetRequiredService<ICommandPolicyService>();
                await commandPolicyService.SavePolicyAsync();

                // Save MCP servers and dispose
                var mcpService = _host.Services.GetRequiredService<IMcpService>();
                await mcpService.SaveServersAsync();
                if (mcpService is IDisposable disposableMcp)
                {
                    disposableMcp.Dispose();
                }
                
                // Dispose tool approval UI service
                var toolApprovalUIService = _host.Services.GetRequiredService<ToolApprovalUIService>();
                toolApprovalUIService.Dispose();
                
                // Save tool approval rules
                var toolApprovalService = _host.Services.GetRequiredService<IToolApprovalService>();
                await toolApprovalService.SaveRulesAsync();
                
                // Close browser automation service (saves storage state)
                var browserService = _host.Services.GetRequiredService<IBrowserAutomationService>();
                await browserService.DisposeAsync();

                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during application shutdown");
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
