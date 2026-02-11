// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CopilotAgent.Panel.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Panel.Resilience;

/// <summary>
/// Per-tool circuit breaker that prevents cascading failures when external tools fail repeatedly.
///
/// STATE MACHINE:
///   Closed  → (failures >= threshold) → Open
///   Open    → (cooldown elapsed)      → HalfOpen
///   HalfOpen → (probe succeeds)       → Closed
///   HalfOpen → (probe fails)          → Open
///
/// THREAD SAFETY: All state mutations are guarded by <see cref="SemaphoreSlim"/>.
/// Each tool gets its own <see cref="ToolCircuitBreaker"/> instance.
/// </summary>
public sealed class ToolCircuitBreaker : IDisposable
{
    private readonly CircuitBreakerConfig _config;
    private readonly ILogger _logger;
    private readonly string _toolName;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _consecutiveFailures;
    private int _halfOpenSuccesses;
    private DateTimeOffset _openedAt;
    private bool _disposed;

    /// <summary>Current state of the circuit breaker.</summary>
    public CircuitBreakerState State => _state;

    /// <summary>Number of consecutive failures in current failure sequence.</summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    public ToolCircuitBreaker(
        string toolName,
        CircuitBreakerConfig? config = null,
        ILogger? logger = null)
    {
        _toolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        _config = config ?? CircuitBreakerConfig.Default;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    /// <summary>
    /// Execute an action through the circuit breaker.
    /// If the circuit is open, throws <see cref="CircuitBreakerOpenException"/> immediately.
    /// If the circuit is half-open, allows exactly one probe call.
    /// </summary>
    /// <typeparam name="T">Return type of the action.</typeparam>
    /// <param name="action">The async action to execute (typically a tool call).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the action.</returns>
    /// <exception cref="CircuitBreakerOpenException">Thrown when the circuit is open.</exception>
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(ct);
        try
        {
            switch (_state)
            {
                case CircuitBreakerState.Open:
                    if (DateTimeOffset.UtcNow - _openedAt >= _config.EffectiveRecoveryTimeout)
                    {
                        _state = CircuitBreakerState.HalfOpen;
                        _halfOpenSuccesses = 0;
                        _logger.LogInformation(
                            "[CircuitBreaker:{Tool}] Transitioning Open → HalfOpen (cooldown elapsed)",
                            _toolName);
                    }
                    else
                    {
                        throw new CircuitBreakerOpenException(_toolName,
                            _openedAt + _config.EffectiveRecoveryTimeout);
                    }
                    break;

                case CircuitBreakerState.HalfOpen:
                    // Allow the probe call to proceed
                    break;

                case CircuitBreakerState.Closed:
                    break;
            }
        }
        finally
        {
            _gate.Release();
        }

        // Execute the action outside the lock
        try
        {
            var result = await action(ct);

            await _gate.WaitAsync(ct);
            try
            {
                OnSuccess();
            }
            finally
            {
                _gate.Release();
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not CircuitBreakerOpenException)
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                OnFailure(ex);
            }
            finally
            {
                _gate.Release();
            }

            throw;
        }
    }

    /// <summary>
    /// Execute a void action through the circuit breaker.
    /// </summary>
    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        await ExecuteAsync<object?>(async token =>
        {
            await action(token);
            return null;
        }, ct);
    }

    /// <summary>
    /// Manually reset the circuit breaker to Closed state.
    /// </summary>
    public async Task ResetAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _state = CircuitBreakerState.Closed;
            _consecutiveFailures = 0;
            _halfOpenSuccesses = 0;
            _logger.LogInformation("[CircuitBreaker:{Tool}] Manually reset to Closed", _toolName);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnSuccess()
    {
        switch (_state)
        {
            case CircuitBreakerState.HalfOpen:
                _halfOpenSuccesses++;
                if (_halfOpenSuccesses >= _config.SuccessThresholdInHalfOpen)
                {
                    _state = CircuitBreakerState.Closed;
                    _consecutiveFailures = 0;
                    _halfOpenSuccesses = 0;
                    _logger.LogInformation(
                        "[CircuitBreaker:{Tool}] HalfOpen → Closed (probe succeeded)", _toolName);
                }
                break;

            case CircuitBreakerState.Closed:
                _consecutiveFailures = 0;
                break;
        }
    }

    private void OnFailure(Exception ex)
    {
        _consecutiveFailures++;

        switch (_state)
        {
            case CircuitBreakerState.Closed:
                if (_consecutiveFailures >= _config.FailureThreshold)
                {
                    _state = CircuitBreakerState.Open;
                    _openedAt = DateTimeOffset.UtcNow;
                    _logger.LogWarning(
                        "[CircuitBreaker:{Tool}] Closed → Open ({Failures} consecutive failures): {Error}",
                        _toolName, _consecutiveFailures, ex.Message);
                }
                break;

            case CircuitBreakerState.HalfOpen:
                _state = CircuitBreakerState.Open;
                _openedAt = DateTimeOffset.UtcNow;
                _halfOpenSuccesses = 0;
                _logger.LogWarning(
                    "[CircuitBreaker:{Tool}] HalfOpen → Open (probe failed): {Error}",
                    _toolName, ex.Message);
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }
}

/// <summary>
/// Exception thrown when a circuit breaker is in the Open state and rejects a call.
/// </summary>
public sealed class CircuitBreakerOpenException : Exception
{
    /// <summary>Name of the tool whose circuit is open.</summary>
    public string ToolName { get; }

    /// <summary>When the circuit breaker is expected to transition to HalfOpen.</summary>
    public DateTimeOffset RetryAfter { get; }

    public CircuitBreakerOpenException(string toolName, DateTimeOffset retryAfter)
        : base($"Circuit breaker for tool '{toolName}' is open. Retry after {retryAfter:O}.")
    {
        ToolName = toolName;
        RetryAfter = retryAfter;
    }
}