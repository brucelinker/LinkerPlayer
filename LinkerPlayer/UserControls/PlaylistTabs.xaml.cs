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
    private static MediaFile? _mediaFile;

    public PlaylistTabs()
    {
        DataContext = new PlayListsViewModel();

        InitializeComponent();
    }

    public static void UpdatePlayerState(PlayerState state)
    {
        if (_mediaFile != null) _mediaFile.State = state;
    }

    public RoutedEventHandler? ClickRowElement;

    private void TrackRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_mediaFile is { State: PlayerState.Playing })
        {
            _mediaFile.State = PlayerState.Stopped;
        }

        Windows.MainWindow mainWindow = (Windows.MainWindow)Window.GetWindow(this)!;

        if (sender is DataGrid { SelectedItem: not null } grid)
        {
            _mediaFile = ((grid.SelectedItem as MediaFile)!);
            _mediaFile.State = PlayerState.Playing;
        }

        mainWindow.Song_Click(sender, e);
    }

    private void TrackRow_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }
}