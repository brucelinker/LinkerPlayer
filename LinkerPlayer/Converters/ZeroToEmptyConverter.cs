using System.Globalization;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

/// <summary>
/// Converts an int/uint value of 0 to an empty string so DataGrid cells stay blank
/// instead of showing "0". Non-zero values are formatted as strings normally.
/// ConvertBack parses the string back to int/uint so inline editing still works.
/// </summary>
public class ZeroToEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            int i when i == 0 => string.Empty,
            uint u when u == 0 => string.Empty,
            double d when d == 0 => string.Empty,
            null => string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string s = (value as string ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(s))
        {
            if (targetType == typeof(uint))
                return (uint)0;
            if (targetType == typeof(double))
                return (double)0;
            return 0;
        }

        if (targetType == typeof(uint) && uint.TryParse(s, out uint u))
            return u;
        if (targetType == typeof(double) && double.TryParse(s, NumberStyles.Any, culture, out double d))
            return d;
        if (int.TryParse(s, out int i))
            return i;

        return Binding.DoNothing;
    }
}
