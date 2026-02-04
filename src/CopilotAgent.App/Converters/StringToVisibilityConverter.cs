using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CopilotAgent.App.Converters;

/// <summary>
/// Converts a string value to Visibility.
/// Returns Visible if the string is not null or empty, otherwise Collapsed.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var str = value as string;
        return string.IsNullOrEmpty(str) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}