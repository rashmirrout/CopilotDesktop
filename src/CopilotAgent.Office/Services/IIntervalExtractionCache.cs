using CopilotAgent.Office.Models;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Session-scoped cache for interval extraction results.
/// Avoids redundant LLM calls for identical text within the same Office run.
/// </summary>
public interface IIntervalExtractionCache
{
    /// <summary>Returns a cached result for the given text, or null if not cached.</summary>
    ExtractionResult? TryGet(string text);

    /// <summary>Stores an extraction result for the given text.</summary>
    void Set(string text, ExtractionResult result);

    /// <summary>Clears all cached entries. Called on Office reset.</summary>
    void Clear();
}