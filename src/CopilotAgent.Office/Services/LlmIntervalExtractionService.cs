using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using CopilotAgent.Office.Models;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.Office.Services;

/// <summary>
/// LLM-canonical interval extraction service. Sends a tightly constrained prompt
/// to the LLM to extract a rest interval from arbitrary natural language text.
///
/// Reuses <see cref="IReasoningStream"/> — the established LLM calling pattern in this codebase.
/// Response is capped at 10 tokens (integer or "null") for minimum latency/cost.
///
/// Resilience: all failures are caught and logged; <see cref="ExtractionResult.NotFound"/> is returned.
/// </summary>
public sealed class LlmIntervalExtractionService : IIntervalExtractionService
{
    /// <summary>
    /// Versioned system prompt — deterministic, structured, minimal.
    /// The LLM only returns a single integer or "null". No explanation.
    /// </summary>
    private const string SystemPrompt = """
        You are an interval extractor. Your ONLY job is to determine if the user text expresses
        a desired repeat/check/rest interval (how often to check, run, or repeat something).

        Rules:
        1. Convert any time expression to whole minutes:
           - "every 5 minutes" → 5
           - "12 times an hour" → 5  (60 ÷ 12)
           - "twice an hour" → 30
           - "every 2 hours" → 120
           - "every half hour" → 30
           - "every 30 seconds" → 1
           - "daily" → 1440
           - "hourly" → 60
        2. Respond with ONLY a single integer, or the exact word: null
        3. No explanation. No units. No punctuation. Just the number or null.
        4. If the text does not express a repeat/check interval at all, respond: null
        """;

    private readonly ICopilotService _copilotService;
    private readonly IReasoningStream _reasoningStream;
    private readonly IIntervalExtractionCache _cache;
    private readonly ILogger<LlmIntervalExtractionService> _logger;

    /// <summary>
    /// Dedicated LLM session for interval extraction — lightweight, short-lived prompts.
    /// Created lazily on first use and reused across calls within the same app lifetime.
    /// </summary>
    private Session? _extractionSession;

    public LlmIntervalExtractionService(
        ICopilotService copilotService,
        IReasoningStream reasoningStream,
        IIntervalExtractionCache cache,
        ILogger<LlmIntervalExtractionService> logger)
    {
        _copilotService = copilotService;
        _reasoningStream = reasoningStream;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ExtractionResult> ExtractAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ExtractionResult.NotFound;

        // Check cache first — identical text → identical result
        var cached = _cache.TryGet(text);
        if (cached.HasValue)
        {
            _logger.LogDebug("Interval extraction cache hit for text: {Text}", Truncate(text));
            return cached.Value;
        }

        try
        {
            var session = EnsureSession();
            var prompt = $"Extract the repeat/check interval from this text:\n\n{text}";

            var response = await _reasoningStream.StreamAsync(
                _copilotService.SendMessageStreamingAsync(session, prompt, ct),
                "IntervalExtractor",
                iterationNumber: 0,
                onDelta: null, // No live commentary for extraction — silent background call
                ct).ConfigureAwait(false);

            var result = Parse(response.Trim(), text);
            _cache.Set(text, result);

            if (result.IsFound)
            {
                _logger.LogInformation(
                    "Interval extracted: {Minutes} min from text: {Text}",
                    result.Minutes, Truncate(text));
            }
            else
            {
                _logger.LogDebug("No interval found in text: {Text}", Truncate(text));
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Interval extraction cancelled");
            return ExtractionResult.NotFound;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Interval extraction LLM call failed for text: {Text}", Truncate(text));
            return ExtractionResult.NotFound;
        }
    }

    /// <summary>
    /// Parses the LLM response. Expects a single integer or "null".
    /// </summary>
    private static ExtractionResult Parse(string response, string originalText)
    {
        if (string.Equals(response, "null", StringComparison.OrdinalIgnoreCase))
            return ExtractionResult.NotFound;

        // Handle cases where LLM might return extra text around the number
        // Try to extract just the first integer from the response
        var cleaned = response.Trim();

        if (int.TryParse(cleaned, out var minutes) && minutes > 0)
            return ExtractionResult.Found(minutes, originalText);

        // Fallback: try to find an integer anywhere in the response (LLM may add minor text)
        var match = System.Text.RegularExpressions.Regex.Match(cleaned, @"\b(\d+)\b");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var fallbackMinutes) && fallbackMinutes > 0)
            return ExtractionResult.Found(fallbackMinutes, originalText);

        return ExtractionResult.NotFound;
    }

    /// <summary>
    /// Lazily creates a dedicated session for interval extraction.
    /// Uses a minimal system prompt and no tools/skills.
    /// </summary>
    private Session EnsureSession()
    {
        return _extractionSession ??= new Session
        {
            SessionId = $"office-interval-extractor-{Guid.NewGuid():N}",
            DisplayName = "Interval Extractor",
            SystemPrompt = SystemPrompt,
            AutonomousMode = new AutonomousModeSettings { AllowAll = false }
        };
    }

    private static string Truncate(string text) =>
        text.Length > 100 ? text[..100] + "..." : text;
}