using System;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

public class DurationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        TimeSpan ts = (TimeSpan)value;

        string output = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";

        return output;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}