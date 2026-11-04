using LinkerPlayer.ViewModels;
using System.ComponentModel;
using System.Windows;

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
