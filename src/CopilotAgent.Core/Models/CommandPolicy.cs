using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Command execution policy for security
/// </summary>
public class CommandPolicy
{
    /// <summary>Global allowed command patterns</summary>
    [JsonPropertyName("globalAllowedCommands")]
    public List<string> GlobalAllowedCommands { get; set; } = new()
    {
        "dotnet", "npm", "git", "gh", "dir", "ls", "cat", "type", 
        "echo", "cd", "pwd", "mkdir", "code"
    };

    /// <summary>Global denied command patterns</summary>
    [JsonPropertyName("globalDeniedCommands")]
    public List<string> GlobalDeniedCommands { get; set; } = new()
    {
        "rm -rf", "del /s", "format", "rmdir /s", "rd /s"
    };

    /// <summary>Require approval for unknown commands</summary>
    [JsonPropertyName("requireApprovalForUnknown")]
    public bool RequireApprovalForUnknown { get; set; } = true;

    /// <summary>Audit log of command approvals/denials</summary>
    [JsonPropertyName("auditLog")]
    public List<CommandAuditEntry> AuditLog { get; set; } = new();
}

/// <summary>
/// Audit entry for a command execution decision
/// </summary>
public class CommandAuditEntry
{
    /// <summary>Command that was evaluated</summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    /// <summary>Session ID where command was executed</summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Decision made</summary>
    [JsonPropertyName("decision")]
    public CommandDecision Decision { get; set; }

    /// <summary>Reason for the decision</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>Whether user was prompted</summary>
    [JsonPropertyName("userPrompted")]
    public bool UserPrompted { get; set; }

    /// <summary>Timestamp of the decision</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Risk level assessed</summary>
    [JsonPropertyName("riskLevel")]
    public RiskLevel RiskLevel { get; set; }
}

/// <summary>
/// Decision for command execution
/// </summary>
public enum CommandDecision
{
    /// <summary>Command allowed automatically</summary>
    AllowedAutomatic,
    
    /// <summary>Command allowed by user</summary>
    AllowedByUser,
    
    /// <summary>Command denied automatically</summary>
    DeniedAutomatic,
    
    /// <summary>Command denied by user</summary>
    DeniedByUser,
    
    /// <summary>Command allowed once (not saved to policy)</summary>
    AllowedOnce
}

/// <summary>
/// Risk level of a command
/// </summary>
public enum RiskLevel
{
    /// <summary>Low risk - safe operation</summary>
    Low,
    
    /// <summary>Medium risk - requires attention</summary>
    Medium,
    
    /// <summary>High risk - destructive operation</summary>
    High,
    
    /// <summary>Critical risk - system-level operation</summary>
    Critical
}