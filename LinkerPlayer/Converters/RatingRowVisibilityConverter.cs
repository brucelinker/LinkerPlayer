using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

/// <summary>
/// Returns Collapsed for the "Rating" metadata row; Visible for all other rows.
/// Used in PropertiesWindow to hide the plain TextBlock when showing the star control.
/// </summary>
public class RatingRowVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string name && name == "Rating")
            return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
