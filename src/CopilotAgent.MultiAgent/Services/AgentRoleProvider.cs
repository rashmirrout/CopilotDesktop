using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.MultiAgent.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Provides role-specialized configurations for worker agents.
/// Merges built-in default role configs with user overrides from <see cref="MultiAgentConfig.RoleConfigs"/>.
/// </summary>
public sealed class AgentRoleProvider : IAgentRoleProvider
{
    private readonly ICopilotService _copilotService;
    private readonly ILogger<AgentRoleProvider> _logger;

    /// <summary>
    /// Lazily-initialized built-in defaults from <see cref="DefaultRoleConfigs.GetDefaults"/>.
    /// </summary>
    private static readonly Lazy<Dictionary<AgentRole, AgentRoleConfig>> BuiltInDefaults =
        new(() => DefaultRoleConfigs.GetDefaults());

    public AgentRoleProvider(
        ICopilotService copilotService,
        ILogger<AgentRoleProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(copilotService);
        ArgumentNullException.ThrowIfNull(logger);

        _copilotService = copilotService;
        _logger = logger;
    }

    public async Task<string> CreateRoleSessionAsync(
        AgentRole role,
        string workspacePath,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentNullException.ThrowIfNull(config);

        var roleConfig = GetEffectiveRoleConfig(role, config);

        // Create a new session with role-specific configuration
        var session = new Session
        {
            SessionId = Guid.NewGuid().ToString(),
            DisplayName = $"Worker-{role}",
            WorkingDirectory = workspacePath,
            ModelId = config.WorkerModelId ?? config.OrchestratorModelId ?? AppSettings.FallbackModel,
            SystemPrompt = roleConfig.SystemInstructions,
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            EnabledMcpServers = config.EnabledMcpServers.Count > 0
                ? new List<string>(config.EnabledMcpServers)
                : null,
            DisabledSkills = config.DisabledSkills.Count > 0
                ? new List<string>(config.DisabledSkills)
                : null
        };

        // Apply model override from role config if specified
        if (!string.IsNullOrWhiteSpace(roleConfig.ModelOverride))
        {
            session.ModelId = roleConfig.ModelOverride;
        }

        _logger.LogDebug(
            "Created role session {SessionId} for role {Role} in {Workspace}",
            session.SessionId, role, workspacePath);

        // We return the session ID; the actual Copilot SDK session is created lazily
        // when SendMessageAsync is first called on this session.
        await Task.CompletedTask; // Satisfy async contract
        return session.SessionId;
    }

    public AgentRoleConfig GetEffectiveRoleConfig(AgentRole role, MultiAgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Start with the built-in default
        var defaultConfig = BuiltInDefaults.Value.TryGetValue(role, out var builtIn)
            ? builtIn
            : new AgentRoleConfig { Role = role };

        // Check for user override
        if (config.RoleConfigs.TryGetValue(role, out var userOverride))
        {
            return MergeConfigs(defaultConfig, userOverride);
        }

        return defaultConfig;
    }

    /// <summary>
    /// Merge user overrides on top of defaults. User values take precedence
    /// when non-null/non-empty; otherwise the default is preserved.
    /// </summary>
    private static AgentRoleConfig MergeConfigs(AgentRoleConfig defaults, AgentRoleConfig overrides)
    {
        return new AgentRoleConfig
        {
            Role = overrides.Role != AgentRole.Generic ? overrides.Role : defaults.Role,
            SystemInstructions = !string.IsNullOrWhiteSpace(overrides.SystemInstructions)
                ? overrides.SystemInstructions
                : defaults.SystemInstructions,
            ModelOverride = !string.IsNullOrWhiteSpace(overrides.ModelOverride)
                ? overrides.ModelOverride
                : defaults.ModelOverride,
            PreferredTools = overrides.PreferredTools.Count > 0
                ? overrides.PreferredTools
                : defaults.PreferredTools
        };
    }
}