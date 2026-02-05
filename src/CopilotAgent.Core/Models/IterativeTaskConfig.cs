using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Configuration for iterative agent task execution
/// </summary>
public class IterativeTaskConfig
{
    /// <summary>Task description</summary>
    [JsonPropertyName("taskDescription")]
    public string TaskDescription { get; set; } = string.Empty;

    /// <summary>Success criteria</summary>
    [JsonPropertyName("successCriteria")]
    public string SuccessCriteria { get; set; } = string.Empty;

    /// <summary>Maximum number of iterations</summary>
    [JsonPropertyName("maxIterations")]
    public int MaxIterations { get; set; } = 10;

    /// <summary>Current state of the task</summary>
    [JsonPropertyName("state")]
    public IterativeTaskState State { get; set; } = new();

    /// <summary>When the task was started</summary>
    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    /// <summary>When the task completed or was stopped</summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// State tracking for an iterative task
/// </summary>
public class IterativeTaskState
{
    /// <summary>Current iteration number (0-based)</summary>
    [JsonPropertyName("currentIteration")]
    public int CurrentIteration { get; set; }

    /// <summary>Overall status of the task</summary>
    [JsonPropertyName("status")]
    public IterativeTaskStatus Status { get; set; } = IterativeTaskStatus.NotStarted;

    /// <summary>History of iteration results</summary>
    [JsonPropertyName("iterations")]
    public List<IterationResult> Iterations { get; set; } = new();

    /// <summary>Reason for completion or stoppage</summary>
    [JsonPropertyName("completionReason")]
    public string? CompletionReason { get; set; }
}

/// <summary>
/// Result of a single iteration in a task with detailed tool execution history
/// </summary>
public class IterationResult
{
    /// <summary>Iteration number</summary>
    [JsonPropertyName("iterationNumber")]
    public int IterationNumber { get; set; }

    /// <summary>Human-readable summary of what was accomplished</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>What action was taken (legacy, kept for compatibility)</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>Result of the action (legacy, kept for compatibility)</summary>
    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    /// <summary>Self-evaluation: Is task complete?</summary>
    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; set; }

    /// <summary>Explanation of evaluation</summary>
    [JsonPropertyName("evaluation")]
    public string Evaluation { get; set; } = string.Empty;

    /// <summary>When this iteration started</summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this iteration completed</summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>Duration in milliseconds</summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs => CompletedAt.HasValue 
        ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds 
        : 0;

    /// <summary>Current status of this iteration</summary>
    [JsonPropertyName("status")]
    public IterationStatus Status { get; set; } = IterationStatus.Pending;

    /// <summary>Error message if iteration failed</summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>All tool executions in this iteration</summary>
    [JsonPropertyName("toolExecutions")]
    public List<ToolExecutionDetail> ToolExecutions { get; set; } = new();

    /// <summary>Agent reasoning/thinking steps</summary>
    [JsonPropertyName("agentReasoning")]
    public List<ReasoningStep> AgentReasoning { get; set; } = new();

    /// <summary>Tool currently being executed (for progress display)</summary>
    [JsonIgnore]
    public string? CurrentToolName { get; set; }

    /// <summary>Header text for UI display</summary>
    [JsonIgnore]
    public string HeaderText => $"Iteration {IterationNumber}: {Summary}";

    /// <summary>Whether there are reasoning steps to display</summary>
    [JsonIgnore]
    public bool HasReasoning => AgentReasoning.Count > 0;

    /// <summary>Color for status indicator</summary>
    [JsonIgnore]
    public string StatusColor => Status switch
    {
        IterationStatus.Completed => "#4CAF50", // Green
        IterationStatus.Failed => "#F44336",    // Red
        IterationStatus.Cancelled => "#FF9800", // Orange
        IterationStatus.Running => "#2196F3",   // Blue
        _ => "#9E9E9E"                           // Gray
    };
}

/// <summary>
/// Detailed information about a tool execution within an iteration
/// </summary>
public class ToolExecutionDetail
{
    /// <summary>Unique identifier for this tool call</summary>
    [JsonPropertyName("toolCallId")]
    public string ToolCallId { get; set; } = string.Empty;

    /// <summary>Name of the tool that was executed</summary>
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    /// <summary>User-friendly display name for the tool</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>Arguments passed to the tool</summary>
    [JsonPropertyName("arguments")]
    public object? Arguments { get; set; }

