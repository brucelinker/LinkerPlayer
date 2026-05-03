using LinkerPlayer.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace LinkerPlayer.UserControls;

/// <summary>
/// A single-line search box that syntax-highlights query tokens as the user types.
///
/// Token colours (defined in Light.xaml / Dark.xaml):
///   QueryFieldBrush    — recognised field names  (title, bitrate, year …)
///   QueryOperatorBrush — operators               (=, &gt;, contains …)
///   QueryValueBrush    — value part of a clause  (jazz, 320 …)
///   QueryOrBrush       — OR / || / |  separators
///   QueryErrorBrush    — unknown / unrecognised word in query context
///
/// When the input contains no structured clauses the text is rendered in the
/// normal foreground colour (plain-text search mode).
/// </summary>
public partial class SyntaxSearchBox : UserControl
{
    // -------------------------------------------------------------------------
    // Dependency properties
    // -------------------------------------------------------------------------

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(SyntaxSearchBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextPropertyChanged));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(SyntaxSearchBox),
            new PropertyMetadata("Search keywords or query (? for help)...", OnPlaceholderChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    // Re-entrancy guard — set BEFORE any document mutation, cleared only after
    // the dispatcher has drained all queued TextChanged events.
    private bool _isUpdating;

    // Adorner that shows the watermark placeholder text
    private PlaceholderAdorner? _placeholderAdorner;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    public SyntaxSearchBox()
    {
        InitializeComponent();
        RichBox.TextChanged += RichBox_TextChanged;
        RichBox.GotFocus    += (_, _) => UpdatePlaceholderVisibility();
        RichBox.LostFocus   += (_, _) => UpdatePlaceholderVisibility();
        Loaded += SyntaxSearchBox_Loaded;
    }

    private void SyntaxSearchBox_Loaded(object sender, RoutedEventArgs e)
    {
        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(RichBox);
        if (layer != null)
        {
            _placeholderAdorner = new PlaceholderAdorner(RichBox, Placeholder);
            layer.Add(_placeholderAdorner);
        }

        UpdatePlaceholderVisibility();
    }

    // -------------------------------------------------------------------------
    // DP callbacks
    // -------------------------------------------------------------------------

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SyntaxSearchBox)d).ApplyTextFromProperty((string)e.NewValue);
    }

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        SyntaxSearchBox box = (SyntaxSearchBox)d;
        if (box._placeholderAdorner != null)
        {
            box._placeholderAdorner.PlaceholderText = (string)e.NewValue;
        }
    }

    // -------------------------------------------------------------------------
    // RichTextBox events
    // -------------------------------------------------------------------------

    private void RichBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdating)
        {
            return;
        }

        // Snapshot the plain text before rebuilding — rebuilding changes
        // the document which would re-fire TextChanged if not guarded.
        string plain = GetPlainText();

        RebuildDocument(plain);

        // Update the VM binding after the document is stable.
        // Use BeginInvoke so that any further deferred TextChanged events the
        // RichTextBox may queue during the rebuild are already guarded by
        // _isUpdating before they fire.
        Dispatcher.BeginInvoke(() =>
        {
            _isUpdating = false;
            Text = plain;
            UpdatePlaceholderVisibility();
        }, DispatcherPriority.DataBind);
    }

    // -------------------------------------------------------------------------
    // Highlighting
    // -------------------------------------------------------------------------

    private void ApplyTextFromProperty(string? incoming)
    {
        if (_isUpdating)
        {
            return;
        }

        if (GetPlainText() == incoming)
        {
            return; // already in sync — no rebuild needed
        }

        RebuildDocument(incoming ?? string.Empty);

        Dispatcher.BeginInvoke(() =>
        {
            _isUpdating = false;
            UpdatePlaceholderVisibility();
        }, DispatcherPriority.DataBind);
    }

    /// <summary>
    /// Rebuilds the RichTextBox document with syntax-coloured Runs.
    /// Sets <c>_isUpdating = true</c> before touching the document and
    /// relies on the caller's <c>BeginInvoke</c> to reset it afterward.
    /// </summary>
    private void RebuildDocument(string input)
    {
        // Measure caret position in plain-text characters BEFORE the rebuild
        int caretOffset = GetCaretCharOffset();

        _isUpdating = true;

        Paragraph para = new() { Margin = new Thickness(0) };

        if (!string.IsNullOrEmpty(input))
        {
            foreach (QueryToken token in QueryParser.Tokenize(input))
            {
                para.Inlines.Add(new Run(token.Slice(input))
                {
                    Foreground = BrushForKind(token.Kind)
                });
            }
        }

        RichBox.Document.Blocks.Clear();
        RichBox.Document.Blocks.Add(para);

        // Restore caret at the same character offset
        SetCaretCharOffset(caretOffset);
    }

    // -------------------------------------------------------------------------
    // Brush resolution
    // -------------------------------------------------------------------------

    private Brush BrushForKind(QueryTokenKind kind)
    {
        string key = kind switch
        {
            QueryTokenKind.Field        => "QueryFieldBrush",
            QueryTokenKind.Operator     => "QueryOperatorBrush",
            QueryTokenKind.Value        => "QueryValueBrush",
            QueryTokenKind.OrKeyword    => "QueryOrBrush",
            QueryTokenKind.UnknownField => "QueryErrorBrush",
            _                           => "TrackListItemForegroundBrush"
        };

        return TryFindResource(key) as Brush ?? SystemColors.WindowTextBrush;
    }

    // -------------------------------------------------------------------------
    // Plain-text extraction
    // -------------------------------------------------------------------------

    private string GetPlainText()
    {
        TextRange range = new(RichBox.Document.ContentStart, RichBox.Document.ContentEnd);
        // RichTextBox appends "\r\n" for each paragraph end — strip it
        return range.Text.TrimEnd('\r', '\n');
    }

    // -------------------------------------------------------------------------
    // Caret helpers
    //
    // WPF TextPointer offsets count logical *symbols* (paragraph start/end
    // markers count as positions), which differs from plain character counts.
    // We walk Run inlines directly to stay in character-space.
    // -------------------------------------------------------------------------

    private int GetCaretCharOffset()
    {
        int offset = 0;
        try
        {
            TextPointer caret = RichBox.CaretPosition;
            foreach (Block block in RichBox.Document.Blocks)
            {
                if (block is not Paragraph para)
                {
                    continue;
                }

                foreach (Inline inline in para.Inlines)
                {
                    if (inline is not Run run)
                    {
                        continue;
                    }

                    TextPointer runEnd = run.ContentEnd;
                    if (caret.CompareTo(runEnd) <= 0)
                    {
                        // Caret is inside this Run
                        offset += new TextRange(run.ContentStart, caret).Text.Length;
                        return offset;
                    }

                    offset += run.Text.Length;
                }
            }
        }
        catch { }

        return offset;
    }

    private void SetCaretCharOffset(int targetOffset)
    {
        try
        {
            int remaining = targetOffset;
            foreach (Block block in RichBox.Document.Blocks)
            {
                if (block is not Paragraph para)
                {
                    continue;
                }

                foreach (Inline inline in para.Inlines)
                {
                    if (inline is not Run run)
                    {
                        continue;
                    }

                    if (remaining <= run.Text.Length)
                    {
                        // Target is inside this Run — walk forward from ContentStart
                        TextPointer pos = run.ContentStart;
                        for (int i = 0; i < remaining; i++)
                        {
                            TextPointer? next = pos.GetNextInsertionPosition(LogicalDirection.Forward);
                            if (next == null)
                            {
                                break;
                            }
                            pos = next;
                        }
                        RichBox.CaretPosition = pos;
                        return;
                    }

                    remaining -= run.Text.Length;
                }
            }

            // Fallback: end of document
            RichBox.CaretPosition = RichBox.Document.ContentEnd;
        }
        catch { }
    }

    // -------------------------------------------------------------------------
    // Placeholder visibility
    // -------------------------------------------------------------------------

    private void UpdatePlaceholderVisibility()
    {
        if (_placeholderAdorner == null)
        {
            return;
        }

        bool isEmpty  = string.IsNullOrEmpty(GetPlainText());
        bool hasFocus = RichBox.IsFocused;
        _placeholderAdorner.SetIsVisible(isEmpty && !hasFocus);
    }
}
