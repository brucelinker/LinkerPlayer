using System.Windows.Data;

namespace LinkerPlayer.Converters;

public class UintToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value != null && value is uint uintValue && uintValue > 0)
        {
            return uintValue.ToString();
        }

        return "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
