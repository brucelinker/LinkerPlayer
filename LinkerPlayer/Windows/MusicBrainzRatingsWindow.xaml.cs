using System.ComponentModel;
using System.Windows;
using LinkerPlayer.ViewModels;

namespace LinkerPlayer.Windows;

public partial class MusicBrainzRatingsWindow : Window
{
    public MusicBrainzRatingsWindow()
    {
        InitializeComponent();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MusicBrainzRatingsWindowViewModel progress && progress.IsRunning)
        {
            e.Cancel = true;
            progress.RequestCancel();
        }
    }
}
