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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LinkerPlayer.ViewModels;

public partial class PlaylistTabsViewModel : BaseViewModel
{
    [ObservableProperty] private static PlaylistTab? _selectedTab;
    [ObservableProperty] private static PlaylistTab? _activeTab;
    [ObservableProperty] private static int? _selectedTabIndex;
    [ObservableProperty] private static Playlist? _selectedPlaylist;
    [ObservableProperty] private static PlaybackState _state;
    [ObservableProperty] private static bool _shuffleMode;
    [ObservableProperty] private ObservableCollection<PlaylistTab> _tabList = new();

    public static PlaylistTabsViewModel Instance { get; } = new();

    private static TabControl? _tabControl;
    private static DataGrid? _dataGrid;
    private readonly MainWindow _mainWindow;

    //private static List<MediaFile> _tracksView = new();
    private static readonly List<MediaFile> ShuffleList = new();
    private static int _shuffledIndex;

    private readonly string[] _supportedAudioExtensions = [".mp3", ".flac", ".wav"];

    private const string SupportedAudioFilter = "(*.mp3; *.flac)|*.mp3; *.flac";
    private const string SupportedPlaylistFilter = "(*.m3u;*.pls;*.wpl;*.zpl)|*.m3u;*.pls;*.wpl;*.zpl";
    const string SupportedFilters = $"Audio Formats {SupportedAudioFilter}|Playlist Files {SupportedPlaylistFilter}|All files (*.*)|*.*";
    private static int _count;

    public PlaylistTabsViewModel()
    {
        Log.Information($"PLAYLISTTABSVIEWMODEL - {++_count}");
        _mainWindow = (MainWindow?)Application.Current.MainWindow!;

        _shuffleMode = Settings.Default.ShuffleMode;

        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (_, m) =>
        {
            OnPlaybackStateChanged(m.Value);
        });

        WeakReferenceMessenger.Default.Register<ShuffleModeMessage>(this, (_, m) =>
        {
            OnShuffleChanged(m.Value);
        });

