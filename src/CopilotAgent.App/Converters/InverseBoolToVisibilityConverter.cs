using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CopilotAgent.App.Converters;

/// <summary>
/// Converts boolean to visibility (inverted: true = Collapsed, false = Visible)
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        // Non-bool path: used for object bindings (e.g., SelectedAgent).
        // null → Visible (show placeholder), non-null → Collapsed (hide placeholder).
        return value is not null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        return false;
    }
}