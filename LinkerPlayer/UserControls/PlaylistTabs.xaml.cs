using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using System.Windows;
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

    private void DataGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _playlistTabsViewModel.OnDataGridLoaded(sender, e);
    }

    private void TracksTable_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _playlistTabsViewModel.OnTrackSelectionChanged(sender, e);
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _playlistTabsViewModel.OnTabSelectionChanged(sender, e);
    }

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

    private void MenuItem_NewPlaylist(object sender, RoutedEventArgs e)
    {
        _playlistTabsViewModel.NewPlaylist();
    }

    private void MenuItem_LoadPlaylist(object sender, RoutedEventArgs e)
    {
        _playlistTabsViewModel.LoadPlaylist();
    }

    private void MenuItem_RenamePlaylist(object sender, RoutedEventArgs e)
    {
        _playlistTabsViewModel.RenamePlaylist(sender, e);
    }

    private void MenuItem_RemovePlaylist(object sender, RoutedEventArgs e)
    {
        _playlistTabsViewModel.RemovePlaylist(sender, e);
    }

    private void MenuItem_AddFolder(object sender, RoutedEventArgs e)
    {
        _playlistTabsViewModel.AddFolder(sender, e);
    }

    private void MenuItem_AddFiles(object sender, RoutedEventArgs e)
    {
        _playlistTabsViewModel.AddFiles(sender, e);
    }

    private void MenuItem_RemoveTrack(object sender, RoutedEventArgs e)
    {
        _playlistTabsViewModel.RemoveTrack(sender, e);

    }
}