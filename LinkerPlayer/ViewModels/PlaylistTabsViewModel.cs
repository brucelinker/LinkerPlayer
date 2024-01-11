using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Windows;
using PlaylistsNET.Content;
using PlaylistsNET.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Application = System.Windows.Application;
using TabControl = System.Windows.Controls.TabControl;

namespace LinkerPlayer.ViewModels;

public partial class PlaylistTabsViewModel : ObservableObject
{
    [ObservableProperty]
    private static PlaylistTab? _selectedPlaylistTab;
    [ObservableProperty]
    private string? _selectedPlaylistName;
    [ObservableProperty]
    private int? _selectedPlaylistIndex;
    [ObservableProperty]
    private static Playlist? _selectedPlaylist;
    [ObservableProperty]
    private static MediaFile? _selectedTrack;
    [ObservableProperty]
    private static int _selectedIndex;
    [ObservableProperty]
    private static PlayerState _state;
    [ObservableProperty]
    private static bool _shuffleMode;
    public static ObservableCollection<PlaylistTab> TabList { get; set; } = new();

    private static TabControl? _tabControl;
    private static DataGrid? _dataGrid;
    private readonly List<MediaFile?> _shuffleList = new();
    private int _shuffledIndex = -1;
    private readonly MainWindow _mainWindow;

    private const string SupportedAudioFormats = "(*.mp3; *.flac)|*.mp3; *.flac";
    private const string SupportedPlaylistFormats = "(*.m3u;*.pls;*.wpl;*.zpl)|*.m3u;*.pls;*.wpl;*.zpl";
    const string SupportedExtensions = $"Audio Formats {SupportedAudioFormats}|Playlist Files {SupportedPlaylistFormats}|All files (*.*)|*.*";

    public PlaylistTabsViewModel()
    {
        _mainWindow = (MainWindow?)Application.Current.MainWindow!;

        WeakReferenceMessenger.Default.Register<PlayerStateMessage>(this, (_, m) =>
        {
            OnPlayerStateChanged(m.Value);
        });
    }

