using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
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
    private readonly PlayerControlsViewModel _playerControlsViewModel = new();
    private EditableTabHeaderControl? _selectedEditableTabHeaderControl;

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

    private void PlaylistRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _playlistTabsViewModel.OnDoubleClickDataGrid();

        WeakReferenceMessenger.Default.Send(new DataGridPlayMessage(PlayerState.Playing));
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
        _selectedEditableTabHeaderControl?.SetEditMode(true);
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

    private void MenuItem_PlayTrack(object sender, RoutedEventArgs e)
    {
        _playerControlsViewModel.PlayTrack();
    }
    
    private void MenuItem_RemoveTrack(object sender, RoutedEventArgs e)
    {
        _playlistTabsViewModel.RemoveTrack(sender, e);

    }

    private void MenuItem_NewPlaylistFromFolder(object sender, RoutedEventArgs e)
    {
        _playlistTabsViewModel.NewPlaylistFromFolder(sender, e);
    }

    private void TabHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _selectedEditableTabHeaderControl = (EditableTabHeaderControl)sender;
    }

    private void TabHeader_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _selectedEditableTabHeaderControl = (EditableTabHeaderControl)sender;

        _playlistTabsViewModel.RightMouseDown_TabSelect((string)_selectedEditableTabHeaderControl.Content);
    }
}