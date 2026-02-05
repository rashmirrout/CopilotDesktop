using System.Globalization;
using System.Windows.Data;

namespace CopilotAgent.App.Converters;

/// <summary>
/// Converts a UTC DateTime to local time for display.
/// Used in chat message timestamps to show times in the user's local timezone.
/// </summary>
public class UtcToLocalTimeConverter : IValueConverter
{
    /// <summary>
    /// Converts a UTC DateTime to local time.
    /// </summary>
    /// <param name="value">The UTC DateTime value to convert.</param>
    /// <param name="targetType">The target type (not used).</param>
    /// <param name="parameter">Optional format string parameter.</param>
    /// <param name="culture">The culture for formatting.</param>
    /// <returns>The local DateTime, or the original value if conversion fails.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime utcDateTime)
        {
            // If the Kind is not specified, assume it's UTC
            if (utcDateTime.Kind == DateTimeKind.Unspecified)
            {
                utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
            }
            
            // Convert to local time
            return utcDateTime.ToLocalTime();
        }
        
        return value;
    }

    /// <summary>
    /// Converts a local DateTime back to UTC (not typically needed for display).
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime localDateTime)
        {
            return localDateTime.ToUniversalTime();
        }
        
        return value;
    }
}