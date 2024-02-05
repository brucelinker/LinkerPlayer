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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using Application = System.Windows.Application;
using TabControl = System.Windows.Controls.TabControl;

namespace LinkerPlayer.ViewModels;

public partial class PlaylistTabsViewModel : ObservableObject
{
    [ObservableProperty]
    private static PlaylistTab? _selectedPlaylistTab;
    [ObservableProperty]
    private int _selectedPlaylistIndex;
    [ObservableProperty]
    private static Playlist? _selectedPlaylist;
    [ObservableProperty]
    private static MediaFile? _selectedTrack;
    [ObservableProperty]
    private static int _selectedIndex;
    [ObservableProperty]
    private static MediaFile? _runningTrack;
    [ObservableProperty]
    private static PlayerState _state;
    [ObservableProperty]
    private static bool _shuffleMode;
    public static ObservableCollection<PlaylistTab> TabList { get; set; } = new();

    private static TabControl? _tabControl;
    private static DataGrid? _dataGrid;
    private static readonly List<MediaFile?> ShuffleList = new();
    private static int _shuffledIndex = 0;
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

            SelectedPlaylist = MusicLibrary.GetPlaylistByName(SelectedPlaylistTab.Name!);
            if (SelectedPlaylist == null) return;

            SelectedTrack = MusicLibrary.MainLibrary.First(x => x!.Id == SelectedPlaylist.SelectedSong);

            if (!SelectedPlaylistTab!.Tracks.Any()) return;
            SelectedPlaylistTab!.SelectedIndex = SelectedPlaylistTab.Tracks.ToList().FindIndex(x => x.Id == SelectedTrack!.Id);
            SelectedIndex = (int)SelectedPlaylistTab!.SelectedIndex;
            //SelectedTrack = SelectedPlaylistTab.Tracks[SelectedIndex];

