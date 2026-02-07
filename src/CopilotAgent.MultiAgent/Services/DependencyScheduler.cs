namespace CopilotAgent.MultiAgent.Services;

using CopilotAgent.MultiAgent.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// DAG-based dependency scheduler that topologically sorts work chunks
/// into execution stages. Chunks within the same stage can run in parallel;
/// stages execute sequentially. Validates for cycles and dangling references.
/// </summary>
public sealed class DependencyScheduler : IDependencyScheduler
{
    private readonly ILogger<DependencyScheduler> _logger;

    public DependencyScheduler(ILogger<DependencyScheduler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public List<ExecutionStage> BuildSchedule(OrchestrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Chunks.Count == 0)
        {
            _logger.LogWarning("BuildSchedule called with empty plan {PlanId}", plan.PlanId);
            return [];
        }

        var validation = ValidateDependencies(plan);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Cannot build schedule for plan {plan.PlanId}: {string.Join("; ", validation.Errors)}");
        }

        // Build adjacency and in-degree maps
        var chunkMap = plan.Chunks.ToDictionary(c => c.ChunkId);
        var inDegree = plan.Chunks.ToDictionary(c => c.ChunkId, _ => 0);
        var dependents = new Dictionary<string, List<string>>();

        foreach (var chunk in plan.Chunks)
        {
            dependents[chunk.ChunkId] = [];
        }

        foreach (var chunk in plan.Chunks)
        {
            foreach (var depId in chunk.DependsOnChunkIds)
            {
                inDegree[chunk.ChunkId]++;
                dependents[depId].Add(chunk.ChunkId);
            }
        }

        // Kahn's algorithm — topological sort by layers (BFS)
        var stages = new List<ExecutionStage>();
        var ready = new Queue<string>();

        // Seed with all chunks that have no dependencies
        foreach (var (chunkId, degree) in inDegree)
        {
            if (degree == 0)
            {
                ready.Enqueue(chunkId);
            }
        }

        int stageIndex = 0;
        int processedCount = 0;

        while (ready.Count > 0)
        {
            var stageChunks = new List<WorkChunk>();

            // Drain all currently ready chunks into this stage
            int readyCount = ready.Count;
            for (int i = 0; i < readyCount; i++)
            {
                var chunkId = ready.Dequeue();
                var chunk = chunkMap[chunkId];
                chunk.Status = AgentStatus.WaitingForDependencies;
                stageChunks.Add(chunk);
                processedCount++;
            }

            // Sort chunks within a stage by SequenceIndex for deterministic ordering
            stageChunks.Sort((a, b) => a.SequenceIndex.CompareTo(b.SequenceIndex));

            stages.Add(new ExecutionStage
            {
                StageIndex = stageIndex,
                Chunks = stageChunks
            });

            _logger.LogDebug(
                "Stage {StageIndex}: {ChunkCount} chunks — [{Chunks}]",
                stageIndex,
                stageChunks.Count,
                string.Join(", ", stageChunks.Select(c => c.Title)));

            // Decrement in-degree for dependents of completed stage
            foreach (var chunk in stageChunks)
            {
                foreach (var dependentId in dependents[chunk.ChunkId])
                {
                    inDegree[dependentId]--;
                    if (inDegree[dependentId] == 0)
                    {
                        ready.Enqueue(dependentId);
                    }
                }
            }

            stageIndex++;
        }

        // Safety check — if we didn't process all chunks, there's a cycle
        // (should not happen since ValidateDependencies already checks for cycles)
        if (processedCount != plan.Chunks.Count)
        {
            throw new InvalidOperationException(
                $"Cycle detected during scheduling: processed {processedCount} of {plan.Chunks.Count} chunks");
        }

        _logger.LogInformation(
            "Built schedule for plan {PlanId}: {StageCount} stages, {ChunkCount} total chunks",
            plan.PlanId, stages.Count, plan.Chunks.Count);

        return stages;
    }

    /// <inheritdoc />
    public DependencyValidationResult ValidateDependencies(OrchestrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var errors = new List<string>();
        var chunkIds = new HashSet<string>(plan.Chunks.Select(c => c.ChunkId));

        // 1. Check for duplicate chunk IDs
        var duplicates = plan.Chunks
            .GroupBy(c => c.ChunkId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            errors.Add($"Duplicate chunk IDs: {string.Join(", ", duplicates)}");
        }

        // 2. Check for dangling dependency references
        foreach (var chunk in plan.Chunks)
        {
            foreach (var depId in chunk.DependsOnChunkIds)
            {
                if (!chunkIds.Contains(depId))
                {
                    errors.Add(
                        $"Chunk '{chunk.Title}' ({chunk.ChunkId}) depends on non-existent chunk '{depId}'");
                }
            }
        }

        // 3. Check for self-dependencies
        foreach (var chunk in plan.Chunks)
        {
            if (chunk.DependsOnChunkIds.Contains(chunk.ChunkId))
            {
                errors.Add($"Chunk '{chunk.Title}' ({chunk.ChunkId}) depends on itself");
            }
        }

        // 4. Check for cycles using DFS
        if (errors.Count == 0) // Only check cycles if basic validation passed
        {
            var cycleError = DetectCycles(plan.Chunks, chunkIds);
            if (cycleError is not null)
            {
                errors.Add(cycleError);
            }
        }

        // 5. Check for empty plan
        if (plan.Chunks.Count == 0)
        {
            errors.Add("Plan contains no work chunks");
        }

        var result = new DependencyValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };

        if (!result.IsValid)
        {
            _logger.LogWarning(
                "Dependency validation failed for plan {PlanId}: {Errors}",
                plan.PlanId, string.Join("; ", errors));
        }

        return result;
    }

    /// <summary>
    /// Detect cycles in the dependency graph using iterative DFS with three-color marking.
    /// White = unvisited, Gray = in current path, Black = fully processed.
    /// </summary>
    private static string? DetectCycles(List<WorkChunk> chunks, HashSet<string> chunkIds)
    {
        var color = new Dictionary<string, int>(); // 0=white, 1=gray, 2=black
        foreach (var id in chunkIds)
        {
            color[id] = 0;
        }

        var dependencyMap = chunks.ToDictionary(c => c.ChunkId, c => c.DependsOnChunkIds);

        foreach (var startId in chunkIds)
        {
            if (color[startId] != 0)
                continue;

            // Iterative DFS
            var stack = new Stack<(string ChunkId, bool IsBacktrack)>();
            stack.Push((startId, false));

            while (stack.Count > 0)
            {
                var (currentId, isBacktrack) = stack.Pop();

                if (isBacktrack)
                {
                    color[currentId] = 2; // Black — fully processed
                    continue;
                }

                if (color[currentId] == 2)
                    continue;

                if (color[currentId] == 1)
                {
                    // Already gray — we've come back to a node in the current path.
                    // This is a cycle.
                    return $"Dependency cycle detected involving chunk '{currentId}'";
                }

                color[currentId] = 1; // Gray — in current path

                // Push backtrack marker
                stack.Push((currentId, true));

                // Push dependencies (these are the edges: current depends on dep, so dep → current)
                // We traverse in reverse: from chunk to its dependencies
                if (dependencyMap.TryGetValue(currentId, out var deps))
                {
                    foreach (var depId in deps)
                    {
                        if (!chunkIds.Contains(depId))
                            continue;

                        if (color[depId] == 1)
                        {
                            return $"Dependency cycle detected: '{currentId}' → '{depId}'";
                        }

                        if (color[depId] == 0)
                        {
                            stack.Push((depId, false));
                        }
                    }
                }
            }
        }

        return null;
    }
}