using System.Collections.ObjectModel;

namespace LinkerPlayer.Models;

public partial class PlaylistTab
{
    public string? Name { get; set; }
    public ObservableCollection<MediaFile> Tracks { get; set; } = new();
    public MediaFile? SelectedTrack;
    public int? SelectedIndex;
    public bool HasActiveTrack = false;

}
