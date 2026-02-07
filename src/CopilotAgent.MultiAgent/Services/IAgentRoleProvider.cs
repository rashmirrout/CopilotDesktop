namespace CopilotAgent.MultiAgent.Services;

using CopilotAgent.MultiAgent.Models;

/// <summary>
/// Creates role-specialized Copilot SDK sessions for worker agents.
/// Merges default role configurations with user overrides from <see cref="MultiAgentConfig"/>.
/// </summary>
public interface IAgentRoleProvider
{
    /// <summary>
    /// Create a Copilot session configured for the specified role.
    /// Applies role-specific system instructions, tools, and model overrides.
    /// </summary>
    /// <param name="role">The agent role specialization.</param>
    /// <param name="workspacePath">The workspace directory for the session.</param>
    /// <param name="config">The multi-agent configuration containing role overrides.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session ID of the created role-specialized session.</returns>
    Task<string> CreateRoleSessionAsync(
        AgentRole role,
        string workspacePath,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the effective configuration for a role, merging defaults with user overrides.
    /// User overrides in <see cref="MultiAgentConfig.RoleConfigs"/> take precedence.
    /// </summary>
    /// <param name="role">The agent role.</param>
    /// <param name="config">The multi-agent configuration containing role overrides.</param>
    /// <returns>The merged role configuration.</returns>
    AgentRoleConfig GetEffectiveRoleConfig(AgentRole role, MultiAgentConfig config);
}