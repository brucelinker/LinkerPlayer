using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Properties;
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
    private static Playlist? _selectedPlaylist;
    [ObservableProperty]
    private static int _selectedPlaylistIndex;
    [ObservableProperty]
    private static int? _activePlaylistIndex;
    [ObservableProperty]
    private static MediaFile? _selectedTrack;
    [ObservableProperty]
    private static int _selectedTrackIndex;
    [ObservableProperty]
    private static int? _activeTrackIndex;
    [ObservableProperty]
    private static PlayerState _state;
    [ObservableProperty]
    private static bool _shuffleMode;
    public static ObservableCollection<PlaylistTab> TabList { get; set; } = new();

    private static TabControl? _tabControl;
    private static DataGrid? _dataGrid;
    private readonly MainWindow _mainWindow;

    private static readonly List<MediaFile?> ShuffleList = new();
    private static int _shuffledIndex;

    private const string SupportedAudioFormats = "(*.mp3; *.flac)|*.mp3; *.flac";
    private const string SupportedPlaylistFormats = "(*.m3u;*.pls;*.wpl;*.zpl)|*.m3u;*.pls;*.wpl;*.zpl";
    const string SupportedExtensions = $"Audio Formats {SupportedAudioFormats}|Playlist Files {SupportedPlaylistFormats}|All files (*.*)|*.*";

    public PlaylistTabsViewModel()
    {
        _mainWindow = (MainWindow?)Application.Current.MainWindow!;

        WeakReferenceMessenger.Default.Register<PlayerControlsStateMessage>(this, (_, m) =>
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
            _dataGrid.SelectedIndex = SelectedTrackIndex;
            _dataGrid.UpdateLayout();
            _dataGrid.ScrollIntoView(_dataGrid.SelectedItem!);
        }
    }

    public void OnTabSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (sender is TabControl tabControl)
        {
            _tabControl = tabControl;

            if (tabControl.SelectedIndex < 0) return;
            SelectedPlaylistIndex = tabControl.SelectedIndex;   

            if (tabControl.SelectedContent is not PlaylistTab playlistTab) return;
            SelectedPlaylistTab = playlistTab;

            SelectedPlaylist = MusicLibrary.GetPlaylistByName(SelectedPlaylistTab.Name!);
            if (SelectedPlaylist == null) return;

            SelectedTrack = MusicLibrary.MainLibrary.First(x => x!.Id == SelectedPlaylist.SelectedSong);

            if (!SelectedPlaylistTab!.Tracks.Any()) return;
            SelectedPlaylistTab!.SelectedIndex = SelectedPlaylistTab.Tracks.ToList().FindIndex(x => x.Id == SelectedTrack!.Id);
            SelectedTrackIndex = (int)SelectedPlaylistTab!.SelectedIndex;

            if (_dataGrid != null)
            {
                _dataGrid.Items.Refresh();
                _dataGrid.UpdateLayout();
                _dataGrid.SelectedIndex = SelectedTrackIndex;
                _dataGrid.SelectedItem = SelectedTrack;
                _dataGrid.ScrollIntoView(_dataGrid.SelectedItem!);
            }

            _shuffleMode = Settings.Default.ShuffleMode;
            ShuffleTracks(_shuffleMode);
        }
    }

    public void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _dataGrid = (sender as DataGrid);

        if (_dataGrid is { SelectedItem: not null })
        {
            SelectedTrack = _dataGrid.SelectedItem as MediaFile;
            SelectedTrackIndex = _dataGrid.SelectedIndex;
            SelectedPlaylistTab!.SelectedTrack = SelectedTrack;
            SelectedPlaylistTab.SelectedIndex = SelectedTrackIndex;

            MusicLibrary.Playlists[SelectedPlaylistIndex]!.SelectedSong = SelectedTrack!.Id;
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
                ActivePlaylistIndex = SelectedPlaylistIndex;
                ActiveTrackIndex = SelectedTrackIndex;
            }
            else
            {
                ActivePlaylistIndex = null;
                ActiveTrackIndex = null;
            }
        }
    }

    public void OnDoubleClickDataGrid()
    {
        if (ActiveTrackIndex != null)
        {
            GetActiveTrackByIndex((int)ActiveTrackIndex)!.State = PlayerState.Stopped;
        }

        if (ActivePlaylistIndex != SelectedPlaylistIndex)
        {
            ActivePlaylistIndex = SelectedPlaylistIndex;
        }

        ActiveTrackIndex = SelectedTrackIndex = _dataGrid!.SelectedIndex;
        MediaFile dataGridSelectedItem = (MediaFile)_dataGrid.SelectedItem;
        dataGridSelectedItem.State = PlayerState.Playing;
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

    public MediaFile? SelectTrack(Playlist playlist, MediaFile track)
    {
        if (SelectedPlaylistTab == null) return null;

        if (!playlist.Name!.Equals(SelectedPlaylistTab!.Name) && _tabControl != null)
        {
            _tabControl.SelectedIndex = TabList.ToList().FindIndex(x => x.Name == playlist.Name);
        }

        SelectedTrack = track;

        SelectedTrackIndex = SelectedPlaylistTab!.Tracks.ToList()
            .FindIndex(x => x.Id.Contains(track.Id));

        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));

        return SelectedTrack;
    }

    public MediaFile SelectFirstTrack()
    {
        SelectedTrackIndex = 0;
        SelectedTrack = SelectedPlaylistTab!.Tracks[SelectedTrackIndex];


        _dataGrid!.SelectedItem = SelectedTrack;
        _dataGrid.SelectedIndex = SelectedTrackIndex;

        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));

        return SelectedTrack;
    }

    public MediaFile? PreviousMediaFile()
    {
        if (SelectedTrack != null && _dataGrid != null)
        {
            int newIndex;
            int oldIndex;
            int tabIndex;

            if (ActivePlaylistIndex != null)
            {
                tabIndex = (int)ActivePlaylistIndex;
                TabList[tabIndex].Tracks[(int)ActiveTrackIndex!].State = PlayerState.Stopped;
                oldIndex = (int)ActiveTrackIndex;
            }
            else
            {
                tabIndex = SelectedPlaylistIndex;
                oldIndex = SelectedTrackIndex;
            }

            if (_shuffleMode)
            {
                newIndex = GetPreviousShuffledIndex();
            }
            else
            {

                if (oldIndex == 0)
                    newIndex = TabList[tabIndex].Tracks.Count - 1;
                else
                    newIndex = oldIndex - 1;
            }

            SelectedTrack = TabList[tabIndex].Tracks[newIndex];
            SelectedTrack.State = PlayerState.Playing;

            _dataGrid.SelectedIndex = newIndex;
            _dataGrid.SelectedItem = SelectedTrack;
            _dataGrid.Items.Refresh();
            _dataGrid.ScrollIntoView(SelectedTrack);

            ActiveTrackIndex = SelectedTrackIndex = newIndex;
            ActivePlaylistIndex = tabIndex;
            TabList[(int)ActivePlaylistIndex].SelectedIndex = SelectedTrackIndex;
        }

        return SelectedTrack;
    }

    public MediaFile? NextMediaFile()
    {
        if (SelectedTrack != null && _dataGrid != null)
        {
            int newIndex;
            int oldIndex;
            int tabIndex;

            if (ActivePlaylistIndex != null)
            {
                tabIndex = (int)ActivePlaylistIndex;
                TabList[tabIndex].Tracks[(int)ActiveTrackIndex!].State = PlayerState.Stopped;
                oldIndex = (int)ActiveTrackIndex;
            }
            else
            {
                tabIndex = SelectedPlaylistIndex;
                oldIndex = SelectedTrackIndex;
            }

            if (_shuffleMode)
            {
                newIndex = GetNextShuffledIndex();
            }
            else
            {
                if (oldIndex == TabList[tabIndex].Tracks.Count - 1)
                    newIndex = 0;
                else
                    newIndex = oldIndex + 1;
            }

            SelectedTrack = TabList[tabIndex].Tracks[newIndex];
            SelectedTrack.State = PlayerState.Playing;

            _dataGrid.SelectedIndex = newIndex;
            _dataGrid.SelectedItem = SelectedTrack;
            _dataGrid.Items.Refresh();
            _dataGrid.ScrollIntoView(SelectedTrack);

            ActiveTrackIndex = SelectedTrackIndex = newIndex;
            ActivePlaylistIndex = tabIndex;
            TabList[(int)ActivePlaylistIndex].SelectedIndex = SelectedTrackIndex;
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
        PlaylistTab tab = new PlaylistTab
        {
            Name = p.Name ?? "Bupkis",
            Tracks = LoadPlaylistTracks(p.Name)
        };

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

            MediaFile? runningTrack = GetActiveTrack();

            if (runningTrack != null && ShuffleList.Any())
            {
                _shuffledIndex = ShuffleList.FindIndex(x => x!.Id == runningTrack.Id);
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

        int index = ActivePlaylistIndex ?? SelectedPlaylistIndex;
        int newIndex = TabList[index].Tracks.ToList()
            .FindIndex(x => x.Id.Contains(ShuffleList[_shuffledIndex]!.Id));

        return newIndex;
    }

    private int GetPreviousShuffledIndex()
    {
        if (_shuffledIndex == 0)
            _shuffledIndex = ShuffleList.Count - 1;
        else
            _shuffledIndex -= 1;

        int index = ActivePlaylistIndex ?? SelectedPlaylistIndex;
        int newIndex = TabList[index].Tracks.ToList()
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

        ObservableCollection<MediaFile> tracks = TabList[SelectedPlaylistIndex].Tracks;

        if (SelectedTrackIndex >= 0 && SelectedTrackIndex < tracks.Count)
        {
            int indexToRemove = SelectedTrackIndex;
            string songId = tracks[SelectedTrackIndex].Id;

            MusicLibrary.RemoveTrackFromPlaylist(SelectedPlaylist!.Name!, songId);

            if (_dataGrid!.SelectedIndex == tracks.Count - 1)
            {
                indexToRemove = SelectedTrackIndex;
                _dataGrid.SelectedIndex = SelectedTrackIndex - 1;
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
        TabList[SelectedTrackIndex].Name = newPlaylistName;
        SelectedPlaylist!.Name = newPlaylistName;
        SelectedPlaylistTab!.Name = newPlaylistName;
    }

    public MediaFile? GetActiveTrackByIndex(int index)
    {
        if (ActivePlaylistIndex == null) { return null; }
        return TabList[(int)ActivePlaylistIndex].Tracks[index];
    }

    public MediaFile? GetActiveTrack()
    {
        if (ActivePlaylistIndex == null) { return null; }
        return TabList[(int)ActivePlaylistIndex].Tracks.FirstOrDefault(x => x.State != PlayerState.Stopped);
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
        ActivePlaylistIndex = null;
        ActiveTrackIndex = null;

        string? lastSelectedPlaylistName = Settings.Default.LastSelectedPlaylistName;

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
}