    /// <summary>Human-readable summary of the arguments</summary>
    [JsonPropertyName("argumentsSummary")]
    public string? ArgumentsSummary { get; set; }

    /// <summary>When the tool started executing</summary>
    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    /// <summary>When the tool finished executing</summary>
    [JsonPropertyName("endTime")]
    public DateTime? EndTime { get; set; }

    /// <summary>Duration of tool execution</summary>
    [JsonIgnore]
    public TimeSpan Duration => EndTime.HasValue 
        ? EndTime.Value - StartTime 
        : TimeSpan.Zero;

    /// <summary>Duration in human-readable format</summary>
    [JsonIgnore]
    public string DurationText => Duration.TotalSeconds < 1 
        ? $"{Duration.TotalMilliseconds:0}ms" 
        : $"{Duration.TotalSeconds:0.0}s";

    /// <summary>Whether the tool executed successfully</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>Result summary from the tool</summary>
    [JsonPropertyName("result")]
    public string? Result { get; set; }

    /// <summary>Full detailed result (may be large)</summary>
    [JsonPropertyName("detailedResult")]
    public string? DetailedResult { get; set; }

    /// <summary>Error message if tool failed</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Additional telemetry data</summary>
    [JsonPropertyName("telemetry")]
    public Dictionary<string, object>? Telemetry { get; set; }

    /// <summary>Whether this tool is currently executing</summary>
    [JsonIgnore]
    public bool IsRunning => EndTime == null;

    /// <summary>MCP server name if this was an MCP tool</summary>
    [JsonPropertyName("mcpServerName")]
    public string? McpServerName { get; set; }

    /// <summary>Header for UI display</summary>
    [JsonIgnore]
    public string ToolDisplayHeader
    {
        get
        {
            var icon = GetToolIcon();
            var status = Success ? "✓" : (Error != null ? "✗" : "⋯");
            var name = DisplayName ?? GetFriendlyToolName(ToolName);
            return $"{icon} {name} [{status}] ({DurationText})";
        }
    }

    /// <summary>Gets an icon for the tool type</summary>
    private string GetToolIcon()
    {
        var lowerName = ToolName.ToLowerInvariant();
        
        if (lowerName.Contains("write") || lowerName.Contains("create"))
            return "📝";
        if (lowerName.Contains("read") || lowerName.Contains("view"))
            return "📖";
        if (lowerName.Contains("exec") || lowerName.Contains("command") || 
            lowerName.Contains("shell") || lowerName.Contains("run"))
            return "⚡";
        if (lowerName.Contains("search") || lowerName.Contains("find") || lowerName.Contains("grep"))
            return "🔍";
        if (lowerName.Contains("list") || lowerName.Contains("directory"))
            return "📁";
        if (lowerName.Contains("delete") || lowerName.Contains("remove"))
            return "🗑️";
        if (lowerName.Contains("http") || lowerName.Contains("fetch") || lowerName.Contains("web"))
            return "🌐";
        if (lowerName.Contains("git"))
            return "📦";
        
        return "🔧";
    }

    /// <summary>Gets a friendly name for common tools</summary>
    public static string GetFriendlyToolName(string toolName)
    {
        return toolName.ToLowerInvariant() switch
        {
            "write_to_file" => "Write File",
            "read_file" => "Read File",
            "execute_command" => "Execute Command",
            "search_files" => "Search Files",
            "list_files" => "List Files",
            "list_code_definition_names" => "List Definitions",
            "replace_in_file" => "Edit File",
            "ask_followup_question" => "Ask Question",
            "attempt_completion" => "Complete Task",
            "web_search" => "Web Search",
            "fetch_url" => "Fetch URL",
            "git_status" => "Git Status",
            "git_diff" => "Git Diff",
            "git_commit" => "Git Commit",
            _ => toolName
        };
    }
}

/// <summary>
/// A step in the agent's reasoning process
/// </summary>
public class ReasoningStep
{
    /// <summary>When this reasoning occurred</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>The reasoning content</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>Type of reasoning (planning, analyzing, deciding, etc.)</summary>
    [JsonPropertyName("reasoningType")]
    public string? ReasoningType { get; set; }

    /// <summary>Opaque reasoning data (for models that provide it)</summary>
    [JsonPropertyName("reasoningOpaque")]
    public object? ReasoningOpaque { get; set; }
}

/// <summary>
/// Status of an individual iteration
/// </summary>
public enum IterationStatus
{
    /// <summary>Iteration hasn't started yet</summary>
    Pending,
    
