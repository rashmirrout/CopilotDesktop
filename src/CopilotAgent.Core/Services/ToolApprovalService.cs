using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using CopilotAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Service for managing tool approval requests and decisions.
/// Thread-safe implementation with persistence support.
/// </summary>
public class ToolApprovalService : IToolApprovalService
{
    private readonly IPersistenceService _persistenceService;
    private readonly ILogger<ToolApprovalService> _logger;
    
    private readonly object _rulesLock = new();
    private readonly List<ToolApprovalRule> _globalRules = new();
    private readonly ConcurrentDictionary<string, List<ToolApprovalRule>> _sessionRules = new();
    
    // Track one-time approvals per session (not persisted)
    private readonly ConcurrentDictionary<string, HashSet<string>> _onceApprovals = new();
    
    private bool _isLoaded;

    /// <inheritdoc />
    public event EventHandler<ToolApprovalRequestEventArgs>? ApprovalRequested;

    public ToolApprovalService(
        IPersistenceService persistenceService,
        ILogger<ToolApprovalService> logger)
    {
        _persistenceService = persistenceService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ToolApprovalResponse> RequestApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        // Ensure rules are loaded
        await EnsureLoadedAsync(cancellationToken);
        
        _logger.LogDebug("Approval requested for tool {Tool} in session {Session}",
            request.ToolName, request.SessionId);
        
        // Check if already approved
        if (IsApproved(request.SessionId, request.ToolName, request.ToolArgs))
        {
            _logger.LogInformation("Tool {Tool} pre-approved via existing rule", request.ToolName);
            return new ToolApprovalResponse
            {
                Approved = true,
                Scope = ApprovalScope.Session,
                Reason = "Pre-approved by saved rule"
            };
        }
        
        // Raise event for UI to handle
        var eventArgs = new ToolApprovalRequestEventArgs { Request = request };
        ApprovalRequested?.Invoke(this, eventArgs);
        
        // If no handlers, deny by default (safe behavior)
        if (ApprovalRequested == null)
        {
            _logger.LogWarning("No approval UI handler registered, denying tool {Tool}", request.ToolName);
            return new ToolApprovalResponse
            {
                Approved = false,
                Reason = "No approval handler available"
            };
        }
        
        try
        {
            // Wait for UI to provide response
            var response = await eventArgs.ResponseSource.Task.WaitAsync(cancellationToken);
            
            _logger.LogInformation("Tool {Tool} {Decision} by user (scope: {Scope})",
                request.ToolName, 
                response.Approved ? "approved" : "denied",
                response.Scope);
            
            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Approval request for tool {Tool} was cancelled", request.ToolName);
            return new ToolApprovalResponse
            {
                Approved = false,
                Reason = "Request cancelled"
            };
        }
    }

    /// <inheritdoc />
    public bool IsApproved(string sessionId, string toolName, object? args = null)
    {
        // Check one-time approvals for this session
        if (_onceApprovals.TryGetValue(sessionId, out var onceSet))
        {
            var onceKey = GetToolKey(toolName, args);
            if (onceSet.Contains(onceKey))
            {
                // Remove after use
                onceSet.Remove(onceKey);
                return true;
            }
        }
        
        lock (_rulesLock)
        {
            // Check session-specific rules first (more specific)
            if (_sessionRules.TryGetValue(sessionId, out var sessionRules))
            {
                var sessionMatch = FindMatchingRule(sessionRules, toolName, args);
                if (sessionMatch != null)
                {
                    return sessionMatch.Approved;
                }
            }
            
            // Check global rules
            var globalMatch = FindMatchingRule(_globalRules, toolName, args);
            if (globalMatch != null)
            {
                return globalMatch.Approved;
            }
        }
        
        return false;
    }

