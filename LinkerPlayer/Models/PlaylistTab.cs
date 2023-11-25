using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace LinkerPlayer.Models;

public partial class PlaylistTab : ObservableObject
{
    [ObservableProperty] private string? _header = "New Playlist";
    [ObservableProperty] private ObservableCollection<MediaFile>? _tracks;
}
