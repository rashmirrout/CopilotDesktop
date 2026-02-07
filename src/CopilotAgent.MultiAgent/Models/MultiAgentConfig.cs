namespace CopilotAgent.MultiAgent.Models;

public sealed class MultiAgentConfig
{
    public int MaxParallelSessions { get; set; } = 5;
    public WorkspaceStrategyType WorkspaceStrategy { get; set; } = WorkspaceStrategyType.GitWorktree;
    public RetryPolicy RetryPolicy { get; set; } = new();
    public string? OrchestratorModelId { get; set; }
    public string? WorkerModelId { get; set; }
    public string WorkingDirectory { get; set; } = string.Empty;
    public List<string> EnabledMcpServers { get; set; } = new();
    public List<string> DisabledSkills { get; set; } = new();
    public bool AutoApproveReadOnlyTools { get; set; } = true;
    public TimeSpan WorkerTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan OrchestratorLlmTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public bool MaintainFollowUpContext { get; set; } = true;

    /// <summary>Role-specific configurations (V3). Keyed by AgentRole.</summary>
    public Dictionary<AgentRole, AgentRoleConfig> RoleConfigs { get; set; } = new();
}