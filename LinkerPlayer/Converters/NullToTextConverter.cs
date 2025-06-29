using System;
using System.Globalization;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

public class NullToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? parameter?.ToString() ?? "" : "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}