using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Models;

namespace LinkerPlayer.ViewModels;

public partial class BaseViewModel : ObservableRecipient
{
    [ObservableProperty]
    private static int _selectedPlaylistIndex;
    [ObservableProperty]
    private static int _selectedTrackIndex;
    [ObservableProperty]
    private static MediaFile? _selectedTrack;
    [ObservableProperty]
    private static Playlist? _activePlaylist;
    [ObservableProperty]
    private static int? _activePlaylistIndex;
    [ObservableProperty]
    private static int? _activeTrackIndex;
    [ObservableProperty]
    private static MediaFile? _activeTrack;
}