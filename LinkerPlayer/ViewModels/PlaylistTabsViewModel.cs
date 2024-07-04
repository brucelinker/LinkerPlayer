using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Properties;
using LinkerPlayer.Windows;
using Microsoft.Win32;
using NAudio.Wave;
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

namespace LinkerPlayer.ViewModels;

public partial class PlaylistTabsViewModel : BaseViewModel
{
    [ObservableProperty]
    private static PlaylistTab? _selectedTab;
    [ObservableProperty]
    private static PlaylistTab? _activeTab;
    [ObservableProperty]
    private static Playlist? _selectedPlaylist;
    [ObservableProperty]
    private static PlaybackState _state;
    [ObservableProperty]
    private static bool _shuffleMode;
    public static ObservableCollection<PlaylistTab> TabList { get; set; } = new();

    public static PlaylistTabsViewModel Instance { get; } = new();

    private static TabControl? _tabControl;
    private static DataGrid? _dataGrid;
    private readonly MainWindow _mainWindow;

    private static List<MediaFile> _tracksView = new();
    private static readonly List<MediaFile> ShuffleList = new();
    private static int _shuffledIndex;

    private const string SupportedAudioFormats = "(*.mp3; *.flac)|*.mp3; *.flac";
    private const string SupportedPlaylistFormats = "(*.m3u;*.pls;*.wpl;*.zpl)|*.m3u;*.pls;*.wpl;*.zpl";
    const string SupportedExtensions = $"Audio Formats {SupportedAudioFormats}|Playlist Files {SupportedPlaylistFormats}|All files (*.*)|*.*";
    private static int _count;

    public PlaylistTabsViewModel()
    {
        Log.Information($"PLAYLISTTABSVIEWMODEL - {++_count}");
        _mainWindow = (MainWindow?)Application.Current.MainWindow!;

        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (_, m) =>
        {
            OnPlaybackStateChanged(m.Value);
        });

