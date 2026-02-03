using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Service for evaluating and enforcing command execution policies
/// </summary>
public interface ICommandPolicyService
{
    /// <summary>
    /// Gets the current command policy
    /// </summary>
    CommandPolicy Policy { get; }

    /// <summary>
    /// Evaluates a command against the policy
    /// </summary>
    /// <param name="command">The command to evaluate</param>
    /// <param name="sessionId">The session ID</param>
    /// <returns>Evaluation result with decision and risk level</returns>
    CommandEvaluationResult EvaluateCommand(string command, string sessionId);

    /// <summary>
    /// Records a user's decision for a command
    /// </summary>
    /// <param name="command">The command</param>
    /// <param name="sessionId">The session ID</param>
    /// <param name="decision">The user's decision</param>
    /// <param name="addToAllowList">Whether to add to permanent allow list</param>
    void RecordDecision(string command, string sessionId, CommandDecision decision, bool addToAllowList = false);

    /// <summary>
    /// Adds a command pattern to the allow list
    /// </summary>
    void AddToAllowList(string pattern);

    /// <summary>
    /// Adds a command pattern to the deny list
    /// </summary>
    void AddToDenyList(string pattern);

    /// <summary>
    /// Removes a command pattern from the allow list
    /// </summary>
    void RemoveFromAllowList(string pattern);

    /// <summary>
    /// Removes a command pattern from the deny list
    /// </summary>
    void RemoveFromDenyList(string pattern);

    /// <summary>
    /// Gets the audit log entries
    /// </summary>
    IReadOnlyList<CommandAuditEntry> GetAuditLog(int? limit = null);

    /// <summary>
    /// Clears the audit log
    /// </summary>
    void ClearAuditLog();

    /// <summary>
    /// Saves the policy to persistent storage
    /// </summary>
    Task SavePolicyAsync();

    /// <summary>
    /// Loads the policy from persistent storage
    /// </summary>
    Task LoadPolicyAsync();
}

/// <summary>
/// Result of evaluating a command against the policy
/// </summary>
public class CommandEvaluationResult
{
    /// <summary>
    /// Whether the command is allowed
    /// </summary>
    public bool IsAllowed { get; set; }

    /// <summary>
    /// Whether user approval is required
    /// </summary>
    public bool RequiresApproval { get; set; }

    /// <summary>
    /// The decision made (if automatic)
    /// </summary>
    public CommandDecision Decision { get; set; }

    /// <summary>
    /// The assessed risk level
    /// </summary>
    public RiskLevel RiskLevel { get; set; }

    /// <summary>
    /// Reason for the decision
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Matched pattern (if any)
    /// </summary>
    public string? MatchedPattern { get; set; }
}