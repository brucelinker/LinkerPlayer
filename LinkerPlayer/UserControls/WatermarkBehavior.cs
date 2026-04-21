using Microsoft.Xaml.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace LinkerPlayer.UserControls;

public class WatermarkBehavior : Behavior<ComboBox>
{
    private WaterMarkAdorner _adorner;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register("Text", typeof(string), typeof(WatermarkBehavior), new PropertyMetadata("Select an item..."));

    protected override void OnAttached()
    {
        base.OnAttached();

        _adorner = new WaterMarkAdorner(AssociatedObject, Text);

        AssociatedObject.Loaded += OnLoaded;
        AssociatedObject.GotFocus += OnGotFocus;
        AssociatedObject.LostFocus += OnLostFocus;
        AssociatedObject.SelectionChanged += OnSelectionChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateAdorner();
    }

    private void OnGotFocus(object sender, RoutedEventArgs e) => RemoveAdorner();
    private void OnLostFocus(object sender, RoutedEventArgs e) => UpdateAdorner();
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAdorner();

    private void UpdateAdorner()
    {
        if (AssociatedObject == null)
            return;

        AdornerLayer layer = AdornerLayer.GetAdornerLayer(AssociatedObject);
        if (layer == null)
            return;

        if (AssociatedObject.SelectedItem == null && !AssociatedObject.IsFocused)
        {
            if (_adorner != null)
            {
                Adorner[]? adorners = layer.GetAdorners(AssociatedObject);
                bool alreadyAdded = adorners != null && Array.IndexOf(adorners, _adorner) >= 0;
                if (!alreadyAdded)
                {
                    layer.Add(_adorner);
                }
            }
        }
        else
        {
            layer.Remove(_adorner);
        }
    }

    private void RemoveAdorner()
    {
        AdornerLayer layer = AdornerLayer.GetAdornerLayer(AssociatedObject);
        if (layer != null && _adorner != null)
        {
            Adorner[]? adorners = layer.GetAdorners(AssociatedObject);
            bool present = adorners != null && Array.IndexOf(adorners, _adorner) >= 0;
            if (present)
            {
                layer.Remove(_adorner);
            }
        }
    }

    protected override void OnDetaching()
    {
        // Cleanup events
        if (AssociatedObject != null)
        {
            AssociatedObject.Loaded -= OnLoaded;
            AssociatedObject.GotFocus -= OnGotFocus;
            AssociatedObject.LostFocus -= OnLostFocus;
            AssociatedObject.SelectionChanged -= OnSelectionChanged;
        }

        RemoveAdorner();

        base.OnDetaching();
    }
}

public class TextBoxWatermarkBehavior : Behavior<TextBox>
{
    private WaterMarkAdorner _adorner;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register("Text", typeof(string), typeof(TextBoxWatermarkBehavior),
            new PropertyMetadata("Keywords...", OnTextChanged));

    // Foreground dependency property added so XAML can set the brush on the behavior.
    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(TextBoxWatermarkBehavior),
            new PropertyMetadata(Brushes.Gray, OnForegroundChanged));

    private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBoxWatermarkBehavior behavior)
        {
            if (behavior._adorner != null && e.NewValue is Brush brush)
            {
                behavior._adorner.Foreground = brush;
                behavior.UpdateAdorner();
            }
        }
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBoxWatermarkBehavior behavior && behavior.AssociatedObject != null)
        {
            behavior.UpdateAdorner();
        }
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        _adorner = new WaterMarkAdorner(AssociatedObject, Text);
        // apply any Foreground set via XAML to the underlying adorner
        _adorner.Foreground = Foreground;

        AssociatedObject.Loaded += AssociatedObject_Loaded;
        AssociatedObject.TextChanged += AssociatedObject_TextChanged;
        AssociatedObject.GotFocus += AssociatedObject_GotFocus;
        AssociatedObject.LostFocus += AssociatedObject_LostFocus;
    }

    private void AssociatedObject_Loaded(object sender, RoutedEventArgs e) => UpdateAdorner();
    private void AssociatedObject_TextChanged(object sender, TextChangedEventArgs e) => UpdateAdorner();
    private void AssociatedObject_GotFocus(object sender, RoutedEventArgs e) => RemoveAdorner();
    private void AssociatedObject_LostFocus(object sender, RoutedEventArgs e) => UpdateAdorner();

    private void UpdateAdorner()
    {
        if (AssociatedObject == null)
            return;

        if (string.IsNullOrEmpty(AssociatedObject.Text))
        {
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(AssociatedObject);
            if (layer != null && _adorner != null)
            {
                Adorner[]? adorners = layer.GetAdorners(AssociatedObject);
                bool alreadyAdded = adorners != null && Array.IndexOf(adorners, _adorner) >= 0;
                if (!alreadyAdded)
                {
                    layer.Add(_adorner);
                }
            }
        }
        else
        {
            RemoveAdorner();
        }
    }

    private void RemoveAdorner()
    {
        AdornerLayer layer = AdornerLayer.GetAdornerLayer(AssociatedObject);
        if (layer != null && _adorner != null)
        {
            Adorner[]? adorners = layer.GetAdorners(AssociatedObject);
            bool present = adorners != null && Array.IndexOf(adorners, _adorner) >= 0;
            if (present)
            {
                layer.Remove(_adorner);
            }
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.Loaded -= AssociatedObject_Loaded;
            AssociatedObject.TextChanged -= AssociatedObject_TextChanged;
            AssociatedObject.GotFocus -= AssociatedObject_GotFocus;
            AssociatedObject.LostFocus -= AssociatedObject_LostFocus;
        }

        RemoveAdorner();

        base.OnDetaching();
    }
}

public class WaterMarkAdorner : Adorner
{
    private readonly string _text;
    public Brush Foreground { get; set; } = Brushes.Gray;   // default

    public WaterMarkAdorner(UIElement element, string text) : base(element)
    {
        IsHitTestVisible = false;
        _text = text;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        FormattedText formattedText = new FormattedText(
            _text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,                    // you can also make this a property if needed
            Foreground,            // ← now uses the dynamic brush
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        drawingContext.DrawText(formattedText, new Point(5, 3)); // tweak offset if needed
    }
}
