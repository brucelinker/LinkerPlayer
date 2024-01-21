using System.Collections.ObjectModel;

namespace LinkerPlayer.Models;

public partial class PlaylistTab
{
    public string? Name { get; set; }
    public ObservableCollection<MediaFile> Tracks { get; set; } = new();
    public MediaFile? LastSelectedMediaFile;
    public int? LastSelectedIndex;

}
