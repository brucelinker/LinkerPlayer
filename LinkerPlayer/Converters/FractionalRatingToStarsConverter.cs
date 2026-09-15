using System.Globalization;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

/// <summary>
/// Renders a 0–5 <see cref="double"/> rating as per-star glyphs with fractional support
/// (e.g. 3.5 → "★★★⯨☆"). Cheap and allocation-free for the grid's read-only Rating column.
/// Pass ConverterParameter="Unrated" to get the "all five stars shown full but disabled"
/// placeholder used for tracks that have no rating yet.
/// </summary>
public class FractionalRatingToStarsConverter : IValueConverter
{
    private const int MaxStars = 5;
    private const char Full = '★';
    private const char Half = '⯨';
    private const char Empty = '☆';

    // Unrated placeholder: five full stars, intended to be dimmed via the "disabled" look.
    private const string UnratedPlaceholder = "★★★★★";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double rating || rating <= 0)
            return UnratedPlaceholder;

        rating = Math.Clamp(rating, 0, MaxStars);
        int fullStars = (int)Math.Floor(rating);
        double frac = rating - fullStars;

        char[] chars = new char[MaxStars];
        for (int i = 0; i < MaxStars; i++)
        {
            if (i < fullStars)
                chars[i] = Full;
            else if (i == fullStars && frac >= 0.25)
                chars[i] = frac >= 0.75 ? Full : Half;
            else
                chars[i] = Empty;
        }
        return new string(chars);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
