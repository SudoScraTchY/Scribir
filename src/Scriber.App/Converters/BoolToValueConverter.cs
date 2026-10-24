using System.Globalization;

namespace Scriber.App.Converters;

/// <summary>
/// Converts a boolean to arbitrary values (Colors, Brushes, Strings, Visibility, etc.)
/// Prevents WinUI/MAUI InvalidCastException by safely typing and coercing values.
/// </summary>
public sealed class BoolToValueConverter : IValueConverter
{
    public object? TrueValue { get; set; }
    public object? FalseValue { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is bool flag && flag;
        object? result = b ? TrueValue : FalseValue;

        if (parameter is string paramStr && !string.IsNullOrWhiteSpace(paramStr))
        {
            // Optional "TrueVal:FalseVal" format
            var parts = paramStr.Split(':');
            if (parts.Length == 2)
            {
                result = b ? parts[0] : parts[1];
            }
            else if (parts.Length == 1)
            {
                result = b ? parts[0] : FalseValue;
            }
        }

        if (result == null) return null;

        // Auto-convert string hex color to Color if target expects Color
        if (targetType == typeof(Color) && result is string hex)
        {
            return Color.FromArgb(hex);
        }

        return result;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
