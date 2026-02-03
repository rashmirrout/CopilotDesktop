using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CopilotAgent.App.Helpers;

/// <summary>
/// Parses ANSI escape sequences and converts them to WPF formatted text
/// </summary>
public static class AnsiParser
{
    // ANSI escape sequence pattern: ESC[<params>m
    private static readonly Regex AnsiPattern = new(@"\x1b\[([0-9;]*)m", RegexOptions.Compiled);

    // Standard ANSI colors (0-7)
    private static readonly Color[] StandardColors = new[]
    {
        Color.FromRgb(12, 12, 12),    // 0: Black
        Color.FromRgb(197, 15, 31),   // 1: Red
        Color.FromRgb(19, 161, 14),   // 2: Green
        Color.FromRgb(193, 156, 0),   // 3: Yellow
        Color.FromRgb(0, 55, 218),    // 4: Blue
        Color.FromRgb(136, 23, 152),  // 5: Magenta
        Color.FromRgb(58, 150, 221),  // 6: Cyan
        Color.FromRgb(204, 204, 204)  // 7: White
    };

    // Bright ANSI colors (8-15)
    private static readonly Color[] BrightColors = new[]
    {
        Color.FromRgb(118, 118, 118), // 8: Bright Black (Gray)
        Color.FromRgb(231, 72, 86),   // 9: Bright Red
        Color.FromRgb(22, 198, 12),   // 10: Bright Green
        Color.FromRgb(249, 241, 165), // 11: Bright Yellow
        Color.FromRgb(59, 120, 255),  // 12: Bright Blue
        Color.FromRgb(180, 0, 158),   // 13: Bright Magenta
        Color.FromRgb(97, 214, 214),  // 14: Bright Cyan
        Color.FromRgb(242, 242, 242)  // 15: Bright White
    };

    private static readonly Color DefaultForeground = Color.FromRgb(204, 204, 204);
    private static readonly Color DefaultBackground = Color.FromRgb(12, 12, 12);

    /// <summary>
    /// Current text style state
    /// </summary>
    private class TextStyle
    {
        public Color Foreground { get; set; } = DefaultForeground;
        public Color Background { get; set; } = DefaultBackground;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool Inverse { get; set; }

        public void Reset()
        {
            Foreground = DefaultForeground;
            Background = DefaultBackground;
            Bold = false;
            Italic = false;
            Underline = false;
            Inverse = false;
        }

        public TextStyle Clone() => new()
        {
            Foreground = Foreground,
            Background = Background,
            Bold = Bold,
            Italic = Italic,
            Underline = Underline,
            Inverse = Inverse
        };
    }

    /// <summary>
    /// Parse ANSI text and create FlowDocument Paragraph with styled Runs
    /// </summary>
    public static void AppendAnsiText(Paragraph paragraph, string text)
    {
        var style = new TextStyle();
        var lastIndex = 0;

        foreach (Match match in AnsiPattern.Matches(text))
        {
            // Add text before this escape sequence
            if (match.Index > lastIndex)
            {
                var plainText = text.Substring(lastIndex, match.Index - lastIndex);
                var run = CreateRun(plainText, style);
                paragraph.Inlines.Add(run);
            }

            // Parse and apply the escape sequence
            ParseAnsiCode(match.Groups[1].Value, style);
            lastIndex = match.Index + match.Length;
        }

        // Add remaining text after last escape sequence
        if (lastIndex < text.Length)
        {
            var remainingText = text.Substring(lastIndex);
            var run = CreateRun(remainingText, style);
            paragraph.Inlines.Add(run);
        }
    }

    /// <summary>
    /// Create a FlowDocument from ANSI text
    /// </summary>
    public static FlowDocument CreateDocument(string text)
    {
        var document = new FlowDocument
        {
            Background = new SolidColorBrush(DefaultBackground),
            Foreground = new SolidColorBrush(DefaultForeground),
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 12,
            PagePadding = new Thickness(8)
        };

        var paragraph = new Paragraph { Margin = new Thickness(0) };
        AppendAnsiText(paragraph, text);
        document.Blocks.Add(paragraph);

        return document;
    }

    /// <summary>
    /// Strip all ANSI escape sequences from text
    /// </summary>
    public static string StripAnsi(string text)
    {
        return AnsiPattern.Replace(text, string.Empty);
    }

