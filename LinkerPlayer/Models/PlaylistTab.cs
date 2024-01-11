using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace LinkerPlayer.Models;

public partial class PlaylistTab : ObservableObject
{
    public ObservableCollection<MediaFile> Tracks { get; set; } = new();
    [ObservableProperty] private string? _header = "New Playlist";
    [ObservableProperty] private MediaFile? _lastSelectedMediaFile;
    [ObservableProperty] private int? _lastSelectedIndex;
}
