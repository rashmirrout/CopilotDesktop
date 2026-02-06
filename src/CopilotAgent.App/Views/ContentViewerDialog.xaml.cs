using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace CopilotAgent.App.Views;

/// <summary>
/// Generic content viewer dialog for displaying text content with format-based rendering.
/// Supports various content types: Markdown, JSON, YAML, Code, Plain Text, etc.
/// </summary>
public partial class ContentViewerDialog : Window
{
    public ContentViewerOptions Options { get; }

    public ContentViewerDialog(ContentViewerOptions options)
    {
        InitializeComponent();
        Options = options;
        DataContext = options;
    }

    private void OnMetadataAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is MetadataItem metadata)
        {
            metadata.Action?.Invoke();
        }
    }

    private void OnCopyContent_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Options.Content);
            
            // Brief visual feedback could be added here
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to copy: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Shows the dialog with the specified options
    /// </summary>
    public static void Show(ContentViewerOptions options, Window? owner = null)
    {
        var dialog = new ContentViewerDialog(options)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    /// <summary>
    /// Shows content from a file path with auto-detected format
    /// </summary>
    public static void ShowFile(string filePath, string? title = null, Window? owner = null)
    {
        if (!File.Exists(filePath))
        {
            MessageBox.Show($"File not found: {filePath}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var content = File.ReadAllText(filePath);
        var format = DetectFormat(filePath);
        var fileName = Path.GetFileName(filePath);

        var options = new ContentViewerOptions
        {
            Title = title ?? $"View: {fileName}",
            DisplayTitle = fileName,
            Subtitle = Path.GetDirectoryName(filePath) ?? string.Empty,
            Content = content,
            ContentFormat = format,
            Icon = GetFormatIcon(format),
            IconBackground = GetFormatIconBackground(format)
        };

        options.AddMetadata("📂", filePath, "Open Folder", () =>
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
        });

        options.AddBadge(GetFormatBadgeText(format), GetFormatBadgeForeground(format), GetFormatBadgeBackground(format));

        Show(options, owner);
    }

    private static ContentFormat DetectFormat(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".md" or ".markdown" => ContentFormat.Markdown,
            ".json" => ContentFormat.Json,
            ".yaml" or ".yml" => ContentFormat.Yaml,
            ".xml" or ".xaml" or ".config" => ContentFormat.Xml,
            ".cs" or ".csx" => ContentFormat.CSharp,
            ".js" or ".ts" or ".jsx" or ".tsx" => ContentFormat.JavaScript,
            ".py" => ContentFormat.Python,
            ".html" or ".htm" => ContentFormat.Html,
            ".css" or ".scss" or ".sass" => ContentFormat.Css,
            ".sql" => ContentFormat.Sql,
            ".sh" or ".bash" or ".ps1" or ".bat" or ".cmd" => ContentFormat.Shell,
            ".txt" or ".log" => ContentFormat.PlainText,
            _ => ContentFormat.PlainText
        };
    }

    private static string GetFormatIcon(ContentFormat format) => format switch
    {
        ContentFormat.Markdown => "📝",
        ContentFormat.Json => "📦",
        ContentFormat.Yaml => "📋",
        ContentFormat.Xml => "📄",
        ContentFormat.CSharp => "🔷",
        ContentFormat.JavaScript => "🟨",
        ContentFormat.Python => "🐍",
        ContentFormat.Html => "🌐",
        ContentFormat.Css => "🎨",
        ContentFormat.Sql => "🗃️",
        ContentFormat.Shell => "⌨️",
        _ => "📄"
    };

    private static string GetFormatIconBackground(ContentFormat format) => format switch
    {
        ContentFormat.Markdown => "#DBEAFE",
        ContentFormat.Json => "#EDE9FE",
        ContentFormat.Yaml => "#FEF3C7",
        ContentFormat.Xml => "#F3E8FF",
        ContentFormat.CSharp => "#DBEAFE",
        ContentFormat.JavaScript => "#FEF9C3",
        ContentFormat.Python => "#D1FAE5",
        ContentFormat.Html => "#FFE4E6",
        ContentFormat.Css => "#FCE7F3",
        ContentFormat.Sql => "#E0E7FF",
        ContentFormat.Shell => "#F3F4F6",
        _ => "#F5F5F5"
    };

    private static string GetFormatBadgeText(ContentFormat format) => format switch
    {
        ContentFormat.Markdown => "Markdown",
        ContentFormat.Json => "JSON",
        ContentFormat.Yaml => "YAML",
        ContentFormat.Xml => "XML",
        ContentFormat.CSharp => "C#",
        ContentFormat.JavaScript => "JS/TS",
        ContentFormat.Python => "Python",
        ContentFormat.Html => "HTML",
        ContentFormat.Css => "CSS",
        ContentFormat.Sql => "SQL",
        ContentFormat.Shell => "Shell",
        _ => "Text"
    };

    private static string GetFormatBadgeForeground(ContentFormat format) => format switch
    {
        ContentFormat.Markdown => "#1D4ED8",
        ContentFormat.Json => "#7C3AED",
        ContentFormat.Yaml => "#D97706",
        ContentFormat.Xml => "#9333EA",
        ContentFormat.CSharp => "#2563EB",
        ContentFormat.JavaScript => "#CA8A04",
        ContentFormat.Python => "#059669",
        ContentFormat.Html => "#DC2626",
        ContentFormat.Css => "#DB2777",
        ContentFormat.Sql => "#4F46E5",
        ContentFormat.Shell => "#374151",
        _ => "#6B7280"
    };

    private static string GetFormatBadgeBackground(ContentFormat format) => format switch
    {
        ContentFormat.Markdown => "#DBEAFE",
        ContentFormat.Json => "#EDE9FE",
        ContentFormat.Yaml => "#FEF3C7",
        ContentFormat.Xml => "#F3E8FF",
        ContentFormat.CSharp => "#DBEAFE",
        ContentFormat.JavaScript => "#FEF9C3",
        ContentFormat.Python => "#D1FAE5",
        ContentFormat.Html => "#FEE2E2",
        ContentFormat.Css => "#FCE7F3",
        ContentFormat.Sql => "#E0E7FF",
        ContentFormat.Shell => "#F3F4F6",
        _ => "#F3F4F6"
    };
}

