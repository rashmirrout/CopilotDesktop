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

namespace CopilotAgent.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure Serilog
        var logsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CopilotAgent", "Logs"
        );
        Directory.CreateDirectory(logsPath);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logsPath, "copilot-agent-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

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