    private static Run CreateRun(string text, TextStyle style)
    {
        var fg = style.Inverse ? style.Background : style.Foreground;
        var bg = style.Inverse ? style.Foreground : style.Background;

        // Apply bold brightness boost
        if (style.Bold && !style.Inverse)
        {
            fg = GetBrightColor(fg);
        }

        var run = new Run(text)
        {
            Foreground = new SolidColorBrush(fg)
        };

        if (bg != DefaultBackground)
        {
            run.Background = new SolidColorBrush(bg);
        }

        if (style.Bold)
        {
            run.FontWeight = FontWeights.Bold;
        }

        if (style.Italic)
        {
            run.FontStyle = FontStyles.Italic;
        }

        if (style.Underline)
        {
            run.TextDecorations = TextDecorations.Underline;
        }

        return run;
    }

    private static void ParseAnsiCode(string paramString, TextStyle style)
    {
        if (string.IsNullOrEmpty(paramString))
        {
            style.Reset();
            return;
        }

        var codes = paramString.Split(';');
        var i = 0;

        while (i < codes.Length)
        {
            if (!int.TryParse(codes[i], out var code))
            {
                i++;
                continue;
            }

            switch (code)
            {
                case 0: // Reset
                    style.Reset();
                    break;
                case 1: // Bold
                    style.Bold = true;
                    break;
                case 3: // Italic
                    style.Italic = true;
                    break;
                case 4: // Underline
                    style.Underline = true;
                    break;
                case 7: // Inverse
                    style.Inverse = true;
                    break;
                case 22: // Normal intensity
                    style.Bold = false;
                    break;
                case 23: // Not italic
                    style.Italic = false;
                    break;
                case 24: // Not underlined
                    style.Underline = false;
                    break;
                case 27: // Not inverse
                    style.Inverse = false;
                    break;
                case >= 30 and <= 37: // Standard foreground colors
                    style.Foreground = StandardColors[code - 30];
                    break;
                case 38: // Extended foreground color
                    i = ParseExtendedColor(codes, i, out var fg);
                    if (fg.HasValue) style.Foreground = fg.Value;
                    break;
                case 39: // Default foreground
                    style.Foreground = DefaultForeground;
                    break;
                case >= 40 and <= 47: // Standard background colors
                    style.Background = StandardColors[code - 40];
                    break;
                case 48: // Extended background color
                    i = ParseExtendedColor(codes, i, out var bg);
                    if (bg.HasValue) style.Background = bg.Value;
                    break;
                case 49: // Default background
                    style.Background = DefaultBackground;
                    break;
                case >= 90 and <= 97: // Bright foreground colors
                    style.Foreground = BrightColors[code - 90];
                    break;
                case >= 100 and <= 107: // Bright background colors
                    style.Background = BrightColors[code - 100];
                    break;
            }
            i++;
        }
    }

    private static int ParseExtendedColor(string[] codes, int startIndex, out Color? color)
    {
        color = null;
        
        if (startIndex + 1 >= codes.Length)
            return startIndex;

        if (!int.TryParse(codes[startIndex + 1], out var mode))
            return startIndex;

        if (mode == 5 && startIndex + 2 < codes.Length)
        {
            // 256 color mode: 38;5;n
            if (int.TryParse(codes[startIndex + 2], out var colorIndex))
            {
                color = Get256Color(colorIndex);
                return startIndex + 2;
            }
        }
        else if (mode == 2 && startIndex + 4 < codes.Length)
        {
            // RGB mode: 38;2;r;g;b
            if (int.TryParse(codes[startIndex + 2], out var r) &&
                int.TryParse(codes[startIndex + 3], out var g) &&
                int.TryParse(codes[startIndex + 4], out var b))
            {
                color = Color.FromRgb((byte)r, (byte)g, (byte)b);
                return startIndex + 4;
            }
        }

        return startIndex;
    }

    private static Color Get256Color(int index)
    {
        if (index < 8)
            return StandardColors[index];
        if (index < 16)
            return BrightColors[index - 8];
        if (index < 232)
        {
            // 216 color cube (6x6x6)
            index -= 16;
            var b = index % 6;
            var g = (index / 6) % 6;
            var r = index / 36;
            return Color.FromRgb(
                (byte)(r > 0 ? r * 40 + 55 : 0),
                (byte)(g > 0 ? g * 40 + 55 : 0),
                (byte)(b > 0 ? b * 40 + 55 : 0));
        }
        // Grayscale (24 levels)
        var gray = (byte)((index - 232) * 10 + 8);
        return Color.FromRgb(gray, gray, gray);
    }

    private static Color GetBrightColor(Color color)
    {
        // Find if this is a standard color and return bright version
        for (int i = 0; i < StandardColors.Length; i++)
        {
            if (StandardColors[i] == color)
                return BrightColors[i];
        }
        
        // Otherwise just lighten it
        return Color.FromRgb(
            (byte)Math.Min(255, color.R + 30),
            (byte)Math.Min(255, color.G + 30),
            (byte)Math.Min(255, color.B + 30));
    }
}