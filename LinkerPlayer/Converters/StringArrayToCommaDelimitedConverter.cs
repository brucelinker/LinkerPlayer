using System;
using System.Globalization;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

public class StringArrayToCommaDelimitedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string[] arr ? string.Join(", ", arr) : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString()?.Split(new[] { ", " }, StringSplitOptions.None) ?? Array.Empty<string>();
    }
}
