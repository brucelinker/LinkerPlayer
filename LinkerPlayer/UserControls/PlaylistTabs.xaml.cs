using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace LinkerPlayer.UserControls;

[ObservableObject]
public partial class PlaylistTabs
{
    private readonly PlaylistTabsViewModel _playlistTabsViewModel = new();

    public PlaylistTabs()
    {
        DataContext = _playlistTabsViewModel;

        InitializeComponent();
    }

    private void TracksTable_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _playlistTabsViewModel.OnSelectionChanged(sender, e);
    }

    //public static void UpdatePlayerState(PlayerState state)
    //{
    //    if (SelectedTrack != null) SelectedTrack.State = state;
    //}

    //public RoutedEventHandler? ClickRowElement;

    private void TrackRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_playlistTabsViewModel.SelectedTrack is { State: PlayerState.Playing })
        {
            _playlistTabsViewModel.SelectedTrack.State = PlayerState.Stopped;
        }

        if (sender is DataGrid { SelectedItem: not null } grid)
        {
            _playlistTabsViewModel.SelectedTrack = ((grid.SelectedItem as MediaFile)!);
            _playlistTabsViewModel.SelectedTrack.State = PlayerState.Playing;
        }
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _playlistTabsViewModel.UpdatePlaylistTab(sender, e);
    }
}