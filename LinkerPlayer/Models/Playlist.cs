using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace LinkerPlayer.Models;

public partial class Playlist : ObservableObject
{
    [ObservableProperty] private string? _name = "New Playlist";
    public ObservableCollection<string>? SongIds { get; set; }
}