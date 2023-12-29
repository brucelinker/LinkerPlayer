using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace LinkerPlayer.Models;

public partial class Playlist : ObservableObject
{
    [ObservableProperty] private string? _name = "New Playlist";
    [ObservableProperty] private string? _selectedSong;
    public ObservableCollection<string>? SongIds { get; set; }
}