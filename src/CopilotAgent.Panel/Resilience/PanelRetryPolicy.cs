// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CopilotAgent.Panel.Resilience;

/// <summary>
/// Retry policy with exponential backoff and jitter for transient failures.
/// Used by tool execution and LLM call wrappers.
///
/// ALGORITHM:
///   delay = min(base * 2^attempt ± jitter, maxDelay)
///   jitter = ±25% of computed delay
///
/// THREAD SAFETY: Stateless — safe for concurrent use.
/// </summary>
public sealed class PanelRetryPolicy
{
    private static readonly Random s_jitterRng = new();

    /// <summary>Maximum number of retry attempts.</summary>
    public int MaxRetries { get; }

    /// <summary>Base delay for the first retry.</summary>
    public TimeSpan BaseDelay { get; }

    /// <summary>Maximum delay cap to prevent excessively long waits.</summary>
    public TimeSpan MaxDelay { get; }

    /// <summary>Jitter factor (0.0 – 1.0). Default 0.25 = ±25%.</summary>
    public double JitterFactor { get; }

    /// <summary>
    /// Default retry policy: 3 retries, 1s base, 60s max, ±25% jitter.
    /// </summary>
    public static PanelRetryPolicy Default => new(
        maxRetries: 3,
        baseDelay: TimeSpan.FromSeconds(1),
        maxDelay: TimeSpan.FromSeconds(60),
        jitterFactor: 0.25);

    /// <summary>
    /// Aggressive retry policy for critical operations: 5 retries, 500ms base.
    /// </summary>
    public static PanelRetryPolicy Aggressive => new(
        maxRetries: 5,
        baseDelay: TimeSpan.FromMilliseconds(500),
        maxDelay: TimeSpan.FromSeconds(30),
        jitterFactor: 0.25);

    /// <summary>
    /// No retry policy — used when retries are not desired.
    /// </summary>
    public static PanelRetryPolicy None => new(
        maxRetries: 0,
        baseDelay: TimeSpan.Zero,
        maxDelay: TimeSpan.Zero,
        jitterFactor: 0);

    public PanelRetryPolicy(
        int maxRetries,
        TimeSpan baseDelay,
        TimeSpan maxDelay,
        double jitterFactor = 0.25)
    {
        MaxRetries = Math.Max(0, maxRetries);
        BaseDelay = baseDelay;
        MaxDelay = maxDelay;
        JitterFactor = Math.Clamp(jitterFactor, 0.0, 1.0);
    }

    /// <summary>
    /// Compute the delay for a given retry attempt (0-based).
    /// </summary>
    /// <param name="attempt">The retry attempt number (0 = first retry).</param>
    /// <returns>The computed delay with jitter applied.</returns>
    public TimeSpan GetDelay(int attempt)
    {
        if (attempt < 0 || MaxRetries == 0)
            return TimeSpan.Zero;

        // Exponential backoff: base * 2^attempt
        var exponentialMs = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt);

        // Cap at max delay
        var cappedMs = Math.Min(exponentialMs, MaxDelay.TotalMilliseconds);

        // Apply jitter: ±JitterFactor
        double jitteredMs;
        lock (s_jitterRng)
        {
            var jitterRange = cappedMs * JitterFactor;
            var jitter = (s_jitterRng.NextDouble() * 2 - 1) * jitterRange; // [-range, +range]
            jitteredMs = cappedMs + jitter;
        }

        // Ensure non-negative
        return TimeSpan.FromMilliseconds(Math.Max(0, jitteredMs));
    }

    /// <summary>
    /// Execute an async action with retry logic.
    /// </summary>
    /// <typeparam name="T">Return type.</typeparam>
    /// <param name="action">The action to retry.</param>
    /// <param name="shouldRetry">
    /// Predicate to determine if an exception is retryable.
    /// Defaults to retrying all exceptions except <see cref="OperationCanceledException"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the successful action.</returns>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        Func<Exception, bool>? shouldRetry = null,
        CancellationToken ct = default)
    {
        shouldRetry ??= static ex => ex is not OperationCanceledException;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action(ct);
            }
            catch (Exception ex) when (attempt < MaxRetries && shouldRetry(ex))
            {
                var delay = GetDelay(attempt);
                await Task.Delay(delay, ct);
            }
        }
    }

    /// <summary>
    /// Execute a void async action with retry logic.
    /// </summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        Func<Exception, bool>? shouldRetry = null,
        CancellationToken ct = default)
    {
        await ExecuteAsync<object?>(async token =>
        {
            await action(token);
            return null;
        }, shouldRetry, ct);
    }
}