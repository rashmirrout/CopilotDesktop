using System.Diagnostics;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Represents the current streaming state of a session.
/// Used to determine appropriate timeout behavior and user messaging.
/// </summary>
public enum SessionStreamingState
{
    /// <summary>
    /// Session is idle, waiting for initial response or between operations.
    /// Timeout: IdleTimeoutSeconds
    /// </summary>
    Idle,
    
    /// <summary>
    /// Receiving streaming message chunks from LLM.
    /// Timeout: IdleTimeoutSeconds (content should arrive quickly)
    /// </summary>
    Streaming,
    
    /// <summary>
    /// Tool is currently executing (file ops, network, etc.).
    /// Timeout: ToolExecutionTimeoutSeconds (tools can take time)
    /// </summary>
    ToolExecuting,
    
    /// <summary>
    /// Waiting for user to approve or deny a tool.
    /// Timeout: ApprovalWaitTimeoutSeconds (typically infinite)
    /// </summary>
    WaitingForApproval,
    
    /// <summary>
    /// Session has completed successfully (SessionIdleEvent received).
    /// No timeout applies.
    /// </summary>
    Completed,
    
    /// <summary>
    /// Session encountered an error.
    /// No timeout applies.
    /// </summary>
    Error
}

/// <summary>
/// Tracks the current state and active operations for a streaming session.
/// This class manages state transitions, timeout calculations, and progress reporting.
/// 
/// Thread-safety: This class is designed to be used from a single thread (the streaming loop).
/// State updates should be atomic with respect to timeout checks.
/// 
/// Enterprise Feature: Smart Event-Rate Based Timeout
/// ---------------------------------------------------
/// This class implements an intelligent timeout algorithm that considers event flow patterns:
/// 1. Rolling window tracking: Maintains timestamps of recent events (default: 2 minute window)
/// 2. Event rate calculation: Computes events-per-minute to detect active streaming
/// 3. Adaptive timeout: If event rate is healthy (>= 1 event/min), timeout is suppressed
/// 4. Minimum event threshold: If recent events exist, never timeout even with gaps
/// 
/// This prevents false timeouts in scenarios like:
/// - Long tool executions that emit events periodically
/// - Playbooks with many sequential tools
/// - Network delays that cause event bursts
/// </summary>
public class SessionStreamingContext
{
    private readonly object _lock = new();
    private int _warningCount;
    private DateTime _lastProgressUpdateTime;
    
    /// <summary>
    /// Rolling window of event timestamps for rate calculation.
    /// Uses a circular buffer approach with cleanup on access.
    /// </summary>
    private readonly Queue<DateTime> _eventTimestamps = new();
    
    /// <summary>
    /// Rolling window duration for event rate calculation (in seconds).
    /// Events older than this are pruned from the tracking queue.
    /// Default: 120 seconds (2 minutes) provides good smoothing for bursty patterns.
    /// </summary>
    private const double RollingWindowSeconds = 120.0;
    
    /// <summary>
    /// Minimum event rate (events per minute) to consider the session "active".
    /// If the rolling event rate is at or above this threshold, timeout is suppressed.
    /// Default: 1.0 event/min is very conservative - even sparse events prevent timeout.
    /// </summary>
    private const double MinActiveEventRate = 1.0;
    
    /// <summary>
    /// Minimum number of events in rolling window to suppress timeout.
    /// Even if rate calculation is low, having this many events indicates activity.
    /// Default: 3 events in window means something is happening.
    /// </summary>
    private const int MinEventsInWindow = 3;
    
    /// <summary>
    /// Current state of the streaming session.
    /// </summary>
    public SessionStreamingState State { get; private set; } = SessionStreamingState.Idle;
    
