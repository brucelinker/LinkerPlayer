using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

/// <summary>
/// Converts two string values to Visibility.
/// If both strings are equal (case-sensitive), returns Visible; otherwise Hidden.
/// </summary>
public class StringEqualityToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return Visibility.Hidden;

        string? value1 = values[0] as string;
        string? value2 = values[1] as string;

        if (string.IsNullOrEmpty(value1) || string.IsNullOrEmpty(value2))
            return Visibility.Hidden;

        return string.Equals(value1, value2, StringComparison.Ordinal) ? Visibility.Visible : Visibility.Hidden;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
