using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace LinkerPlayer.UserControls;

/// <summary>
/// A TabControl that caches each tab's realized content and keeps it alive across
/// tab switches. The stock TabControl destroys and re-creates the selected tab's
/// ContentPresenter (and its entire visual tree) on every switch, which forces the
/// DataGrid to rebuild columns, re-sort, and re-generate containers for 50k+ rows.
/// This subclass keeps the content in memory and swaps visibility instead.
/// </summary>
[TemplatePart(Name = SelectedContentHostPartName, Type = typeof(Grid))]
public class CachingTabControl : TabControl
{
    private const string SelectedContentHostPartName = "PART_SelectedContentHost";

    private readonly Dictionary<object, ContentPresenter> _contentCache = new();
    private ContentPresenter? _activeContent;

    static CachingTabControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CachingTabControl),
            new FrameworkPropertyMetadata(typeof(CachingTabControl)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateActiveContent();
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        UpdateActiveContent();
    }

    private void UpdateActiveContent()
    {
        object? selectedItem = SelectedItem;
        if (selectedItem == null)
        {
            ClearActiveContent();
            return;
        }

        // When ItemsSource is bound, WPF wraps each item in a TabItem container.
        // We key the cache by the data item, not the container.
        object cacheKey = selectedItem is TabItem tabItem && tabItem.DataContext != null
            ? tabItem.DataContext
            : selectedItem;

        if (!_contentCache.TryGetValue(cacheKey, out ContentPresenter? presenter))
        {
            presenter = CreateContentForItem(selectedItem);
            _contentCache[cacheKey] = presenter;
        }

        SetActiveContent(presenter);
    }

    private ContentPresenter CreateContentForItem(object item)
    {
        // Resolve the data item for template selection
        object dataItem = item is TabItem tabItem ? tabItem.DataContext : item;

        // Build the content through the template selector so each tab gets its
        // correct DataTemplate (MusicLibraryContentTemplate / PlaylistContentTemplate).
        DataTemplate? template = ContentTemplateSelector?.SelectTemplate(dataItem, this)
            ?? ContentTemplate;

        ContentPresenter presenter = new ContentPresenter
        {
            Content = dataItem,
            ContentTemplate = template,
            // Allow the content to stretch and fill the tab area.
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        return presenter;
    }

    private void SetActiveContent(ContentPresenter presenter)
    {
        if (ReferenceEquals(_activeContent, presenter))
            return;

        // Detach old, attach new. The parent Grid is the stock TabControl's
        // content host (named PART_SelectedContentHost in the template).
        if (GetTemplateChild(SelectedContentHostPartName) is Grid hostGrid)
        {
            hostGrid.Children.Clear();
            hostGrid.Children.Add(presenter);
        }

        _activeContent = presenter;
    }

    private void ClearActiveContent()
    {
        if (GetTemplateChild(SelectedContentHostPartName) is Grid hostGrid)
        {
            hostGrid.Children.Clear();
        }
        _activeContent = null;
    }

    /// <summary>
    /// Removes cached content for a specific tab item (e.g., when a tab is closed).
    /// The next time that tab is selected, its content will be rebuilt.
    /// </summary>
    public void InvalidateContent(object item)
    {
        if (_contentCache.Remove(item, out ContentPresenter? presenter))
        {
            if (ReferenceEquals(_activeContent, presenter))
            {
                _activeContent = null;
                UpdateActiveContent();
            }
        }
    }

    /// <summary>
    /// Clears all cached content. Use sparingly (e.g., when the tab list is reset).
    /// </summary>
    public void InvalidateAllContent()
    {
        _contentCache.Clear();
        _activeContent = null;
        UpdateActiveContent();
    }
}
