using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Windows;
using PlaylistsNET.Content;
using PlaylistsNET.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;

namespace LinkerPlayer.UserControls;

public partial class FunctionButtons
{
    private const string SupportedAudioFormats = "(*.mp3; *.flac)|*.mp3; *.flac";
    private const string SupportedPlaylistFormats = "(*.m3u;*.pls;*.wpl;*.zpl)|*.m3u;*.pls;*.wpl;*.zpl";
    const string SupportedExtensions = $"Audio Formats {SupportedAudioFormats}|Playlist Files {SupportedPlaylistFormats}|All files (*.*)|*.*";

    private bool _isEqualizerWindowOpen;
    private EqualizerWindow? _equalizerWin;

    public FunctionButtons()
    {
        InitializeComponent();
    }

    private bool _isSettingsWindowOpen;
    private SettingsWindow? _settingsWin;


    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSettingsWindowOpen)
        {
            if (_settingsWin is { WindowState: WindowState.Minimized })
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

    private void LoadFileButton_Click(object sender, RoutedEventArgs e)
    {
        LoadFileButton.IsHitTestVisible = false;

        using OpenFileDialog openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = SupportedExtensions;
        openFileDialog.Multiselect = true;
        openFileDialog.Title = "Select file(s)";

        DialogResult fileDialogRes = openFileDialog.ShowDialog();

        LoadFileButton.IsHitTestVisible = true;

        if (fileDialogRes != DialogResult.OK) return;

        foreach (string fileName in openFileDialog.FileNames)
        {
            var extension = Path.GetExtension(fileName);

            if (string.Equals(".mp3", extension, StringComparison.OrdinalIgnoreCase))
            {
                LoadAudioFile(fileName);
            }

            if (string.Equals(".m3u", extension, StringComparison.OrdinalIgnoreCase))
            {
                LoadPlaylistFile(fileName);
            }
        }
    }

    private void LoadFolderButton_Click(object sender, RoutedEventArgs e)
    {
        LoadFolderButton.IsHitTestVisible = false;

        using FolderBrowserDialog folderDialog = new FolderBrowserDialog();
        folderDialog.RootFolder = Environment.SpecialFolder.MyMusic;

        DialogResult fileDialogResult = folderDialog.ShowDialog();

        LoadFolderButton.IsHitTestVisible = true;

        if (fileDialogResult != DialogResult.OK) return;

        string selectedFolderPath = folderDialog.SelectedPath;
        DirectoryInfo dirInfo = new DirectoryInfo(selectedFolderPath);
        List<FileInfo> files = dirInfo.GetFiles("*.mp3", SearchOption.AllDirectories).ToList();

        if (!files.Any())
        {
            MainWindow win = (MainWindow)Window.GetWindow(this)!;
            win.InfoSnackbar.MessageQueue?.Clear();
            win.InfoSnackbar.MessageQueue?.Enqueue($"No files were found in {selectedFolderPath}.", null, null, null,
                false, true, TimeSpan.FromSeconds(3));
        }

        foreach (FileInfo? file in files)
        {
            MediaFile mediaFile = new MediaFile(file.FullName);

            if (MusicLibrary.AddSong(mediaFile))
            {
                MainWindow mainWindow = (MainWindow)Window.GetWindow(this)!;

                Playlist? selectedPlaylist = mainWindow.SelectedPlaylist;

                if (selectedPlaylist == null)
                {
                    selectedPlaylist = MusicLibrary.GetPlaylists().FirstOrDefault();

                    if (selectedPlaylist != null)
                    {
                        mainWindow.SelectPlaylistByName(selectedPlaylist.Name!);

                        MusicLibrary.AddSongToPlaylist(mediaFile.Id, selectedPlaylist.Name);
                        mainWindow.TrackList.List.Items.Add(mediaFile);
                    }
                }
                else
                {
                    MusicLibrary.AddSongToPlaylist(mediaFile.Id, selectedPlaylist.Name);
                    mainWindow.TrackList.List.Items.Add(mediaFile);
                }
            }
        }
    }

    private void LoadAudioFile(string fileName)
    {
        if (File.Exists(fileName))
        {
            MediaFile song = new MediaFile (fileName);

            if (MusicLibrary.AddSong(song))
            {
                MainWindow win = (MainWindow)Window.GetWindow(this)!;

                Playlist? selectedPlaylist = win.SelectedPlaylist;

                if (selectedPlaylist == null)
                {
                    selectedPlaylist = MusicLibrary.GetPlaylists().FirstOrDefault();

                    if (selectedPlaylist != null)
                    {
                        win.SelectPlaylistByName(selectedPlaylist.Name!);

                        MusicLibrary.AddSongToPlaylist(song.Id, selectedPlaylist.Name);
                        win.TrackList.List.Items.Add(song);
                    }
                }
                else
                {
                    MusicLibrary.AddSongToPlaylist(song.Id, selectedPlaylist.Name);
                    win.TrackList.List.Items.Add(song);
                }
            }
        }
        else
        {
            MainWindow win = (MainWindow)Window.GetWindow(this)!;
            win.InfoSnackbar.MessageQueue?.Clear();
            win.InfoSnackbar.MessageQueue?.Enqueue($"Error while converting {fileName}", null, null, null, false, true, TimeSpan.FromSeconds(2));
        }
    }

    private void LoadPlaylistFile(string fileName)
    {
        if (File.Exists(fileName))
        {
            if (fileName.EndsWith("m3u"))
            {
                string directoryName = Path.GetDirectoryName(fileName)!;

                M3uContent content = new M3uContent();
                FileStream stream = File.OpenRead(fileName);
                M3uPlaylist playlist = content.GetFromStream(stream);

                List<string> paths = playlist.GetTracksPaths();

                foreach (string path in paths)
                {
                    LoadAudioFile($"{directoryName}\\{path}");
                }
            }
        }
    }

    private void EqualizerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isEqualizerWindowOpen)
        {
            if (_equalizerWin is { WindowState: WindowState.Minimized })
            {
                _equalizerWin.WindowState = WindowState.Normal;
            }

            return;
        }

        _equalizerWin = new EqualizerWindow
        {
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        _equalizerWin.Closed += (_, _) => { _isEqualizerWindowOpen = false; };
        _equalizerWin.Closing += (_, _) => { _equalizerWin.Owner = null; };
        _isEqualizerWindowOpen = true;

        _equalizerWin.Show();

        MainWindow win = (MainWindow)Window.GetWindow(this)!;

        _equalizerWin.StartStopText.Text = win.AudioStreamControl.MainMusic!.IsEqualizerWorking ? "Stop" : "Start";

        _equalizerWin.LoadSelectedBand(win.SelectedBandsSettings);

        if (_equalizerWin.StartStopText.Text == "Start")
        {
            _equalizerWin.ButtonsSetEnabledState(false);
            _equalizerWin.SliderSetEnabledState(false);
        }
    }
}