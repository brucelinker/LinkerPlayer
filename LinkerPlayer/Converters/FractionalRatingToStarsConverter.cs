using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LinkerPlayer.Converters;

/// <summary>
/// Draws only the stars a rating has earned. A fractional star is a full star
/// clipped to its earned width, so the unearned half is empty rather than a
/// boxed glyph. No rating draws five dimmed stars.
/// </summary>
public class FractionalRatingStars : FrameworkElement
{
    private const int MaxStars = 5;
    private const double StarSize = 16;
    private static readonly Geometry StarGeometry = Geometry.Parse(
        "M16,0 L20.9,10.5 L32,11.8 L23.6,19.6 L25.8,30.6 L16,25.2 L6.2,30.6 L8.4,19.6 L0,11.8 L11.1,10.5 Z");

    public static readonly DependencyProperty RatingProperty =
        DependencyProperty.Register(
            nameof(Rating),
            typeof(double),
            typeof(FractionalRatingStars),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Rating
    {
        get => (double)GetValue(RatingProperty);
        set => SetValue(RatingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
        => new Size(MaxStars * StarSize, StarSize);

    protected override void OnRender(DrawingContext drawingContext)
    {
        double rating = Math.Clamp(Rating, 0, MaxStars);
        bool unrated = rating <= 0;
        double earned = unrated ? MaxStars : rating;
        Brush fill = unrated
            ? new SolidColorBrush(Color.FromArgb(0x4C, 0xDA, 0xA5, 0x20))
            : Brushes.Goldenrod;
        if (fill.CanFreeze)
            fill.Freeze();

        Rect bounds = StarGeometry.Bounds;
        double scale = StarSize / Math.Max(bounds.Width, bounds.Height);
        double offsetY = Math.Max(0, (RenderSize.Height - StarSize) / 2);

        for (int i = 0; i < MaxStars && i < earned; i++)
        {
            double visible = Math.Min(1, earned - i) * StarSize;
            drawingContext.PushClip(new RectangleGeometry(
                new Rect(i * StarSize, offsetY, visible, StarSize)));
            drawingContext.PushTransform(new TranslateTransform(i * StarSize, offsetY));
            drawingContext.PushTransform(new ScaleTransform(scale, scale));
            drawingContext.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            drawingContext.DrawGeometry(fill, null, StarGeometry);
            drawingContext.Pop();
            drawingContext.Pop();
            drawingContext.Pop();
            drawingContext.Pop();
        }
    }
}

/// <summary>
/// Renders a 0-5 rating as earned-star glyphs with fractional support
/// (for example 2.3 shows two full stars and a half star). The unearned
/// remainder is left blank so a partial star never sits next to outlined
/// empty stars. Tracks with no rating return five full stars, which the
/// grid dims.
/// </summary>
/// <summary>Formats a rating tooltip as one decimal place, including whole numbers.</summary>
public class RatingTooltipConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double rating ? rating.ToString("0.0", CultureInfo.InvariantCulture) : "0.0";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public class FractionalRatingToStarsConverter : IValueConverter
{
    private const int MaxStars = 5;
    private const char Full = '\u2605';
    private const char Half = '\u2BEA';

    // Unrated placeholder: five full stars, dimmed by the rating cell style.
    private const string UnratedPlaceholder = "\u2605\u2605\u2605\u2605\u2605";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double rating || rating <= 0)
            return UnratedPlaceholder;

        rating = Math.Clamp(rating, 0, MaxStars);
        int fullStars = (int)Math.Floor(rating);
        double fraction = rating - fullStars;

        char trailing = '\0';
        if (fraction >= 0.75)
            fullStars++;
        else if (fraction >= 0.25)
            trailing = Half;

        if (fullStars <= 0 && trailing == '\0')
            return UnratedPlaceholder;

        string stars = new string(Full, fullStars);
        return trailing == '\0' ? stars : stars + trailing;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
