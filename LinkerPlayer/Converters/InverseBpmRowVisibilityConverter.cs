using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

/// <summary>
/// Shows UI only for "Beats Per Minute" row (returns Visible for BPM row, Collapsed for others)
/// </summary>
public class InverseBpmRowVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string name && name == "Beats Per Minute")
        {
            return Visibility.Visible; // Show BPM detection UI for BPM row
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