/// <summary>
/// Options for configuring the ContentViewerDialog
/// </summary>
public class ContentViewerOptions
{
    /// <summary>Window title</summary>
    public string Title { get; set; } = "View Content";
    
    /// <summary>Display title in header</summary>
    public string DisplayTitle { get; set; } = string.Empty;
    
    /// <summary>Optional subtitle below title</summary>
    public string Subtitle { get; set; } = string.Empty;
    
    /// <summary>Icon emoji</summary>
    public string Icon { get; set; } = "📄";
    
    /// <summary>Icon background color</summary>
    public string IconBackground { get; set; } = "#F5F5F5";
    
    /// <summary>The content to display</summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>Content format for rendering hints</summary>
    public ContentFormat ContentFormat { get; set; } = ContentFormat.PlainText;
    
    /// <summary>Optional footer text</summary>
    public string FooterText { get; set; } = string.Empty;
    
    /// <summary>Badges to show in header</summary>
    public List<BadgeItem> Badges { get; } = new();
    
    /// <summary>Metadata items to show</summary>
    public List<MetadataItem> MetadataItems { get; } = new();

    // Computed properties for binding
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
    public bool HasFooterText => !string.IsNullOrWhiteSpace(FooterText);
    public bool HasMetadata => MetadataItems.Count > 0;
    
    public string ContentIcon => ContentFormat switch
    {
        ContentFormat.Markdown => "📝",
        ContentFormat.Json => "📦",
        ContentFormat.Yaml => "📋",
        ContentFormat.Xml => "📄",
        _ => "📝"
    };
    
    public string ContentTypeLabel => ContentFormat switch
    {
        ContentFormat.Markdown => "Markdown",
        ContentFormat.Json => "JSON",
        ContentFormat.Yaml => "YAML", 
        ContentFormat.Xml => "XML",
        ContentFormat.CSharp => "C# Code",
        ContentFormat.JavaScript => "JavaScript",
        ContentFormat.Python => "Python",
        ContentFormat.Html => "HTML",
        ContentFormat.Css => "CSS",
        ContentFormat.Sql => "SQL",
        ContentFormat.Shell => "Shell Script",
        _ => "Content"
    };
    
    public string ContentStats => $"({Content.Length:N0} chars, {Content.Split('\n').Length} lines)";
    
    public string ContentFontFamily => ContentFormat switch
    {
        ContentFormat.Json or ContentFormat.Yaml or ContentFormat.Xml 
        or ContentFormat.CSharp or ContentFormat.JavaScript or ContentFormat.Python
        or ContentFormat.Html or ContentFormat.Css or ContentFormat.Sql or ContentFormat.Shell
            => "Cascadia Code, Consolas, Courier New",
        _ => "Segoe UI, Arial"
    };
    
    public double ContentFontSize => ContentFormat switch
    {
        ContentFormat.Json or ContentFormat.Yaml or ContentFormat.Xml 
        or ContentFormat.CSharp or ContentFormat.JavaScript or ContentFormat.Python
        or ContentFormat.Html or ContentFormat.Css or ContentFormat.Sql or ContentFormat.Shell
            => 12,
        _ => 13
    };
    
    public TextWrapping TextWrapping => ContentFormat switch
    {
        ContentFormat.Json or ContentFormat.Xml or ContentFormat.CSharp 
        or ContentFormat.JavaScript or ContentFormat.Python or ContentFormat.Html
        or ContentFormat.Css or ContentFormat.Sql or ContentFormat.Shell
            => TextWrapping.NoWrap,
        _ => TextWrapping.Wrap
    };

    /// <summary>Adds a badge to the header</summary>
    public void AddBadge(string text, string foreground = "#666", string background = "#F3F4F6")
    {
        Badges.Add(new BadgeItem { Text = text, Foreground = foreground, Background = background });
    }

    /// <summary>Adds a metadata row</summary>
    public void AddMetadata(string icon, string value, string? actionText = null, Action? action = null)
    {
        MetadataItems.Add(new MetadataItem
        {
            Icon = icon,
            Value = value,
            ActionText = actionText ?? string.Empty,
            Action = action
        });
    }
}

/// <summary>
/// Badge display item
/// </summary>
public class BadgeItem
{
    public string Text { get; set; } = string.Empty;
    public string Foreground { get; set; } = "#666";
    public string Background { get; set; } = "#F3F4F6";
}

/// <summary>
/// Metadata row item
/// </summary>
public class MetadataItem
{
    public string Icon { get; set; } = "📁";
    public string Value { get; set; } = string.Empty;
    public string ActionText { get; set; } = string.Empty;
    public Action? Action { get; set; }
    public bool HasAction => Action != null && !string.IsNullOrEmpty(ActionText);
}

/// <summary>
/// Content format types for rendering hints
/// </summary>
public enum ContentFormat
{
    PlainText,
    Markdown,
    Json,
    Yaml,
    Xml,
    CSharp,
    JavaScript,
    Python,
    Html,
    Css,
    Sql,
    Shell
}