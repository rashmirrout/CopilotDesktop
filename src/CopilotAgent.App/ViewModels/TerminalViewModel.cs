using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for the integrated PowerShell terminal with command history support.
/// Uses Process with proper VT100 mode for a native terminal experience.
/// </summary>
public partial class TerminalViewModel : ViewModelBase
{
    private readonly ILogger<TerminalViewModel> _logger;
    private Process? _shellProcess;
    private StreamWriter? _inputWriter;
    private readonly object _outputLock = new();
    private readonly StringBuilder _outputBuffer = new();
    
    // Command history
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private string _currentInput = string.Empty;

    [ObservableProperty]
    private string _terminalOutput = string.Empty;

    [ObservableProperty]
    private string _commandInput = string.Empty;

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    [ObservableProperty]
    private bool _isShellReady;

    [ObservableProperty]
    private string _promptText = "PS>";

    public TerminalViewModel(ILogger<TerminalViewModel> logger)
    {
        _logger = logger;
        WorkingDirectory = Environment.CurrentDirectory;
        
        // Start PowerShell process
        StartPowerShell();
    }

    private void StartPowerShell()
    {
        try
        {
            var shellPath = GetPowerShellPath();
            var shellName = Path.GetFileNameWithoutExtension(shellPath);
            
            _logger.LogInformation("Starting PowerShell: {Shell}", shellPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = shellPath,
                Arguments = "-NoLogo -NoProfile -NoExit",
                WorkingDirectory = WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Enable VT100 mode for ANSI escape sequence support
            startInfo.Environment["TERM"] = "xterm-256color";

            _shellProcess = new Process { StartInfo = startInfo };
            _shellProcess.OutputDataReceived += OnOutputReceived;
            _shellProcess.ErrorDataReceived += OnErrorReceived;
            _shellProcess.EnableRaisingEvents = true;
            _shellProcess.Exited += OnShellExited;

            _shellProcess.Start();
            _inputWriter = _shellProcess.StandardInput;
            _inputWriter.AutoFlush = true;
            
            _shellProcess.BeginOutputReadLine();
            _shellProcess.BeginErrorReadLine();

            IsShellReady = true;
            PromptText = shellName.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ? "PS>" : "PS>";
            
            _logger.LogInformation("PowerShell started successfully: {FileName}", shellPath);
            
            AppendOutput($"PowerShell Terminal ({shellName})\r\n");
            AppendOutput($"Working Directory: {WorkingDirectory}\r\n");
            AppendOutput($"Use Up/Down arrows for command history\r\n\r\n");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start PowerShell");
            AppendOutput($"Failed to start PowerShell: {ex.Message}\r\n");
            IsShellReady = false;
        }
    }

    private string GetPowerShellPath()
    {
        // Check for PowerShell Core (pwsh) first
        var pwshPaths = new[]
        {
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Program Files (x86)\PowerShell\7\pwsh.exe"
        };

        foreach (var path in pwshPaths)
        {
            if (File.Exists(path))
                return path;
        }

        // Check PATH for pwsh
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(';'))
        {
            var fullPath = Path.Combine(dir.Trim(), "pwsh.exe");
            if (File.Exists(fullPath))
                return fullPath;
        }

        // Fall back to Windows PowerShell
        return @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
    }

    private void OnOutputReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                AppendOutput(e.Data + "\r\n");
            });
        }
    }

    private void OnErrorReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                AppendOutput(e.Data + "\r\n");
            });
        }
    }

    private void OnShellExited(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            AppendOutput("\r\n[Shell process exited]\r\n");
            IsShellReady = false;
        });
    }

    /// <summary>
    /// Execute the current command
    /// </summary>
    [RelayCommand]
    private void ExecuteCommand()
    {
        if (string.IsNullOrWhiteSpace(CommandInput))
            return;

        var command = CommandInput.Trim();
        
        // Add to history if not duplicate of last command
        if (_commandHistory.Count == 0 || _commandHistory[^1] != command)
        {
            _commandHistory.Add(command);
        }
        _historyIndex = _commandHistory.Count;
        _currentInput = string.Empty;
        
        CommandInput = string.Empty;

        if (!IsShellReady || _inputWriter == null)
        {
            AppendOutput($"Shell not ready. Command: {command}\r\n");
            return;
        }

        try
        {
            _logger.LogInformation("Executing command: {Command}", command);
            _inputWriter.WriteLine(command);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command");
            AppendOutput($"Error: {ex.Message}\r\n");
        }
    }

    /// <summary>
    /// Navigate to previous command in history
    /// </summary>
    public void HistoryUp()
    {
        if (_commandHistory.Count == 0)
            return;

        // Save current input if at the end of history
        if (_historyIndex == _commandHistory.Count)
        {
            _currentInput = CommandInput;
        }

        if (_historyIndex > 0)
        {
            _historyIndex--;
            CommandInput = _commandHistory[_historyIndex];
        }
    }

    /// <summary>
    /// Navigate to next command in history
    /// </summary>
    public void HistoryDown()
    {
        if (_commandHistory.Count == 0)
            return;

        if (_historyIndex < _commandHistory.Count - 1)
        {
            _historyIndex++;
            CommandInput = _commandHistory[_historyIndex];
        }
        else if (_historyIndex == _commandHistory.Count - 1)
        {
            _historyIndex = _commandHistory.Count;
            CommandInput = _currentInput;
        }
    }

    /// <summary>
    /// Send Ctrl+C to interrupt the current command
    /// </summary>
    [RelayCommand]
    private void SendInterrupt()
    {
        if (_shellProcess != null && !_shellProcess.HasExited)
        {
            try
            {
                // Send Ctrl+C character
                _inputWriter?.Write('\x03');
                _inputWriter?.Flush();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send interrupt");
            }
        }
    }

    public void SetWorkingDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            WorkingDirectory = directory;
            if (IsShellReady && _inputWriter != null)
            {
                _inputWriter.WriteLine($"cd \"{directory}\"");
            }
        }
    }

    private void AppendOutput(string text)
    {
        lock (_outputLock)
        {
            _outputBuffer.Append(text);
            
            // Keep buffer manageable (max 100KB)
            if (_outputBuffer.Length > 100000)
            {
                var excess = _outputBuffer.Length - 80000;
                _outputBuffer.Remove(0, excess);
            }
            
            TerminalOutput = _outputBuffer.ToString();
        }
    }

    [RelayCommand]
    private void ClearTerminal()
    {
        lock (_outputLock)
        {
            _outputBuffer.Clear();
            TerminalOutput = string.Empty;
        }
        
        if (IsShellReady && _inputWriter != null)
        {
            _inputWriter.WriteLine("cls");
        }
    }

    [RelayCommand]
    private void RestartShell()
    {
        StopShell();
        
        lock (_outputLock)
        {
            _outputBuffer.Clear();
            TerminalOutput = string.Empty;
        }
        
        // Keep command history across restarts
        StartPowerShell();
    }

    private void StopShell()
    {
        try
        {
            _inputWriter?.Close();
            _inputWriter = null;

            if (_shellProcess != null && !_shellProcess.HasExited)
            {
                _shellProcess.Kill();
            }
            _shellProcess?.Dispose();
            _shellProcess = null;
            IsShellReady = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping shell");
        }
    }

    public override void Cleanup()
    {
        StopShell();
        base.Cleanup();
    }
}