    /// <summary>Iteration is currently running</summary>
    Running,
    
    /// <summary>Iteration completed successfully</summary>
    Completed,
    
    /// <summary>Iteration failed with an error</summary>
    Failed,
    
    /// <summary>Iteration was cancelled by user</summary>
    Cancelled
}

/// <summary>
/// Status of an iterative task
/// </summary>
public enum IterativeTaskStatus
{
    /// <summary>Task has not started</summary>
    NotStarted,
    
    /// <summary>Task is currently running</summary>
    Running,
    
    /// <summary>Task completed successfully</summary>
    Completed,
    
    /// <summary>Task failed</summary>
    Failed,
    
    /// <summary>Task was stopped by user</summary>
    Stopped,
    
    /// <summary>Task reached max iterations without completion</summary>
    MaxIterationsReached
}

/// <summary>
/// Helper for summarizing tool arguments for display
/// </summary>
public static class ToolArgumentSummarizer
{
    /// <summary>
    /// Creates a human-readable summary of tool arguments
    /// </summary>
    public static string Summarize(string toolName, object? args)
    {
        if (args == null)
            return "(no arguments)";

        try
        {
            var lowerName = toolName.ToLowerInvariant();
            
            // Handle JsonElement
            if (args is JsonElement je)
            {
                return SummarizeJsonElement(lowerName, je);
            }
            
            // Handle Dictionary
            if (args is Dictionary<string, object> dict)
            {
                return SummarizeDictionary(lowerName, dict);
            }
            
            // Handle string
            if (args is string s)
            {
                return TruncateString(s, 100);
            }
            
            // Try to serialize and summarize
            var json = JsonSerializer.Serialize(args);
            using var doc = JsonDocument.Parse(json);
            return SummarizeJsonElement(lowerName, doc.RootElement);
        }
        catch
        {
            return args.ToString() ?? "(unknown)";
        }
    }

    private static string SummarizeJsonElement(string toolName, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return TruncateString(element.GetString() ?? "", 100);
        }
        
        if (element.ValueKind != JsonValueKind.Object)
        {
            return element.ToString() ?? "";
        }

        // File operations - show path
        if (element.TryGetProperty("path", out var pathProp))
        {
            var path = pathProp.GetString() ?? "";
            
            // Check for content to show file size
            if (element.TryGetProperty("content", out var contentProp))
            {
                var content = contentProp.GetString() ?? "";
                var sizeKb = content.Length / 1024.0;
                return sizeKb >= 1 
                    ? $"File: {path} ({sizeKb:0.1} KB)"
                    : $"File: {path} ({content.Length} bytes)";
            }
            
            return $"Path: {path}";
        }

        // Command execution - show command
        foreach (var propName in new[] { "command", "cmd", "script" })
        {
            if (element.TryGetProperty(propName, out var cmdProp))
            {
                var cmd = cmdProp.GetString() ?? "";
                return $"Command: {TruncateString(cmd, 80)}";
            }
        }

        // Search - show query/pattern
        foreach (var propName in new[] { "regex", "query", "pattern", "search" })
        {
            if (element.TryGetProperty(propName, out var queryProp))
            {
                var query = queryProp.GetString() ?? "";
                return $"Search: {TruncateString(query, 80)}";
            }
        }

        // URL operations
        if (element.TryGetProperty("url", out var urlProp))
        {
            return $"URL: {TruncateString(urlProp.GetString() ?? "", 80)}";
        }

        // Default: show property count
        var propCount = element.EnumerateObject().Count();
        return $"({propCount} parameters)";
    }

    private static string SummarizeDictionary(string toolName, Dictionary<string, object> dict)
    {
        if (dict.TryGetValue("path", out var path))
        {
            if (dict.TryGetValue("content", out var content))
            {
                var contentStr = content?.ToString() ?? "";
                var sizeKb = contentStr.Length / 1024.0;
                return sizeKb >= 1 
                    ? $"File: {path} ({sizeKb:0.1} KB)"
                    : $"File: {path} ({contentStr.Length} bytes)";
            }
            return $"Path: {path}";
        }

        if (dict.TryGetValue("command", out var cmd))
        {
            return $"Command: {TruncateString(cmd?.ToString() ?? "", 80)}";
        }

        return $"({dict.Count} parameters)";
    }

    private static string TruncateString(string s, int maxLength)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        if (s.Length <= maxLength)
            return s;
        return s[..(maxLength - 3)] + "...";
    }
}