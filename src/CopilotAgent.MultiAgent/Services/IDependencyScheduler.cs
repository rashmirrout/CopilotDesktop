using CopilotAgent.MultiAgent.Models;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Builds a dependency-aware execution schedule from an OrchestrationPlan
/// using DAG topological sort. Groups chunks into parallel execution stages.
/// </summary>
public interface IDependencyScheduler
{
    /// <summary>
    /// Build an ordered list of execution stages from the plan.
    /// Each stage contains chunks that can run in parallel.
    /// Stages are executed sequentially.
    /// </summary>
    List<ExecutionStage> BuildSchedule(OrchestrationPlan plan);

    /// <summary>
    /// Validate the dependency graph for cycles, missing references, and self-dependencies.
    /// </summary>
    DependencyValidationResult ValidateDependencies(OrchestrationPlan plan);
}

/// <summary>
/// A group of work chunks that can execute in parallel (same dependency depth).
/// </summary>
public sealed class ExecutionStage
{
    public int StageIndex { get; set; }
    public List<WorkChunk> Chunks { get; set; } = new();
}

/// <summary>
/// Result of dependency graph validation.
/// </summary>
public sealed class DependencyValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}