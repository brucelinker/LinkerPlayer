using System;
using System.Globalization;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

public class EqFloatToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return "";

        float val = (float)value;

        string output = val > 0 ? val.ToString("+0.0") : val.ToString("0.0");

        return output;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}