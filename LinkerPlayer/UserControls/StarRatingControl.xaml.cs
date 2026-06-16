using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LinkerPlayer.UserControls;

public partial class StarRatingControl : UserControl
{
    private const double StarSlotWidth = 15.0;
    private const double TotalWidth = StarSlotWidth * 5;
    private const double Step = 0.1;

    private double _ratingBeforeEdit;
    private bool _closingPopup;
    private bool _isPopupOpening;

    // -----------------------------------------------------------------------
    //  Dependency Properties
    // -----------------------------------------------------------------------

    public static readonly DependencyProperty RatingProperty =
        DependencyProperty.Register(nameof(Rating), typeof(double), typeof(StarRatingControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRatingChanged));

    public double Rating
    {
        get => (double)GetValue(RatingProperty);
        set => SetValue(RatingProperty, value);
    }

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(StarRatingControl),
            new PropertyMetadata(false, OnIsReadOnlyChanged));

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    // -----------------------------------------------------------------------
    //  Constructor
    // -----------------------------------------------------------------------

    ILogger<StarRatingControl>? _logger = App.AppHost?.Services?.GetService<ILogger<StarRatingControl>>();

    public StarRatingControl()
    {
        if (Application.Current != null)
            InitializeComponent();

        AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(RootGrid_MouseLeftButtonDown), handledEventsToo: true);

        Loaded += (_, _) =>
        {
            UpdateClip(Rating);
            UpdateTooltip(Rating);
        };
    }

    // -----------------------------------------------------------------------
    //  Property callbacks
    // -----------------------------------------------------------------------

    private static void OnRatingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StarRatingControl ctrl)
        {
            ctrl.UpdateClip((double)e.NewValue);
            ctrl.UpdateTooltip((double)e.NewValue);
        }
    }

    private static void OnIsReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StarRatingControl ctrl)
            ctrl.RootGrid.Cursor = (bool)e.NewValue ? Cursors.Arrow : Cursors.Hand;
    }

    // -----------------------------------------------------------------------
    //  Mouse handling
    // -----------------------------------------------------------------------

    private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsReadOnly)
            return;

        if (EditorPopup.IsOpen && EditorPopup.Child is UIElement popupChild && popupChild.IsMouseOver)
            return;

        e.Handled = true;

        if (!EditorPopup.IsOpen)
            OpenEditor();
    }

    // -----------------------------------------------------------------------
    //  Popup
    // -----------------------------------------------------------------------

    private void OpenEditor()
    {
        if (_isPopupOpening || EditorPopup.IsOpen)
            return;

        _isPopupOpening = true;
        _ratingBeforeEdit = Rating;
        UpdateClip(Rating);

        EditorTextBox.Text = Rating > 0
            ? Rating.ToString("0.0", CultureInfo.InvariantCulture)
            : string.Empty;

        EditorPopup.IsOpen = true;

        EditorPopup.Dispatcher.BeginInvoke(() =>
        {
            EditorTextBox.Focus();
            EditorTextBox.SelectAll();

            // Ensure handlers
            EditorTextBox.KeyDown -= EditorTextBox_KeyDown;
            EditorTextBox.KeyDown += EditorTextBox_KeyDown;

            _isPopupOpening = false;
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void EditorPopup_Opened(object sender, EventArgs e)
    {
        if (Application.Current?.MainWindow is Window w)
        {
            w.PreviewMouseDown += OnMainWindowPreviewMouseDown;
            w.PreviewKeyDown += OnMainWindowPreviewKeyDown;
        }
    }

    private void OnMainWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!EditorPopup.IsOpen)
            return;
        if (IsClickInsidePopup(e.OriginalSource))
            return;

        CommitEditor();
    }

    private void OnMainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!EditorPopup.IsOpen)
            return;

        if (e.Key == Key.Escape)
        {
            CancelEditor();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CommitEditor();
            e.Handled = true;
        }
    }

    private bool IsClickInsidePopup(object originalSource)
    {
        if (EditorPopup.Child is not UIElement popupChild)
            return false;

        if (popupChild.IsMouseOver)
            return true;

        return originalSource is Visual visual && visual.IsDescendantOf(popupChild);
    }

    private void EditorPopup_Closed(object sender, EventArgs e)
    {
        if (Application.Current?.MainWindow is Window w)
        {
            w.PreviewMouseDown -= OnMainWindowPreviewMouseDown;
            w.PreviewKeyDown -= OnMainWindowPreviewKeyDown;
        }
    }

    // -----------------------------------------------------------------------
    //  Keyboard & Buttons
    // -----------------------------------------------------------------------

    private void EditorTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitEditor();
                e.Handled = true;
                break;
            case Key.Escape:
                CancelEditor();
                e.Handled = true;
                break;
            case Key.Up:
                ApplyStep(+Step);
                e.Handled = true;
                break;
            case Key.Down:
                ApplyStep(-Step);
                e.Handled = true;
                break;
        }
    }

    private void Border_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitEditor();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelEditor();
            e.Handled = true;
        }
    }

    private void CommitEditor()
    {
        _logger?.LogInformation("CommitEditor called - New rating: {Rating:0.0}", ParseEditorText());

        if (_closingPopup)
            return;
        _closingPopup = true;
        try
        {
            double newRating = ParseEditorText();
            ClosePopup();
            Rating = newRating;
            PersistRating(newRating);
        }
        finally
        {
            _closingPopup = false;
        }
    }

    private void CancelEditor()
    {
        if (_closingPopup)
            return;
        _closingPopup = true;
        try
        {
            ClosePopup();
            Rating = _ratingBeforeEdit;
            UpdateClip(Rating);
        }
        finally
        {
            _closingPopup = false;
        }
    }

    private void ClosePopup()
    {
        EditorPopup.IsOpen = false;
    }

    private double ParseEditorText()
    {
        string text = EditorTextBox.Text.Trim();
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
            return Math.Clamp(Math.Round(v, 1), 0.0, 5.0);
        return _ratingBeforeEdit;
    }

    private void EditorTextBox_GotFocus(object sender, RoutedEventArgs e)
        => EditorTextBox.SelectAll();

    private void EditorTextBox_LostFocus(object sender, RoutedEventArgs e) { }

    private void DecrementButton_Click(object sender, RoutedEventArgs e) => ApplyStep(-Step);
    private void IncrementButton_Click(object sender, RoutedEventArgs e) => ApplyStep(+Step);

    private void ApplyStep(double delta)
    {
        double current = ParseEditorText();
        double next = Math.Clamp(Math.Round(current + delta, 1), 0.0, 5.0);
        EditorTextBox.Text = next.ToString("0.0", CultureInfo.InvariantCulture);
        UpdateClip(next);
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    private void UpdateClip(double rating)
    {
        if (FilledClip == null)
            return;
        double w = Math.Max(0, Math.Min(TotalWidth, rating * StarSlotWidth));
        double h = ActualHeight > 0 ? ActualHeight : 20;
        FilledClip.Rect = new Rect(0, 0, w, h);
    }

    private void UpdateTooltip(double rating)
    {
        if (RootGrid == null)
            return;
        RootGrid.ToolTip = rating.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private void PersistRating(double rating)
    {
        if (DataContext is not MediaFile mediaFile)
            return;

        string trackId = mediaFile.Id;
        string path = mediaFile.Path ?? "unknown";

        _logger?.LogInformation("PersistRating triggered for {Path} → {Rating:0.0}", path, rating);

        IMusicLibrary? library = App.AppHost?.Services?.GetService<IMusicLibrary>();
        if (library != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    mediaFile.MarkPropertyDirty(nameof(MediaFile.Rating));

                    await library.UpdateRatingAsync(trackId, rating).ConfigureAwait(false);
                    await library.SaveToDatabaseAsync().ConfigureAwait(false);

                    mediaFile.ClearDirty();   // Prevent close prompt

                    ILogger<StarRatingControl>? logger = App.AppHost?.Services?.GetService<ILogger<StarRatingControl>>();
                    logger?.LogInformation("Manual rating save successful for {Path}", path);
                }
                catch (Exception ex)
                {
                    ILogger<StarRatingControl>? logger = App.AppHost?.Services?.GetService<ILogger<StarRatingControl>>();
                    logger?.LogError(ex, "Failed to save manual rating for {Path}", path);
                }
            });
        }

        // MusicBrainz submission
        IMusicBrainzRatingService? mbService = App.AppHost?.Services?.GetService<IMusicBrainzRatingService>();
        string? mbid = mediaFile.GetTag("MUSICBRAINZ_TRACKID") ?? mediaFile.GetTag("MUSICBRAINZ_TRACK_ID");

        if (mbService != null && !string.IsNullOrWhiteSpace(mbid))
        {
            Task.Run(async () =>
            {
                try
                {
                    await mbService.SubmitRatingAsync(mbid, rating);
                }
                catch (Exception ex)
                {
                    ILogger<StarRatingControl>? logger = App.AppHost?.Services?.GetService<ILogger<StarRatingControl>>();
                    logger?.LogError(ex, "MusicBrainz submission failed");
                }
            });
        }
    }
}
