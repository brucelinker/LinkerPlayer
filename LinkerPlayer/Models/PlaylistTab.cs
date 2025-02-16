using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace LinkerPlayer.Models;

public partial class PlaylistTab : ObservableObject
{
    public string? Name { get; set; }

    [ObservableProperty]
    private ObservableCollection<MediaFile> _tracks = new();
    public MediaFile? SelectedTrack;
    public int? SelectedIndex;
}