        WeakReferenceMessenger.Default.Register<MainWindowLoadedMessage>(this, (_, m) =>
        {
            OnMainWindowLoaded(m.Value);
        });
    }

    public void OnDataGridLoaded(object sender, RoutedEventArgs _)
    {
        if (sender is DataGrid dataGrid)
        {
            _dataGrid = dataGrid;

            if (dataGrid.Items.Count > 0)
            {
                SetupSelectedPlaylist();
                ShuffleTracks(_shuffleMode);

                _dataGrid.SelectedItem = SelectedTrack;
            }
        }
    }

    public void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tabControl == null && sender is TabControl tabControl)
        {
            _tabControl = tabControl;
        }

        SelectedTab = (sender as TabControl)?.SelectedItem as PlaylistTab;

        if (_dataGrid == null) return;
        _dataGrid.ItemsSource = SelectedTab!.Tracks;

        //if (SelectedTab != null && SelectedTab.Tracks.Any())
        //{
        //    _dataGrid.ItemsSource = SelectedTab.Tracks;
        //}
        //else
        //{
        //    // Optionally, you can subscribe to an event or use a timer to check again when tracks are loaded
        //    SelectedTab!.PropertyChanged += (_, args) =>
        //    {
        //        if (args.PropertyName == nameof(PlaylistTab.Tracks))
        //        {
        //            _dataGrid.ItemsSource = SelectedTab.Tracks;
        //            SelectedTab.PropertyChanged -= null; // Unsubscribe after setting ItemsSource to prevent multiple bindings
        //        }
        //    };
        //}





        //        if (_dataGrid?.ItemsSource == null) return;

        //        /*
        //         * The ActiveTab is the tab that contains the ActiveTrack - or the track that is playing. We want to be able to select
        //         * a different tab to view the list and select a track, but it will not be queued up or play after
        //         * the track in the ActiveTab has finished. The ActiveTab will simply play the next track in the list.
        //         *
        //         * You can play a song in a tab that is not the ActiveTab by double-clicking the track. This will
        //         * change the current tab to be the new ActiveTab.
        //         */

        //        /*
        //         * SelectedTab = the currently selected tab
        //         * ActiveTab = the tab where the currently ActiveTrack resides
        //         * ActiveTrack = the track that is currently Playing or Paused
        //         * NOTE: If there is no track playing or paused, the ActiveTab AND ActiveTrack should be null
        //         *
        //         * 1. If the ActiveTab equals the SelectedTab, then we need to:
        //         *  - Select the track
        //         *  - Scroll to view
        //         *  - return
        //         *
        //         * 2. If ActiveTab is null (ActiveTrack should be null as well)
        //         *  - Initialize _tracksView
        //         *  - Select the SelectedTrack for that tab.
        //         *
        //         * 3. If ActiveTab is not null and SelectedTab does not equal the ActiveTab
        //         *  - If SelectedTrackIndex is 0 or greater, then scroll to view
        //         *  - If SelectedTrackIndex is -1, then return
        //         *  - NOTE: If a track is double-clicked in a non-Active tab, then the SelectedTab becomes the
        //         *    new ActiveTab and the SelectedTrack becomes the new ActiveTrack. Then the _viewTrack should
        //         *    be initialized.
        //         *
        //         * NOTE: If there is an ActiveTab, should we passively disable the non-Active tabs? This would
        //         * mean the user can look at the tabs, but cannot select or play from those tabs. Once the Stop
        //         * button is clicked and no track is active, it "enables" all tabs.
        //         *
        //         */


        //        // Need to use ActivePlaylist to know which tab to play from
        //        // If ActivePlaylist is null, then we need to reinitialize _tracksView

        //        if (_tabControl?.SelectedContent is not PlaylistTab playlistTab || SelectedTab == playlistTab) return;
        //        SelectedTab = playlistTab;

        //        if (_tabControl.SelectedIndex < 0) return;

        if (_tabControl != null) SelectedPlaylistIndex = _tabControl.SelectedIndex;

        SelectedPlaylist = MusicLibrary.GetPlaylistByName(SelectedTab.Name!);
        if (SelectedPlaylist == null) return;

        if (SelectedTab == ActiveTab && ActiveTrack != null)
        {
            SelectedTrack = ActiveTrack;
        }
        else
        {
            SelectedTrack = MusicLibrary.MainLibrary.FirstOrDefault(x => x!.Id == SelectedPlaylist.SelectedTrack);
        }

        Settings.Default.LastSelectedTrackId = SelectedTrack != null ? SelectedTrack.Id : "";

        Log.Information("OnTabSelectionChanged");

        //        if (SelectedTrack == null) return;

        //        if (_dataGrid != null)
        //        {
        //            if (ActivePlaylistIndex == null && _dataGrid.Items.Count > 0)
        //            {
        //                //_tracksView = _dataGrid.Items.Cast<MediaFile>().ToList();
        //                SelectedTab!.SelectedIndex = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()
        //                    .FindIndex(x => x.Id == SelectedTrack!.Id);
        //                SelectedTrackIndex = (int)SelectedTab!.SelectedIndex;

        //                if (ActiveTrackIndex == null)
        //                {
        //                    if (ShuffleList.Any())
        //                    {
        //                        ShuffleList.Clear();
        //                    }

        //                    _shuffleMode = Settings.Default.ShuffleMode;
        //                    ShuffleTracks(_shuffleMode);
        //                }
        //            }

        //            MediaFile item;
        //            if(ActiveTrack != null && ActivePlaylistIndex == SelectedPlaylistIndex)
        //            {
        //                item = ActiveTrack;
        //            }
        //            else
        //            {
        //                item = SelectedTrack;
        //            }

        //_dataGrid.Items.Refresh();
        //_dataGrid.UpdateLayout();
        ////            List<MediaFile> mediaFiles = _dataGrid.Items.Cast<MediaFile>().ToList();
        //            SelectedTrackIndex = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()
        //                .FindIndex(x => x.FileName == item!.FileName);
        //            _dataGrid.SelectedIndex = SelectedTrackIndex;
        //        }
    }

    public void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs _)
    {
        _dataGrid = (sender as DataGrid);

        if (_dataGrid is { SelectedItem: not null })
        {

            SelectedTrack = _dataGrid.SelectedItem as MediaFile;
            SelectedTrackIndex = _dataGrid.SelectedIndex;

            if (SelectedTab == null) return;
            SelectedTab.SelectedTrack = SelectedTrack;
            SelectedTab.SelectedIndex = SelectedTrackIndex;

            Settings.Default.LastSelectedTrackId = SelectedTrack != null ? SelectedTrack.Id : "";

            // Make note of the current SelectedTrack for this tab
            if (MusicLibrary.Playlists.Count > 0)
            {
                MusicLibrary.Playlists[SelectedPlaylistIndex]!.SelectedTrack = SelectedTrack!.Id;

                Log.Information($"OnTrackSelectionChanged: SelectedTrackIndex: {SelectedTrackIndex}");
                Log.Information($"OnTrackSelectionChanged: SelectedTrack: {SelectedTrack!.FileName}");

                _dataGrid.ScrollIntoView(SelectedTrack);
            }
            //WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(ActiveTrack ?? SelectedTrack));
        }
        Log.Information("OnTrackSelectionChanged");
    }

    public void OnDataGridSorted(string propertyName, ListSortDirection direction)
    {
        if (_dataGrid is null) return;

        List<MediaFile> sortedList = new(TabList[SelectedPlaylistIndex].Tracks);

        sortedList.Sort((x, y) =>
        {
            object? propX = x.GetType().GetProperty(propertyName)!.GetValue(x);
            object? propY = y.GetType().GetProperty(propertyName)!.GetValue(y);

            if (propX == null && propY == null) return 0;
            if (propX == null) return direction == ListSortDirection.Ascending ? -1 : 1;
            if (propY == null) return direction == ListSortDirection.Ascending ? 1 : -1;

            return Comparer.Default.Compare(propX, propY) * (direction == ListSortDirection.Ascending ? 1 : -1);
        });

        MediaFile saveSelectedItem = (MediaFile)_dataGrid.SelectedItem;

        // Clear the current collection and add sorted items back
        TabList[SelectedPlaylistIndex].Tracks.Clear();
        foreach (MediaFile song in sortedList)
        {
            TabList[SelectedPlaylistIndex].Tracks.Add(song);
        }

        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _dataGrid.ItemsSource = null;
            _dataGrid.ItemsSource = TabList[SelectedPlaylistIndex].Tracks;
            _dataGrid.SelectedIndex = TabList[SelectedPlaylistIndex].Tracks.IndexOf(saveSelectedItem);
        }));

        if (_dataGrid is { Items.Count: > 0 })
        {
            _dataGrid.Items.Refresh();
            _dataGrid.UpdateLayout();
            _dataGrid.ScrollIntoView(_dataGrid.SelectedIndex);

            SelectedTrackIndex = _dataGrid.SelectedIndex;
            SelectedTrack = _dataGrid.SelectedItem as MediaFile;

            Settings.Default.LastSelectedTrackId = SelectedTrack != null ? SelectedTrack.Id : SelectFirstTrack().Id;

            ActiveTrackIndex = ActiveTrackIndex != null ? _dataGrid.SelectedIndex : null;
        }

        if (ShuffleList.Any())
        {
            ShuffleList.Clear();
        }

        _shuffleMode = Settings.Default.ShuffleMode;
        ShuffleTracks(_shuffleMode);
    }

    private void OnPlaybackStateChanged(PlaybackState state)
    {
        State = state;

        if (ActiveTab != null && ActiveTab != SelectedTab) return;

        if (SelectedTrack != null)
        {
            SelectedTrack.State = state;

            if (ActivePlaylistIndex == null || ActivePlaylistIndex == SelectedPlaylistIndex)
            {
                if (state != PlaybackState.Stopped)
                {
                    ActivePlaylistIndex = SelectedPlaylistIndex;
                    ActiveTrackIndex = SelectedTrackIndex;
                    ActiveTab = SelectedTab;
                }
                else
                {
                    ActivePlaylistIndex = null;
                    ActiveTrackIndex = null;
                    ActiveTab = null;
                }
            }
        }
    }

    public void OnDoubleClickDataGrid()
    {
        if (ActiveTrackIndex is >= 0)
        {
            GetActiveTrackByIndex((int)ActiveTrackIndex)!.State = PlaybackState.Stopped;
        }

        if (ActivePlaylistIndex != SelectedPlaylistIndex)
        {
            ActivePlaylistIndex = SelectedPlaylistIndex;
            ActiveTrackIndex = SelectedTrackIndex;
            ActiveTab = SelectedTab;
        }

        ActiveTrackIndex = SelectedTrackIndex = _dataGrid!.SelectedIndex;
        MediaFile dataGridSelectedItem = (MediaFile)_dataGrid.SelectedItem;
        dataGridSelectedItem.State = PlaybackState.Playing;

        Settings.Default.LastSelectedTrackId = SelectedTrack != null ? SelectedTrack.Id : "";
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
            Filter = SupportedPlaylistFilter,
            Multiselect = false,
            Title = "Select file(s)"
        };

        bool? result = openFileDialog.ShowDialog();

        if (result == false) return;

        LoadPlaylistFile(openFileDialog.FileName);
    }

    public void AddFolder()
    {
        OpenFolderDialog folderDialog = new();

        bool? result = folderDialog.ShowDialog();

        if (result is null or false) return;

        string selectedFolderPath = folderDialog.FolderName;
        DirectoryInfo dirInfo = new(selectedFolderPath);
        //List<FileInfo> files = dirInfo.GetFiles("*.*", SearchOption.AllDirectories)
        //    .Where(file => _supportedAudioExtensions.Any(ext => file.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToList();

        IEnumerable<FileInfo> files = dirInfo.GetFiles("*.*", SearchOption.AllDirectories)
            .Where(file => _supportedAudioExtensions.Any(ext => file.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase))).ToList();

        //List<FileInfo> files = dirInfo.GetFiles("*.*", SearchOption.AllDirectories).ToList();

        //List<FileInfo> mp3Files = files.Where(f => f.Name.EndsWith("mp3", StringComparison.OrdinalIgnoreCase)).ToList();
        //List<FileInfo> flacFiles = files.Where(f => f.Name.EndsWith("flac", StringComparison.OrdinalIgnoreCase)).ToList();

        // ChatGPT
        //List<FileInfo> files = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
        //    .Where(file => SupportedAudioExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToList();

        if (!files.Any())
        {
            _mainWindow.InfoSnackbar.MessageQueue?.Clear();
            _mainWindow.InfoSnackbar.MessageQueue?.Enqueue($"No files were found in {selectedFolderPath}.", null, null, null,
                false, true, TimeSpan.FromSeconds(3));
        }

        foreach (FileInfo? file in files)
        {
            LoadAudioFile(file.FullName, SelectedPlaylist!.Name);
            //if (track == null) continue;
            //MusicLibrary.AddTrackToPlaylist(track.Id, SelectedPlaylist!.Name);
            //SelectedTab!.Tracks.Add(track);
        }

        _dataGrid!.ItemsSource = SelectedTab!.Tracks;
        MusicLibrary.SaveToJson();
    }

    public void NewPlaylistFromFolder()
    {
        OpenFolderDialog folderDialog = new()
        {
            FolderName = Environment.SpecialFolder.MyMusic.ToString()
        };

        bool? result = folderDialog.ShowDialog();

        if (result is null or false) return;

        string selectedFolderPath = folderDialog.FolderName;
        DirectoryInfo dirInfo = new(selectedFolderPath);
        List<FileInfo> files = dirInfo.GetFiles("*.*", SearchOption.AllDirectories)
            .Where(file => _supportedAudioExtensions.Any(ext => file.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToList();

        if (!files.Any())
        {
            _mainWindow.InfoSnackbar.MessageQueue?.Clear();
            _mainWindow.InfoSnackbar.MessageQueue?.Enqueue($"No files were found in {selectedFolderPath}.", null, null, null,
                false, true, TimeSpan.FromSeconds(3));
        }

        string playlistName = dirInfo.Name;

        Playlist playlist = MusicLibrary.AddNewPlaylist(playlistName);
        PlaylistTab playlistTab = AddPlaylistTab(playlist);
        SelectedTab = playlistTab;
        _tabControl!.SelectedIndex = _tabControl.Items.IndexOf(playlistTab);

        Stopwatch timer = new();
        timer.Reset();
        timer.Start();

        foreach (FileInfo? file in files)
        {
            LoadAudioFile(file.FullName, playlistName);
        }

        _dataGrid!.ItemsSource = SelectedTab!.Tracks;

        MusicLibrary.SaveToJson();
        
        timer.Stop();
        Log.Information($"{playlistName} playlist took {timer.Elapsed.TotalSeconds} seconds to load.");
    }

    public void AddFiles()
    {
        OpenFileDialog openFileDialog = new()
        {
            Filter = SupportedFilters,
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
                LoadAudioFile(fileName, SelectedPlaylist!.Name);
            }
        }

        _dataGrid!.ItemsSource = SelectedTab!.Tracks;

        MusicLibrary.SaveToJson();
    }

    public void RemovePlaylist(object sender)
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

    private void SelectTrack(Playlist playlist, MediaFile track)
    {
        if (_dataGrid == null || _dataGrid.ItemsSource == null) return;

        if (!playlist.Name.Equals(SelectedTab!.Name) && _tabControl != null)
        {
            _tabControl.SelectedIndex = TabList.ToList().FindIndex(x => x.Name == playlist.Name);
        }

        SelectedTrack = track;

        SelectedTrackIndex = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()
            .FindIndex(x => x.Id.Contains(track.Id));

        Settings.Default.LastSelectedTrackId = SelectedTrack != null ? SelectedTrack.Id : "";

        MediaFile musicFile = (ActiveTrack ?? SelectedTrack)!;
        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(musicFile));
    }

    public MediaFile SelectFirstTrack()
    {
        SelectedTrackIndex = 0;
        SelectedTrack = TabList[SelectedPlaylistIndex].Tracks[SelectedTrackIndex];

        Settings.Default.LastSelectedTrackId = SelectedTrack != null ? SelectedTrack.Id : "";

        _dataGrid!.SelectedItem = SelectedTrack;
        _dataGrid.SelectedIndex = SelectedTrackIndex;

        _dataGrid.Items.Refresh();
        _dataGrid.UpdateLayout();
        _dataGrid.ScrollIntoView(_dataGrid.SelectedItem!);

        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));

        return SelectedTrack!;
    }

    public MediaFile? PreviousMediaFile()
    {
        if (SelectedTrack != null && _dataGrid != null)
        {
            //if (!_tracksView.Any())
            //{
            //    _tracksView = _dataGrid.Items.Cast<MediaFile>().ToList();
            //}

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
                    newIndex = _dataGrid.ItemsSource.Cast<MediaFile>().ToList().Count - 1;
                else
                    newIndex = oldIndex - 1;
            }

            if (SelectedPlaylistIndex == ActivePlaylistIndex)
            {
                SelectedTrack = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()[newIndex];
                SelectedTrackIndex = newIndex;

                Settings.Default.LastSelectedTrackId = SelectedTrack != null ? SelectedTrack.Id : "";
            }

            ActiveTrack = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()[newIndex];
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
            //if (!_tracksView.Any())
            //{
            //    _tracksView = _dataGrid.Items.Cast<MediaFile>().ToList();
            //}

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
                if (oldIndex == _dataGrid.ItemsSource.Cast<MediaFile>().ToList().Count - 1)
                    newIndex = 0;
                else
                    newIndex = oldIndex + 1;
            }

            if (SelectedPlaylistIndex == ActivePlaylistIndex)
            {
                SelectedTrack = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()[newIndex];
                SelectedTrackIndex = newIndex;

                Settings.Default.LastSelectedTrackId = SelectedTrack != null ? SelectedTrack.Id : "";
            }

            ActiveTrack = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()[newIndex];
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

    private void SetupSelectedPlaylist()
    {
        MusicLibrary.ClearPlayState();
        ActivePlaylistIndex = null;
        ActiveTrackIndex = null;

        string? lastSelectedPlaylistName = Settings.Default.LastSelectedPlaylistName;

        if (!string.IsNullOrEmpty(lastSelectedPlaylistName))
        {
            SelectedTab = TabList.ToList()
                .FirstOrDefault(x => x.Name == lastSelectedPlaylistName, TabList[0]);

            SelectedPlaylist = MusicLibrary.Playlists.First(x => x!.Name == SelectedTab.Name);

            if (SelectedPlaylist != null)
            {
                SelectPlaylistByName(lastSelectedPlaylistName);

                SelectedTrack = MusicLibrary.GetTracksFromPlaylist(lastSelectedPlaylistName)
                    .Find(s => s.Id == SelectedPlaylist.SelectedTrack);

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

            Log.Information($"LoadPlaylistTabs - added PlaylistTab {tab.Name}");
        }
    }

    private PlaylistTab AddPlaylistTab(Playlist p)
    {
        PlaylistTab tab = new()
        {
            Name = p.Name,
            Tracks = LoadPlaylistTracks(p.Name)
        };

        TabList.Add(tab);

        return tab;
    }

    private void OnShuffleChanged(bool shuffle)
    {
        ShuffleTracks(shuffle);
    }

    private void ShuffleTracks(bool shuffleMode)
    {
        if (_dataGrid == null) return;

        _shuffleMode = shuffleMode;

        if (shuffleMode)
        {
            List<MediaFile> tempList = _dataGrid.ItemsSource.Cast<MediaFile>().ToList();

            Random random = new();

            while (tempList.Count > 0)
            {
                int index = random.Next(0, tempList.Count - 1);

                ShuffleList.Add(tempList[index]);
                tempList.RemoveAt(index);
            }

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
        if (_dataGrid == null) return 0;

        if (_shuffledIndex == ShuffleList.Count - 1)
            _shuffledIndex = 0;
        else
            _shuffledIndex += 1;

        int newIndex = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()
            .FindIndex(x => x.Id.Contains(ShuffleList[_shuffledIndex].Id));

        Log.Information($"GetNextShuffledIndex: {newIndex}");

        return newIndex;
    }

    private int GetPreviousShuffledIndex()
    {
        if (_dataGrid == null) return 0;

        if (_shuffledIndex == 0)
            _shuffledIndex = ShuffleList.Count - 1;
        else
            _shuffledIndex -= 1;

        int newIndex = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()
            .FindIndex(x => x.Id.Contains(ShuffleList[_shuffledIndex].Id));

        Log.Information($"GetPreviousShuffledIndex: {newIndex}");

        return newIndex;
    }

    private ObservableCollection<MediaFile> LoadPlaylistTracks(string? playListName)
    {
        ObservableCollection<MediaFile> tracks = new();
        List<MediaFile> songs = MusicLibrary.GetTracksFromPlaylist(playListName);

        foreach (MediaFile song in songs)
        {
            tracks.Add(song);
        }

        return tracks;
    }

    private void LoadAudioFile(string fileName, string playlistName)
    {
        if (File.Exists(fileName))
        {
            MediaFile track = new(fileName);

            MusicLibrary.AddTrackToLibrary(track);
            MusicLibrary.AddTrackToPlaylist(track.Id, playlistName);
            SelectedTab!.Tracks.Add(track);
        }
        else
        {
            Log.Information($"File {fileName} does not exist.");
        }
    }

    private void LoadPlaylistFile(string fileName)
    {
        if (File.Exists(fileName))
        {
            string directoryName = Path.GetDirectoryName(fileName)!;
            string playlistName = Path.GetFileNameWithoutExtension(fileName);

            Playlist playlist = MusicLibrary.AddNewPlaylist(playlistName);
            SelectedPlaylist = playlist;
            PlaylistTab playlistTab = AddPlaylistTab(playlist);
            _tabControl!.SelectedIndex = TabList.Count - 1;
            SelectPlaylistByName(playlistTab.Name!);

            List<string> paths = new List<string>();

            if (fileName.EndsWith("m3u"))
            {
                M3uContent content = new();
                FileStream stream = File.OpenRead(fileName);
                M3uPlaylist m3UPlaylist = content.GetFromStream(stream);

                paths = m3UPlaylist.GetTracksPaths();
            }

            if (fileName.EndsWith("pls"))
            {
                PlsContent content = new();
                FileStream stream = File.OpenRead(fileName);
                PlsPlaylist plsPlaylist = content.GetFromStream(stream);

                paths = plsPlaylist.GetTracksPaths();
            }

            if (fileName.EndsWith("wpl"))
            {
                WplContent content = new();
                FileStream stream = File.OpenRead(fileName);
                WplPlaylist plsPlaylist = content.GetFromStream(stream);

                paths = plsPlaylist.GetTracksPaths();
            }

            if (fileName.EndsWith("zpl"))
            {
                ZplContent content = new();
                FileStream stream = File.OpenRead(fileName);
                ZplPlaylist plsPlaylist = content.GetFromStream(stream);

                paths = plsPlaylist.GetTracksPaths();
            }

            if (paths.Any())
            {
                foreach (string path in paths)
                {
                    string mediaFilePath = Path.Combine(directoryName, path);

                    // Ensure the directory exists
                    string mediaFileDirectory = Path.GetDirectoryName(mediaFilePath)!;
                    if (!Directory.Exists(mediaFileDirectory))
                    {
                        Log.Error("Directory not found: {0}", mediaFileDirectory);
                        continue;
                    }

                    // Handle special characters in the path
                    mediaFilePath = Uri.UnescapeDataString(mediaFilePath);

                    try
                    {
                        LoadAudioFile(mediaFilePath, playlistName);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Failed to load media file: {0} - {1}", mediaFilePath, ex.Message);
                    }
                }

                _dataGrid!.ItemsSource = SelectedTab!.Tracks;
                MusicLibrary.SaveToJson();
            }
        }
    }

    public void RemoveTrack()
    {
        if (_dataGrid is { SelectedItem: null }) return;

        ObservableCollection<MediaFile> tracks = TabList[SelectedPlaylistIndex].Tracks;

        if (SelectedTrackIndex >= 0 && SelectedTrackIndex < tracks.Count)
        {
            int indexToRemove = SelectedTrackIndex;
            string songId = tracks[SelectedTrackIndex].Id;

            MusicLibrary.RemoveTrackFromPlaylist(SelectedPlaylist!.Name, songId);

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

    private MediaFile? GetActiveTrackByIndex(int index)
    {
        if (ActivePlaylistIndex == null) { return null; }
        return TabList[(int)ActivePlaylistIndex].Tracks[index];
    }

    private void SelectPlaylistByName(string name)
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

    private void OnMainWindowLoaded(bool isLoaded)
    {
        if (isLoaded)
        {
            //SetupSelectedPlaylist();
        }
    }

    //private void OnMainWindowClosing(bool isClosing)
    //{
    //    //if (isClosing)
    //    //{
    //    //    Settings.Default.LastSelectedPlaylistName = SelectedPlaylist!.Name;
    //    //    Settings.Default.LastSelectedSongId = SelectedTrack != null ? SelectedTrack.Id : "";
    //    //}
    //}
}