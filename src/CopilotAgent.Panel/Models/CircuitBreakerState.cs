// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CopilotAgent.Panel.Models;

/// <summary>
/// Represents the state of a circuit breaker for tool execution resilience.
/// Follows the standard circuit breaker pattern: Closed → Open → HalfOpen → Closed.
/// </summary>
public enum CircuitBreakerState
{
    /// <summary>Circuit is closed — tool calls proceed normally.</summary>
    Closed,

    /// <summary>Circuit is open — tool calls are rejected immediately after repeated failures.</summary>
    Open,

    /// <summary>Circuit is half-open — a single probe call is allowed to test recovery.</summary>
    HalfOpen
}

/// <summary>
/// Configuration for the tool circuit breaker.
/// </summary>
/// <param name="FailureThreshold">Number of consecutive failures before opening the circuit.</param>
/// <param name="RecoveryTimeout">Time to wait before transitioning from Open to HalfOpen.</param>
/// <param name="SuccessThresholdInHalfOpen">
/// Number of consecutive successes in HalfOpen state required to close the circuit.
/// </param>
public sealed record CircuitBreakerConfig(
    int FailureThreshold = 3,
    TimeSpan? RecoveryTimeout = null,
    int SuccessThresholdInHalfOpen = 1)
{
    /// <summary>Default recovery timeout of 30 seconds.</summary>
    public TimeSpan EffectiveRecoveryTimeout =>
        RecoveryTimeout ?? TimeSpan.FromSeconds(30);

    /// <summary>Default configuration suitable for most tool scenarios.</summary>
    public static CircuitBreakerConfig Default => new();
}