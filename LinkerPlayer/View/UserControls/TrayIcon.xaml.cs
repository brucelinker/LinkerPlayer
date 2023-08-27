using System;
using System.Windows;

namespace LinkerPlayer.View.UserControls;

public partial class TrayIcon
{
    public TrayIcon()
    {
        InitializeComponent();
    }

    private void TaskbarIcon_Loaded(object sender, RoutedEventArgs e)
    {
        Window? win = Window.GetWindow(this);
        if (win != null)
            win.IsVisibleChanged += (_, _) =>
            {
                TaskbarIconOpenButton.Content =
                    win.Visibility != Visibility.Hidden ? "Minimize to Tray" : "Open LinkerPlayer";
            };
    }

    private void TaskbarIcon_Click(object sender, RoutedEventArgs e)
    {
        Window? win = Window.GetWindow(this);

        if (win is { Visibility: Visibility.Hidden })
        {
            win.Visibility = Visibility.Visible;
            win.Activate();
        }
        else
        {
            if (win is { WindowState: WindowState.Minimized })
            {
                win.WindowState = WindowState.Normal;
                win.Activate();
            }
            else
            {
                win?.Hide();
            }
        }
    }

    private void TaskbarIconCloseButton_Click(object sender, RoutedEventArgs e)
    {
        TaskbarIcon.Dispose();

        if (Window.GetWindow(this) is Windows.MainWindow win)
        {
            win.Window_Closed(null, null); // saves settings
            win.Close();
        }

        Environment.Exit(0);
    }
}