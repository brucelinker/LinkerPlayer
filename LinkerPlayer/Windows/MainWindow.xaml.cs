using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LinkerPlayer.Windows;

public partial class MainWindow
{
    public static MainWindow? Instance;
    public AudioStreamControl AudioStreamControl;
    public readonly DispatcherTimer SeekBarTimer = new();
    private static readonly ThemeManager ThemeManager = new();

    public Playlist? SelectedPlaylist;
    public bool PlaylistLoaded = false;
    public MediaFile? SelectedTrack;
    public string? BackgroundPlaylistName;
    public ThemeColors SelectedTheme;

    public bool VisualizationEnabled = Properties.Settings.Default.VisualizationEnabled;
    private string? _currentlyVisualizedPath;

    public BandsSettings SelectedBandsSettings = null!;
    private readonly PlayerControlsViewModel _playerControlsViewModel;
    private readonly PlaylistTabsViewModel _playlistTabsViewModel;

    public MainWindow(PlayerControlsViewModel playerControlsViewModel, PlaylistTabsViewModel playlistTabsViewModel)
    {
        _playerControlsViewModel = playerControlsViewModel;
        _playlistTabsViewModel = playlistTabsViewModel;

        InitializeComponent();

        Instance = this;
        DataContext = this;

        ((App)Application.Current).WindowPlace.Register(this);
        WinMax.DoSourceInitialized(this);

        WeakReferenceMessenger.Default.Register<PlayerStateMessage>(this, (r, m) =>
        {
            OnPlayerStateChanged(m.Value);
        });

        WeakReferenceMessenger.Default.Register<SelectedTrackChangedMessage>(this, (r, m) =>
        {
            OnSelectedTrackChanged(m.Value);
        });

        if (string.IsNullOrEmpty(Properties.Settings.Default.MainOutputDevice))
        {
            Properties.Settings.Default.MainOutputDevice = OutputDevice.GetOutputDeviceNameById(0);
        }
        else if (!OutputDevice.GetOutputDevicesList().Contains(Properties.Settings.Default.MainOutputDevice))
        {
            Properties.Settings.Default.MainOutputDevice = OutputDevice.GetOutputDeviceNameById(0);
        }

        if (string.IsNullOrEmpty(Properties.Settings.Default.AdditionalOutputDevice))
        {
            foreach (string outputDevice in OutputDevice.GetOutputDevicesList())
            {
                if (outputDevice.Contains("virtual", StringComparison.OrdinalIgnoreCase))
                {
                    Properties.Settings.Default.AdditionalOutputDevice = outputDevice;
                }
            }
        }
        else if (!OutputDevice.GetOutputDevicesList().Contains(Properties.Settings.Default.AdditionalOutputDevice))
        {
            Properties.Settings.Default.AdditionalOutputDevice = "";

            foreach (string outputDevice in OutputDevice.GetOutputDevicesList())
            {
                if (outputDevice.Contains("virtual", StringComparison.OrdinalIgnoreCase))
                {
                    Properties.Settings.Default.AdditionalOutputDevice = outputDevice;
                }
            }
        }

        AudioStreamControl = new AudioStreamControl(Properties.Settings.Default.MainOutputDevice);

        AudioStreamControl.MainMusic!.MusicVolume = (float)Properties.Settings.Default.VolumeSliderValue / 100;

        if (Properties.Settings.Default.AdditionalOutputEnabled &&
            !string.IsNullOrEmpty(Properties.Settings.Default.AdditionalOutputDevice))
        {
            //AudioStreamControl.ActivateAdditionalMusic(Properties.Settings.Default.AdditionalOutputDevice);
            AudioStreamControl.AdditionalMusic!.MusicVolume =
                (float)Properties.Settings.Default.AdditionalVolumeSliderValue / 100;
        }

        AudioStreamControl.MainMusic.StoppedEvent += Music_StoppedEvent!;

        //if (AudioStreamControl.AdditionalMusic != null)
        //{
        //    AudioStreamControl.AdditionalMusic.StoppedEvent += Music_StoppedEvent!;
        //}

        PlaylistTabsViewModel.LoadPlaylistTabs();

        //PlayerControls.PlayButton.Click += PlayButton_Click;
        //PlayerControls.PrevButton.Click += PrevButton_Click;
        //PlayerControls.NextButton.Click += NextButton_Click;
        PlayerControls.SeekBar.PreviewMouseLeftButtonUp += SeekBar_PreviewMouseLeftButtonUp;
        PlayerControls.SeekBar.ValueChanged += SeekBar_ValueChanged;

        //PlayerControls.AdditionalVolumeSlider.IsEnabled = Properties.Settings.Default.AdditionalOutputEnabled;
        //PlayerControls.AdditionalVolumeButton.IsEnabled = Properties.Settings.Default.AdditionalOutputEnabled;

        PlayerControls.VolumeSlider.Value = Properties.Settings.Default.VolumeSliderValue;
        //PlayerControls.AdditionalVolumeSlider.Value = Properties.Settings.Default.AdditionalVolumeSliderValue;

        if (Properties.Settings.Default.EqualizerOnStartEnabled)
        {
            if (!String.IsNullOrEmpty(Properties.Settings.Default.EqualizerProfileName))
            {
                SelectedBandsSettings = new BandsSettings() { Name = Properties.Settings.Default.EqualizerProfileName ?? "Flat" };

                AudioStreamControl.InitializeEqualizer(Properties.Settings.Default.EqualizerProfileName);
            }
        }

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

        PlayerControls.VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
        //PlayerControls.AdditionalVolumeSlider.ValueChanged += AdditionalVolumeSlider_ValueChanged;

        //PlaylistTabs.ClickRowElement += Song_Click;


        //PlaylistTabs.ClickRowElement += (s, o) =>
        //{
        //    SelectPlaylistByName(
        //        (((s as Button)!.Content as ContentPresenter)!.Content as Playlist)!.Name!.ToString());
        //};

        SeekBarTimer.Interval = TimeSpan.FromMilliseconds(50);
        SeekBarTimer.Tick += timer_Tick!;

        Properties.Settings.Default.Save();

        //Log.Information("MainWindow - Constructor");
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _playlistTabsViewModel.InitializePlaylists();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void VolumeSlider_ValueChanged(object sender, EventArgs e)
    {
        AudioStreamControl.MainMusic!.MusicVolume = (float)PlayerControls.VolumeSlider.Value / 100;
    }

    public void Music_StoppedEvent(object? sender, EventArgs e)
    {
        if ((AudioStreamControl.CurrentTrackPosition + 0.3) >= AudioStreamControl.CurrentTrackLength)
        {
            SeekBarTimer.Stop();
            _playerControlsViewModel.NextTrack();
        }
        else if (sender == null)
        {
            AudioStreamControl.Pause();
            SeekBarTimer.Stop();
        }
    }

    public bool PlayTrack(MediaFile mediaFile)
    {
        //Log.Information("MainWindow - PlayTrack");

        if (!File.Exists(mediaFile.Path))
        {
            InfoSnackbar.MessageQueue?.Clear();
            InfoSnackbar.MessageQueue?.Enqueue($"Song \"{mediaFile.Title}\" could not be found", null, null, null,
                false, true, TimeSpan.FromSeconds(2));

            return false;
        }

        SelectedTrack = mediaFile;

        AudioStreamControl.PathToMusic = SelectedTrack.Path;

        AudioStreamControl.StopAndPlayFromPosition(0);
        SeekBarTimer.Start();

        PlayerControls.CurrentSongName.Text = SelectedTrack.Title;
        TimeSpan ts = SelectedTrack.Duration;
        PlayerControls.TotalTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        PlayerControls.CurrentTime.Text = "0:00";

        TrackInfo.SetSelectedMediaFile(SelectedTrack);

        StartVisualization();

        return true;
    }

    public bool ResumeTrack(MediaFile mediaFile)
    {
        AudioStreamControl.Play();

        return true;
    }

    private void SeekBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        double posInSeekBar = (PlayerControls.SeekBar.Value * AudioStreamControl.CurrentTrackLength) / 100;

        if (AudioStreamControl.PathToMusic != null &&
            Math.Abs(AudioStreamControl.CurrentTrackPosition - posInSeekBar) > 0 &&
            !AudioStreamControl.MainMusic!.IsPaused)
        {
            AudioStreamControl.StopAndPlayFromPosition(posInSeekBar);

            _playerControlsViewModel.State = PlayerState.Playing;
            SeekBarTimer.Start();
        }
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SelectedTrack != null)
        {
            double posInSeekBar = (PlayerControls.SeekBar.Value * AudioStreamControl.CurrentTrackLength) / 100;
            TimeSpan ts = TimeSpan.FromSeconds(posInSeekBar);
            PlayerControls.CurrentTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }
    }

    private void timer_Tick(object sender, EventArgs e)
    {
        if (!(PlayerControls.SeekBar.IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed))
        {
            PlayerControls.SeekBar.Value =
                (AudioStreamControl.CurrentTrackPosition * 100) / AudioStreamControl.CurrentTrackLength;
        }
    }

    private void OnSelectedTrackChanged(MediaFile? selectedTrack)
    {
        SelectedTrack = selectedTrack;
    }

    private void OnPlayerStateChanged(PlayerState state)
    {
        //Log.Information("MainWindow - OnPlayerStateChanged");

        if (SelectedTrack != null)
        {
            switch (state)
            {
                case PlayerState.Playing:
                    AudioStreamControl.StopAndPlayFromPosition(
                        (PlayerControls.SeekBar.Value * AudioStreamControl.CurrentTrackLength) / 100);
                    SeekBarTimer.Start();
                    break;

                case PlayerState.Paused:
                    AudioStreamControl.Pause();
                    SeekBarTimer.Stop();
                    break;

                default:
                    AudioStreamControl.Stop();
                    AudioStreamControl.CurrentTrackPosition = 0;
                    SeekBarTimer.Stop();
                    PlayerControls.CurrentTime.Text = "0:00";
                    PlayerControls.SeekBar.Value = 0;
                    break;
            }
        }
    }

    //public void SelectPlaylistByName(string name)
    //{
    //    //Log.Information("MainWindow - SelectPlaylistByName");

    //    if (SelectedPlaylist != null && string.Equals(SelectedPlaylist.Name, name) && PlaylistLoaded)
    //    {
    //        return;
    //    }

    //    foreach (Playlist playlist in MusicLibrary.GetPlaylists())
    //    {
    //        if (playlist.Name == name)
    //        {
    //            SelectedPlaylist = playlist;
    //            //PlaylistLoaded = LoadSelectedPlaylist();

    //            break;
    //        }
    //    }
    //}

    //private bool LoadSelectedPlaylist()
    //{
    //    //Log.Information("MainWindow - LoadSelectedPlaylist");

    //    if (SelectedPlaylist != null)
    //    {
    //        List<MediaFile> songs = MusicLibrary.GetSongsFromPlaylist(SelectedPlaylist.Name);

    //        PlaylistLoaded = true;
    //    }

    //    return PlaylistLoaded;
    //}

    public void SelectedSongRemoved()
    {
        //Log.Information("MainWindow - SelectedSongRemoved");

        if (SelectedTrack != null)
        {
            AudioStreamControl.Stop();
            SelectedTrack = null;
            _playerControlsViewModel.State = PlayerState.Paused;
            SeekBarTimer.Stop();
            PlayerControls.CurrentSongName.Text = "Song not selected";
            PlayerControls.TotalTime.Text = "0:00";
            PlayerControls.CurrentTime.Text = "0:00";
            PlayerControls.SeekBar.Value = 0;

            // TODO 
            //foreach (Button button in Helper.FindVisualChildren<Button>(PlaylistList.List))
            //{ // remove outlining from playlist
            //    if (((button.Content as ContentPresenter)!.Content as Playlist)!.Name == BackgroundPlaylistName)
            //    {
            //        button.FontWeight = FontWeights.Normal;
            //        break;
            //    }
            //}

            BackgroundPlaylistName = null;

            SelectedTrack = null;

            StopVisualization();
        }
    }

    public void RenameSelectedPlaylist(string newName)
    {
        //Log.Information("MainWindow - RenameSelectedPlaylist");

        if (SelectedPlaylist != null)
        {
            SelectedPlaylist.Name = newName;
            //PlaylistText.CurrentPlaylistName.Text = newName;
        }
    }

    public void RenameSelectedSong(string newName)
    {
        //Log.Information("MainWindow - RenameSelectedSong");

        if (SelectedTrack != null)
        {
            SelectedTrack.Title = newName;
            PlayerControls.CurrentSongName.Text = newName;
        }
    }

    public void StartVisualization()
    {
        //Log.Information("MainWindow - StartVisualization");

        if (VisualizationEnabled && SelectedTrack != null)
        {
            if (_currentlyVisualizedPath != SelectedTrack.Path)
            {
                _currentlyVisualizedPath = SelectedTrack.Path;

                PlayerControls.VisualizeAudio(SelectedTrack.Path);
            }
        }
    }

    public void StopVisualization()
    {
        //Log.Information("MainWindow - StopVisualization");
        PlayerControls.Rendering = false;
        PlayerControls.ShowSeekBarHideBorders();
        PlayerControls.UniGrid.Children.Clear();

        _currentlyVisualizedPath = null;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Helper.FindVisualChildren<Grid>(this).FirstOrDefault()!.Focus();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            Uri uri = new Uri("/Images/restore.png", UriKind.Relative);
            ImageSource imgSource = new BitmapImage(uri);
            TitlebarButtons.MaximizeButtonImage.Source = imgSource;
        }
        else if (WindowState == WindowState.Normal)
        {
            Uri uri = new Uri("/Images/maximize.png", UriKind.Relative);
            ImageSource imgSource = new BitmapImage(uri);
            TitlebarButtons.MaximizeButtonImage.Source = imgSource;
        }
    }

    public void Window_Closed(object sender, EventArgs e)
    {
        MusicLibrary.ClearPlayState();
        MusicLibrary.SaveToJson();

        Properties.Settings.Default.VolumeSliderValue = PlayerControls.VolumeSlider.Value;
        //Properties.Settings.Default.AdditionalVolumeSliderValue = PlayerControls.AdditionalVolumeSlider.Value;

        Properties.Settings.Default.LastSelectedPlaylistName = _playlistTabsViewModel.SelectedPlaylist!.Name;
        Properties.Settings.Default.LastSelectedSongId = SelectedTrack != null ? SelectedTrack.Id : "";
        Properties.Settings.Default.ShuffleMode = _playerControlsViewModel.ShuffleMode;


        Properties.Settings.Default.LastSeekBarValue = PlayerControls.SeekBar.Value;

        //Properties.Settings.Default.LastPlaybackMode = (int)PlayerControls.Mode;

        if (Properties.Settings.Default.EqualizerOnStartEnabled)
        {
            Properties.Settings.Default.EqualizerProfileName =
                SelectedBandsSettings != null! ? SelectedBandsSettings.Name : null;
        }

        Properties.Settings.Default.MainOutputDevice =
            OutputDevice.GetOutputDeviceNameById(AudioStreamControl.MainMusic!.GetOutputDeviceId());

        if (AudioStreamControl.AdditionalMusic != null)
        {
            Properties.Settings.Default.AdditionalOutputDevice =
                OutputDevice.GetOutputDeviceNameById(AudioStreamControl.AdditionalMusic.GetOutputDeviceId());
        }

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
        List<Window> childWindows = new List<Window>();

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