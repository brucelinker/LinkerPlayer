using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LinkerPlayer.UserControls;

[ObservableObject]
public partial class PlaylistTabs
{
    public readonly PlaylistTabsViewModel playlistTabsViewModel;
    public readonly PlayerControlsViewModel playerControlsViewModel;
    private EditableTabHeaderControl? _selectedEditableTabHeaderControl;

    public PlaylistTabs()
    {
        playlistTabsViewModel = PlaylistTabsViewModel.Instance;
        playerControlsViewModel = PlayerControlsViewModel.Instance;

        DataContext = playlistTabsViewModel;

        InitializeComponent();

        playlistTabsViewModel.LoadPlaylistTabs();
    }

    private void DataGrid_Loaded(object sender, RoutedEventArgs e)
    {
        playlistTabsViewModel.OnDataGridLoaded(sender, e);
    }

    private void TracksTable_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        playlistTabsViewModel.OnTrackSelectionChanged(sender, e);
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        playlistTabsViewModel.OnTabSelectionChanged(sender, e);
    }

    private void PlaylistRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        playlistTabsViewModel.OnDoubleClickDataGrid();

        WeakReferenceMessenger.Default.Send(new DataGridPlayMessage(PlayerState.Playing));
    }

    private void MenuItem_NewPlaylist(object sender, RoutedEventArgs e)
    {
        playlistTabsViewModel.NewPlaylist();
    }

    private void MenuItem_LoadPlaylist(object sender, RoutedEventArgs e)
    {
        playlistTabsViewModel.LoadPlaylist();
    }

    private void MenuItem_RenamePlaylist(object sender, RoutedEventArgs e)
    {
        _selectedEditableTabHeaderControl?.SetEditMode(true);
    }

    private void MenuItem_RemovePlaylist(object sender, RoutedEventArgs e)
    {
        playlistTabsViewModel.RemovePlaylist(sender);
    }

    private void MenuItem_AddFolder(object sender, RoutedEventArgs e)
    {
        playlistTabsViewModel.AddFolder();
    }

    private void MenuItem_AddFiles(object sender, RoutedEventArgs e)
    {
        playlistTabsViewModel.AddFiles();
    }

    private void MenuItem_PlayTrack(object sender, RoutedEventArgs e)
    {
        playerControlsViewModel.PlayTrack();
    }
    
    private void MenuItem_RemoveTrack(object sender, RoutedEventArgs e)
    {
        playlistTabsViewModel.RemoveTrack();

    }

    private void MenuItem_NewPlaylistFromFolder(object sender, RoutedEventArgs e)
    {
        playlistTabsViewModel.NewPlaylistFromFolder();
    }

    private void TabHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _selectedEditableTabHeaderControl = (EditableTabHeaderControl)sender;
    }

    private void TabHeader_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _selectedEditableTabHeaderControl = (EditableTabHeaderControl)sender;

        playlistTabsViewModel.RightMouseDown_TabSelect((string)_selectedEditableTabHeaderControl.Content);
    }

    private void TracksTable_OnSorting(object sender, DataGridSortingEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            playlistTabsViewModel.OnDataGridSorted(sender);
        }, null);
    }
}