    /// <summary>
    /// Time when the last event was received (any event).
    /// Used for activity-based timeout extension.
    /// </summary>
    public DateTime LastActivityTime { get; private set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Time when the current state was entered.
    /// Used for state-based timeout calculation.
    /// </summary>
    public DateTime StateStartTime { get; private set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Total number of events received in this streaming session.
    /// </summary>
    public int EventCount { get; private set; }
    
    /// <summary>
    /// Name of the currently executing tool (if any).
    /// </summary>
    public string? CurrentToolName { get; private set; }
    
    /// <summary>
    /// Number of tools that have completed execution.
    /// </summary>
    public int ToolsExecutedCount { get; private set; }
    
    /// <summary>
    /// Estimated total number of tools in the current operation (if known).
    /// This is estimated from context and may not be accurate.
    /// </summary>
    public int TotalToolsEstimated { get; set; }
    
    /// <summary>
    /// Session ID for logging purposes.
    /// </summary>
    public string SessionId { get; }
    
    /// <summary>
    /// Creates a new streaming context for a session.
    /// </summary>
    public SessionStreamingContext(string sessionId)
    {
        SessionId = sessionId;
        _lastProgressUpdateTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the appropriate timeout for the current state.
    /// </summary>
    public TimeSpan GetCurrentTimeout(StreamingTimeoutSettings settings)
    {
        lock (_lock)
        {
            return State switch
            {
                SessionStreamingState.ToolExecuting => settings.ToolExecutionTimeout,
                SessionStreamingState.WaitingForApproval => settings.ApprovalWaitTimeout,
                SessionStreamingState.Completed or SessionStreamingState.Error => TimeSpan.MaxValue,
                SessionStreamingState.Idle or SessionStreamingState.Streaming or _ => settings.IdleTimeout
            };
        }
    }
    
    /// <summary>
    /// Gets the elapsed time since entering the current state.
    /// </summary>
    public TimeSpan GetStateElapsedTime()
    {
        lock (_lock)
        {
            return DateTime.UtcNow - StateStartTime;
        }
    }
    
    /// <summary>
    /// Gets the elapsed time since the last activity (any event).
    /// </summary>
    public TimeSpan GetActivityElapsedTime()
    {
        lock (_lock)
        {
            return DateTime.UtcNow - LastActivityTime;
        }
    }
    
    /// <summary>
    /// Checks if the warning threshold has been reached for the current state.
    /// Returns false if:
    /// - Timeout is infinite
    /// - State just started (less than 5 seconds ago)
    /// - Maximum consecutive warnings exceeded
    /// </summary>
    public bool ShouldShowWarning(StreamingTimeoutSettings settings)
    {
        lock (_lock)
        {
            var timeout = GetCurrentTimeout(settings);
            if (timeout == TimeSpan.MaxValue) 
                return false;
            
            // Don't warn if we're in a terminal state
            if (State == SessionStreamingState.Completed || State == SessionStreamingState.Error)
                return false;
            
            // Check max consecutive warnings
            if (settings.MaxConsecutiveWarnings > 0 && _warningCount >= settings.MaxConsecutiveWarnings)
                return false;
            
            var elapsed = settings.ExtendTimeoutOnActivity 
                ? GetActivityElapsedTime() 
                : GetStateElapsedTime();
            
            // Don't warn for very short durations (avoids flickering)
            if (elapsed.TotalSeconds < 5)
                return false;
            
            var warningThreshold = timeout.TotalSeconds * settings.WarningThresholdPercentage;
            return elapsed.TotalSeconds >= warningThreshold;
        }
    }
    
    /// <summary>
    /// Checks if the timeout has been exceeded for the current state.
    /// Returns false if timeout is infinite.
    /// 
    /// IMPORTANT: This method implements a multi-layered timeout protection:
    /// 1. Recent activity check - if event received within threshold, no timeout
    /// 2. Event rate check - if events flowing at healthy rate, no timeout
    /// 3. Rolling window check - if enough events in window, no timeout
    /// 4. Traditional timeout - only then check elapsed time vs limit
    /// 
    /// This prevents false timeouts during active tool execution where events are flowing.
    /// </summary>
    public bool IsTimedOut(StreamingTimeoutSettings settings)
    {
        lock (_lock)
        {
            var timeout = GetCurrentTimeout(settings);
            if (timeout == TimeSpan.MaxValue) 
                return false;
            
            // Don't timeout if we're in a terminal state
            if (State == SessionStreamingState.Completed || State == SessionStreamingState.Error)
                return false;
            
            // Calculate elapsed times
            var activityElapsed = GetActivityElapsedTime();
            var stateElapsed = GetStateElapsedTime();
            
            // LAYER 1: Recent activity threshold (configurable, default 30s)
            // If ANY event received recently, absolutely no timeout
            if (activityElapsed < settings.RecentActivityThreshold)
            {
                return false;
            }
            
            // LAYER 2: Event rate check (smart timeout)
            // If events are flowing at a healthy rate, don't timeout
            var eventRate = GetEventRatePerMinute();
            if (eventRate >= MinActiveEventRate)
            {
                return false;
            }
            
            // LAYER 3: Rolling window event count
            // If we have enough events in the window, something is happening
            var eventsInWindow = GetEventsInRollingWindow();
            if (eventsInWindow >= MinEventsInWindow)
            {
                // Additional check: if last event was within 2x the threshold, don't timeout
                // This handles bursty patterns where events come in groups
                if (activityElapsed < TimeSpan.FromSeconds(settings.RecentActivityThresholdSeconds * 2))
                {
                    return false;
                }
            }
            
            // LAYER 4: Traditional timeout check
            var elapsed = settings.ExtendTimeoutOnActivity 
                ? activityElapsed 
                : stateElapsed;
            
            return elapsed >= timeout;
        }
    }
    
    /// <summary>
    /// Gets detailed timeout diagnostic information for logging.
    /// Call this before timeout to understand why it's happening.
    /// </summary>
    public TimeoutDiagnostics GetTimeoutDiagnostics(StreamingTimeoutSettings settings)
    {
        lock (_lock)
        {
            return new TimeoutDiagnostics
            {
                State = State,
                CurrentToolName = CurrentToolName,
                TotalEventCount = EventCount,
                EventsInRollingWindow = GetEventsInRollingWindowUnsafe(),
                EventRatePerMinute = GetEventRatePerMinuteUnsafe(),
                ActivityElapsed = GetActivityElapsedTime(),
                StateElapsed = GetStateElapsedTime(),
                ConfiguredTimeout = GetCurrentTimeout(settings),
                RecentActivityThreshold = settings.RecentActivityThreshold,
                SessionId = SessionId,
                ToolsExecuted = ToolsExecutedCount
            };
        }
    }
    
    /// <summary>
    /// Records that an event was received, updating activity time and event count.
    /// Call this for every SDK event received.
    /// Also tracks event timestamp in rolling window for rate calculation.
    /// </summary>
    public void RecordEvent()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            EventCount++;
            LastActivityTime = now;
            _warningCount = 0; // Reset warning count on new activity
            
            // Add to rolling window for rate tracking
            _eventTimestamps.Enqueue(now);
            
            // Prune old events from window (cleanup on write)
            PruneOldEventsUnsafe(now);
        }
    }
    
    /// <summary>
    /// Gets the number of events in the rolling window.
    /// </summary>
    public int GetEventsInRollingWindow()
    {
        lock (_lock)
        {
            return GetEventsInRollingWindowUnsafe();
        }
    }
    
    /// <summary>
    /// Gets the current event rate in events per minute.
    /// Calculated over the rolling window.
    /// </summary>
    public double GetEventRatePerMinute()
    {
        lock (_lock)
        {
            return GetEventRatePerMinuteUnsafe();
        }
    }
    
    // Internal helpers (must hold lock)
    
    private int GetEventsInRollingWindowUnsafe()
    {
        PruneOldEventsUnsafe(DateTime.UtcNow);
        return _eventTimestamps.Count;
    }
    
    private double GetEventRatePerMinuteUnsafe()
    {
        var now = DateTime.UtcNow;
        PruneOldEventsUnsafe(now);
        
        if (_eventTimestamps.Count < 2)
            return 0.0;
        
        // Calculate actual window duration from oldest event to now
        var oldestEvent = _eventTimestamps.Peek();
        var windowDuration = (now - oldestEvent).TotalMinutes;
        
        if (windowDuration < 0.001) // Less than ~60ms
            return 0.0;
        
        // Events per minute = count / duration in minutes
        return _eventTimestamps.Count / windowDuration;
    }
    
    private void PruneOldEventsUnsafe(DateTime now)
    {
        var cutoff = now.AddSeconds(-RollingWindowSeconds);
        
        while (_eventTimestamps.Count > 0 && _eventTimestamps.Peek() < cutoff)
        {
            _eventTimestamps.Dequeue();
        }
    }
    
    /// <summary>
    /// Transitions to a new state, resetting state-specific timers.
    /// </summary>
    public void TransitionTo(SessionStreamingState newState, string? toolName = null)
    {
        lock (_lock)
        {
            var previousState = State;
            State = newState;
            StateStartTime = DateTime.UtcNow;
            LastActivityTime = DateTime.UtcNow;
            _warningCount = 0;
            
            if (newState == SessionStreamingState.ToolExecuting)
            {
                CurrentToolName = toolName;
            }
            else
            {
                // If transitioning away from tool execution, count it
                if (previousState == SessionStreamingState.ToolExecuting && 
                    newState != SessionStreamingState.WaitingForApproval)
                {
                    ToolsExecutedCount++;
                }
                
                if (newState != SessionStreamingState.WaitingForApproval)
                {
                    CurrentToolName = null;
                }
            }
        }
    }
    
    /// <summary>
    /// Marks that a tool has started execution.
    /// </summary>
    public void StartToolExecution(string toolName)
    {
        TransitionTo(SessionStreamingState.ToolExecuting, toolName);
    }
    
    /// <summary>
    /// Marks that a tool has completed execution.
    /// </summary>
    public void CompleteToolExecution()
    {
        lock (_lock)
        {
            ToolsExecutedCount++;
            TransitionTo(SessionStreamingState.Idle);
        }
    }
    
    /// <summary>
    /// Marks that we're waiting for user approval.
    /// </summary>
    public void StartWaitingForApproval(string toolName)
    {
        lock (_lock)
        {
            CurrentToolName = toolName;
            TransitionTo(SessionStreamingState.WaitingForApproval, toolName);
        }
    }
    
    /// <summary>
    /// Marks that user approval has been received (or denied).
    /// </summary>
    public void CompleteApproval()
    {
        // Transition back to tool execution if tool is about to run
        // or idle if tool was denied
        TransitionTo(SessionStreamingState.Idle);
    }
    
    /// <summary>
    /// Increments the warning count and returns whether max warnings reached.
    /// </summary>
    public bool IncrementWarningCount(int maxWarnings)
    {
        lock (_lock)
        {
            _warningCount++;
            return maxWarnings > 0 && _warningCount >= maxWarnings;
        }
    }
    
    /// <summary>
    /// Checks if enough time has passed since last progress update.
    /// Used to throttle progress messages.
    /// </summary>
    public bool ShouldUpdateProgress(StreamingTimeoutSettings settings)
    {
        lock (_lock)
        {
            if (!settings.EnableProgressTracking)
                return false;
            
            var elapsed = DateTime.UtcNow - _lastProgressUpdateTime;
            return elapsed >= settings.ProgressUpdateInterval;
        }
    }
    
    /// <summary>
    /// Marks that a progress update was sent.
    /// </summary>
    public void MarkProgressUpdated()
    {
        lock (_lock)
        {
            _lastProgressUpdateTime = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// Gets a human-readable progress message for the current state.
    /// </summary>
    public string GetProgressMessage(StreamingTimeoutSettings settings)
    {
        lock (_lock)
        {
            var messages = new List<string>();
            
            switch (State)
            {
                case SessionStreamingState.ToolExecuting when CurrentToolName != null:
                    if (TotalToolsEstimated > 0)
                        messages.Add($"Executing: {CurrentToolName} ({ToolsExecutedCount + 1}/{TotalToolsEstimated})");
                    else if (ToolsExecutedCount > 0)
                        messages.Add($"Executing: {CurrentToolName} (step {ToolsExecutedCount + 1})");
                    else
                        messages.Add($"Executing: {CurrentToolName}");
                    break;
                
                case SessionStreamingState.WaitingForApproval:
                    messages.Add($"Waiting for approval: {CurrentToolName ?? "tool"}");
                    break;
                
                case SessionStreamingState.Streaming:
                    messages.Add("Receiving response...");
                    break;
                
                case SessionStreamingState.Idle:
                    if (ToolsExecutedCount > 0)
                        messages.Add($"Processing... ({ToolsExecutedCount} tool(s) completed)");
                    else
                        messages.Add("Waiting for response...");
                    break;
                
                case SessionStreamingState.Completed:
                    messages.Add("Completed");
                    break;
                
                case SessionStreamingState.Error:
                    messages.Add("Error occurred");
                    break;
            }
            
            if (settings.ShowElapsedTime && 
                State != SessionStreamingState.Completed && 
                State != SessionStreamingState.Error)
            {
                var elapsed = GetStateElapsedTime();
                if (elapsed.TotalSeconds >= 2) // Only show if meaningful duration
                {
                    messages.Add($"{elapsed.TotalSeconds:F0}s");
                }
            }
            
            return string.Join(" | ", messages);
        }
    }
    
    /// <summary>
    /// Gets a detailed timeout warning message.
    /// </summary>
    public string GetTimeoutWarningMessage(StreamingTimeoutSettings settings)
    {
        lock (_lock)
        {
            var timeout = GetCurrentTimeout(settings);
            var elapsed = settings.ExtendTimeoutOnActivity 
                ? GetActivityElapsedTime() 
                : GetStateElapsedTime();
            var remaining = timeout - elapsed;
            
            var stateDescription = State switch
            {
                SessionStreamingState.ToolExecuting => $"Tool '{CurrentToolName}'",
                SessionStreamingState.WaitingForApproval => "Approval wait",
                SessionStreamingState.Idle => "Response wait",
                SessionStreamingState.Streaming => "Streaming",
                _ => "Operation"
            };
            
            if (remaining.TotalSeconds <= 5)
            {
                return $"⚠️ {stateDescription} about to timeout ({elapsed.TotalSeconds:F0}s/{timeout.TotalSeconds:F0}s)";
            }
            
            return $"⏱️ {stateDescription} taking longer than expected ({elapsed.TotalSeconds:F0}s/{timeout.TotalSeconds:F0}s)";
        }
    }
    
    /// <summary>
    /// Gets a detailed timeout message when timeout has been exceeded.
    /// </summary>
    public string GetTimeoutMessage(StreamingTimeoutSettings settings)
    {
        lock (_lock)
        {
            var timeout = GetCurrentTimeout(settings);
            var elapsed = settings.ExtendTimeoutOnActivity 
                ? GetActivityElapsedTime() 
                : GetStateElapsedTime();
            
            var stateDescription = State switch
            {
                SessionStreamingState.ToolExecuting => $"Tool '{CurrentToolName}' execution",
                SessionStreamingState.WaitingForApproval => "Waiting for approval",
                SessionStreamingState.Idle => "Waiting for response",
                SessionStreamingState.Streaming => "Streaming response",
                _ => "Operation"
            };
            
            return $"[{stateDescription} timed out after {elapsed.TotalSeconds:F0}s " +
                   $"(limit: {timeout.TotalSeconds:F0}s). Events received: {EventCount}]";
        }
    }
    
    /// <summary>
    /// Returns a summary of the current context for logging.
    /// </summary>
    public override string ToString()
    {
        lock (_lock)
        {
            return $"SessionStreamingContext[{SessionId}]: State={State}, " +
                   $"Tool={CurrentToolName ?? "none"}, " +
                   $"Events={EventCount}, " +
                   $"EventsInWindow={GetEventsInRollingWindowUnsafe()}, " +
                   $"Rate={GetEventRatePerMinuteUnsafe():F1}/min, " +
                   $"ToolsCompleted={ToolsExecutedCount}, " +
                   $"StateElapsed={GetStateElapsedTime().TotalSeconds:F1}s, " +
                   $"LastActivity={GetActivityElapsedTime().TotalSeconds:F1}s ago";
        }
    }
}

/// <summary>
/// Diagnostic information for timeout decisions.
/// Used for logging and debugging timeout issues.
/// </summary>
public class TimeoutDiagnostics
{
    public SessionStreamingState State { get; init; }
    public string? CurrentToolName { get; init; }
    public int TotalEventCount { get; init; }
    public int EventsInRollingWindow { get; init; }
    public double EventRatePerMinute { get; init; }
    public TimeSpan ActivityElapsed { get; init; }
    public TimeSpan StateElapsed { get; init; }
    public TimeSpan ConfiguredTimeout { get; init; }
    public TimeSpan RecentActivityThreshold { get; init; }
    public string SessionId { get; init; } = "";
    public int ToolsExecuted { get; init; }
    
    public override string ToString()
    {
        return $"TimeoutDiagnostics[{SessionId}]: " +
               $"State={State}, Tool={CurrentToolName ?? "none"}, " +
               $"TotalEvents={TotalEventCount}, WindowEvents={EventsInRollingWindow}, " +
               $"Rate={EventRatePerMinute:F2}/min, " +
               $"ActivityGap={ActivityElapsed.TotalSeconds:F1}s, StateAge={StateElapsed.TotalSeconds:F1}s, " +
               $"Timeout={ConfiguredTimeout.TotalSeconds:F0}s, Threshold={RecentActivityThreshold.TotalSeconds:F0}s, " +
               $"ToolsCompleted={ToolsExecuted}";
    }
    
    /// <summary>
    /// Explains why timeout would or would not trigger.
    /// </summary>
    public string GetTimeoutExplanation()
    {
        var reasons = new List<string>();
        
        if (State == SessionStreamingState.Completed || State == SessionStreamingState.Error)
        {
            reasons.Add($"Terminal state ({State}) - no timeout");
            return string.Join("; ", reasons);
        }
        
        if (ConfiguredTimeout == TimeSpan.MaxValue)
        {
            reasons.Add("Infinite timeout configured");
            return string.Join("; ", reasons);
        }
        
        // Check each layer
        if (ActivityElapsed < RecentActivityThreshold)
        {
            reasons.Add($"PROTECTED: Recent activity {ActivityElapsed.TotalSeconds:F1}s < threshold {RecentActivityThreshold.TotalSeconds:F0}s");
        }
        else if (EventRatePerMinute >= 1.0)
        {
            reasons.Add($"PROTECTED: Event rate {EventRatePerMinute:F2}/min >= 1.0/min");
        }
        else if (EventsInRollingWindow >= 3 && ActivityElapsed < TimeSpan.FromSeconds(RecentActivityThreshold.TotalSeconds * 2))
        {
            reasons.Add($"PROTECTED: {EventsInRollingWindow} events in window, activity gap {ActivityElapsed.TotalSeconds:F1}s < 2x threshold");
        }
        else if (ActivityElapsed >= ConfiguredTimeout)
        {
            reasons.Add($"TIMEOUT: Activity gap {ActivityElapsed.TotalSeconds:F1}s >= timeout {ConfiguredTimeout.TotalSeconds:F0}s");
        }
        else
        {
            reasons.Add($"OK: Activity gap {ActivityElapsed.TotalSeconds:F1}s < timeout {ConfiguredTimeout.TotalSeconds:F0}s");
        }
        
        return string.Join("; ", reasons);
    }
}
