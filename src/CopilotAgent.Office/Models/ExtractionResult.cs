namespace CopilotAgent.Office.Models;

/// <summary>
/// Result of an interval extraction attempt. Immutable value object.
/// Carries both the extracted value and the normalized expression for observability.
/// </summary>
public readonly record struct ExtractionResult
{
    /// <summary>Sentinel for "no interval found in the text".</summary>
    public static readonly ExtractionResult NotFound = new(false, 0, string.Empty);

    /// <summary>Whether an interval expression was found.</summary>
    public bool IsFound { get; }

    /// <summary>Extracted interval in minutes, clamped to [1, 60]. Only meaningful when <see cref="IsFound"/> is true.</summary>
    public int Minutes { get; }

    /// <summary>
    /// The original expression in the text that was interpreted (for logging/UI).
    /// Truncated to 100 chars max.
    /// </summary>
    public string NormalizedExpression { get; }

    private ExtractionResult(bool found, int minutes, string expression)
    {
        IsFound = found;
        Minutes = minutes;
        NormalizedExpression = expression;
    }

    /// <summary>
    /// Creates a successful extraction result with clamped minutes.
    /// </summary>
    /// <param name="minutes">Raw extracted minutes (will be clamped to [1, 60]).</param>
    /// <param name="expression">The original text expression that was interpreted.</param>
    public static ExtractionResult Found(int minutes, string expression) =>
        new(true,
            Math.Clamp(minutes, 1, 60),
            expression.Length > 100 ? expression[..100] : expression);
}