    /// <inheritdoc />
    public void RecordDecision(ToolApprovalRequest request, ToolApprovalResponse response)
    {
        if (!response.RememberDecision && response.Scope == ApprovalScope.Once)
        {
            // For one-time approvals, just record in memory (if approved)
            if (response.Approved)
            {
                var onceKey = GetToolKey(request.ToolName, request.ToolArgs);
                var onceSet = _onceApprovals.GetOrAdd(request.SessionId, _ => new HashSet<string>());
                lock (onceSet)
                {
                    onceSet.Add(onceKey);
                }
            }
            return;
        }
        
        if (!response.RememberDecision)
        {
            return;
        }
        
        var rule = new ToolApprovalRule
        {
            ToolName = request.ToolName,
            Approved = response.Approved,
            Scope = response.Scope,
            SessionId = response.Scope == ApprovalScope.Session ? request.SessionId : null,
            Description = response.Reason ?? $"User {(response.Approved ? "approved" : "denied")} {request.ToolName}"
        };
        
        AddRule(rule);
        
        _logger.LogInformation("Recorded {Decision} rule for tool {Tool} (scope: {Scope})",
            response.Approved ? "approval" : "denial",
            request.ToolName,
            response.Scope);
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolApprovalRule> GetSavedRules()
    {
        lock (_rulesLock)
        {
            var allRules = new List<ToolApprovalRule>(_globalRules);
            foreach (var sessionRules in _sessionRules.Values)
            {
                allRules.AddRange(sessionRules);
            }
            return allRules.AsReadOnly();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolApprovalRule> GetGlobalRules()
    {
        lock (_rulesLock)
        {
            return _globalRules.ToList().AsReadOnly();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolApprovalRule> GetSessionRules(string sessionId)
    {
        if (_sessionRules.TryGetValue(sessionId, out var rules))
        {
            lock (_rulesLock)
            {
                return rules.ToList().AsReadOnly();
            }
        }
        return Array.Empty<ToolApprovalRule>();
    }

    /// <inheritdoc />
    public void AddRule(ToolApprovalRule rule)
    {
        lock (_rulesLock)
        {
            if (rule.Scope == ApprovalScope.Global || string.IsNullOrEmpty(rule.SessionId))
            {
                // Remove existing rule for same tool (if any)
                _globalRules.RemoveAll(r => 
                    r.ToolName.Equals(rule.ToolName, StringComparison.OrdinalIgnoreCase) &&
                    r.ToolArgsPattern == rule.ToolArgsPattern);
                _globalRules.Add(rule);
            }
            else
            {
                var sessionRules = _sessionRules.GetOrAdd(rule.SessionId, _ => new List<ToolApprovalRule>());
                lock (sessionRules)
                {
                    // Remove existing rule for same tool (if any)
                    sessionRules.RemoveAll(r =>
                        r.ToolName.Equals(rule.ToolName, StringComparison.OrdinalIgnoreCase) &&
                        r.ToolArgsPattern == rule.ToolArgsPattern);
                    sessionRules.Add(rule);
                }
            }
        }
        
        _logger.LogDebug("Added rule: {Tool} -> {Approved} (scope: {Scope})",
            rule.ToolName, rule.Approved, rule.Scope);
    }

    /// <inheritdoc />
    public bool RemoveRule(string ruleId)
    {
        lock (_rulesLock)
        {
            // Check global rules
            var globalIndex = _globalRules.FindIndex(r => r.Id == ruleId);
            if (globalIndex >= 0)
            {
                _globalRules.RemoveAt(globalIndex);
                _logger.LogDebug("Removed global rule: {RuleId}", ruleId);
                return true;
            }
            
            // Check session rules
            foreach (var sessionRules in _sessionRules.Values)
            {
                var sessionIndex = sessionRules.FindIndex(r => r.Id == ruleId);
                if (sessionIndex >= 0)
                {
                    sessionRules.RemoveAt(sessionIndex);
                    _logger.LogDebug("Removed session rule: {RuleId}", ruleId);
                    return true;
                }
            }
        }
        
        return false;
    }

    /// <inheritdoc />
    public bool RemoveRule(ToolApprovalRule rule)
    {
        return RemoveRule(rule.Id);
    }

    /// <inheritdoc />
    public void ClearSessionApprovals(string sessionId)
    {
        _sessionRules.TryRemove(sessionId, out _);
        _onceApprovals.TryRemove(sessionId, out _);
        
        _logger.LogInformation("Cleared all approvals for session {SessionId}", sessionId);
    }

    /// <inheritdoc />
    public void ClearAllRules()
    {
        lock (_rulesLock)
        {
            _globalRules.Clear();
            _sessionRules.Clear();
            _onceApprovals.Clear();
        }
        
        _logger.LogInformation("Cleared all approval rules");
    }

    /// <inheritdoc />
    public ToolRiskLevel GetToolRiskLevel(string toolName)
    {
        return ToolRiskClassifier.ClassifyTool(toolName);
    }

    /// <inheritdoc />
    public async Task SaveRulesAsync(CancellationToken cancellationToken = default)
    {
        var collection = new ApprovalRulesCollection();
        
        lock (_rulesLock)
        {
            collection.GlobalRules = _globalRules.ToList();
            foreach (var (sessionId, rules) in _sessionRules)
            {
                collection.SessionRules[sessionId] = rules.ToList();
            }
        }
        
        await _persistenceService.SaveApprovalRulesAsync(collection);
        _logger.LogInformation("Saved approval rules to disk");
    }

    /// <inheritdoc />
    public async Task LoadRulesAsync(CancellationToken cancellationToken = default)
    {
        var collection = await _persistenceService.LoadApprovalRulesAsync();
        
        if (collection != null)
        {
            lock (_rulesLock)
            {
                _globalRules.Clear();
                _globalRules.AddRange(collection.GlobalRules);
                
                _sessionRules.Clear();
                foreach (var (sessionId, rules) in collection.SessionRules)
                {
                    _sessionRules[sessionId] = rules;
                }
            }
            
            _logger.LogInformation("Loaded {GlobalCount} global rules and {SessionCount} session rule sets",
                collection.GlobalRules.Count, collection.SessionRules.Count);
        }
        
        _isLoaded = true;
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (!_isLoaded)
        {
            await LoadRulesAsync(cancellationToken);
        }
    }

    private static ToolApprovalRule? FindMatchingRule(
        List<ToolApprovalRule> rules, 
        string toolName, 
        object? args)
    {
        // First, try exact tool name match
        foreach (var rule in rules)
        {
            if (!rule.ToolName.Equals(toolName, StringComparison.OrdinalIgnoreCase))
                continue;
            
            // If rule has args pattern, check it
            if (!string.IsNullOrEmpty(rule.ToolArgsPattern))
            {
                var argsJson = args is string s ? s : JsonSerializer.Serialize(args ?? "");
                try
                {
                    if (Regex.IsMatch(argsJson, rule.ToolArgsPattern, RegexOptions.IgnoreCase))
                    {
                        return rule;
                    }
                }
                catch (RegexParseException)
                {
                    // Invalid regex, skip this rule
                }
            }
            else
            {
                // No args pattern means match any args
                return rule;
            }
        }
        
        return null;
    }

    private static string GetToolKey(string toolName, object? args)
    {
        if (args == null)
            return toolName.ToLowerInvariant();
        
        var argsJson = args is string s ? s : JsonSerializer.Serialize(args);
        return $"{toolName.ToLowerInvariant()}:{argsJson}";
    }
}