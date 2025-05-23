using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using ManagedBass;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LinkerPlayer.UserControls;

[ObservableObject]
public partial class PlaylistTabs
{
    public readonly PlaylistTabsViewModel PlaylistTabsViewModel;
    public readonly PlayerControlsViewModel PlayerControlsViewModel;
    private EditableTabHeaderControl? _selectedEditableTabHeaderControl;

    public PlaylistTabs()
    {
        PlaylistTabsViewModel = PlaylistTabsViewModel.Instance;
        PlayerControlsViewModel = PlayerControlsViewModel.Instance;

        DataContext = PlaylistTabsViewModel;

        InitializeComponent();

        PlaylistTabsViewModel.LoadPlaylistTabs();
    }

    private void DataGrid_Loaded(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            PlaylistTabsViewModel.OnDataGridLoaded(sender, e);
        }, null);
    }

    private void TracksTable_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            PlaylistTabsViewModel.OnTrackSelectionChanged(sender, e);
        }, null);

    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl && (e.AddedItems.Count > 0 || e.RemovedItems.Count > 0))
        {
            // Verify that the items are TabItems (or your tab content type)
            bool isTabChange = e.AddedItems.OfType<PlaylistTab>().Any() || e.RemovedItems.OfType<PlaylistTab>().Any();

            if (isTabChange)
            {
                this.Dispatcher.BeginInvoke(
                    (Action)delegate { PlaylistTabsViewModel.OnTabSelectionChanged(sender, e); }, null);
                Console.WriteLine("Tab selection changed.");
            }
        }
        else
        {
            // Ignore events from child controls like DataGrid
            Console.WriteLine($"Event from child control {e.OriginalSource}, ignoring.");
        }
    }

    private void PlaylistRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            PlaylistTabsViewModel.OnDoubleClickDataGrid();
        }, null);

        WeakReferenceMessenger.Default.Send(new DataGridPlayMessage(PlaybackState.Playing));
    }

    private void MenuItem_NewPlaylist(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            PlaylistTabsViewModel.NewPlaylist();
        }, null);
    }

    private void MenuItem_LoadPlaylist(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            PlaylistTabsViewModel.LoadPlaylist();
        }, null);
    }

    private void MenuItem_RenamePlaylist(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            _selectedEditableTabHeaderControl?.SetEditMode(true);
        }, null);
    }

    private void MenuItem_RemovePlaylist(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            PlaylistTabsViewModel.RemovePlaylist(sender);
        }, null);
    }

    private void MenuItem_AddFolder(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            PlaylistTabsViewModel.AddFolder();
        }, null);
    }

    private void MenuItem_AddFiles(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            PlaylistTabsViewModel.AddFiles();
        }, null);
    }

    private void MenuItem_PlayTrack(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            PlayerControlsViewModel.PlayTrack();
        }, null);
    }

    private void MenuItem_RemoveTrack(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            PlaylistTabsViewModel.RemoveTrack();
        }, null);
    }

    private void MenuItem_NewPlaylistFromFolder(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            PlaylistTabsViewModel.NewPlaylistFromFolder();
        }, null);
    }

    private void TabHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
        _selectedEditableTabHeaderControl = (EditableTabHeaderControl)sender;
        }, null);
    }

    private void TabHeader_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _selectedEditableTabHeaderControl = (EditableTabHeaderControl)sender;

        PlaylistTabsViewModel.RightMouseDown_TabSelect((string)_selectedEditableTabHeaderControl.Content);
    }

    private void TracksTable_OnSorting(object sender, DataGridSortingEventArgs e)
    {
        // Cast the Column to DataGridColumn to access its properties
        if (e.Column is DataGridColumn column)
        {
            // Get the sort direction
            ListSortDirection direction = (column.SortDirection != ListSortDirection.Ascending)
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;

            // Determine the property name. This assumes your column's Binding Path is the property name.
            string propertyName = (column.SortMemberPath ?? column.Header.ToString()!);

            this.Dispatcher.BeginInvoke((Action)delegate
            {
                PlaylistTabsViewModel.OnDataGridSorted(propertyName, direction);
            }, null);
        }
    }
}
