using System.Text.RegularExpressions;

namespace CopilotAgent.App.Helpers;

/// <summary>
/// Parses recommended next-step actions from LLM-generated summary text.
/// Supports two formats:
/// 1. Explicit markers: <c>[ACTION:description]</c>
/// 2. Markdown list items under a "Next Steps" heading.
/// 
/// The parser strips markers from the display text so the summary reads naturally,
/// while extracting actionable items for the UI to present as clickable buttons.
/// </summary>
public static class NextStepsParser
{
    // Matches [ACTION:some description here]
    private static readonly Regex ActionMarkerRegex = new(
        @"\[ACTION:([^\]]+)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches markdown list items (- or *) under a "Next Steps" or "Recommended Next Steps" heading
    private static readonly Regex NextStepsSectionRegex = new(
        @"(?:#{1,4}\s*(?:Recommended\s+)?Next\s+Steps.*?\n)((?:\s*[-*]\s+.+\n?)+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // Matches individual list items
    private static readonly Regex ListItemRegex = new(
        @"^\s*[-*]\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Extracts next-step action descriptions from the summary text.
    /// First tries explicit [ACTION:...] markers, then falls back to
    /// parsing markdown list items under a "Next Steps" heading.
    /// </summary>
    /// <param name="summaryText">The LLM-generated summary containing action markers or next steps section.</param>
    /// <returns>A list of action descriptions, empty if none found.</returns>
    public static List<string> ExtractNextSteps(string? summaryText)
    {
        if (string.IsNullOrWhiteSpace(summaryText))
            return new List<string>();

        // Strategy 1: Explicit [ACTION:...] markers (preferred — deterministic)
        var explicitActions = ExtractExplicitActions(summaryText);
        if (explicitActions.Count > 0)
            return explicitActions;

        // Strategy 2: Markdown section-based extraction (fallback)
        return ExtractFromMarkdownSection(summaryText);
    }

    /// <summary>
    /// Removes [ACTION:...] markers from the summary text so it reads cleanly.
    /// The action descriptions remain inline as normal text.
    /// </summary>
    /// <param name="summaryText">Raw summary text with [ACTION:...] markers.</param>
    /// <returns>Cleaned summary text with markers removed but descriptions preserved.</returns>
    public static string StripActionMarkers(string? summaryText)
    {
        if (string.IsNullOrWhiteSpace(summaryText))
            return string.Empty;

        // Replace [ACTION:description] with just the description text
        return ActionMarkerRegex.Replace(summaryText, match => match.Groups[1].Value.Trim());
    }

    private static List<string> ExtractExplicitActions(string text)
    {
        var actions = new List<string>();
        var matches = ActionMarkerRegex.Matches(text);

        foreach (Match match in matches)
        {
            var description = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(description) && !actions.Contains(description, StringComparer.OrdinalIgnoreCase))
            {
                actions.Add(description);
            }
        }

        return actions;
    }

    private static List<string> ExtractFromMarkdownSection(string text)
    {
        var actions = new List<string>();
        var sectionMatch = NextStepsSectionRegex.Match(text);

        if (!sectionMatch.Success)
            return actions;

        var sectionContent = sectionMatch.Groups[1].Value;
        var itemMatches = ListItemRegex.Matches(sectionContent);

        foreach (Match itemMatch in itemMatches)
        {
            var item = itemMatch.Groups[1].Value.Trim();
            // Strip any bold/italic markdown formatting for cleaner button text
            item = item.Replace("**", "").Replace("*", "").Trim();

            if (!string.IsNullOrWhiteSpace(item) && !actions.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                actions.Add(item);
            }
        }

        return actions;
    }
}