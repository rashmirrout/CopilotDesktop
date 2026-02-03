using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Implementation of MCP server management service
/// </summary>
public class McpService : IMcpService, IDisposable
{
    private readonly ILogger<McpService> _logger;
    private readonly IPersistenceService _persistence;
    private readonly List<McpServerConfig> _servers = new();
    private readonly ConcurrentDictionary<string, McpServerInstance> _runningServers = new();
    private readonly object _serversLock = new();
    private bool _disposed;

    public event EventHandler<McpServerStatusChangedEventArgs>? ServerStatusChanged;

    public McpService(ILogger<McpService> logger, IPersistenceService persistence)
    {
        _logger = logger;
        _persistence = persistence;
    }

    public IReadOnlyList<McpServerConfig> GetServers()
    {
        lock (_serversLock)
        {
            return _servers.ToList().AsReadOnly();
        }
    }

    public McpServerConfig? GetServer(string name)
    {
        lock (_serversLock)
        {
            return _servers.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task AddServerAsync(McpServerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Name))
        {
            throw new ArgumentException("Server name is required", nameof(config));
        }

        lock (_serversLock)
        {
            if (_servers.Any(s => s.Name.Equals(config.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Server with name '{config.Name}' already exists");
            }
            _servers.Add(config);
        }

        _logger.LogInformation("Added MCP server: {ServerName}", config.Name);
        await SaveServersAsync();
    }

    public async Task UpdateServerAsync(McpServerConfig config)
    {
        lock (_serversLock)
        {
            var index = _servers.FindIndex(s => s.Name.Equals(config.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException($"Server '{config.Name}' not found");
            }
            _servers[index] = config;
        }

        _logger.LogInformation("Updated MCP server: {ServerName}", config.Name);
        await SaveServersAsync();
    }

    public async Task RemoveServerAsync(string name)
    {
        // Stop if running
        if (IsServerRunning(name))
        {
            await StopServerAsync(name);
        }

        lock (_serversLock)
        {
            var removed = _servers.RemoveAll(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                throw new InvalidOperationException($"Server '{name}' not found");
            }
        }

        _logger.LogInformation("Removed MCP server: {ServerName}", name);
        await SaveServersAsync();
    }

    public async Task<bool> StartServerAsync(string name)
    {
        var config = GetServer(name);
        if (config == null)
        {
            _logger.LogError("Cannot start server '{ServerName}': not found", name);
            return false;
        }

        if (IsServerRunning(name))
        {
            _logger.LogWarning("Server '{ServerName}' is already running", name);
            return true;
        }

        if (config.Transport != McpTransport.Stdio)
        {
            // HTTP transport doesn't need a process
            var httpInstance = new McpServerInstance(config, null);
            httpInstance.Status = McpServerStatus.Running;
            _runningServers[name] = httpInstance;
            RaiseStatusChanged(name, McpServerStatus.Stopped, McpServerStatus.Running);
            return true;
        }

        if (string.IsNullOrWhiteSpace(config.Command))
        {
            _logger.LogError("Cannot start server '{ServerName}': no command specified", name);
            return false;
        }

        RaiseStatusChanged(name, McpServerStatus.Stopped, McpServerStatus.Starting);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = config.Command,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Add arguments
            if (config.Args != null)
            {
                foreach (var arg in config.Args)
                {
                    startInfo.ArgumentList.Add(arg);
                }
            }

            // Add environment variables
            if (config.Env != null)
            {
                foreach (var (key, value) in config.Env)
                {
                    startInfo.EnvironmentVariables[key] = value;
                }
            }

            var process = new Process { StartInfo = startInfo };
            process.EnableRaisingEvents = true;

            var instance = new McpServerInstance(config, process);

            process.Exited += (s, e) =>
            {
                var oldStatus = instance.Status;
                instance.Status = McpServerStatus.Stopped;
                _runningServers.TryRemove(name, out _);
                RaiseStatusChanged(name, oldStatus, McpServerStatus.Stopped, "Process exited");
                _logger.LogInformation("MCP server '{ServerName}' process exited", name);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger.LogWarning("MCP server '{ServerName}' stderr: {Data}", name, e.Data);
                }
            };

            if (!process.Start())
            {
                RaiseStatusChanged(name, McpServerStatus.Starting, McpServerStatus.Error, "Failed to start process");
                return false;
            }

            process.BeginErrorReadLine();

            instance.Status = McpServerStatus.Running;
            _runningServers[name] = instance;

            RaiseStatusChanged(name, McpServerStatus.Starting, McpServerStatus.Running);
            _logger.LogInformation("Started MCP server '{ServerName}' (PID: {ProcessId})", name, process.Id);

            // Initialize MCP protocol
            await InitializeMcpProtocolAsync(instance);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start MCP server '{ServerName}'", name);
            RaiseStatusChanged(name, McpServerStatus.Starting, McpServerStatus.Error, ex.Message);
            return false;
        }
    }

    public async Task StopServerAsync(string name)
    {
        if (!_runningServers.TryRemove(name, out var instance))
        {
            _logger.LogWarning("Server '{ServerName}' is not running", name);
            return;
        }

        var oldStatus = instance.Status;
        instance.Status = McpServerStatus.Stopping;
        RaiseStatusChanged(name, oldStatus, McpServerStatus.Stopping);

        try
        {
            if (instance.Process != null && !instance.Process.HasExited)
            {
                // Send graceful shutdown via stdin close
                instance.Process.StandardInput.Close();

                // Wait for graceful exit
                var exited = await Task.Run(() => instance.Process.WaitForExit(5000));
                
                if (!exited)
                {
                    _logger.LogWarning("MCP server '{ServerName}' did not exit gracefully, killing process", name);
                    instance.Process.Kill(entireProcessTree: true);
                }
            }

            instance.Status = McpServerStatus.Stopped;
            RaiseStatusChanged(name, McpServerStatus.Stopping, McpServerStatus.Stopped);
            _logger.LogInformation("Stopped MCP server '{ServerName}'", name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping MCP server '{ServerName}'", name);
            instance.Status = McpServerStatus.Error;
            RaiseStatusChanged(name, McpServerStatus.Stopping, McpServerStatus.Error, ex.Message);
        }
    }

    public bool IsServerRunning(string name)
    {
        return _runningServers.TryGetValue(name, out var instance) && 
               instance.Status == McpServerStatus.Running;
    }

    public McpServerStatus GetServerStatus(string name)
    {
        if (_runningServers.TryGetValue(name, out var instance))
        {
            return instance.Status;
        }
        return McpServerStatus.Stopped;
    }

    public async Task<IReadOnlyList<McpToolInfo>> GetToolsAsync(string serverName)
    {
        if (!_runningServers.TryGetValue(serverName, out var instance))
        {
            throw new InvalidOperationException($"Server '{serverName}' is not running");
        }

        // Return cached tools if available
        if (instance.Tools != null)
        {
            return instance.Tools;
        }

        // Request tools list from server
        var response = await SendMcpRequestAsync(instance, "tools/list", null);
        
        if (response.HasValue && response.Value.TryGetProperty("tools", out var toolsElement))
        {
            var tools = new List<McpToolInfo>();
            foreach (var tool in toolsElement.EnumerateArray())
            {
                var toolInfo = new McpToolInfo
                {
                    Name = tool.GetProperty("name").GetString() ?? "",
                    Description = tool.TryGetProperty("description", out var desc) ? desc.GetString() : null
                };

                if (tool.TryGetProperty("inputSchema", out var schema) && 
                    schema.TryGetProperty("properties", out var props))
                {
                    toolInfo.Parameters = new Dictionary<string, McpToolParameter>();
                    var required = schema.TryGetProperty("required", out var reqArray) 
                        ? reqArray.EnumerateArray().Select(r => r.GetString()).ToHashSet()
                        : new HashSet<string?>();

                    foreach (var prop in props.EnumerateObject())
                    {
                        toolInfo.Parameters[prop.Name] = new McpToolParameter
                        {
                            Type = prop.Value.TryGetProperty("type", out var t) ? t.GetString() ?? "string" : "string",
                            Description = prop.Value.TryGetProperty("description", out var d) ? d.GetString() : null,
                            Required = required.Contains(prop.Name)
                        };
                    }
                }

                tools.Add(toolInfo);
            }

            instance.Tools = tools;
            return tools;
        }

        return Array.Empty<McpToolInfo>();
    }

    public async Task<McpToolCallResult> CallToolAsync(string serverName, string toolName, Dictionary<string, object>? arguments)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!_runningServers.TryGetValue(serverName, out var instance))
        {
            return new McpToolCallResult
            {
                Success = false,
                Error = $"Server '{serverName}' is not running",
                Duration = stopwatch.Elapsed
            };
        }

        try
        {
            var response = await SendMcpRequestAsync(instance, "tools/call", new
            {
                name = toolName,
                arguments = arguments ?? new Dictionary<string, object>()
            });

            stopwatch.Stop();

            if (response != null)
            {
                if (response.Value.TryGetProperty("content", out var content))
                {
                    return new McpToolCallResult
                    {
                        Success = true,
                        Result = content.ToString(),
                        Duration = stopwatch.Elapsed
                    };
                }

                if (response.Value.TryGetProperty("error", out var error))
                {
                    return new McpToolCallResult
                    {
                        Success = false,
                        Error = error.GetProperty("message").GetString(),
                        Duration = stopwatch.Elapsed
                    };
                }
            }

            return new McpToolCallResult
            {
                Success = false,
                Error = "Invalid response from MCP server",
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error calling tool '{ToolName}' on server '{ServerName}'", toolName, serverName);
            return new McpToolCallResult
            {
                Success = false,
                Error = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    public async Task LoadServersAsync()
    {
        try
        {
            var servers = await _persistence.LoadMcpServersAsync();
            lock (_serversLock)
            {
                _servers.Clear();
                if (servers != null)
                {
                    _servers.AddRange(servers);
                }
            }
            _logger.LogInformation("Loaded {Count} MCP server configurations", _servers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load MCP server configurations");
        }
    }

    public async Task SaveServersAsync()
    {
        try
        {
            List<McpServerConfig> serversToSave;
            lock (_serversLock)
            {
                serversToSave = _servers.ToList();
            }
            await _persistence.SaveMcpServersAsync(serversToSave);
            _logger.LogDebug("Saved {Count} MCP server configurations", serversToSave.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save MCP server configurations");
        }
    }

    private async Task InitializeMcpProtocolAsync(McpServerInstance instance)
    {
        // Send initialize request
        var response = await SendMcpRequestAsync(instance, "initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new
            {
                name = "CopilotAgent",
                version = "1.0.0"
            }
        });

        if (response != null)
        {
            _logger.LogInformation("MCP server '{ServerName}' initialized: {Response}", 
                instance.Config.Name, response.Value.ToString());
            
            // Send initialized notification
            await SendMcpNotificationAsync(instance, "notifications/initialized", null);
        }
    }

    private async Task<JsonElement?> SendMcpRequestAsync(McpServerInstance instance, string method, object? parameters)
    {
        if (instance.Process == null)
        {
            // HTTP transport
            return await SendHttpRequestAsync(instance, method, parameters);
        }

        var requestId = Interlocked.Increment(ref instance.RequestId);
        var request = new
        {
            jsonrpc = "2.0",
            id = requestId,
            method = method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(request);
        
        try
        {
            await instance.Process.StandardInput.WriteLineAsync(json);
            await instance.Process.StandardInput.FlushAsync();

            // Read response with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(instance.Config.TimeoutSeconds));
            var responseLine = await instance.Process.StandardOutput.ReadLineAsync(cts.Token);

            if (!string.IsNullOrEmpty(responseLine))
            {
                var responseDoc = JsonDocument.Parse(responseLine);
                var root = responseDoc.RootElement;

                if (root.TryGetProperty("result", out var result))
                {
                    return result;
                }
                if (root.TryGetProperty("error", out var error))
                {
                    _logger.LogError("MCP error: {Error}", error.ToString());
                    return error;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("MCP request timed out: {Method}", method);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending MCP request: {Method}", method);
        }

        return null;
    }

    private async Task SendMcpNotificationAsync(McpServerInstance instance, string method, object? parameters)
    {
        if (instance.Process == null) return;

        var notification = new
        {
            jsonrpc = "2.0",
            method = method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(notification);
        
        try
        {
            await instance.Process.StandardInput.WriteLineAsync(json);
            await instance.Process.StandardInput.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending MCP notification: {Method}", method);
        }
    }

    private async Task<JsonElement?> SendHttpRequestAsync(McpServerInstance instance, string method, object? parameters)
    {
        if (string.IsNullOrEmpty(instance.Config.Url))
        {
            _logger.LogError("No URL configured for HTTP MCP server '{ServerName}'", instance.Config.Name);
            return null;
        }

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(instance.Config.TimeoutSeconds);

            if (instance.Config.Headers != null)
            {
                foreach (var (key, value) in instance.Config.Headers)
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
                }
            }

            var request = new
            {
                jsonrpc = "2.0",
                id = Interlocked.Increment(ref instance.RequestId),
                method = method,
                @params = parameters
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync(instance.Config.Url, content);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync();
            var responseDoc = JsonDocument.Parse(responseText);
            var root = responseDoc.RootElement;

            if (root.TryGetProperty("result", out var result))
            {
                return result;
            }
            if (root.TryGetProperty("error", out var error))
            {
                _logger.LogError("MCP HTTP error: {Error}", error.ToString());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending HTTP MCP request: {Method}", method);
        }

        return null;
    }

    private void RaiseStatusChanged(string serverName, McpServerStatus oldStatus, McpServerStatus newStatus, string? message = null)
    {
        ServerStatusChanged?.Invoke(this, new McpServerStatusChangedEventArgs(serverName, oldStatus, newStatus, message));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop all running servers
        foreach (var (name, instance) in _runningServers)
        {
            try
            {
                instance.Process?.Kill(entireProcessTree: true);
                instance.Process?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing MCP server '{ServerName}'", name);
            }
        }
        _runningServers.Clear();
    }

    /// <summary>
    /// Internal class to track running MCP server instances
    /// </summary>
    private class McpServerInstance
    {
        public McpServerConfig Config { get; }
        public Process? Process { get; }
        public McpServerStatus Status { get; set; }
        public int RequestId;
        public List<McpToolInfo>? Tools { get; set; }

        public McpServerInstance(McpServerConfig config, Process? process)
        {
            Config = config;
            Process = process;
            Status = McpServerStatus.Stopped;
        }
    }
}