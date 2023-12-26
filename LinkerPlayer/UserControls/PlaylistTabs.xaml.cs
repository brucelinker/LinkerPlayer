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
    private readonly PlayListsViewModel _playListsViewModel = new();

    public PlaylistTabs()
    {
        DataContext = _playListsViewModel;

        InitializeComponent();
    }

    private void TracksTable_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _playListsViewModel.OnSelectionChanged(sender, e);
    }


    //public static void UpdatePlayerState(PlayerState state)
    //{
    //    if (SelectedTrack != null) SelectedTrack.State = state;
    //}

    //public RoutedEventHandler? ClickRowElement;

    //private void TrackRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    //{
    //    if (SelectedTrack is { State: PlayerState.Playing })
    //    {
    //        SelectedTrack.State = PlayerState.Stopped;
    //    }

    //    Windows.MainWindow mainWindow = (Windows.MainWindow)Window.GetWindow(this)!;

    //    if (sender is DataGrid { SelectedItem: not null } grid)
    //    {
    //        SelectedTrack = ((grid.SelectedItem as MediaFile)!);
    //        SelectedTrack.State = PlayerState.Playing;
    //    }

    //    mainWindow.Song_Click(sender, e);
    //}

    //private void TrackRow_SelectionChanged(object sender, SelectionChangedEventArgs e)
    //{
    //    if (sender is DataGrid { SelectedItem: not null } grid)
    //    {
    //        SelectedTrack = ((grid.SelectedItem as MediaFile)!);
    //    }
    //}
}