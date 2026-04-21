using LinkerPlayer.Models;
using System.Windows;
using System.Windows.Controls;

namespace LinkerPlayer.UserControls;

/// <summary>
/// Selects the appropriate tab content template based on the tab type.
/// MusicLibraryTab gets FilterBar + DataGrid, PlaylistTab gets DataGrid only.
/// </summary>
public class TabContentTemplateSelector : DataTemplateSelector
{
    public DataTemplate? MusicLibraryContentTemplate { get; set; }
    public DataTemplate? PlaylistContentTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is MusicLibraryTab)
        {
            return MusicLibraryContentTemplate;
        }

        if (item is PlaylistTab)
        {
            return PlaylistContentTemplate;
        }

        return base.SelectTemplate(item, container);
    }
}
