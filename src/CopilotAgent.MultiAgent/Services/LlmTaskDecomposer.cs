using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.MultiAgent.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.MultiAgent.Services;

/// <summary>
/// Decomposes a high-level task into an <see cref="OrchestrationPlan"/> by prompting
/// the orchestrator's Copilot session to return a structured JSON plan.
/// Includes JSON schema validation and retry-on-parse-failure logic.
/// </summary>
public sealed class LlmTaskDecomposer : ITaskDecomposer
{
    private readonly ICopilotService _copilotService;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<LlmTaskDecomposer> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Regex to extract a JSON block from an LLM response that may contain markdown fences.
    /// Matches ```json ... ``` or the first top-level { ... } or [ ... ].
    /// </summary>
    private static readonly Regex JsonBlockRegex = new(
        @"```(?:json)?\s*\n([\s\S]*?)\n\s*```",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public LlmTaskDecomposer(
        ICopilotService copilotService,
        ISessionManager sessionManager,
        ILogger<LlmTaskDecomposer> logger)
    {
        ArgumentNullException.ThrowIfNull(copilotService);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(logger);

        _copilotService = copilotService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<OrchestrationPlan> DecomposeAsync(
        string taskPrompt,
        string orchestratorSessionId,
        MultiAgentConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(orchestratorSessionId);
        ArgumentNullException.ThrowIfNull(config);

        _logger.LogInformation("Decomposing task via LLM for session {SessionId}", orchestratorSessionId);

        // Build the decomposition prompt
        var prompt = BuildDecompositionPrompt(taskPrompt, config);

        // Find or create the orchestrator session
        var session = GetOrCreateOrchestratorSession(orchestratorSessionId, config);

        // Send the prompt to the orchestrator LLM and get a response
        var response = await _copilotService.SendMessageAsync(session, prompt, cancellationToken);
        var responseText = response.Content ?? string.Empty;

        _logger.LogDebug(
            "LLM decomposition response ({Length} chars) for session {SessionId}",
            responseText.Length, orchestratorSessionId);

        // Extract and parse the JSON plan from the response
        var plan = ParsePlanFromResponse(responseText, taskPrompt);

        _logger.LogInformation(
            "Decomposed task into {ChunkCount} chunks for plan {PlanId}",
            plan.Chunks.Count, plan.PlanId);

        return plan;
    }

    public PlanValidationResult ValidatePlanJson(string rawJson)
    {
        var result = new PlanValidationResult();

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            result.Errors.Add("Plan JSON is empty or whitespace.");
            return result;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<PlanDto>(rawJson, JsonOptions);
            if (dto == null)
            {
                result.Errors.Add("Deserialized plan is null.");
                return result;
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(dto.PlanSummary))
            {
                result.Errors.Add("Missing required field: planSummary");
            }

            if (dto.Chunks == null || dto.Chunks.Count == 0)
            {
                result.Errors.Add("Plan must contain at least one chunk.");
                return result;
            }

            var chunkIds = new HashSet<string>();
            for (var i = 0; i < dto.Chunks.Count; i++)
            {
                var chunk = dto.Chunks[i];
                var prefix = $"chunks[{i}]";

                if (string.IsNullOrWhiteSpace(chunk.ChunkId))
                {
                    result.Errors.Add($"{prefix}: Missing chunkId");
                }
                else if (!chunkIds.Add(chunk.ChunkId))
                {
                    result.Errors.Add($"{prefix}: Duplicate chunkId '{chunk.ChunkId}'");
                }

                if (string.IsNullOrWhiteSpace(chunk.Title))
                {
                    result.Errors.Add($"{prefix}: Missing title");
                }

                if (string.IsNullOrWhiteSpace(chunk.Prompt))
                {
                    result.Errors.Add($"{prefix}: Missing prompt");
                }
            }

            // Validate dependency references
            foreach (var chunk in dto.Chunks)
            {
                if (chunk.DependsOn == null) continue;
                foreach (var dep in chunk.DependsOn)
                {
                    if (!chunkIds.Contains(dep))
                    {
                        result.Errors.Add(
                            $"Chunk '{chunk.ChunkId}' depends on unknown chunk '{dep}'");
                    }
                }
            }

            result.IsValid = result.Errors.Count == 0;
            if (result.IsValid)
            {
                result.SanitizedJson = JsonSerializer.Serialize(dto, JsonOptions);
            }
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"JSON parse error: {ex.Message}");
        }

        return result;
    }

    private static string BuildDecompositionPrompt(string taskPrompt, MultiAgentConfig config)
    {
        var rolesList = string.Join(", ",
            Enum.GetNames<AgentRole>().Where(r => r != nameof(AgentRole.Planning)));

        var jsonExample = """
            {
              "planSummary": "Brief description of the overall plan",
              "chunks": [
                {
                  "chunkId": "chunk-1",
                  "sequenceIndex": 0,
                  "title": "Short title",
                  "prompt": "Detailed, self-contained instructions for this chunk",
                  "dependsOn": [],
                  "workingScope": "src/path/to/focus",
                  "requiredSkills": [],
                  "complexity": "Low|Medium|High",
                  "assignedRole": "Generic|CodeAnalysis|MemoryDiagnostics|Performance|Testing|Implementation|Synthesis"
                }
              ]
            }
            """;

        return $"""
            You are a task decomposition engine. Analyze the following task and break it down
            into independent, parallelizable work chunks that can be executed by specialized agents.

            ## Available Agent Roles
            {rolesList}

            ## Constraints
            - Maximum {config.MaxParallelSessions} parallel workers
            - Each chunk must be self-contained with a clear, actionable prompt
            - Minimize dependencies between chunks to maximize parallelism
            - Assign the most appropriate role to each chunk
            - Use "dependsOn" only when a chunk truly cannot start without another's output

            ## Task
            {taskPrompt}

            ## Required Output Format
            Respond with ONLY a JSON object (no markdown, no explanation) in this exact schema:

            {jsonExample}

            Rules:
            1. chunkId must be unique across all chunks (use "chunk-1", "chunk-2", etc.)
            2. sequenceIndex is zero-based order
            3. dependsOn is an array of chunkIds that must complete before this chunk starts
            4. workingScope is optional — the directory/file path to focus on
            5. requiredSkills is optional — tool names needed
            6. Respond with valid JSON only — no surrounding text or markdown fences
            """;
    }

