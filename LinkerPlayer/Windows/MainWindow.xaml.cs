using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LinkerPlayer.Windows;

public partial class MainWindow
{
    public static MainWindow Instance = null!;
    public AudioStreamControl AudioStreamControl;
    private readonly DispatcherTimer _seekBarTimer = new();
    private static readonly ThemeManager ThemeManager = new();

    public Playlist? SelectedPlaylist;
    public MediaFile? SelectedSong;
    public string? BackgroundPlaylistName;
    public ThemeColors SelectedTheme;

    public bool VisualizationEnabled = Properties.Settings.Default.VisualizationEnabled;
    private string? _currentlyVisualizedPath;

    public BandsSettings SelectedBandsSettings = null!;

    public MainWindow()
    {
        InitializeComponent();

        Instance = this;
        DataContext = this;

        ((App)Application.Current).WindowPlace.Register(this);
        WinMax.DoSourceInitialized(this);

        if (string.IsNullOrEmpty(Properties.Settings.Default.MainOutputDevice))
        {
            Properties.Settings.Default.MainOutputDevice = DeviceControl.GetOutputDeviceNameById(0);
        }
        else if (!DeviceControl.GetOutputDevicesList().Contains(Properties.Settings.Default.MainOutputDevice))
        {
            Properties.Settings.Default.MainOutputDevice = DeviceControl.GetOutputDeviceNameById(0);
        }

        if (string.IsNullOrEmpty(Properties.Settings.Default.AdditionalOutputDevice))
        {
            foreach (string outputDevice in DeviceControl.GetOutputDevicesList())
            {
                if (outputDevice.Contains("virtual", StringComparison.OrdinalIgnoreCase))
                {
                    Properties.Settings.Default.AdditionalOutputDevice = outputDevice;
                }
            }
        }
        else if (!DeviceControl.GetOutputDevicesList().Contains(Properties.Settings.Default.AdditionalOutputDevice))
        {
            Properties.Settings.Default.AdditionalOutputDevice = "";

            foreach (string outputDevice in DeviceControl.GetOutputDevicesList())
            {
                if (outputDevice.Contains("virtual", StringComparison.OrdinalIgnoreCase))
                {
                    Properties.Settings.Default.AdditionalOutputDevice = outputDevice;
                }
            }
        }

        AudioStreamControl = new AudioStreamControl(Properties.Settings.Default.MainOutputDevice, null!);

        AudioStreamControl.MainMusic!.MusicVolume = (float)Properties.Settings.Default.MainVolumeSliderValue / 100;

        if (Properties.Settings.Default.AdditionalOutputEnabled && !string.IsNullOrEmpty(Properties.Settings.Default.AdditionalOutputDevice))
        {
            AudioStreamControl.ActivateAdditionalMusic(Properties.Settings.Default.AdditionalOutputDevice);
            AudioStreamControl.AdditionalMusic!.MusicVolume = (float)Properties.Settings.Default.AdditionalVolumeSliderValue / 100;
        }

        AudioStreamControl.MainMusic.StoppedEvent += Music_StoppedEvent!;

        if (AudioStreamControl.AdditionalMusic != null)
        {
            AudioStreamControl.AdditionalMusic.StoppedEvent += Music_StoppedEvent!;
        }

        DisplayPlaylists();

        PlayerControls.PlayButton.Click += PlayButton_Click;
        PlayerControls.PrevButton.Click += PrevButton_Click;
        PlayerControls.NextButton.Click += NextButton_Click;
        PlayerControls.SeekBar.PreviewMouseLeftButtonUp += SeekBar_PreviewMouseLeftButtonUp;
        PlayerControls.SeekBar.ValueChanged += SeekBar_ValueChanged;

        //PlayerControls.AdditionalVolumeSlider.IsEnabled = Properties.Settings.Default.AdditionalOutputEnabled;
        //PlayerControls.AdditionalVolumeButton.IsEnabled = Properties.Settings.Default.AdditionalOutputEnabled;

        PlayerControls.MainVolumeSlider.Value = Properties.Settings.Default.MainVolumeSliderValue;
        //PlayerControls.AdditionalVolumeSlider.Value = Properties.Settings.Default.AdditionalVolumeSliderValue;

        if (Properties.Settings.Default.EqualizerOnStartEnabled)
        {
            if (!String.IsNullOrEmpty(Properties.Settings.Default.EqualizerBandName))
            {
                SelectedBandsSettings = new BandsSettings() { Name = Properties.Settings.Default.EqualizerBandName };

                AudioStreamControl.InitializeEqualizer(Properties.Settings.Default.EqualizerBandName);
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

        SelectedTheme = ThemeManager.ModifyTheme(SelectedTheme);

        PlayerControls.MainVolumeSlider.ValueChanged += MainVolumeSlider_ValueChanged;
        //PlayerControls.AdditionalVolumeSlider.ValueChanged += AdditionalVolumeSlider_ValueChanged;

        TracksTable.ClickRowElement += Song_Click;

        PlaylistList.ClickRowElement += (s, _) =>
        {
            SelectPlaylistByName((((s as Button)!.Content as ContentPresenter)!.Content as Playlist)!.Name!.ToString());
        };

        _seekBarTimer.Interval = TimeSpan.FromMilliseconds(50);
        _seekBarTimer.Tick += timer_Tick!;

        Properties.Settings.Default.Save();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        string? lastSelectedPlaylistName = Properties.Settings.Default.LastSelectedPlaylistName;

        if (!string.IsNullOrEmpty(lastSelectedPlaylistName))
        {
            SelectedPlaylist = MusicLibrary.GetPlaylists().Find(p => p.Name == lastSelectedPlaylistName);

            if (SelectedPlaylist != null)
            {
                SelectPlaylistByName(lastSelectedPlaylistName);
            }
        }

        string? lastBackgroundPlaylistName = Properties.Settings.Default.LastBackgroundPlaylistName;
        string? lastSelectedSongId = Properties.Settings.Default.LastSelectedSongId;

        if (!string.IsNullOrEmpty(lastBackgroundPlaylistName))
        {
            Playlist? backgroundPlaylist = MusicLibrary.GetPlaylists().Find(p => p.Name == lastBackgroundPlaylistName);

            if (backgroundPlaylist != null)
            {
                BackgroundPlaylistName = lastBackgroundPlaylistName;

                foreach (Button button in Helper.FindVisualChildren<Button>(PlaylistList.List))
                {
                    if (((button.Content as ContentPresenter)!.Content as Playlist)!.Name == BackgroundPlaylistName)
                    {
                        button.FontWeight = FontWeights.ExtraBold;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(lastSelectedSongId))
                {
                    SelectedSong = MusicLibrary.GetSongsFromPlaylist(BackgroundPlaylistName).Find(s => s.Id == lastSelectedSongId);

                    if (SelectedSong != null)
                    {
                        if (SelectSong(SelectedSong))
                        {
                            PlayButton_Click(null!, null!);

                            AudioStreamControl.CurrentTrackPosition = AudioStreamControl.CurrentTrackLength * Properties.Settings.Default.LastSeekBarValue / 100;

                            PlayerControls.SeekBar.Value = Properties.Settings.Default.LastSeekBarValue;
                        }
                        else
                        {
                            SelectedSong = null;
                        }
                    }
                }
            }
        }

        PlayerControls.Mode = (PlaybackMode)Properties.Settings.Default.LastPlaybackMode;

        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    //public void ModifyTheme(ThemeColors themeColor, FontSize fontSize = Models.FontSize.Normal)
    //{
    //    ThemeManager.ClearStyles();
    //    ThemeManager.AddTheme(themeColor);

    //    const string colors = @"Styles\SolidColorBrushes.xaml";
    //    Uri colorsUri = new Uri(colors, UriKind.Relative);
    //    ResourceDictionary brushesDict = (Application.LoadComponent(colorsUri) as ResourceDictionary)!;

    //    ThemeManager.AddDict(brushesDict);

    //    Uri sizeUri = ThemeManager.GetSizeUri(fontSize);
    //    ResourceDictionary sizesDict = (Application.LoadComponent(sizeUri) as ResourceDictionary)!;

    //    ThemeManager.AddDict(sizesDict);

    //    SelectedTheme = (int)themeColor;
    //}

    private void MainVolumeSlider_ValueChanged(object sender, EventArgs e)
    {
        AudioStreamControl.MainMusic!.MusicVolume = (float)PlayerControls.MainVolumeSlider.Value / 100;
    }

    public void Music_StoppedEvent(object sender, EventArgs e)
    {
        if ((AudioStreamControl.CurrentTrackPosition + 0.3) >= AudioStreamControl.CurrentTrackLength)
        {
            PlayerControls.State = PlayerState.Paused;
            _seekBarTimer.Stop();

            NextButton_Click(null!, null!);
        }
        else if (sender == null!)
        {
            AudioStreamControl.Pause();

            PlayerControls.State = PlayerState.Paused;
            _seekBarTimer.Stop();
        }
    }

    public void Song_Click(object sender, RoutedEventArgs e)
    {
        //string idBefore = SelectedSong != null ? SelectedSong.Id : "";

        if (sender is DataGrid { SelectedItem: not null } grid)
        {
            SelectSong((grid.SelectedItem as MediaFile)!);
            //SelectSong((((sender as Button)!.Content as GridViewRowPresenter)!.Content as MediaFile)!);
        }

        //string idAfter = SelectedSong != null ? SelectedSong.Id : "";

        //if (idBefore != idAfter)
        //{ // outline background playlist
        //    BackgroundPlaylistName = SelectedPlaylist!.Name;

        //    foreach (Button button in Helper.FindVisualChildren<Button>(PlaylistList.List))
        //    {
        //        button.FontWeight = ((button.Content as ContentPresenter)!
        //            .Content as Playlist)!.Name == SelectedPlaylist.Name ?
        //                FontWeights.ExtraBold : FontWeights.Normal;
        //    }
        //}
    }

    public bool SelectSong(MediaFile mediaFile)
    {
        if (!File.Exists(mediaFile.Path))
        {
            InfoSnackbar.MessageQueue?.Clear();
            InfoSnackbar.MessageQueue?.Enqueue($"Song \"{mediaFile.Title}\" could not be found", null, null, null, false, true, TimeSpan.FromSeconds(2));

            return false;
        }
        else
        {
            mediaFile.UpdateFromTag();
            SelectedSong = mediaFile.Clone();

            AudioStreamControl.PathToMusic = SelectedSong.Path;

            AudioStreamControl.StopAndPlayFromPosition(0);
            _seekBarTimer.Start();

            PlayerControls.State = PlayerState.Playing;

            PlayerControls.CurrentSongName.Text = SelectedSong.Title;
            TimeSpan ts = SelectedSong.Duration;
            PlayerControls.TotalTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            PlayerControls.CurrentTime.Text = "0:00";

            TrackInfo.SetSelectedMediaFile(SelectedSong);

            //foreach (Button button in Helper.FindVisualChildren<Button>(TracksTable.TracksTable))
            //{
            //    // outline selected mediaFile
            //    button.FontWeight = ((button.Content as GridViewRowPresenter)!
            //        .Content as MediaFile)!.Id == SelectedSong.Id ?
            //        FontWeights.ExtraBold : FontWeights.Normal;
            //}

            StartVisualization();

            return true;
        }
    }

    private void SeekBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        double posInSeekBar = (PlayerControls.SeekBar.Value * AudioStreamControl.CurrentTrackLength) / 100;

        if (AudioStreamControl.PathToMusic != null && Math.Abs(AudioStreamControl.CurrentTrackPosition - posInSeekBar) > 0 && !AudioStreamControl.MainMusic!.IsPaused)
        {
            AudioStreamControl.StopAndPlayFromPosition(posInSeekBar);

            PlayerControls.State = PlayerState.Playing;
            _seekBarTimer.Start();
        }
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SelectedSong != null)
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
            PlayerControls.SeekBar.Value = (AudioStreamControl.CurrentTrackPosition * 100) / AudioStreamControl.CurrentTrackLength;
        }
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSong != null)
        {
            if (PlayerControls.State == PlayerState.Paused)
            {
                PlayerControls.State = PlayerState.Playing;

                AudioStreamControl.StopAndPlayFromPosition((PlayerControls.SeekBar.Value * AudioStreamControl.CurrentTrackLength) / 100);

                _seekBarTimer.Start();
            }
            else
            {
                PlayerControls.State = PlayerState.Paused;

                AudioStreamControl.Pause();
                _seekBarTimer.Stop();
            }
        }
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSong != null)
        {
            int selectedSongIndex = TracksTable.TracksTable.Items
                                    .Cast<MediaFile>()
                                    .ToList()
                                    .FindIndex(item => item.Id == SelectedSong.Id);

            if (selectedSongIndex == -1)
            { // changed displayed playlist
                switch (PlayerControls.Mode)
                {
                    case PlaybackMode.Loop:

                        List<MediaFile> backgroundSongs = MusicLibrary.GetSongsFromPlaylist(BackgroundPlaylistName);
                        selectedSongIndex = backgroundSongs.FindIndex(item => item.Id == SelectedSong.Id);

                        if (selectedSongIndex != -1)
                        {
                            SelectWithSkipping(
                                selectedSongIndex == backgroundSongs.Count - 1
                                    ? backgroundSongs[0]
                                    : backgroundSongs[selectedSongIndex + 1], NextButton_Click);
                        }

                        break;

                    case PlaybackMode.Loop1:
                        SelectSong(SelectedSong);
                        break;

                    case PlaybackMode.NoLoop:
                        break;
                }

                return;
            }

            switch (PlayerControls.Mode)
            {
                case PlaybackMode.Loop:
                    if (selectedSongIndex == TracksTable.TracksTable.Items.Count - 1)
                    {
                        SelectWithSkipping((TracksTable.TracksTable.Items[0] as MediaFile)!, NextButton_Click);
                    }
                    else
                    {
                        SelectWithSkipping((TracksTable.TracksTable.Items[selectedSongIndex + 1] as MediaFile)!, NextButton_Click);
                    }
                    break;

                case PlaybackMode.Loop1:
                    SelectSong(SelectedSong);
                    break;

                case PlaybackMode.NoLoop:
                    break;
            }
        }
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSong != null)
        {
            int selectedSongIndex = TracksTable.TracksTable.Items
                                    .Cast<MediaFile>()
                                    .ToList()
                                    .FindIndex(item => item.Id == SelectedSong.Id);

            if (selectedSongIndex == -1)
            { // changed displayed playlist
                switch (PlayerControls.Mode)
                {
                    case PlaybackMode.Loop:

                        List<MediaFile> backgroundSongs = MusicLibrary.GetSongsFromPlaylist(BackgroundPlaylistName);
                        selectedSongIndex = backgroundSongs.FindIndex(item => item.Id == SelectedSong.Id);

                        if (selectedSongIndex != -1)
                        {
                            SelectWithSkipping(selectedSongIndex == 0
                                    ? backgroundSongs[^1]
                                    : backgroundSongs[selectedSongIndex - 1],
                                PrevButton_Click);
                        }

                        break;

                    case PlaybackMode.Loop1:
                        SelectSong(SelectedSong);
                        break;

                    case PlaybackMode.NoLoop:
                        SelectSong(SelectedSong);
                        break;
                }

                return;
            }

            switch (PlayerControls.Mode)
            {
                case PlaybackMode.Loop:
                    if (selectedSongIndex == 0)
                    {
                        SelectWithSkipping((TracksTable.TracksTable.Items[^1] as MediaFile)!, PrevButton_Click);
                    }
                    else
                    {
                        SelectWithSkipping((TracksTable.TracksTable.Items[selectedSongIndex - 1] as MediaFile)!, PrevButton_Click);
                    }
                    break;

                case PlaybackMode.Loop1:
                    SelectSong(SelectedSong);
                    break;

                case PlaybackMode.NoLoop:
                    SelectSong(SelectedSong);
                    break;
            }
        }
    }

    private void SelectWithSkipping(MediaFile song, Action<object, RoutedEventArgs> nextPrevButtonClick)
    { // skips if mediaFile doesn't exist
        if (!File.Exists(song.Path))
        {
            InfoSnackbar.MessageQueue?.Clear();
            InfoSnackbar.MessageQueue?.Enqueue($"Song \"{song.Title}\" could not be found", null, null, null, false, true, TimeSpan.FromSeconds(2));
            SelectedSong = song.Clone();
            nextPrevButtonClick(null!, null!);
        }
        else
        {
            SelectSong(song);
        }
    }

    private void DisplayPlaylists()
    {
        List<Playlist> playlists = MusicLibrary.GetPlaylists();

        foreach (Playlist p in playlists)
        {
            PlaylistList.List.Items.Add(p);
        }
    }

    public void SelectPlaylistByName(string name)
    {
        foreach (Playlist playlist in MusicLibrary.GetPlaylists())
        {
            if (playlist.Name == name)
            {
                SelectedPlaylist = playlist;

                Task unused = DisplaySelectedPlaylist();

                break;
            }
        }
    }

    private Task DisplaySelectedPlaylist()
    {
        if (SelectedPlaylist != null)
        {
            //PlaylistText.CurrentPlaylistName.Text = SelectedPlaylist.Name;

            List<MediaFile> songs = MusicLibrary.GetSongsFromPlaylist(SelectedPlaylist.Name);
            TracksTable.TracksTable.Items.Clear();

            foreach (MediaFile song in songs)
            {
                TracksTable.TracksTable.Items.Add(song);
            }

            //await Task.Delay(10); // waiting till list is loaded and outline selected mediaFile
            //if (SelectedSong != null)
            //{
            //    foreach (Button button in Helper.FindVisualChildren<Button>(TracksTable.TracksTable))
            //    {
            //        if (((button.Content as GridViewRowPresenter)!.Content as MediaFile)!.Id == SelectedSong.Id)
            //        {
            //            button.FontWeight = FontWeights.ExtraBold;
            //            break;
            //        }
            //    }
            //}
        }

        return Task.CompletedTask;
    }

    public void SelectedSongRemoved()
    {
        if (SelectedSong != null)
        {
            AudioStreamControl.Stop();
            SelectedSong = null;
            PlayerControls.State = PlayerState.Paused;
            _seekBarTimer.Stop();
            PlayerControls.CurrentSongName.Text = "Song not selected";
            PlayerControls.TotalTime.Text = "0:00";
            PlayerControls.CurrentTime.Text = "0:00";
            PlayerControls.SeekBar.Value = 0;

            foreach (Button button in Helper.FindVisualChildren<Button>(PlaylistList.List))
            { // remove outlining from playlist
                if (((button.Content as ContentPresenter)!.Content as Playlist)!.Name == BackgroundPlaylistName)
                {
                    button.FontWeight = FontWeights.Normal;
                    break;
                }
            }

            BackgroundPlaylistName = null;

            SelectedSong = null;

            StopVisualization();
        }
    }

    public void RenameSelectedPlaylist(string newName)
    {
        if (SelectedPlaylist != null)
        {
            SelectedPlaylist.Name = newName;
            //PlaylistText.CurrentPlaylistName.Text = newName;
        }
    }

    public void RenameSelectedSong(string newName)
    {
        if (SelectedSong != null)
        {
            SelectedSong.Title = newName;
            PlayerControls.CurrentSongName.Text = newName;
        }
    }

    public void StartVisualization()
    {
        if (VisualizationEnabled && SelectedSong != null)
        {
            if (_currentlyVisualizedPath != SelectedSong.Path)
            {
                _currentlyVisualizedPath = SelectedSong.Path;

                PlayerControls.VisualizeAudio(SelectedSong.Path);
            }
        }
    }

    public void StopVisualization()
    {
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
        Properties.Settings.Default.MainVolumeSliderValue = PlayerControls.MainVolumeSlider.Value;
        //Properties.Settings.Default.AdditionalVolumeSliderValue = PlayerControls.AdditionalVolumeSlider.Value;

        Properties.Settings.Default.LastSelectedPlaylistName = SelectedPlaylist != null ? SelectedPlaylist.Name : "";
        Properties.Settings.Default.LastBackgroundPlaylistName = BackgroundPlaylistName ?? "";
        Properties.Settings.Default.LastSelectedSongId = SelectedSong != null ? SelectedSong.Id : "";

        Properties.Settings.Default.LastSeekBarValue = PlayerControls.SeekBar.Value;

        Properties.Settings.Default.LastPlaybackMode = (int)PlayerControls.Mode;

        if (Properties.Settings.Default.EqualizerOnStartEnabled)
        {
            Properties.Settings.Default.EqualizerBandName =
                SelectedBandsSettings != null! ? SelectedBandsSettings.Name : null;
        }

        Properties.Settings.Default.MainOutputDevice = DeviceControl.GetOutputDeviceNameById(AudioStreamControl.MainMusic!.GetOutputDeviceId());

        if (AudioStreamControl.AdditionalMusic != null)
        {
            Properties.Settings.Default.AdditionalOutputDevice = DeviceControl.GetOutputDeviceNameById(AudioStreamControl.AdditionalMusic.GetOutputDeviceId());
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
                PlayButton_Click(null!, null!);
                e.Handled = true;
            }
            else if (enteredHotkey == Properties.Settings.Default["NextSongHotkey"].ToString())
            {
                NextButton_Click(null!, null!);
                e.Handled = true;
            }
            else if (enteredHotkey == Properties.Settings.Default["PreviousSongHotkey"].ToString())
            {
                PrevButton_Click(null!, null!);
                e.Handled = true;
            }
            else if (enteredHotkey == Properties.Settings.Default["IncreaseMainVolumeHotkey"].ToString())
            {
                double val = PlayerControls.MainVolumeSlider.Value;
                PlayerControls.MainVolumeSlider.Value = val + 5 > 100 ? 100 : val + 5;
                e.Handled = true;
            }
            else if (enteredHotkey == Properties.Settings.Default["DecreaseMainVolumeHotkey"].ToString())
            {
                double val = PlayerControls.MainVolumeSlider.Value;
                PlayerControls.MainVolumeSlider.Value = val - 5 < 0 ? 0 : val - 5;
                e.Handled = true;
            }
        }
    }
}
