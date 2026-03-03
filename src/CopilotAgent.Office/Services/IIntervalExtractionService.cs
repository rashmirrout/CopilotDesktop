using CopilotAgent.Office.Models;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Extracts a rest/check interval (in minutes) from arbitrary natural language text.
/// Uses LLM for semantic understanding (handles expressions like "12 times an hour" → 5 min).
/// 
/// Contract:
/// - Never throws — failures are swallowed and logged; <see cref="ExtractionResult.NotFound"/> is returned.
/// - Always returns a valid <see cref="ExtractionResult"/>.
/// - Cancellation returns <see cref="ExtractionResult.NotFound"/> gracefully.
/// </summary>
public interface IIntervalExtractionService
{
    /// <summary>
    /// Analyzes text and extracts an explicitly stated rest interval.
    /// Returns <see cref="ExtractionResult.NotFound"/> if no interval is expressed.
    /// </summary>
    /// <param name="text">The user's natural language text (objective or injected instruction).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Extraction result with minutes and the normalized expression for observability.</returns>
    Task<ExtractionResult> ExtractAsync(string text, CancellationToken ct = default);
}