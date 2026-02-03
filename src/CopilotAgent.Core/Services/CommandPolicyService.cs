using System.Text.RegularExpressions;
using CopilotAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Implementation of command policy service
/// </summary>
public class CommandPolicyService : ICommandPolicyService
{
    private readonly IPersistenceService _persistence;
    private readonly ILogger<CommandPolicyService> _logger;
    private CommandPolicy _policy = new();

    // High-risk command patterns
    private static readonly Dictionary<string, RiskLevel> RiskPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        // Critical - system destruction
        { @"rm\s+-rf\s+/", RiskLevel.Critical },
        { @"format\s+[a-z]:", RiskLevel.Critical },
        { @"del\s+/[sf]", RiskLevel.Critical },
        { @"rmdir\s+/s", RiskLevel.Critical },
        { @"rd\s+/s", RiskLevel.Critical },
        { @"shutdown", RiskLevel.Critical },
        { @"reboot", RiskLevel.Critical },
        { @"reg\s+delete", RiskLevel.Critical },
        
        // High - destructive or system modification
        { @"rm\s+-r", RiskLevel.High },
        { @"del\s+\*", RiskLevel.High },
        { @"Remove-Item.*-Recurse", RiskLevel.High },
        { @"Set-ExecutionPolicy", RiskLevel.High },
        { @"chmod\s+777", RiskLevel.High },
        { @"chown\s+-R", RiskLevel.High },
        { @"sudo\s+", RiskLevel.High },
        { @"runas\s+/user", RiskLevel.High },
        { @"net\s+user", RiskLevel.High },
        { @"net\s+localgroup", RiskLevel.High },
        { @"icacls", RiskLevel.High },
        
        // Medium - network/install operations
        { @"npm\s+install\s+-g", RiskLevel.Medium },
        { @"pip\s+install", RiskLevel.Medium },
        { @"dotnet\s+add\s+package", RiskLevel.Medium },
        { @"curl\s+.*\|\s*(bash|sh|powershell)", RiskLevel.High },
        { @"wget\s+.*\|\s*(bash|sh|powershell)", RiskLevel.High },
        { @"Invoke-WebRequest", RiskLevel.Medium },
        { @"iwr\s+", RiskLevel.Medium },
        { @"netsh", RiskLevel.Medium },
        
