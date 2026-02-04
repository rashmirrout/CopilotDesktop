using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Service for managing tool approval requests and decisions.
/// Integrates with autonomous mode settings and persisted approval rules.
/// </summary>
public interface IToolApprovalService
{
    /// <summary>
    /// Raised when a tool approval request needs user interaction.
    /// UI can subscribe to show modal or inline approval UI.
    /// </summary>
    event EventHandler<ToolApprovalRequestEventArgs>? ApprovalRequested;
    
    /// <summary>
    /// Request user approval for a tool invocation.
    /// Checks autonomous mode and saved rules first; shows dialog only if needed.
    /// </summary>
    /// <param name="request">The approval request details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's approval decision.</returns>
    Task<ToolApprovalResponse> RequestApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if a tool is already approved (no dialog needed).
    /// Checks both session-scoped and global rules.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="toolName">The tool name.</param>
    /// <param name="args">Optional tool arguments for pattern matching.</param>
    /// <returns>True if the tool is pre-approved.</returns>
    bool IsApproved(string sessionId, string toolName, object? args = null);
    
    /// <summary>
    /// Record an approval decision for future reference.
    /// Creates rules based on the response scope (Once, Session, Global).
    /// </summary>
    /// <param name="request">The original request.</param>
    /// <param name="response">The user's response.</param>
    void RecordDecision(ToolApprovalRequest request, ToolApprovalResponse response);
    
    /// <summary>
    /// Get all saved approval rules (both global and session-scoped).
    /// </summary>
    /// <returns>All persisted approval rules.</returns>
    IReadOnlyList<ToolApprovalRule> GetSavedRules();
    
    /// <summary>
    /// Get global approval rules only.
    /// </summary>
    /// <returns>Global approval rules.</returns>
    IReadOnlyList<ToolApprovalRule> GetGlobalRules();
    
    /// <summary>
    /// Get session-specific approval rules.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>Session approval rules.</returns>
    IReadOnlyList<ToolApprovalRule> GetSessionRules(string sessionId);
    
    /// <summary>
    /// Add a new approval rule.
    /// </summary>
    /// <param name="rule">The rule to add.</param>
    void AddRule(ToolApprovalRule rule);
    
    /// <summary>
    /// Remove an approval rule by ID.
    /// </summary>
    /// <param name="ruleId">The rule ID to remove.</param>
    /// <returns>True if the rule was found and removed.</returns>
    bool RemoveRule(string ruleId);
    
    /// <summary>
    /// Remove an approval rule.
    /// </summary>
    /// <param name="rule">The rule to remove.</param>
    /// <returns>True if the rule was found and removed.</returns>
    bool RemoveRule(ToolApprovalRule rule);
    
    /// <summary>
    /// Clear all session-scoped approvals for a specific session.
    /// Called when a session ends.
    /// </summary>
    /// <param name="sessionId">The session ID to clear.</param>
    void ClearSessionApprovals(string sessionId);
    
    /// <summary>
    /// Clear all approval rules (both global and session).
    /// </summary>
    void ClearAllRules();
    
    /// <summary>
    /// Get the risk level for a tool based on its name.
    /// Uses built-in classification rules.
    /// </summary>
    /// <param name="toolName">The tool name.</param>
    /// <returns>The assessed risk level.</returns>
    ToolRiskLevel GetToolRiskLevel(string toolName);
    
    /// <summary>
    /// Save approval rules to persistence.
    /// </summary>
    /// <returns>Task that completes when save is done.</returns>
    Task SaveRulesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Load approval rules from persistence.
    /// </summary>
    /// <returns>Task that completes when load is done.</returns>
    Task LoadRulesAsync(CancellationToken cancellationToken = default);
}