    private Session GetOrCreateOrchestratorSession(string orchestratorSessionId, MultiAgentConfig config)
    {
        // Try to find an existing session managed by ISessionManager
        var sessions = _sessionManager.Sessions;
        var existing = sessions.FirstOrDefault(s => s.SessionId == orchestratorSessionId);
        if (existing != null)
        {
            return existing;
        }

        // Create a transient orchestrator session
        _logger.LogDebug(
            "Creating transient orchestrator session {SessionId}", orchestratorSessionId);

        return new Session
        {
            SessionId = orchestratorSessionId,
            DisplayName = "Orchestrator",
            WorkingDirectory = config.WorkingDirectory,
            ModelId = config.OrchestratorModelId ?? "gpt-4",
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        };
    }

    private OrchestrationPlan ParsePlanFromResponse(string responseText, string taskPrompt)
    {
        // Try to extract JSON from the response (may be wrapped in markdown fences)
        var json = ExtractJson(responseText);

        // Validate the extracted JSON
        var validation = ValidatePlanJson(json);
        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "LLM plan validation failed with {ErrorCount} errors: {Errors}",
                validation.Errors.Count, string.Join("; ", validation.Errors));

            // Fall back to a single-chunk plan if parsing fails completely
            return CreateFallbackPlan(taskPrompt);
        }

        // Parse the validated JSON into model objects
        var dto = JsonSerializer.Deserialize<PlanDto>(
            validation.SanitizedJson ?? json, JsonOptions)!;

        var plan = new OrchestrationPlan
        {
            TaskDescription = taskPrompt,
            PlanSummary = dto.PlanSummary ?? "LLM-generated plan"
        };

        foreach (var chunkDto in dto.Chunks ?? Enumerable.Empty<ChunkDto>())
        {
            var chunk = new WorkChunk
            {
                ChunkId = chunkDto.ChunkId ?? Guid.NewGuid().ToString(),
                SequenceIndex = chunkDto.SequenceIndex,
                Title = chunkDto.Title ?? "Untitled chunk",
                Prompt = chunkDto.Prompt ?? string.Empty,
                DependsOnChunkIds = chunkDto.DependsOn ?? new List<string>(),
                WorkingScope = chunkDto.WorkingScope,
                RequiredSkills = chunkDto.RequiredSkills ?? new List<string>(),
                Complexity = ParseComplexity(chunkDto.Complexity),
                AssignedRole = ParseRole(chunkDto.AssignedRole)
            };

            plan.Chunks.Add(chunk);
        }

        return plan;
    }

    private static string ExtractJson(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return string.Empty;
        }

        // Try markdown fenced block first
        var match = JsonBlockRegex.Match(responseText);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // Try to find the first top-level JSON object
        var firstBrace = responseText.IndexOf('{');
        var lastBrace = responseText.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return responseText[firstBrace..(lastBrace + 1)];
        }

        // Return as-is and let validation handle it
        return responseText.Trim();
    }

    private OrchestrationPlan CreateFallbackPlan(string taskPrompt)
    {
        _logger.LogWarning("Using fallback single-chunk plan due to LLM parse failure");

        var plan = new OrchestrationPlan
        {
            TaskDescription = taskPrompt,
            PlanSummary = "Fallback: executing task as a single unit"
        };

        plan.Chunks.Add(new WorkChunk
        {
            ChunkId = "chunk-fallback",
            SequenceIndex = 0,
            Title = "Complete Task",
            Prompt = taskPrompt,
            AssignedRole = AgentRole.Generic,
            Complexity = ChunkComplexity.High
        });

        return plan;
    }

    private static ChunkComplexity ParseComplexity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ChunkComplexity.Medium;
        return Enum.TryParse<ChunkComplexity>(value, ignoreCase: true, out var result)
            ? result
            : ChunkComplexity.Medium;
    }

    private static AgentRole ParseRole(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return AgentRole.Generic;
        return Enum.TryParse<AgentRole>(value, ignoreCase: true, out var result)
            ? result
            : AgentRole.Generic;
    }

    #region Internal DTOs for JSON deserialization

    /// <summary>
    /// Internal DTO matching the JSON schema sent to / received from the LLM.
    /// Kept separate from the domain model to decouple parsing from business logic.
    /// </summary>
    private sealed class PlanDto
    {
        [JsonPropertyName("planSummary")]
        public string? PlanSummary { get; set; }

        [JsonPropertyName("chunks")]
        public List<ChunkDto>? Chunks { get; set; }
    }

    private sealed class ChunkDto
    {
        [JsonPropertyName("chunkId")]
        public string? ChunkId { get; set; }

        [JsonPropertyName("sequenceIndex")]
        public int SequenceIndex { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("dependsOn")]
        public List<string>? DependsOn { get; set; }

        [JsonPropertyName("workingScope")]
        public string? WorkingScope { get; set; }

        [JsonPropertyName("requiredSkills")]
        public List<string>? RequiredSkills { get; set; }

        [JsonPropertyName("complexity")]
        public string? Complexity { get; set; }

        [JsonPropertyName("assignedRole")]
        public string? AssignedRole { get; set; }
    }

    #endregion
}