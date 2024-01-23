using LinkerPlayer.Windows;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LinkerPlayer.UserControls;

public partial class TitlebarButtons
{
    private bool _isSettingsWindowOpen = false;
    private SettingsWindow _settingsWin;

    public TitlebarButtons()
    {
        InitializeComponent();
    }

    private void ButtonMouseEnter(object sender, MouseEventArgs e)
    {
        ((sender as Button)?.Content as Image)!.Opacity = 1;
    }

    private void ButtonMouseLeave(object sender, MouseEventArgs e)
    {
        (((sender as Button)?.Content as Image)!).Opacity = 0.6;
    }
    
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSettingsWindowOpen)
        {
            if (_settingsWin.WindowState == WindowState.Minimized)
            {
                _settingsWin.WindowState = WindowState.Normal;
            }
            return;
        }

        _settingsWin = new SettingsWindow
        {
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        _settingsWin.Closed += (_, _) => { _isSettingsWindowOpen = false; };
        _settingsWin.Closing += (_, _) => { _settingsWin.Owner = null; };
        _isSettingsWindowOpen = true;

        _settingsWin.Show();
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
}