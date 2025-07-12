﻿using LinkerPlayer.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;

namespace LinkerPlayer.UserControls;

public partial class TitlebarButtons
{
    private readonly SettingsWindow _settingsWindow;

    public TitlebarButtons()
    {
        _settingsWindow = App.AppHost.Services.GetRequiredService<SettingsWindow>();

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
        Application.Current.Shutdown();
    }

    private void TitlebarButtons_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Window? win = Window.GetWindow(this);
        if (win != null)
            win.WindowState = win.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}