            if (_dataGrid != null)
            {
                _dataGrid.SelectedIndex = SelectedIndex;
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
            SelectedPlaylistTab!.SelectedTrack = SelectedTrack;
            SelectedPlaylistTab.SelectedIndex = SelectedIndex;

            MusicLibrary.Playlists[(int)SelectedPlaylistIndex!]!.SelectedSong = SelectedTrack!.Id;
        }

        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));
    }

    public void OnPlayerStateChanged(PlayerState state)
    {
        State = state;
        if (SelectedTrack != null)
        {
            SelectedTrack.State = state;

            if (state != PlayerState.Stopped)
            {
                RunningTrack = SelectedTrack;
            }
        }
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

        Playlist newPlaylist = MusicLibrary.AddNewPlaylist(playlistName);
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

        foreach (FileInfo? file in files)
        {
            LoadAudioFile(file.FullName);
        }

        MusicLibrary.SaveToJson();
    }
    public void NewPlaylistFromFolder(object sender, RoutedEventArgs routedEventArgs)
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
        Playlist playlist = MusicLibrary.AddNewPlaylist(playlistName);
        PlaylistTab playlistTab = AddPlaylistTab(playlist);
        _tabControl!.SelectedIndex = _tabControl.Items.IndexOf(playlistTab);

        Stopwatch timer = new Stopwatch();
        timer.Reset();
        timer.Start();

        foreach (FileInfo? file in files)
        {
            LoadAudioFile(file.FullName);
        }

        MusicLibrary.SaveToJson();

        timer.Stop();
        Log.Information($"{playlistName} playlist took {timer.Elapsed.TotalSeconds} seconds to load.");
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

        MusicLibrary.SaveToJson();
    }

    public void RemovePlaylist(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            PlaylistTab playlistTab = (item.DataContext as PlaylistTab)!;
            MusicLibrary.RemovePlaylist(playlistTab.Name!);
            int tabIndex = TabList.ToList().FindIndex(x => x.Name!.Contains(playlistTab.Name!));
            TabList.RemoveAt(tabIndex);

            if (!TabList.Any())
            {
                NewPlaylist();
            }

            MusicLibrary.SaveToJson();
        }
    }

    public MediaFile SelectTrack(Playlist playlist, MediaFile track)
    {
        if (!playlist.Name!.Equals(SelectedPlaylistTab!.Name) && _tabControl != null)
        {
            _tabControl.SelectedIndex = TabList.ToList().FindIndex(x => x.Name == playlist.Name);
        }

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

    public static void LoadPlaylistTabs()
    {
        List<Playlist> playlists = MusicLibrary.GetPlaylists();

        foreach (Playlist p in playlists)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;

            PlaylistTab tab = AddPlaylistTab(p);

            Log.Information($"LoadPlaylistTabs - added PlaylistTab {tab}");
        }
    }

    public static PlaylistTab AddPlaylistTab(Playlist p)
    {
        PlaylistTab tab = new PlaylistTab();

        tab.Name = p.Name ?? "Bupkis";
        tab.Tracks = LoadPlaylistTracks(p.Name);


        TabList.Add(tab);

        return tab;
    }

    public static void AddSongToPlaylistTab(MediaFile song, string playlistName)
    {
        foreach (PlaylistTab tab in TabList)
        {
            if (tab.Name == playlistName)
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
            if (SelectedPlaylistTab == null || !SelectedPlaylistTab.Tracks.Any())
            {
                return;
            }

            List<MediaFile> tempList = SelectedPlaylistTab!.Tracks.ToList();

            Random random = new Random();

            while (tempList.Count > 0)
            {
                int index = random.Next(0, tempList.Count - 1);

                ShuffleList.Add(tempList[index]);
                tempList.RemoveAt(index);
            }

            MediaFile? runningTrack = GetRunningTrack();

            if (runningTrack != null)
            {
                _shuffledIndex = ShuffleList.FindIndex(x => x.Id == runningTrack.Id);
            }
            else
            {
                _shuffledIndex = 0;
            }

        }
        else
        {
            ShuffleList.Clear();
        }
    }

    private int GetNextShuffledIndex()
    {
        if (_shuffledIndex == ShuffleList.Count - 1)
            _shuffledIndex = 0;
        else
            _shuffledIndex += 1;

        int newIndex = SelectedPlaylistTab!.Tracks.ToList()
            .FindIndex(x => x.Id.Contains(ShuffleList[_shuffledIndex]!.Id));

        return newIndex;
    }

    private int GetPreviousShuffledIndex()
    {
        if (_shuffledIndex == 0)
            _shuffledIndex = ShuffleList.Count - 1;
        else
            _shuffledIndex -= 1;

        int newIndex = SelectedPlaylistTab!.Tracks.ToList()
            .FindIndex(x => x.Id.Contains(ShuffleList[_shuffledIndex]!.Id));

        return newIndex;
    }

    private static ObservableCollection<MediaFile> LoadPlaylistTracks(string? playListName)
    {
        ObservableCollection<MediaFile> tracks = new();
        List<MediaFile> songs = MusicLibrary.GetSongsFromPlaylist(playListName);

        foreach (MediaFile song in songs)
        {
            tracks.Add(song);
        }

        return tracks;
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
                        SelectPlaylistByName(SelectedPlaylist.Name);

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

                Playlist playlist = MusicLibrary.AddNewPlaylist(playlistName);
                SelectedPlaylist = playlist;
                PlaylistTab playlistTab = AddPlaylistTab(playlist);
                _tabControl!.SelectedIndex = TabList.Count - 1;
                SelectPlaylistByName(playlistTab.Name!);

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

    public void RemoveTrack(object sender, RoutedEventArgs e)
    {
        if (_dataGrid is { SelectedItem: null }) return;

        ObservableCollection<MediaFile> tracks = TabList[(int)SelectedPlaylistIndex!].Tracks;

        if (SelectedIndex >= 0 && SelectedIndex < tracks.Count)
        {
            int indexToRemove = SelectedIndex;
            string songId = tracks[SelectedIndex].Id;

            MusicLibrary.RemoveTrackFromPlaylist(SelectedPlaylist!.Name!, songId);

            if (_dataGrid!.SelectedIndex == tracks.Count - 1)
            {
                indexToRemove = SelectedIndex;
                _dataGrid.SelectedIndex = SelectedIndex - 1;
            }

            tracks.RemoveAt(indexToRemove);
        }
    }

    public void RightMouseDown_TabSelect(string tabName)
    {
        int index = TabList.ToList().FindIndex(x => x.Name == tabName);
        _tabControl!.SelectedIndex = index;
    }

    public void ChangeSelectedPlaylistName(string newPlaylistName)
    {
        TabList[SelectedIndex].Name = newPlaylistName;
        SelectedPlaylist!.Name = newPlaylistName;
        SelectedPlaylistTab!.Name = newPlaylistName;
    }

    public MediaFile? GetRunningTrack()
    {
        return SelectedPlaylistTab!.Tracks.FirstOrDefault(x => x.State != PlayerState.Stopped);
    }

    public void SelectPlaylistByName(string name)
    {
        List<Playlist> playlists = MusicLibrary.GetPlaylists();

        if (SelectedPlaylist != null && string.Equals(SelectedPlaylist.Name, name))
        {
            if (_dataGrid != null)
            {
                _dataGrid.SelectedIndex = playlists.FindIndex(x => x.Name == name);
            }
            return;
        }

        for (int index = 0; index < playlists.Count; index++)
        {
            if (playlists[index].Name == name)
            {
                SelectedPlaylist = playlists[index];
                if (_dataGrid != null)
                {
                    _dataGrid.SelectedIndex = index;
                }

                break;
            }
        }
    }

    public void InitializePlaylists()
    {
        MusicLibrary.ClearPlayState();

        string? lastSelectedPlaylistName = Properties.Settings.Default.LastSelectedPlaylistName;

        if (!string.IsNullOrEmpty(lastSelectedPlaylistName))
        {
            SelectedPlaylist = MusicLibrary.GetPlaylists().Find(p => p.Name == lastSelectedPlaylistName);

            if (SelectedPlaylist != null)
            {
                SelectPlaylistByName(lastSelectedPlaylistName);

                SelectedTrack = MusicLibrary.GetSongsFromPlaylist(lastSelectedPlaylistName)
                    .Find(s => s.Id == SelectedPlaylist.SelectedSong);

                if (SelectedTrack != null)
                {
                    SelectTrack(SelectedPlaylist, SelectedTrack);
                }
            }
        }
    }

    public PlaylistTab? GetActiveTab()
    {
        return TabList.ToList().Find(x => x.HasActiveTrack == true);
    }
}