        WeakReferenceMessenger.Default.Register<ShuffleModeMessage>(this, (_, m) =>
        {
            OnShuffleChanged(m.Value);
        });
    }

    public void OnDataGridLoaded(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is DataGrid dataGrid)
        {
            _dataGrid = dataGrid;

            if (dataGrid.Items.Count > 0)
            {
                //_dataGrid.SelectedIndex = SelectedTrackIndex;

                //_dataGrid.Items.Refresh();
                //_dataGrid.UpdateLayout();
                //_dataGrid.ScrollIntoView(_dataGrid.SelectedItem!);

                _tracksView = dataGrid.Items.Cast<MediaFile>().ToList();

                _shuffleMode = Settings.Default.ShuffleMode;
                ShuffleTracks(_shuffleMode);

                _dataGrid.SelectedItem = SelectedTrack;
            }
        }
    }

    public void OnTabSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        /*
         * While a track is playing, the current tab will be the ActiveTab. We want to be able to select
         * a different tab to view the list and select a track, but it will not be queued up or play after
         * the track in the ActiveTab has finish. The ActiveTab will simply play the next track in the list.
         *
         * You can play a song in a tab that is not the ActiveTab by double clicking the track. This will
         * change the current tab to be the new ActiveTab.
         */

        if (_tabControl == null && sender is TabControl tabControl)
        {
            _tabControl = tabControl;
        }

        // Need to use ActivePlaylist to know which tab to play from
        // If ActivePlaylist is null, then we need to reinitialize _tracksView
        if (_tabControl!.SelectedContent is not PlaylistTab playlistTab || SelectedTab == playlistTab) return;

        SelectedTab = playlistTab;

        if (_tabControl.SelectedIndex < 0 || (SelectedPlaylistIndex == _tabControl.SelectedIndex && _tracksView.Any())) return;

        SelectedPlaylistIndex = _tabControl.SelectedIndex;

        SelectedPlaylist = MusicLibrary.GetPlaylistByName(SelectedTab.Name!);
        if (SelectedPlaylist == null) return;

        SelectedTrack = MusicLibrary.MainLibrary.FirstOrDefault(x => x!.Id == SelectedPlaylist.SelectedSong);
        if (SelectedTrack == null) return;

        if (_dataGrid != null)
        {

            if (ActivePlaylistIndex == null && _dataGrid.Items.Count > 0)
            {
                _tracksView = _dataGrid.Items.Cast<MediaFile>().ToList();
                SelectedTab!.SelectedIndex = _tracksView.FindIndex(x => x.Id == SelectedTrack!.Id);
                SelectedTrackIndex = (int)SelectedTab!.SelectedIndex;

                if (ActiveTrackIndex == null)
                {
                    if (ShuffleList.Any())
                    {
                        ShuffleList.Clear();
                    }

                    _shuffleMode = Settings.Default.ShuffleMode;
                    ShuffleTracks(_shuffleMode);
                }
            }

            if(ActiveTrack != null && ActivePlaylistIndex == SelectedPlaylistIndex)
            {
                _dataGrid.SelectedItem = (object)ActiveTrack;
            }
            else
            {
                _dataGrid.SelectedItem = (object)SelectedTrack;
            }
            _dataGrid.Items.Refresh();
            _dataGrid.UpdateLayout();
            _dataGrid.ScrollIntoView(_dataGrid.SelectedItem!);

            Log.Information($"OnTabSelectionChanged: DataGrid.SelectedIndex: {_dataGrid!.SelectedIndex}");
            Log.Information($"OnTabSelectionChanged: DataGrid.SelectedItem: {(_dataGrid.SelectedItem! as MediaFile)!.FileName}");
        }
    }

    public void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _dataGrid = (sender as DataGrid);

        if (_dataGrid is { SelectedItem: not null })
        {
            SelectedTrack = _dataGrid.SelectedItem as MediaFile;
            SelectedTrackIndex = _dataGrid.SelectedIndex;
            SelectedTab!.SelectedTrack = SelectedTrack;
            SelectedTab.SelectedIndex = SelectedTrackIndex;

            MusicLibrary.Playlists[SelectedPlaylistIndex]!.SelectedSong = SelectedTrack!.Id;

            Log.Information($"OnTrackSelectionChanged: SelectedTrackIndex: {SelectedTrackIndex}");
            Log.Information($"OnTrackSelectionChanged: SelectedTrack: {SelectedTrack!.FileName}");

            _dataGrid.ScrollIntoView(SelectedTrack!);
        }

        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(ActiveTrack ?? SelectedTrack));
    }

    public void OnDataGridSorted(object sender, DataGridSortingEventArgs e)
    {
        SelectedTab!.SelectedIndex =
            _tracksView.FindIndex(x => x.Id == SelectedTrack!.Id);
        SelectedTrackIndex = (int)SelectedTab!.SelectedIndex;

        if (_dataGrid is { Items.Count: > 0 })
        {
            _dataGrid.Items.Refresh();
            _dataGrid.UpdateLayout();
            _dataGrid.ScrollIntoView(_dataGrid.SelectedItem!);

            SelectedTrackIndex = _dataGrid.SelectedIndex;
            SelectedTrack = _dataGrid.SelectedItem as MediaFile;

            ActiveTrackIndex = ActiveTrackIndex != null ? _dataGrid.SelectedIndex : null;
        }

        if (ShuffleList.Any())
        {
            ShuffleList.Clear();
        }

        _shuffleMode = Settings.Default.ShuffleMode;
        ShuffleTracks(_shuffleMode);
    }

    public void OnPlaybackStateChanged(PlaybackState state)
    {
        State = state;
        if (SelectedTrack != null)
        {
            SelectedTrack.State = state;

            if (ActivePlaylistIndex == null || ActivePlaylistIndex == SelectedPlaylistIndex)
            {
                if (state != PlaybackState.Stopped)
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
    }

    public void OnDoubleClickDataGrid()
    {
        if (ActiveTrackIndex != null)
        {
            GetActiveTrackByIndex((int)ActiveTrackIndex)!.State = PlaybackState.Stopped;
        }

        if (ActivePlaylistIndex != SelectedPlaylistIndex)
        {
            ActivePlaylistIndex = SelectedPlaylistIndex;
        }

        ActiveTrackIndex = SelectedTrackIndex = _dataGrid!.SelectedIndex;
        MediaFile dataGridSelectedItem = (MediaFile)_dataGrid.SelectedItem;
        dataGridSelectedItem.State = PlaybackState.Playing;
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
        OpenFileDialog openFileDialog = new()
        {
            Filter = SupportedPlaylistFormats,
            Multiselect = false,
            Title = "Select file(s)"
        };

        bool? result = openFileDialog.ShowDialog();

        if (result ?? false) return;

        LoadPlaylistFile(openFileDialog.FileName);
    }

    public void AddFolder(object sender, RoutedEventArgs routedEventArgs)
    {
        string str = Environment.SpecialFolder.CommonMusic.ToString();
        OpenFolderDialog folderDialog = new();

        bool? result = folderDialog.ShowDialog();

        if (result is null or false) return;

        string selectedFolderPath = folderDialog.FolderName;
        DirectoryInfo dirInfo = new(selectedFolderPath);
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
        OpenFolderDialog folderDialog = new()
        {
            FolderName = Environment.SpecialFolder.MyMusic.ToString()
        };

        bool? result = folderDialog.ShowDialog();

        if (result ?? false) return;

        string selectedFolderPath = folderDialog.FolderName;
        DirectoryInfo dirInfo = new(selectedFolderPath);
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

        Stopwatch timer = new();
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
        OpenFileDialog openFileDialog = new()
        {
            Filter = SupportedPlaylistFormats,
            Multiselect = false,
            Title = "Select file(s)"
        };

        bool? result = openFileDialog.ShowDialog();

        if (result is null or false) return;

        foreach (string fileName in openFileDialog.FileNames)
        {
            string extension = Path.GetExtension(fileName);

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
        if (SelectedTab == null) return null;

        if (!playlist.Name!.Equals(SelectedTab!.Name) && _tabControl != null)
        {
            _tabControl.SelectedIndex = TabList.ToList().FindIndex(x => x.Name == playlist.Name);
        }

        SelectedTrack = track;

        SelectedTrackIndex = _tracksView.FindIndex(x => x.Id.Contains(track.Id));

        MediaFile musicFile = ActiveTrack ?? SelectedTrack;
        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(musicFile));

        return musicFile;
    }

    public MediaFile SelectFirstTrack()
    {
        SelectedTrackIndex = 0;
        SelectedTrack = _tracksView[SelectedTrackIndex];


        _dataGrid!.SelectedItem = SelectedTrack;
        _dataGrid.SelectedIndex = SelectedTrackIndex;

        _dataGrid.Items.Refresh();
        _dataGrid.UpdateLayout();
        _dataGrid.ScrollIntoView(_dataGrid.SelectedItem!);

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

                if (ActiveTrack != null)
                {
                    ActiveTrack.State = PlaybackState.Stopped;
                }

                oldIndex = ActiveTrackIndex ?? 0;
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
                    newIndex = _tracksView.Count - 1;
                else
                    newIndex = oldIndex - 1;
            }

            if (SelectedPlaylistIndex == ActivePlaylistIndex)
            {
                SelectedTrack = _tracksView[newIndex];
                SelectedTrackIndex = newIndex;
            }

            ActiveTrack = _tracksView[newIndex];
            ActiveTrack.State = PlaybackState.Playing;
            ActiveTrackIndex = newIndex;
            ActivePlaylistIndex = tabIndex;
            TabList[(int)ActivePlaylistIndex].SelectedIndex = newIndex;

            if (ActivePlaylistIndex == _tabControl!.SelectedIndex)
            {
                _dataGrid.SelectedIndex = newIndex;
                _dataGrid.ScrollIntoView(_dataGrid.SelectedItem!);
            }
        }

        return ActiveTrack;
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

                if (ActiveTrack != null)
                {
                    ActiveTrack.State = PlaybackState.Stopped;
                }

                oldIndex = ActiveTrackIndex ?? 0;
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
                if (oldIndex == _tracksView.Count - 1)
                    newIndex = 0;
                else
                    newIndex = oldIndex + 1;
            }

            if (SelectedPlaylistIndex == ActivePlaylistIndex)
            {
                SelectedTrack = _tracksView[newIndex];
                SelectedTrackIndex = newIndex;
            }

            ActiveTrack = _tracksView[newIndex];
            ActiveTrack.State = PlaybackState.Playing;
            ActiveTrackIndex = newIndex;
            ActivePlaylistIndex = tabIndex;
            TabList[(int)ActivePlaylistIndex].SelectedIndex = newIndex;

            if (ActivePlaylistIndex == _tabControl!.SelectedIndex)
            {
                _dataGrid.SelectedItem = ActiveTrack;
                _dataGrid.ScrollIntoView(ActiveTrack);
            }
        }

        return ActiveTrack;
    }

    public void SetupSelectedPlaylist()
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

    public void LoadPlaylistTabs()
    {
        List<Playlist> playlists = MusicLibrary.GetPlaylists();

        foreach (Playlist p in playlists)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;

            PlaylistTab tab = AddPlaylistTab(p);

            Log.Information($"LoadPlaylistTabs - added PlaylistTab {tab}");
        }
    }

    public PlaylistTab AddPlaylistTab(Playlist p)
    {
        PlaylistTab tab = new()
        {
            Name = p.Name ?? "Bupkis",
            Tracks = LoadPlaylistTracks(p.Name)
        };

        TabList.Add(tab);

        return tab;
    }

    public void AddSongToPlaylistTab(MediaFile song, string playlistName)
    {
        foreach (PlaylistTab tab in TabList)
        {
            if (tab.Name == playlistName)
            {
                tab.Tracks.Add(song);
            }
        }
    }

    private void OnShuffleChanged(bool shuffle)
    {
        ShuffleTracks(shuffle);
    }

    public void ShuffleTracks(bool shuffleMode)
    {
        _shuffleMode = shuffleMode;

        if (shuffleMode)
        {
            //if (SelectedTab == null || !SelectedTab.Tracks.Any())
            if (!_tracksView.Any())
            {
                return;
            }

            List<MediaFile> tempList = _tracksView.ToList();

            Random random = new();

            while (tempList.Count > 0)
            {
                int index = random.Next(0, tempList.Count - 1);

                ShuffleList.Add(tempList[index]);
                tempList.RemoveAt(index);
            }

            //MediaFile? runningTrack = GetActiveTrack();

            if (ActiveTrack != null && ShuffleList.Any())
            {
                _shuffledIndex = ShuffleList.FindIndex(x => x.Id == ActiveTrack.Id);

                if (_shuffledIndex < 0)
                {
                    _shuffledIndex = SelectedTrackIndex;
                }
                else
                {
                    SelectedTrackIndex = _shuffledIndex;
                }
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

        int newIndex = _tracksView.FindIndex(x => x.Id.Contains(ShuffleList[_shuffledIndex].Id));

        Log.Information($"GetNextShuffledIndex: {newIndex}");

        return newIndex;
    }

    private int GetPreviousShuffledIndex()
    {
        if (_shuffledIndex == 0)
            _shuffledIndex = ShuffleList.Count - 1;
        else
            _shuffledIndex -= 1;

        int newIndex = _tracksView.FindIndex(x => x.Id.Contains(ShuffleList[_shuffledIndex].Id));

        Log.Information($"GetPreviousShuffledIndex: {newIndex}");

        return newIndex;
    }

    private ObservableCollection<MediaFile> LoadPlaylistTracks(string? playListName)
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
            MediaFile song = new(fileName);

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

                M3uContent content = new();
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
                    MediaFile mediaFile = new(mediaFilePath);

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
        TabList[SelectedPlaylistIndex].Name = newPlaylistName;
        SelectedPlaylist!.Name = newPlaylistName;
        SelectedTab!.Name = newPlaylistName;
    }

    public MediaFile? GetActiveTrackByIndex(int index)
    {
        if (ActivePlaylistIndex == null) { return null; }
        return TabList[(int)ActivePlaylistIndex].Tracks[index];
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
}