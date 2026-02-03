using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Represents a Copilot agent session with its own context and history
/// </summary>
public class Session : INotifyPropertyChanged
{
    private string _displayName = "New Session";
    private bool _isActive;

    /// <summary>Occurs when a property value changes</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises the PropertyChanged event</summary>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>Unique identifier for this session</summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name for the session</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName != value)
            {
                _displayName = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Working directory for this session</summary>
    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    /// <summary>Git worktree information (if created as worktree session)</summary>
    [JsonPropertyName("gitWorktreeInfo")]
    public GitWorktreeInfo? GitWorktreeInfo { get; set; }

    /// <summary>Copilot model ID to use</summary>
    [JsonPropertyName("modelId")]
    public string ModelId { get; set; } = "gpt-4";

    /// <summary>Allowed commands for this session (extends global)</summary>
    [JsonPropertyName("allowedCommands")]
    public List<string> AllowedCommands { get; set; } = new();

    /// <summary>Denied commands for this session (extends global)</summary>
    [JsonPropertyName("deniedCommands")]
    public List<string> DeniedCommands { get; set; } = new();

    /// <summary>Enabled MCP servers for this session</summary>
    [JsonPropertyName("enabledMcpServers")]
    public List<string> EnabledMcpServers { get; set; } = new();

    /// <summary>Enabled skills for this session</summary>
    [JsonPropertyName("enabledSkills")]
    public List<string> EnabledSkills { get; set; } = new();

    /// <summary>Message history for this session</summary>
    [JsonPropertyName("messageHistory")]
    public List<ChatMessage> MessageHistory { get; set; } = new();

    /// <summary>Token budget state</summary>
    [JsonPropertyName("tokenBudget")]
    public TokenBudgetState TokenBudget { get; set; } = new();

    /// <summary>Iterative task configuration (if any)</summary>
    [JsonPropertyName("iterativeTaskConfig")]
    public IterativeTaskConfig? IterativeTaskConfig { get; set; }

    /// <summary>When the session was created</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last time the session was active</summary>
    [JsonPropertyName("lastActiveAt")]
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the session is currently active</summary>
    [JsonPropertyName("isActive")]
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>System prompt for this session</summary>
    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Copilot CLI's internal session ID (GUID).
    /// Used with --resume to reconnect to specific Copilot session after app restart.
    /// This is different from our SessionId - it's Copilot's own session tracking.
    /// </summary>
    [JsonPropertyName("copilotSessionId")]
    public string? CopilotSessionId { get; set; }
}

/// <summary>
/// Git worktree information for issue-focused sessions
/// </summary>
public class GitWorktreeInfo
{
    /// <summary>Repository owner</summary>
    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    /// <summary>Repository name</summary>
    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;

    /// <summary>Issue number</summary>
    [JsonPropertyName("issueNumber")]
    public int IssueNumber { get; set; }

    /// <summary>Issue title</summary>
    [JsonPropertyName("issueTitle")]
    public string IssueTitle { get; set; } = string.Empty;

    /// <summary>Issue body/description</summary>
    [JsonPropertyName("issueBody")]
    public string? IssueBody { get; set; }

    /// <summary>Branch name for the worktree</summary>
    [JsonPropertyName("branchName")]
    public string BranchName { get; set; } = string.Empty;

    /// <summary>Path to the worktree</summary>
    [JsonPropertyName("worktreePath")]
    public string WorktreePath { get; set; } = string.Empty;
}

/// <summary>
/// Token budget tracking for a session
/// </summary>
public class TokenBudgetState
{
    /// <summary>Estimated tokens used in current context</summary>
    [JsonPropertyName("tokensUsed")]
    public int TokensUsed { get; set; }

    /// <summary>Maximum tokens allowed</summary>
    [JsonPropertyName("maxTokens")]
    public int MaxTokens { get; set; } = 8000;

    /// <summary>Number of times context has been compacted</summary>
    [JsonPropertyName("compactionCount")]
    public int CompactionCount { get; set; }

    /// <summary>Last compaction timestamp</summary>
    [JsonPropertyName("lastCompactionAt")]
    public DateTime? LastCompactionAt { get; set; }
}