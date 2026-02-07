namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Configuration for a specific agent role. Defines the system instructions,
/// preferred tools, and MCP servers for workers assigned this role.
/// </summary>
public sealed class AgentRoleConfig
{
    /// <summary>The role this config applies to.</summary>
    public AgentRole Role { get; set; }

    /// <summary>
    /// System instructions prepended to the worker's session.
    /// These tailor the LLM's behavior for the specific role.
    /// </summary>
    public string SystemInstructions { get; set; } = string.Empty;

    /// <summary>
    /// Tools that this role prefers to use. These are prioritized
    /// in the worker's prompt but not exclusively enforced.
    /// </summary>
    public List<string> PreferredTools { get; set; } = new();

    /// <summary>
    /// MCP servers specifically enabled for this role.
    /// Merged with the global MCP server list.
    /// </summary>
    public List<string> EnabledMcpServers { get; set; } = new();

    /// <summary>
    /// Optional model override for this role (e.g., use a more powerful
    /// model for Synthesis, a faster one for CodeAnalysis).
    /// </summary>
    public string? ModelOverride { get; set; }

    /// <summary>
    /// Temperature override for this role's LLM calls.
    /// Lower for deterministic roles (CodeAnalysis), higher for creative (Synthesis).
    /// </summary>
    public double? TemperatureOverride { get; set; }
}

/// <summary>
/// Provides sensible default role configurations for all agent roles.
/// </summary>
public static class DefaultRoleConfigs
{
    public static Dictionary<AgentRole, AgentRoleConfig> GetDefaults() => new()
    {
        [AgentRole.Planning] = new AgentRoleConfig
        {
            Role = AgentRole.Planning,
            SystemInstructions = """
                You are a Task Planning specialist. Your job is to:
                1. Analyze complex tasks and break them into atomic, parallelizable work chunks.
                2. Identify dependencies between chunks accurately.
                3. Estimate complexity and assign appropriate roles.
                4. Maximize parallelism while maintaining correctness.
                Be precise and structured. Output plans in the required JSON format.
                """,
            PreferredTools = ["code_analysis", "file_search"]
        },

        [AgentRole.CodeAnalysis] = new AgentRoleConfig
        {
            Role = AgentRole.CodeAnalysis,
            SystemInstructions = """
                You are a Code Analysis specialist. Focus on:
                1. Static analysis: find bugs, code smells, security vulnerabilities.
                2. Code review: readability, maintainability, adherence to patterns.
                3. Architecture analysis: coupling, cohesion, SOLID violations.
                Be thorough. Reference exact file paths and line numbers.
                """,
            PreferredTools = ["code_analysis", "file_search", "read_file"],
            TemperatureOverride = 0.1
        },

        [AgentRole.MemoryDiagnostics] = new AgentRoleConfig
        {
            Role = AgentRole.MemoryDiagnostics,
            SystemInstructions = """
                You are a Memory Diagnostics specialist. Focus on:
                1. Identifying memory leaks (missing Dispose, event handler leaks, static references).
                2. Analyzing allocation patterns and GC pressure.
                3. Detecting unbounded collections and cache growth.
                4. Reviewing IDisposable implementations and using statements.
                Reference exact code locations with file paths and line numbers.
                """,
            PreferredTools = ["code_analysis", "terminal", "read_file"],
            TemperatureOverride = 0.1
        },

        [AgentRole.Performance] = new AgentRoleConfig
        {
            Role = AgentRole.Performance,
            SystemInstructions = """
                You are a Performance Analysis specialist. Focus on:
                1. Algorithmic complexity analysis (time and space).
                2. Hot path identification and optimization opportunities.
                3. Concurrency bottlenecks (lock contention, thread pool starvation).
                4. I/O patterns (async/await correctness, unnecessary blocking).
                Provide specific optimization recommendations with expected impact.
                """,
            PreferredTools = ["code_analysis", "terminal", "read_file"],
            TemperatureOverride = 0.2
        },

        [AgentRole.Testing] = new AgentRoleConfig
        {
            Role = AgentRole.Testing,
            SystemInstructions = """
                You are a Testing specialist. Focus on:
                1. Writing comprehensive unit tests with edge cases.
                2. Integration test design and execution.
                3. Test coverage analysis and gap identification.
                4. Test maintainability and determinism.
                Use the project's testing framework. Ensure tests are deterministic and fast.
                """,
            PreferredTools = ["code_analysis", "terminal", "write_file", "read_file"],
            TemperatureOverride = 0.2
        },

        [AgentRole.Implementation] = new AgentRoleConfig
        {
            Role = AgentRole.Implementation,
            SystemInstructions = """
                You are an Implementation specialist. Focus on:
                1. Writing clean, production-quality code following project conventions.
                2. Minimal, focused changes — avoid unnecessary refactoring.
                3. Proper error handling, logging, and documentation.
                4. Reusing existing utilities and patterns from the codebase.
                Follow the project's coding standards exactly.
                """,
            PreferredTools = ["code_analysis", "terminal", "write_file", "read_file"],
            TemperatureOverride = 0.3
        },

        [AgentRole.Synthesis] = new AgentRoleConfig
        {
            Role = AgentRole.Synthesis,
            SystemInstructions = """
                You are a Synthesis specialist. Your job is to:
                1. Consolidate findings from multiple worker agents into a cohesive report.
                2. Identify conflicts, overlaps, and gaps between worker outputs.
                3. Produce clear, actionable recommendations.
                4. Write a conversational summary that a human can understand immediately.
                Be concise but thorough. Prioritize actionable insights.
                """,
            PreferredTools = ["code_analysis", "read_file"],
            TemperatureOverride = 0.5
        },

        [AgentRole.Generic] = new AgentRoleConfig
        {
            Role = AgentRole.Generic,
            SystemInstructions = """
                You are a general-purpose coding agent. Execute the assigned task
                thoroughly and report your findings clearly.
                """,
            PreferredTools = ["code_analysis", "terminal", "write_file", "read_file"]
        }
    };
}