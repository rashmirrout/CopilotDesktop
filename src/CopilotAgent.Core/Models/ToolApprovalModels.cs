using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Scope of a tool approval decision.
/// </summary>
public enum ApprovalScope
{
    /// <summary>Approve only this specific invocation.</summary>
    Once,
    
    /// <summary>Approve for the current session only.</summary>
    Session,
    
    /// <summary>Approve globally for all sessions.</summary>
    Global
}

/// <summary>
/// Risk level of a tool operation.
/// </summary>
public enum ToolRiskLevel
{
    /// <summary>Low risk - read operations, viewing data.</summary>
    Low,
    
    /// <summary>Medium risk - write operations, file modifications.</summary>
    Medium,
    
    /// <summary>High risk - shell commands, network operations.</summary>
    High,
    
    /// <summary>Critical risk - system modifications, destructive operations.</summary>
    Critical
}

/// <summary>
/// Request for tool approval from the user.
/// </summary>
public class ToolApprovalRequest
{
    /// <summary>
    /// The session ID where the tool is being invoked.
    /// </summary>
    public required string SessionId { get; init; }
    
    /// <summary>
    /// The name of the tool being invoked.
    /// </summary>
    public required string ToolName { get; init; }
    
    /// <summary>
    /// The arguments passed to the tool (as JSON or object).
    /// </summary>
    public object? ToolArgs { get; init; }
    
    /// <summary>
    /// The working directory for the tool execution.
    /// </summary>
    public string? WorkingDirectory { get; init; }
    
    /// <summary>
    /// Timestamp when the request was made.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// Assessed risk level of this tool operation.
    /// </summary>
    public ToolRiskLevel RiskLevel { get; init; } = ToolRiskLevel.Medium;
    
    /// <summary>
    /// Human-readable description of what the tool will do.
    /// </summary>
    public string? Description { get; init; }
    
    /// <summary>
    /// SDK tool call ID for correlation.
    /// </summary>
    public string? ToolCallId { get; init; }
}

/// <summary>
/// User's decision on a tool approval request.
/// </summary>
public class ToolApprovalResponse
{
    /// <summary>
    /// Whether the tool execution was approved.
    /// </summary>
    public bool Approved { get; init; }
    
    /// <summary>
    /// The scope of the approval decision.
    /// </summary>
    public ApprovalScope Scope { get; init; } = ApprovalScope.Once;
    
    /// <summary>
    /// Optional reason provided by the user.
    /// </summary>
    public string? Reason { get; init; }
    
    /// <summary>
    /// Whether to remember this decision for similar future requests.
    /// </summary>
    public bool RememberDecision { get; init; }
}

/// <summary>
/// Persisted approval rule for automatic approval/denial.
/// </summary>
public class ToolApprovalRule
{
    /// <summary>
    /// Unique identifier for this rule.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// The tool name this rule applies to.
    /// </summary>
    public required string ToolName { get; init; }
    
    /// <summary>
    /// Optional pattern to match tool arguments (regex or glob).
    /// Null means match any arguments.
    /// </summary>
    public string? ToolArgsPattern { get; init; }
    
    /// <summary>
    /// Whether tools matching this rule should be approved.
    /// </summary>
    public bool Approved { get; init; }
    
    /// <summary>
    /// The scope of this rule.
    /// </summary>
    public ApprovalScope Scope { get; init; }
    
    /// <summary>
    /// When this rule was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// Session ID for session-scoped rules. Null for global rules.
    /// </summary>
    public string? SessionId { get; init; }
    
    /// <summary>
    /// Human-readable description of the rule.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// How to display tool approval requests to the user.
/// </summary>
public enum ApprovalUIMode
{
    /// <summary>Show modal dialog only.</summary>
    Modal,
    
    /// <summary>Show inline in chat only.</summary>
    Inline,
    
    /// <summary>Let user choose each time (default).</summary>
    Both
}

/// <summary>
/// Event args for tool approval requests (UI notification).
/// </summary>
public class ToolApprovalRequestEventArgs : EventArgs
{
    public required ToolApprovalRequest Request { get; init; }
    public TaskCompletionSource<ToolApprovalResponse> ResponseSource { get; } = new();
}

/// <summary>
/// Collection of approval rules for persistence.
/// </summary>
public class ApprovalRulesCollection
{
    /// <summary>
    /// Global approval rules that apply to all sessions.
    /// </summary>
    [JsonPropertyName("globalRules")]
    public List<ToolApprovalRule> GlobalRules { get; set; } = new();
    
    /// <summary>
    /// Session-specific approval rules keyed by session ID.
    /// </summary>
    [JsonPropertyName("sessionRules")]
    public Dictionary<string, List<ToolApprovalRule>> SessionRules { get; set; } = new();
}

/// <summary>
/// Static helper for tool risk classification.
/// </summary>
public static class ToolRiskClassifier
{
    private static readonly HashSet<string> HighRiskTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "shell", "bash", "cmd", "powershell", "run_command", "execute", "exec",
        "http_request", "fetch", "curl", "wget", "network"
    };
    
    private static readonly HashSet<string> MediumRiskTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "write_file", "edit_file", "create_file", "delete_file", "move_file",
        "rename_file", "edit", "write", "patch", "replace"
    };
    
    private static readonly HashSet<string> LowRiskTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "read_file", "view_file", "list_files", "search", "grep", "find",
        "read", "view", "list", "get"
    };
    
    /// <summary>
    /// Classify the risk level of a tool based on its name.
    /// </summary>
    public static ToolRiskLevel ClassifyTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return ToolRiskLevel.Medium;
        
        // Check for exact or partial matches
        foreach (var highRisk in HighRiskTools)
        {
            if (toolName.Contains(highRisk, StringComparison.OrdinalIgnoreCase))
                return ToolRiskLevel.High;
        }
        
        foreach (var mediumRisk in MediumRiskTools)
        {
            if (toolName.Contains(mediumRisk, StringComparison.OrdinalIgnoreCase))
                return ToolRiskLevel.Medium;
        }
        
        foreach (var lowRisk in LowRiskTools)
        {
            if (toolName.Contains(lowRisk, StringComparison.OrdinalIgnoreCase))
                return ToolRiskLevel.Low;
        }
        
        // Default to medium for unknown tools
        return ToolRiskLevel.Medium;
    }
}