        // Low - safe operations (default)
    };

    public CommandPolicyService(IPersistenceService persistence, ILogger<CommandPolicyService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public CommandPolicy Policy => _policy;

    public CommandEvaluationResult EvaluateCommand(string command, string sessionId)
    {
        _logger.LogInformation("Evaluating command: {Command}", command);

        var result = new CommandEvaluationResult
        {
            RiskLevel = AssessRiskLevel(command)
        };

        // First check deny list
        var denyMatch = MatchesPatternList(command, _policy.GlobalDeniedCommands);
        if (denyMatch != null)
        {
            result.IsAllowed = false;
            result.RequiresApproval = false;
            result.Decision = CommandDecision.DeniedAutomatic;
            result.Reason = $"Command matches denied pattern: {denyMatch}";
            result.MatchedPattern = denyMatch;
            
            _logger.LogWarning("Command denied by policy: {Command}, pattern: {Pattern}", command, denyMatch);
            return result;
        }

        // Check allow list
        var allowMatch = MatchesPatternList(command, _policy.GlobalAllowedCommands);
        if (allowMatch != null)
        {
            result.IsAllowed = true;
            result.RequiresApproval = false;
            result.Decision = CommandDecision.AllowedAutomatic;
            result.Reason = $"Command matches allowed pattern: {allowMatch}";
            result.MatchedPattern = allowMatch;
            
            _logger.LogInformation("Command allowed by policy: {Command}, pattern: {Pattern}", command, allowMatch);
            return result;
        }

        // Unknown command - check if approval is required
        if (_policy.RequireApprovalForUnknown)
        {
            result.IsAllowed = false;
            result.RequiresApproval = true;
            result.Reason = "Command not in allow/deny list - user approval required";
            
            _logger.LogInformation("Command requires approval: {Command}, risk: {Risk}", command, result.RiskLevel);
            return result;
        }

        // Default: allow with low risk
        result.IsAllowed = true;
        result.RequiresApproval = false;
        result.Decision = CommandDecision.AllowedAutomatic;
        result.Reason = "Approval not required for unknown commands";
        
        return result;
    }

    public void RecordDecision(string command, string sessionId, CommandDecision decision, bool addToAllowList = false)
    {
        var entry = new CommandAuditEntry
        {
            Command = command,
            SessionId = sessionId,
            Decision = decision,
            RiskLevel = AssessRiskLevel(command),
            UserPrompted = decision == CommandDecision.AllowedByUser || decision == CommandDecision.DeniedByUser,
            Timestamp = DateTime.UtcNow,
            Reason = decision switch
            {
                CommandDecision.AllowedByUser => "User approved",
                CommandDecision.DeniedByUser => "User denied",
                CommandDecision.AllowedOnce => "User allowed once",
                _ => null
            }
        };

        _policy.AuditLog.Add(entry);

        // Trim audit log to keep only last 1000 entries
        if (_policy.AuditLog.Count > 1000)
        {
            _policy.AuditLog = _policy.AuditLog.TakeLast(1000).ToList();
        }

        // Add to allow list if requested
        if (addToAllowList && (decision == CommandDecision.AllowedByUser || decision == CommandDecision.AllowedAutomatic))
        {
            // Extract the command name (first word)
            var commandName = ExtractCommandName(command);
            if (!string.IsNullOrEmpty(commandName) && !_policy.GlobalAllowedCommands.Contains(commandName, StringComparer.OrdinalIgnoreCase))
            {
                _policy.GlobalAllowedCommands.Add(commandName);
                _logger.LogInformation("Added command to allow list: {Command}", commandName);
            }
        }

        _logger.LogInformation("Recorded decision for command: {Command}, decision: {Decision}", command, decision);
    }

    public void AddToAllowList(string pattern)
    {
        if (!_policy.GlobalAllowedCommands.Contains(pattern, StringComparer.OrdinalIgnoreCase))
        {
            _policy.GlobalAllowedCommands.Add(pattern);
            _logger.LogInformation("Added pattern to allow list: {Pattern}", pattern);
        }
    }

    public void AddToDenyList(string pattern)
    {
        if (!_policy.GlobalDeniedCommands.Contains(pattern, StringComparer.OrdinalIgnoreCase))
        {
            _policy.GlobalDeniedCommands.Add(pattern);
            _logger.LogInformation("Added pattern to deny list: {Pattern}", pattern);
        }
    }

    public void RemoveFromAllowList(string pattern)
    {
        _policy.GlobalAllowedCommands.RemoveAll(p => p.Equals(pattern, StringComparison.OrdinalIgnoreCase));
        _logger.LogInformation("Removed pattern from allow list: {Pattern}", pattern);
    }

    public void RemoveFromDenyList(string pattern)
    {
        _policy.GlobalDeniedCommands.RemoveAll(p => p.Equals(pattern, StringComparison.OrdinalIgnoreCase));
        _logger.LogInformation("Removed pattern from deny list: {Pattern}", pattern);
    }

    public IReadOnlyList<CommandAuditEntry> GetAuditLog(int? limit = null)
    {
        var log = _policy.AuditLog.OrderByDescending(e => e.Timestamp);
        return limit.HasValue ? log.Take(limit.Value).ToList() : log.ToList();
    }

    public void ClearAuditLog()
    {
        _policy.AuditLog.Clear();
        _logger.LogInformation("Cleared audit log");
    }

    public async Task SavePolicyAsync()
    {
        await _persistence.SaveCommandPolicyAsync(_policy);
        _logger.LogInformation("Saved command policy");
    }

    public async Task LoadPolicyAsync()
    {
        _policy = await _persistence.LoadCommandPolicyAsync() ?? new CommandPolicy();
        _logger.LogInformation("Loaded command policy with {AllowCount} allowed and {DenyCount} denied patterns",
            _policy.GlobalAllowedCommands.Count, _policy.GlobalDeniedCommands.Count);
    }

    private RiskLevel AssessRiskLevel(string command)
    {
        foreach (var (pattern, risk) in RiskPatterns)
        {
            try
            {
                if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase))
                {
                    return risk;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Skip malformed patterns
            }
        }

        return RiskLevel.Low;
    }

    private static string? MatchesPatternList(string command, List<string> patterns)
    {
        var commandLower = command.ToLowerInvariant();
        var commandName = ExtractCommandName(command)?.ToLowerInvariant();

        foreach (var pattern in patterns)
        {
            var patternLower = pattern.ToLowerInvariant();

            // Direct match on command name
            if (commandName != null && commandName.Equals(patternLower, StringComparison.OrdinalIgnoreCase))
            {
                return pattern;
            }

            // Starts with pattern
            if (commandLower.StartsWith(patternLower))
            {
                return pattern;
            }

            // Contains pattern (for multi-word patterns like "rm -rf")
            if (commandLower.Contains(patternLower))
            {
                return pattern;
            }

            // Regex pattern (if it looks like a regex)
            if (pattern.Contains('*') || pattern.Contains('?') || pattern.Contains('['))
            {
                try
                {
                    var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    if (Regex.IsMatch(command, regexPattern, RegexOptions.IgnoreCase))
                    {
                        return pattern;
                    }
                }
                catch
                {
                    // Invalid regex, skip
                }
            }
        }

        return null;
    }

    private static string? ExtractCommandName(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var parts = command.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        var firstPart = parts[0];

        // Remove path and extension
        var fileName = Path.GetFileNameWithoutExtension(firstPart);
        return string.IsNullOrEmpty(fileName) ? firstPart : fileName;
    }
}