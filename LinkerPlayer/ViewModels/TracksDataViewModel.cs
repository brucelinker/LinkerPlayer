using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Models;
using System.Collections.ObjectModel;

namespace LinkerPlayer.ViewModels;

public partial class TracksDataViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<MediaFile> _tracks;
}

