// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics;
using CopilotAgent.Panel.Domain.Policies;
using CopilotAgent.Panel.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Panel.Resilience;

/// <summary>
/// Sandboxed tool executor that wraps tool invocations with:
///   1. Circuit breaker protection (per-tool)
///   2. Timeout enforcement (<see cref="GuardRailPolicy.MaxSingleTurnDuration"/>)
///   3. Output size capping (50KB max)
///   4. Retry with exponential backoff
///   5. Timing instrumentation
///
/// DESIGN: Each tool gets its own <see cref="ToolCircuitBreaker"/> instance.
/// The executor creates breakers on-demand and caches them by tool name.
///
/// THREAD SAFETY: Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// for breaker cache and per-breaker internal locking.
/// </summary>
public sealed class SandboxedToolExecutor : IDisposable
{
    /// <summary>Maximum tool output size in bytes (50KB).</summary>
    private const int MaxOutputSizeBytes = 50 * 1024;

    private readonly ConcurrentDictionary<string, ToolCircuitBreaker> _breakers = new();
    private readonly PanelRetryPolicy _retryPolicy;
    private readonly ILogger<SandboxedToolExecutor> _logger;
    private readonly CircuitBreakerConfig _breakerConfig;
    private bool _disposed;

    public SandboxedToolExecutor(
        ILogger<SandboxedToolExecutor> logger,
        PanelRetryPolicy? retryPolicy = null,
        CircuitBreakerConfig? breakerConfig = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _retryPolicy = retryPolicy ?? PanelRetryPolicy.Default;
        _breakerConfig = breakerConfig ?? CircuitBreakerConfig.Default;
    }

    /// <summary>
    /// Execute a tool call through the sandbox with all protections.
    /// </summary>
    /// <param name="toolName">Name of the tool to execute.</param>
    /// <param name="input">Tool input arguments.</param>
    /// <param name="executor">
    /// The actual tool execution function. Takes (input, ct) and returns the output string.
    /// </param>
    /// <param name="timeout">
    /// Maximum time allowed for this tool call. If null, defaults to 3 minutes.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ToolCallRecord"/> with timing and result information.</returns>
    public async Task<ToolCallRecord> ExecuteToolAsync(
        string toolName,
        string input,
        Func<string, CancellationToken, Task<string>> executor,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(3);
        var breaker = _breakers.GetOrAdd(toolName,
            name => new ToolCircuitBreaker(name, _breakerConfig, _logger));

        var sw = Stopwatch.StartNew();

        try
        {
            var output = await _retryPolicy.ExecuteAsync(async token =>
            {
                return await breaker.ExecuteAsync(async innerCt =>
                {
                    // Enforce timeout
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(innerCt);
                    timeoutCts.CancelAfter(effectiveTimeout);

                    var result = await executor(input, timeoutCts.Token);

                    // Cap output size
                    return TruncateOutput(result, toolName);
                }, token);
            },
            shouldRetry: ex => ex is not CircuitBreakerOpenException
                            && ex is not OperationCanceledException,
            ct: ct);

            sw.Stop();

            _logger.LogDebug(
                "[SandboxedTool:{Tool}] Succeeded in {Duration}ms, output: {OutputLength} chars",
                toolName, sw.ElapsedMilliseconds, output?.Length ?? 0);

            return new ToolCallRecord(
                toolName,
                input,
                output,
                Succeeded: true,
                Duration: sw.Elapsed);
        }
        catch (CircuitBreakerOpenException ex)
        {
            sw.Stop();
            _logger.LogWarning(
                "[SandboxedTool:{Tool}] Circuit breaker open — call rejected. Retry after {RetryAfter}",
                toolName, ex.RetryAfter);

            return new ToolCallRecord(
                toolName,
                input,
                Output: $"Tool unavailable (circuit breaker open). Retry after {ex.RetryAfter:HH:mm:ss}.",
                Succeeded: false,
                Duration: sw.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogInformation("[SandboxedTool:{Tool}] Cancelled by user", toolName);

            return new ToolCallRecord(
                toolName,
                input,
                Output: "Tool call cancelled.",
                Succeeded: false,
                Duration: sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning(
                "[SandboxedTool:{Tool}] Timed out after {Timeout}", toolName, effectiveTimeout);

            return new ToolCallRecord(
                toolName,
                input,
                Output: $"Tool call timed out after {effectiveTimeout.TotalSeconds:F0}s.",
                Succeeded: false,
                Duration: sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "[SandboxedTool:{Tool}] Failed after {Duration}ms and {Retries} retries",
                toolName, sw.ElapsedMilliseconds, _retryPolicy.MaxRetries);

            return new ToolCallRecord(
                toolName,
                input,
                Output: $"Tool error: {ex.Message}",
                Succeeded: false,
                Duration: sw.Elapsed);
        }
    }

    /// <summary>
    /// Check whether a tool's circuit breaker is open.
    /// </summary>
    /// <param name="toolName">The tool name to check.</param>
    /// <returns>True if the circuit is open and calls would be rejected.</returns>
    public bool IsToolAvailable(string toolName)
    {
        if (_breakers.TryGetValue(toolName, out var breaker))
            return breaker.State != CircuitBreakerState.Open;

        return true; // No breaker = never failed = available
    }

    /// <summary>
    /// Reset all circuit breakers. Called on session reset.
    /// </summary>
    public async Task ResetAllBreakersAsync()
    {
        foreach (var breaker in _breakers.Values)
            await breaker.ResetAsync();

        _logger.LogInformation("[SandboxedTool] All circuit breakers reset");
    }

    /// <summary>
    /// Truncate tool output to the maximum size limit.
    /// </summary>
    private string TruncateOutput(string? output, string toolName)
    {
        if (output is null) return string.Empty;

        if (output.Length > MaxOutputSizeBytes)
        {
            _logger.LogWarning(
                "[SandboxedTool:{Tool}] Output truncated from {Original} to {Max} chars",
                toolName, output.Length, MaxOutputSizeBytes);
            return output[..MaxOutputSizeBytes] + "\n\n[OUTPUT TRUNCATED — exceeded 50KB limit]";
        }

        return output;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var breaker in _breakers.Values)
            breaker.Dispose();

        _breakers.Clear();
    }
}