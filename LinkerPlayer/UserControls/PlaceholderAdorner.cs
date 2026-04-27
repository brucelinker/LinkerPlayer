using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace LinkerPlayer.UserControls;

/// <summary>
/// Adorner that renders placeholder / watermark text over a RichTextBox
/// when it is empty and unfocused.
/// </summary>
internal sealed class PlaceholderAdorner : Adorner
{
    private string _placeholderText;
    private bool   _isVisible;

    public string PlaceholderText
    {
        get => _placeholderText;
        set { _placeholderText = value; InvalidateVisual(); }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set { _isVisible = value; InvalidateVisual(); }
    }

    public PlaceholderAdorner(UIElement adornedElement, string placeholderText)
        : base(adornedElement)
    {
        _placeholderText = placeholderText;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (!_isVisible || string.IsNullOrEmpty(_placeholderText))
        {
            return;
        }

        // Resolve foreground from the adorned element's resources
        Brush fg = (AdornedElement as FrameworkElement)?.TryFindResource("TrackListItemForegroundBrush") as Brush
                   ?? Brushes.Gray;

        // Make it dimmer than normal text
        fg = new SolidColorBrush(
            Color.FromArgb(
                120,
                ((SolidColorBrush)fg).Color.R,
                ((SolidColorBrush)fg).Color.G,
                ((SolidColorBrush)fg).Color.B));

        double fontSize = (AdornedElement as FrameworkElement)
            ?.TryFindResource("FontSizeNormal") as double? ?? 13d;

        FormattedText formatted = new(
            _placeholderText,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            fg,
            VisualTreeHelper.GetDpi(AdornedElement).PixelsPerDip);

        // Indent to match RichTextBox content padding (6px left + 1px border)
        drawingContext.DrawText(formatted, new Point(8, 4));
    }
}
