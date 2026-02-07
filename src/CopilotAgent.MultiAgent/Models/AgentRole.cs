namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Specialization roles for worker agents. The orchestrator assigns
/// a role to each work chunk during planning.
/// </summary>
public enum AgentRole
{
    /// <summary>No specialization — general-purpose worker.</summary>
    Generic,

    /// <summary>Specialized in task decomposition and planning.</summary>
    Planning,

    /// <summary>Specialized in code analysis, review, and static analysis.</summary>
    CodeAnalysis,

    /// <summary>Specialized in memory diagnostics, leak detection, profiling.</summary>
    MemoryDiagnostics,

    /// <summary>Specialized in performance analysis and optimization.</summary>
    Performance,

    /// <summary>Specialized in test creation, test execution, coverage analysis.</summary>
    Testing,

    /// <summary>Specialized in code implementation, refactoring, feature development.</summary>
    Implementation,

    /// <summary>Specialized in synthesizing results from multiple workers into a cohesive report.</summary>
    Synthesis
}