using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace LinkerPlayer.Models;

public partial class Playlist : ObservableObject
{
    [ObservableProperty] private string? _name;
    [ObservableProperty] private List<string>? _songIds;
}