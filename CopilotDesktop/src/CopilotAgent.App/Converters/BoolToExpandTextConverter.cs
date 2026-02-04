using System.Globalization;
using System.Windows.Data;

namespace CopilotAgent.App.Converters;

/// <summary>
/// Converts a boolean value to "Show Less" (true) or "Show More" (false) text.
/// Used for expand/collapse toggles.
/// </summary>
public class BoolToExpandTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            return isExpanded ? "Show Less" : "Show More";
        }
        return "Show More";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string text)
        {
            return text == "Show Less";
        }
        return false;
    }
}