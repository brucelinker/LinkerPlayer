using LinkerPlayer.Models;
using System.Windows;
using System.Windows.Controls;

namespace LinkerPlayer.UserControls;

/// <summary>
/// Selects the appropriate tab header template based on the tab type.
/// MusicLibraryTab gets an icon template, PlaylistTab gets the editable text template.
/// </summary>
public class TabHeaderTemplateSelector : DataTemplateSelector
{
    public DataTemplate? MusicLibraryTemplate { get; set; }
    public DataTemplate? PlaylistTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is MusicLibraryTab)
        {
            return MusicLibraryTemplate;
        }

        if (item is PlaylistTab)
        {
            return PlaylistTemplate;
        }

        return base.SelectTemplate(item, container);
    }
}
