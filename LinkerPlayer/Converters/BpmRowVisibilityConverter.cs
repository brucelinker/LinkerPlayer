using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

/// <summary>
/// Shows UI only for "Beats Per Minute" row (returns Collapsed for BPM row, Visible for others)
/// </summary>
public class BpmRowVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string name && name == "Beats Per Minute")
        {
            return Visibility.Collapsed; // Hide normal text for BPM row
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
