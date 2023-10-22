using LinkerPlayer.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LinkerPlayer.UserControls;

public partial class TracksDataGrid
{
    private MediaFile? _mediaFile;

    public TracksDataGrid()
    {
        DataContext = this;
        InitializeComponent();
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
}