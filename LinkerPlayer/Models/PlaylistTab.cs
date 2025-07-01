using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace LinkerPlayer.Models;

public partial class PlaylistTab : ObservableObject
{
    [ObservableProperty] private string _name = "New Playlist";
    [ObservableProperty] private ObservableCollection<MediaFile> _tracks = new();
    [ObservableProperty] private MediaFile? _selectedTrack;
    [ObservableProperty] private int? _selectedIndex;
}
