using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

/// <summary>
/// Returns Visible for the "Rating" metadata row; Collapsed for all other rows.
/// Used in PropertiesWindow to show the StarRatingControl only on the Rating row.
/// </summary>
public class InverseRatingRowVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string name && name == "Rating")
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
