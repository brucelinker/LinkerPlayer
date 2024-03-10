using System.Collections.ObjectModel;

namespace LinkerPlayer.Models;

public class PlaylistTab
{
    public string? Name { get; set; }
    public ObservableCollection<MediaFile> Tracks { get; set; } = new();
    public MediaFile? SelectedTrack;
    public int? SelectedIndex;
}
