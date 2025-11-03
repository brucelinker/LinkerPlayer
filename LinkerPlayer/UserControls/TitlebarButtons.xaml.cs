using LinkerPlayer.Audio;
using LinkerPlayer.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;

namespace LinkerPlayer.UserControls;

public partial class TitlebarButtons
{
    private readonly SettingsWindow _settingsWindow;
    private readonly AudioEngine _audioEngine;

    public TitlebarButtons()
    {
        _settingsWindow = App.AppHost.Services.GetRequiredService<SettingsWindow>();
        _audioEngine = App.AppHost.Services.GetRequiredService<AudioEngine>();

        InitializeComponent();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Window window = Window.GetWindow(this)!;

        window.DragMove();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Hide();
        }
        else
        {
            _settingsWindow.Show();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        Window? win = Window.GetWindow(this);
        if (win != null)
            win.WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        Window? win = Window.GetWindow(this);
        if (win != null)
            win.WindowState = win.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _audioEngine.Stop();
        _audioEngine.Dispose();
        Window? win = Window.GetWindow(this);
        win?.Close(); // Triggers the normal window closing event
    }

    private void TitlebarButtons_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Window? win = Window.GetWindow(this);
        if (win != null)
            win.WindowState = win.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}
