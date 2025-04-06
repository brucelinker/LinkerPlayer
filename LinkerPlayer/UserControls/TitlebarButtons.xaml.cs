using LinkerPlayer.Windows;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LinkerPlayer.UserControls;

public partial class TitlebarButtons
{
    public TitlebarButtons()
    {
        InitializeComponent();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Window window = Window.GetWindow(this)!;

        window.DragMove();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsWindow settingsWindow = new()
        {
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        settingsWindow.Show();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        Window? win = Window.GetWindow(this);
        if (win != null) win.WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        Window? win = Window.GetWindow(this);
        if (win != null)
            win.WindowState = win.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Window? win = Window.GetWindow(this);
        if (win != null) win.Close();
    }

    private void TitlebarButtons_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Window? win = Window.GetWindow(this);
        if (win != null)
            win.WindowState = win.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}