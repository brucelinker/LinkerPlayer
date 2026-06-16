using System.Globalization;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

/// <summary>
/// Converts a rating string like "3.5" to a double for binding to StarRatingControl.Rating.
/// Returns 0.0 if the string is empty or cannot be parsed.
/// </summary>
public class RatingStringToDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s &&
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
            return result;
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d && d > 0.0)
            return d.ToString("0.0", CultureInfo.InvariantCulture);
        return string.Empty;
    }
}
