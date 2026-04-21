using System.Windows.Data;

namespace LinkerPlayer.Converters;

public class DurationConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is int seconds && seconds > 0)
        {
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            string output = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            return output;
        }

        return "0:00";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
