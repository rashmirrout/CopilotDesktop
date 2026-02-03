using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using CopilotAgent.Core.Services;
using CopilotAgent.Persistence;
using CopilotAgent.App.ViewModels;

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
                    services.AddTransient<NewWorktreeSessionDialogViewModel>();
                    services.AddTransient<TerminalViewModel>();

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

            // If no sessions exist, create a default one
            if (!sessionManager.Sessions.Any())
            {
                await sessionManager.CreateSessionAsync();
            }

            // Show main window
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var mainViewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
            mainWindow.DataContext = mainViewModel;
            await mainViewModel.InitializeAsync();
            mainWindow.Show();

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
