using System.Globalization;
using System.Windows.Data;

namespace LinkerPlayer.Converters;

/// <summary>
/// Lightweight read-only rating display converter for DataGrid cells. Converts a 0–5
/// <see cref="double"/> rating to a star string (e.g. "★★★☆☆"). Far cheaper to render than
/// a full StarRatingControl UserControl per visible cell on large libraries.
/// </summary>
public class RatingToStarsConverter : IValueConverter
{
    private const int MaxStars = 5;

    // Precomputed star strings keyed by rounded star count. Returning cached instances avoids
    // allocating two strings per visible cell on every row recycle during scroll. Unrated (0)
    // shows five empty stars rather than blank, matching other music managers.
    private static readonly string[] StarStrings =
    [
        "☆☆☆☆☆",   // 0 → unrated
        "★☆☆☆☆",
        "★★☆☆☆",
        "★★★☆☆",
        "★★★★☆",
        "★★★★★"
    ];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double rating || rating <= 0)
            return StarStrings[0];

        int fullStars = (int)Math.Round(Math.Clamp(rating, 0, MaxStars), MidpointRounding.AwayFromZero);
        return StarStrings[fullStars];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