    public void OnDataGridLoaded(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is DataGrid dataGrid)
        {
            _dataGrid = dataGrid;

            _dataGrid.SelectedItem = SelectedTrack;
            _dataGrid.SelectedIndex = SelectedIndex;
        }
    }

    public void OnTabSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (sender is TabControl tabControl)
        {
            _tabControl = tabControl;

            SelectedPlaylistIndex = tabControl.SelectedIndex;
            if (SelectedPlaylistIndex < 0) return;

            SelectedPlaylistTab = tabControl.SelectedContent as PlaylistTab;
            if (SelectedPlaylistTab == null) return;

            SelectedPlaylist = MusicLibrary.GetPlaylistByName(SelectedPlaylistTab.Header!);

            if (!SelectedPlaylistTab!.Tracks.Any()) return;

            SelectedPlaylistTab!.LastSelectedIndex ??= 0;
            SelectedIndex = (int)SelectedPlaylistTab!.LastSelectedIndex;
            SelectedTrack = SelectedPlaylistTab.Tracks[SelectedIndex];

            if (_dataGrid != null)
            {
                _dataGrid!.SelectedIndex = SelectedIndex;
            }
        }
    }

    public void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _dataGrid = (sender as DataGrid);

        if (_dataGrid is { SelectedItem: not null })
        {
            SelectedTrack = _dataGrid.SelectedItem as MediaFile;
            SelectedIndex = _dataGrid.SelectedIndex;
            SelectedPlaylistTab!.LastSelectedMediaFile = SelectedTrack;
            SelectedPlaylistTab.LastSelectedIndex = SelectedIndex;
        }

        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));
    }

    public void OnPlayerStateChanged(PlayerState state)
    {
        State = state;
        if (SelectedTrack != null) SelectedTrack.State = state;
    }

    public void NewPlaylist()
    {
        string playlistName = "New Playlist";
        int index = 0;
        bool found = true;

        while (found)
        {
            found = false;
            foreach (Playlist playlist in MusicLibrary.GetPlaylists())
            {
                if (playlist.Name == playlistName)
                {
                    index++;
                    playlistName = $"New Playlist ({index})";
                    found = true;
                    break;
                }
            }
        }

        Playlist newPlaylist = MusicLibrary.CreatePlaylist(playlistName);
        AddPlaylistTab(newPlaylist);
        _tabControl!.SelectedIndex = TabList.Count - 1;
    }

    public void LoadPlaylist()
    {
        using OpenFileDialog openFileDialog = new();
        openFileDialog.Filter = SupportedPlaylistFormats;
        openFileDialog.Multiselect = false;
        openFileDialog.Title = "Select file(s)";

        DialogResult fileDialogRes = openFileDialog.ShowDialog();

        if (fileDialogRes != DialogResult.OK) return;

        LoadPlaylistFile(openFileDialog.FileName);
    }

    public void AddFolder(object sender, RoutedEventArgs routedEventArgs)
    {
        throw new NotImplementedException();
    }

    public void AddFiles(object sender, RoutedEventArgs routedEventArgs)
    {
        using OpenFileDialog openFileDialog = new();
        openFileDialog.Filter = SupportedExtensions;
        openFileDialog.Multiselect = true;
        openFileDialog.Title = "Select file(s)";

        DialogResult fileDialogRes = openFileDialog.ShowDialog();

        if (fileDialogRes != DialogResult.OK) return;

        foreach (string fileName in openFileDialog.FileNames)
        {
            var extension = Path.GetExtension(fileName);

            if (string.Equals(".mp3", extension, StringComparison.OrdinalIgnoreCase))
            {
                LoadAudioFile(fileName);
            }
        }
    }

    public void RenamePlaylist(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
        }
    }

    public void RemovePlaylist(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            PlaylistTab playlistTab = (item.DataContext as PlaylistTab)!;
            MusicLibrary.RemovePlaylist(playlistTab.Header);
            int tabIndex = TabList.ToList().FindIndex(x => x.Header!.Contains(playlistTab.Header!));
            TabList.RemoveAt(tabIndex);
        }
    }

    public MediaFile SelectTrack(MediaFile track)
    {
        SelectedTrack = track;

        SelectedIndex = SelectedPlaylistTab!.Tracks.ToList()
            .FindIndex(x => x.Id.Contains(track.Id));

        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));

        return SelectedTrack;
    }

    public MediaFile SelectFirstTrack()
    {
        SelectedIndex = 0;
        SelectedTrack = SelectedPlaylistTab!.Tracks[SelectedIndex];


        _dataGrid!.SelectedItem = SelectedTrack;
        _dataGrid.SelectedIndex = SelectedIndex;

        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));

        return SelectedTrack;
    }

    public MediaFile? PreviousMediaFile()
    {
        if (SelectedTrack != null && _dataGrid != null)
        {
            int newIndex;

            SelectedTrack.State = PlayerState.Stopped;

            if (_shuffleMode)
            {
                newIndex = GetPreviousShuffledIndex();
            }
            else
            {
                if (SelectedIndex == 0)
                    newIndex = SelectedPlaylistTab!.Tracks.Count - 1;
                else
                    newIndex = SelectedIndex - 1;
            }

            _dataGrid.SelectedIndex = newIndex;
            SelectedTrack = SelectedPlaylistTab!.Tracks[newIndex];

            _dataGrid.SelectedItem = SelectedTrack;
            _dataGrid.ScrollIntoView(_dataGrid.SelectedItem);
            SelectedTrack.State = PlayerState.Playing;
        }

        return SelectedTrack;
    }

    public MediaFile? NextMediaFile()
    {
        if (SelectedTrack != null && _dataGrid != null)
        {
            int newIndex;

            SelectedTrack.State = PlayerState.Stopped;

            if (_shuffleMode)
            {
                newIndex = GetNextShuffledIndex();
            }
            else
            {
                if (SelectedIndex == SelectedPlaylistTab!.Tracks.Count - 1)
                    newIndex = 0;
                else
                    newIndex = SelectedIndex + 1;
            }

            _dataGrid.SelectedIndex = newIndex;
            SelectedTrack = SelectedPlaylistTab!.Tracks[newIndex];

            _dataGrid.SelectedItem = SelectedTrack;
            _dataGrid.ScrollIntoView(_dataGrid.SelectedItem);
            SelectedTrack.State = PlayerState.Playing;
        }

        return SelectedTrack;
    }

    public static void LoadPlaylists()
    {
        List<Playlist> playlists = MusicLibrary.GetPlaylists();

        foreach (Playlist p in playlists)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;

            PlaylistTab tab = AddPlaylistTab(p);

            Log.Information($"LoadPlaylists - added PlaylistTab {tab}");
        }
    }

    public static PlaylistTab AddPlaylistTab(Playlist p)
    {
        PlaylistTab tab = new PlaylistTab
        {
            Header = p.Name,
            Tracks = LoadPlaylistTracks(p.Name)
        };

        TabList.Add(tab);

        return tab;
    }

    public static void AddSongToPlaylistTab(MediaFile song, string playlistName)
    {
        Log.Information("MainWindow - LoadPlaylistTracks");

        foreach (PlaylistTab tab in TabList)
        {
            if (tab.Header == playlistName)
            {
                tab.Tracks.Add(song);
            }
        }
    }

    public void ShuffleTracks(bool shuffleMode)
    {
        _shuffleMode = shuffleMode;

        if (shuffleMode)
        {
            if (SelectedPlaylistTab != null && SelectedPlaylistTab!.Tracks.Any())
            {
                List<MediaFile> tempList = SelectedPlaylistTab!.Tracks.ToList();

                Random random = new Random();

                while (tempList.Count > 0)
                {
                    int index = random.Next(0, tempList.Count - 1);

                    _shuffleList.Add(tempList[index]);
                    tempList.RemoveAt(index);
                }
            }
        }
        else
        {
            _shuffleList.Clear();
        }
    }

    private int GetNextShuffledIndex()
    {
        if (_shuffledIndex == _shuffleList.Count - 1)
            _shuffledIndex = 0;
        else
            _shuffledIndex += 1;

        int newIndex = SelectedPlaylistTab!.Tracks.ToList()
            .FindIndex(x => x.Id.Contains(_shuffleList[_shuffledIndex]!.Id));

        return newIndex;
    }

    private int GetPreviousShuffledIndex()
    {
        if (_shuffledIndex == 0)
            _shuffledIndex = _shuffleList.Count - 1;
        else
            _shuffledIndex -= 1;

        int newIndex = SelectedPlaylistTab!.Tracks.ToList()
            .FindIndex(x => x.Id.Contains(_shuffleList[_shuffledIndex]!.Id));

        return newIndex;
    }

    private static ObservableCollection<MediaFile> LoadPlaylistTracks(string? playListName)
    {
        Log.Information("MainWindow - LoadPlaylistTracks");

        ObservableCollection<MediaFile> tracks = new();
        List<MediaFile> songs = MusicLibrary.GetSongsFromPlaylist(playListName);

        foreach (MediaFile song in songs)
        {
            tracks.Add(song);
        }

        return tracks;
    }
    private void LoadFileButton_Click(object sender, RoutedEventArgs e)
    {
        using OpenFileDialog openFileDialog = new();
        openFileDialog.Filter = SupportedExtensions;
        openFileDialog.Multiselect = true;
        openFileDialog.Title = "Select file(s)";

        DialogResult fileDialogRes = openFileDialog.ShowDialog();

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
        using FolderBrowserDialog folderDialog = new FolderBrowserDialog();
        folderDialog.RootFolder = Environment.SpecialFolder.MyMusic;

        DialogResult fileDialogResult = folderDialog.ShowDialog();

        if (fileDialogResult != DialogResult.OK) return;

        string selectedFolderPath = folderDialog.SelectedPath;
        DirectoryInfo dirInfo = new DirectoryInfo(selectedFolderPath);
        List<FileInfo> files = dirInfo.GetFiles("*.mp3", SearchOption.AllDirectories).ToList();

        if (!files.Any())
        {
            _mainWindow.InfoSnackbar.MessageQueue?.Clear();
            _mainWindow.InfoSnackbar.MessageQueue?.Enqueue($"No files were found in {selectedFolderPath}.", null, null, null,
                false, true, TimeSpan.FromSeconds(3));
        }

        string playlistName = dirInfo.Name;
        Playlist playlist = MusicLibrary.CreatePlaylist(playlistName);
        SelectedPlaylist = playlist;

        foreach (FileInfo? file in files)
        {
            MediaFile mediaFile = new MediaFile(file.FullName);

            if (MusicLibrary.AddSong(mediaFile))
            {
                LoadAudioFile(file.FullName);
            }
        }

        PlaylistTab playlistTab = AddPlaylistTab(playlist);
        _mainWindow.SelectPlaylistByName(playlistTab.Header!);
    }

    private void LoadAudioFile(string fileName)
    {
        if (File.Exists(fileName))
        {
            MediaFile song = new MediaFile(fileName);

            if (MusicLibrary.AddSong(song))
            {
                if (SelectedPlaylist == null)
                {
                    SelectedPlaylist = MusicLibrary.GetPlaylists().FirstOrDefault();

                    if (SelectedPlaylist != null && !string.IsNullOrWhiteSpace(SelectedPlaylist.Name))
                    {
                        _mainWindow.SelectPlaylistByName(SelectedPlaylist.Name);

                        MusicLibrary.AddSongToPlaylist(song.Id, SelectedPlaylist.Name);
                        AddSongToPlaylistTab(song, SelectedPlaylist.Name);
                    }
                }
                else
                {
                    MusicLibrary.AddSongToPlaylist(song.Id, SelectedPlaylist.Name);
                    AddSongToPlaylistTab(song, SelectedPlaylist.Name!);
                }
            }
        }
        else
        {
            _mainWindow.InfoSnackbar.MessageQueue?.Clear();
            _mainWindow.InfoSnackbar.MessageQueue?.Enqueue($"Error while converting {fileName}", null, null, null, false, true, TimeSpan.FromSeconds(2));
        }
    }

    private void LoadPlaylistFile(string fileName)
    {
        if (File.Exists(fileName))
        {
            if (fileName.EndsWith("m3u"))
            {
                string directoryName = Path.GetDirectoryName(fileName)!;
                string playlistName = Path.GetFileNameWithoutExtension(fileName);

                M3uContent content = new M3uContent();
                FileStream stream = File.OpenRead(fileName);
                M3uPlaylist m3UPlaylist = content.GetFromStream(stream);

                List<string> paths = m3UPlaylist.GetTracksPaths();

                Playlist playlist = MusicLibrary.CreatePlaylist(playlistName);
                SelectedPlaylist = playlist;
                PlaylistTab playlistTab = AddPlaylistTab(playlist);
                _tabControl!.SelectedIndex = TabList.Count - 1;
                _mainWindow.SelectPlaylistByName(playlistTab.Header!);

                foreach (string path in paths)
                {
                    string mediaFilePath = Path.Combine(directoryName, path);
                    MediaFile mediaFile = new MediaFile(mediaFilePath);

                    if (MusicLibrary.AddSong(mediaFile))
                    {
                        LoadAudioFile($"{directoryName}\\{path}");
                    }
                }
            }
        }
    }
}