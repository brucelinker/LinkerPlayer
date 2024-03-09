using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;

namespace LinkerPlayer.Windows;

public partial class MainWindow
{
    public static MainWindow? Instance;
    private static readonly ThemeManager ThemeManager = new();

    public MediaFile? SelectedTrack;
    public ThemeColors SelectedTheme;

    public BandsSettings SelectedEqualizerProfile = null!;
    private readonly PlayerControlsViewModel _playerControlsViewModel;
    private readonly PlaylistTabsViewModel _playlistTabsViewModel;
    private static int _count;

    public MainWindow(PlayerControlsViewModel playerControlsViewModel, PlaylistTabsViewModel playlistTabsViewModel)
    {
        Log.Information($"MAINWINDOW - {++_count}");

        _playerControlsViewModel = playerControlsViewModel;
        _playlistTabsViewModel = playlistTabsViewModel;

        InitializeComponent();

        Instance = this;
        DataContext = this;

        // Remember window placement
        ((App)Application.Current).WindowPlace.Register(this);
        WinMax.DoSourceInitialized(this);

        OutputDevice.InitializeOutputDevice();
        _playlistTabsViewModel.LoadPlaylistTabs();
        
        if (!string.IsNullOrEmpty(Properties.Settings.Default.SelectedTheme))
        {
            SelectedTheme = ThemeManager.StringToThemeColor(Properties.Settings.Default.SelectedTheme);
        }
        else
        {
            SelectedTheme = ThemeColors.Dark;
        }

        // Sets the theme
        SelectedTheme = ThemeManager.ModifyTheme(SelectedTheme);
        
        Properties.Settings.Default.Save();

        WeakReferenceMessenger.Default.Register<SelectedTrackChangedMessage>(this, (_, m) =>
        {
            OnSelectedTrackChanged(m.Value);
        });
    }

    static MainWindow()
    {
        _count = 0;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _playlistTabsViewModel.SetupSelectedPlaylist();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }
    
    private void OnSelectedTrackChanged(MediaFile? selectedTrack)
    {
        SelectedTrack = selectedTrack;
    }
    
    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Helper.FindVisualChildren<Grid>(this).FirstOrDefault()!.Focus();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            Uri uri = new("/Images/restore.png", UriKind.Relative);
            ImageSource imgSource = new BitmapImage(uri);
            TitlebarButtons.MaximizeButtonImage.Source = imgSource;
        }
        else if (WindowState == WindowState.Normal)
        {
            Uri uri = new("/Images/maximize.png", UriKind.Relative);
            ImageSource imgSource = new BitmapImage(uri);
            TitlebarButtons.MaximizeButtonImage.Source = imgSource;
        }
    }

    public void Window_Closed(object sender, EventArgs e)
    {
        MusicLibrary.ClearPlayState();
        MusicLibrary.SaveToJson();

        Properties.Settings.Default.VolumeSliderValue = PlayerControls.VolumeSlider.Value;

        Properties.Settings.Default.LastSelectedPlaylistName = _playlistTabsViewModel.SelectedPlaylist!.Name;
        Properties.Settings.Default.LastSelectedSongId = SelectedTrack != null ? SelectedTrack.Id : "";
        //Properties.Settings.Default.ShuffleMode = _playerControlsViewModel.ShuffleMode;
        Properties.Settings.Default.LastSeekBarValue = PlayerControls.SeekBar.Value;

        if (Properties.Settings.Default.EqualizerOnStartEnabled)
        {
            Properties.Settings.Default.EqualizerProfileName =
                SelectedEqualizerProfile != null! ? SelectedEqualizerProfile.Name : null;
        }

        Properties.Settings.Default.MainOutputDevice =
            OutputDevice.GetOutputDeviceNameById(AudioEngine.GetOutputDeviceId());

        Properties.Settings.Default.Save();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (Properties.Settings.Default.MinimizeToTrayEnabled)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        bool isVisible = (bool)e.NewValue;
        UpdateChildWindowVisibility(this, isVisible);
    }

    private void UpdateChildWindowVisibility(Window parentWindow, bool isVisible)
    {
        List<Window> childWindows = new();

        foreach (Window window in Application.Current.Windows)
        {
            if (window != parentWindow && window.Owner == parentWindow)
            {
                childWindows.Add(window);
            }
        }

        foreach (Window childWindow in childWindows)
        {
            childWindow.Visibility = isVisible ? Visibility.Visible : Visibility.Hidden;
            UpdateChildWindowVisibility(childWindow, isVisible);
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!(Keyboard.FocusedElement is TextBox))
        {
            string enteredHotkey;

            if (e.KeyboardDevice.Modifiers != ModifierKeys.None)
            {
                enteredHotkey = e.KeyboardDevice.Modifiers + " + " + e.Key;
            }
            else
            {
                enteredHotkey = e.Key.ToString();
            }

            if (enteredHotkey == Properties.Settings.Default["PlayPauseHotkey"].ToString())
            {
                //PlayButton_Click(null!, null!);
                e.Handled = true;
            }
            else if (enteredHotkey == Properties.Settings.Default["NextSongHotkey"].ToString())
            {
                _playerControlsViewModel.NextTrack();
                e.Handled = true;
            }
            else if (enteredHotkey == Properties.Settings.Default["PreviousSongHotkey"].ToString())
            {
                _playerControlsViewModel.PreviousTrack();
                e.Handled = true;
            }
            else if (enteredHotkey == Properties.Settings.Default["IncreaseMainVolumeHotkey"].ToString())
            {
                double val = PlayerControls.VolumeSlider.Value;
                PlayerControls.VolumeSlider.Value = val + 5 > 100 ? 100 : val + 5;
                e.Handled = true;
            }
            else if (enteredHotkey == Properties.Settings.Default["DecreaseMainVolumeHotkey"].ToString())
            {
                double val = PlayerControls.VolumeSlider.Value;
                PlayerControls.VolumeSlider.Value = val - 5 < 0 ? 0 : val - 5;
                e.Handled = true;
            }
        }
    }
}