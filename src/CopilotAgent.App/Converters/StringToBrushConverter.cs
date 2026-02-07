using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CopilotAgent.App.Converters;

/// <summary>
/// Converts a hex color string (e.g., "#4CAF50") to a <see cref="SolidColorBrush"/>.
/// Returns <see cref="Brushes.Gray"/> if conversion fails.
/// </summary>
public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch
            {
                // Fall through to default
            }
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a hex color string to a <see cref="Color"/> struct.
/// Returns <see cref="Colors.Gray"/> if conversion fails.
/// </summary>
public class StringToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                // Fall through to default
            }
        }

        return Colors.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}