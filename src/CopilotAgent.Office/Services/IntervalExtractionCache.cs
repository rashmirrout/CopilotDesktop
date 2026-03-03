using CopilotAgent.Office.Models;

namespace CopilotAgent.Office.Services;

/// <summary>
/// Simple Dictionary-backed session cache for interval extraction results.
/// Thread-safe via lock. Desktop app — no distributed caching needed.
/// </summary>
public sealed class IntervalExtractionCache : IIntervalExtractionCache
{
    private readonly Dictionary<string, ExtractionResult> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <inheritdoc />
    public ExtractionResult? TryGet(string text)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(text, out var result) ? result : null;
        }
    }

    /// <inheritdoc />
    public void Set(string text, ExtractionResult result)
    {
        lock (_lock)
        {
            _cache[text] = result;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }
}