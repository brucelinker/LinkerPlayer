using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Core;
using LinkerPlayer.Database;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using ManagedBass;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LinkerPlayer.ViewModels;

public partial class PlaylistTabsViewModel : ObservableObject
{
    [ObservableProperty] private PlaylistTab? _selectedTab;
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private Playlist? _selectedPlaylist;
    [ObservableProperty] private PlaybackState _state;
    [ObservableProperty] private ObservableCollection<PlaylistTab> _tabList = [];
    [ObservableProperty] private bool _allowDrop;

    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private int _totalTracks;
    [ObservableProperty] private int _processedTracks;

    private readonly SharedDataModel _sharedDataModel;
    private readonly SettingsManager _settingsManager;
    private TabControl? _tabControl;
    private DataGrid? _dataGrid;

    private bool _shuffleMode;
    private readonly List<MediaFile> _shuffleList = [];
    private int _shuffledIndex;

    private readonly string[] _supportedAudioExtensions = [".mp3", ".flac", ".wav"];
    private const string SupportedAudioFilter = "(*.mp3; *.flac)|*.mp3;*.flac";
    private const string SupportedPlaylistFilter = "(*.m3u;*.pls;*.wpl;*.zpl)|*.m3u;*.pls;*.wpl;*.zpl";
    private const string SupportedFilters = $"Audio Formats {SupportedAudioFilter}|Playlist Files {SupportedPlaylistFilter}|All files (*.*)|*.*";
    private static int _count;

    public PlaylistTabsViewModel(SharedDataModel sharedDataModel, SettingsManager settingsManager)
    {
        Log.Information($"PLAYLISTTABSVIEWMODEL - {++_count}");
        _sharedDataModel = sharedDataModel;
        _settingsManager = settingsManager;
        _shuffleMode = _settingsManager.Settings.ShuffleMode;
        AllowDrop = true;

        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (_, m) =>
        {
            OnPlaybackStateChanged(m.Value);
        });

        WeakReferenceMessenger.Default.Register<ShuffleModeMessage>(this, (_, m) =>
        {
            OnShuffleChanged(m.Value);
        });
    }

    private bool IsAudioFile(string path)
    {
        return _supportedAudioExtensions.Contains(Path.GetExtension(path).ToLower());
    }

    [RelayCommand]
    private void DragOver(DragEventArgs args)
    {
        if (args.Data.GetDataPresent(DataFormats.FileDrop))
        {
            args.Effects = DragDropEffects.Copy;
            args.Handled = true;
        }
        else
        {
            args.Effects = DragDropEffects.None;
            args.Handled = true;
        }
        //Log.Information("DragOver triggered with effect: {Effect}", args.Effects);
    }

    [RelayCommand]
    private async Task Drop(DragEventArgs args)
    {
        if (!args.Data.GetDataPresent(DataFormats.FileDrop))
        {
            args.Handled = true;
            return;
        }

        string[] droppedItems = (string[])args.Data.GetData(DataFormats.FileDrop);
        bool isControlPressed = (args.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        Log.Information("Drop triggered, Control pressed: {Control}", isControlPressed);

        foreach (var item in droppedItems)
        {
            if (File.Exists(item) && IsAudioFile(item))
            {
                await AddFileToCurrentPlaylistAsync(item);
            }
            else if (Directory.Exists(item))
            {
                await HandleFolderDropAsync(item, isControlPressed);
            }
        }

        args.Handled = true;
    }

    private async Task AddFileToCurrentPlaylistAsync(string filePath)
    {
        if (SelectedTab == null)
        {
            var newPlaylist = await MusicLibrary.AddNewPlaylistAsync("Default Playlist");
            AddPlaylistTab(newPlaylist);
            SelectedTab = TabList.Last();
            SelectedTabIndex = TabList.Count - 1;
        }

        var mediaFile = new MediaFile { Path = filePath, Title = Path.GetFileNameWithoutExtension(filePath) };
        var addedTrack = await MusicLibrary.AddTrackToLibraryAsync(mediaFile);
        if (addedTrack != null)
        {
            await MusicLibrary.AddTrackToPlaylistAsync(addedTrack.Id, SelectedTab.Name);
            SelectedTab.Tracks.Add(addedTrack);
            Log.Information($"Added track {addedTrack.Title} to playlist {SelectedTab.Name}");
        }
    }

    private async Task HandleFolderDropAsync(string folderPath, bool createNewPlaylist)
    {
        if (createNewPlaylist)
        {
            await CreatePlaylistFromFolderAsync(folderPath);
        }
        else
        {
            await AddFolderToCurrentPlaylistAsync(folderPath);
        }
    }

    private async Task AddFolderToCurrentPlaylistAsync(string folderPath)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsProcessing = true;
            TotalTracks = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories).Count(IsAudioFile);
            ProcessedTracks = 0;
            Log.Information($"Started processing folder: TotalTracks={TotalTracks}");
        });

        try
        {
            if (SelectedTab == null)
            {
                var newPlaylist = await MusicLibrary.AddNewPlaylistAsync("Default Playlist");
                AddPlaylistTab(newPlaylist);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SelectedTab = TabList.Last();
                    SelectedTabIndex = TabList.Count - 1;
                });
            }

            var playlist = MusicLibrary.Playlists.FirstOrDefault(p => p.Name == SelectedTab.Name);
            if (playlist == null)
            {
                Log.Error($"Playlist {SelectedTab.Name} not found in MusicLibrary.Playlists");
                return;
            }
            Log.Information($"Before adding tracks, playlist {SelectedTab.Name} TrackIds: {string.Join(", ", playlist.TrackIds)}");

            var tracksToAdd = new List<MediaFile>();
            foreach (var file in Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories))
            {
                if (IsAudioFile(file))
                {
                    var mediaFile = new MediaFile { Path = file, Title = Path.GetFileNameWithoutExtension(file) };
                    var addedTrack = await MusicLibrary.AddTrackToLibraryAsync(mediaFile, saveImmediately: false);
                    if (addedTrack != null)
                    {
                        tracksToAdd.Add(addedTrack);
                        Log.Information($"Prepared track {addedTrack.Title} for playlist {SelectedTab.Name}");
                    }
                    await Application.Current.Dispatcher.InvokeAsync(() => ProcessedTracks++);
                }
            }

            await MusicLibrary.SaveTracksBatchAsync(tracksToAdd);

            foreach (var track in tracksToAdd)
            {
                await MusicLibrary.AddTrackToPlaylistAsync(track.Id, SelectedTab.Name, saveImmediately: false);
                await Application.Current.Dispatcher.InvokeAsync(() => SelectedTab.Tracks.Add(track));
                Log.Information($"Added track {track.Title} to playlist {SelectedTab.Name} with TrackId {track.Id}");
            }

            // Set SelectedTrack to first track
            if (playlist.TrackIds.Any() && playlist.SelectedTrack == null)
            {
                playlist.SelectedTrack = playlist.TrackIds.First();
                Log.Information($"Set SelectedTrack to {playlist.SelectedTrack} for playlist {SelectedTab.Name} with TrackIds: {string.Join(", ", playlist.TrackIds)}");
            }
            else if (!playlist.TrackIds.Any())
            {
                Log.Warning($"No tracks in playlist {SelectedTab.Name}, cannot set SelectedTrack");
            }

            await MusicLibrary.SaveToDatabaseAsync();
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsProcessing = false;
                TotalTracks = 0;
                ProcessedTracks = 0;
                Log.Information("Finished processing folder");
            });
        }
    }

    private async Task CreatePlaylistFromFolderAsync(string folderPath)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsProcessing = true;
            TotalTracks = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories).Count(IsAudioFile);
            ProcessedTracks = 0;
            Log.Information($"Started processing folder: TotalTracks={TotalTracks}");
        });

        try
        {
            var folderName = Path.GetFileName(folderPath);
            var newPlaylist = await MusicLibrary.AddNewPlaylistAsync(folderName);
            AddPlaylistTab(newPlaylist);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SelectedTab = TabList.Last();
                SelectedTabIndex = TabList.Count - 1;
            });

            var playlist = MusicLibrary.Playlists.FirstOrDefault(p => p.Name == newPlaylist.Name);
            if (playlist == null)
            {
                Log.Error($"Playlist {newPlaylist.Name} not found in MusicLibrary.Playlists");
                return;
            }
            Log.Information($"Before adding tracks, playlist {newPlaylist.Name} TrackIds: {string.Join(", ", playlist.TrackIds)}");

            var tracksToAdd = new List<MediaFile>();
            foreach (var file in Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories))
            {
                if (IsAudioFile(file))
                {
                    var mediaFile = new MediaFile { Path = file, Title = Path.GetFileNameWithoutExtension(file) };
                    var addedTrack = await MusicLibrary.AddTrackToLibraryAsync(mediaFile, saveImmediately: false);
                    if (addedTrack != null)
                    {
                        tracksToAdd.Add(addedTrack);
                        Log.Information($"Prepared track {addedTrack.Title} for new playlist {newPlaylist.Name}");
                    }
                    await Application.Current.Dispatcher.InvokeAsync(() => ProcessedTracks++);
                }
            }

            await MusicLibrary.SaveTracksBatchAsync(tracksToAdd);

            foreach (var track in tracksToAdd)
            {
                await MusicLibrary.AddTrackToPlaylistAsync(track.Id, newPlaylist.Name, saveImmediately: false);
                await Application.Current.Dispatcher.InvokeAsync(() => SelectedTab.Tracks.Add(track));
                Log.Information($"Added track {track.Title} to new playlist {newPlaylist.Name} with TrackId {track.Id}");
            }

            // Set SelectedTrack to first track
            if (playlist.TrackIds.Any() && playlist.SelectedTrack == null)
            {
                playlist.SelectedTrack = playlist.TrackIds.First();
                Log.Information($"Set SelectedTrack to {playlist.SelectedTrack} for playlist {newPlaylist.Name} with TrackIds: {string.Join(", ", playlist.TrackIds)}");
            }
            else if (!playlist.TrackIds.Any())
            {
                Log.Warning($"No tracks in playlist {newPlaylist.Name}, cannot set SelectedTrack");
            }

            await MusicLibrary.SaveToDatabaseAsync();
        }
        finally
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsProcessing = false;
                TotalTracks = 0;
                ProcessedTracks = 0;
                Log.Information("Finished processing folder");
            });
        }
    }

    [RelayCommand]
    public async Task RenamePlaylistAsync((PlaylistTab Tab, string? OldName) args)
    {
        if (args.Tab?.Name == null || args.OldName == null) return;

        string oldName = args.OldName;
        string newName = args.Tab.Name;
        try
        {
            await ChangeSelectedPlaylistNameAsync(args.Tab, oldName, newName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"RenamePlaylistAsync failed for '{oldName}' to '{newName}'");
        }
    }

    public int SelectedTrackIndex
    {
        get => _sharedDataModel.SelectedTrackIndex;
        set => _sharedDataModel.UpdateSelectedTrackIndex(value);
    }

    public MediaFile? SelectedTrack
    {
        get => _sharedDataModel.SelectedTrack;
        set => _sharedDataModel.UpdateSelectedTrack(value!);
    }

    public MediaFile? ActiveTrack
    {
        get => _sharedDataModel.ActiveTrack;
        set => _sharedDataModel.UpdateActiveTrack(value!);
    }

    public void OnDataGridLoaded(object sender, RoutedEventArgs _)
    {
        if (sender is DataGrid dataGrid)
        {
            _dataGrid = dataGrid;
            SelectedTabIndex = _settingsManager.Settings.SelectedTabIndex;
            if (SelectedTabIndex < 0 || SelectedTabIndex >= TabList.Count)
            {
                SelectedTabIndex = TabList.Count > 0 ? 0 : -1;
            }

            if (SelectedTabIndex >= 0)
            {
                SelectedTab = TabList[SelectedTabIndex];
                _dataGrid.ItemsSource = SelectedTab!.Tracks;
                SelectedPlaylist = GetSelectedPlaylist();
                _dataGrid.Items.Refresh();
                _dataGrid.UpdateLayout();

                if (SelectedPlaylist == null || SelectedPlaylist.SelectedTrack == null) return;

                Log.Information("OnDataGridLoaded : ScrollIntoView");
                _dataGrid.ScrollIntoView(SelectedPlaylist.SelectedTrack);
            }
        }
    }

    public void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tabControl == null && sender is TabControl tabControl)
        {
            _tabControl = tabControl;
            SelectedTabIndex = _settingsManager.Settings.SelectedTabIndex;
            if (SelectedTabIndex < 0 || SelectedTabIndex >= TabList.Count)
            {
                SelectedTabIndex = TabList.Count > 0 ? 0 : -1;
            }
        }
        else if (SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count)
        {
            _settingsManager.Settings.SelectedTabIndex = SelectedTabIndex;
            _settingsManager.SaveSettings(nameof(AppSettings.SelectedTabIndex));
        }

        SelectedTab = (sender as TabControl)?.SelectedItem as PlaylistTab;
        if (_dataGrid == null || SelectedTab == null) return;
        _dataGrid.ItemsSource = SelectedTab!.Tracks;
        SelectedPlaylist = GetSelectedPlaylist();
        if (SelectedPlaylist == null) return;

        if (!string.IsNullOrEmpty(SelectedPlaylist.SelectedTrack))
        {
            SelectedPlaylist.SelectedTrack = SelectedPlaylist.TrackIds.FirstOrDefault();
        }

        SelectedTrack = MusicLibrary.MainLibrary.FirstOrDefault(x => x.Id == SelectedPlaylist.SelectedTrack);

        if (SelectedTrack == null) return;

        MusicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack!.Id;
        if (_shuffleMode)
        {
            ShuffleTracks(_shuffleMode);
        }

        Log.Information("OnTabSelectionChanged: SelectedTabIndex={Index}, TabName={Name}", SelectedTabIndex, SelectedTab?.Name ?? "none");
    }

    public void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs _)
    {
        _dataGrid = (sender as DataGrid);
        if (_dataGrid is { SelectedItem: not null })
        {
            SelectedTrack = _dataGrid.SelectedItem as MediaFile;
            if (SelectedTrack == null) return;

            SelectedTrackIndex = _dataGrid.SelectedIndex;
            if (SelectedTab == null)
            {
                if (_tabControl == null)
                {
                    return;
                }
                SelectedTab = _tabControl.SelectedItem as PlaylistTab;
            }

            SelectedTab!.SelectedTrack = SelectedTrack;
            SelectedTab.SelectedIndex = SelectedTrackIndex;

            if (MusicLibrary.Playlists.Count > 0 && SelectedTabIndex < MusicLibrary.Playlists.Count)
            {
                MusicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack!.Id;
            }

            //Log.Information("OnTrackSelectionChanged : ScrollIntoView");
            _dataGrid.ScrollIntoView(SelectedTrack!);
            if (ActiveTrack == null || ActiveTrack == SelectedTrack)
            {
                WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));
            }
        }
    }

    public void OnDataGridSorted(string propertyName, ListSortDirection direction)
    {
        if (_dataGrid is null) return;
        List<MediaFile> sortedList = [.. TabList[SelectedTabIndex].Tracks];
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
        TabList[SelectedTabIndex].Tracks.Clear();
        foreach (MediaFile song in sortedList)
        {
            TabList[SelectedTabIndex].Tracks.Add(song);
        }

        _dataGrid.ItemsSource = null;
        _dataGrid.ItemsSource = TabList[SelectedTabIndex].Tracks;
        _dataGrid.SelectedIndex = TabList[SelectedTabIndex].Tracks.ToList()
            .FindIndex(t => t.FileName.Equals(saveSelectedItem.FileName));
        if (_dataGrid is { Items.Count: > 0 })
        {
            _dataGrid.Items.Refresh();
            _dataGrid.UpdateLayout();
            _dataGrid.ScrollIntoView(_dataGrid.SelectedIndex >= 0 ? _dataGrid.SelectedItem : null);
            SelectedTrackIndex = _dataGrid.SelectedIndex;
            SelectedTrack = _dataGrid.SelectedItem as MediaFile;
            MusicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack!.Id;
        }

        if (_shuffleList.Count > 0)
        {
            _shuffleList.Clear();
        }

        _shuffleMode = _settingsManager.Settings.ShuffleMode;
        ShuffleTracks(_shuffleMode);
    }

    private void OnPlaybackStateChanged(PlaybackState state)
    {
        State = state;
        if (ActiveTrack != null)
        {
            ActiveTrack.State = state;
        }
    }

    public void OnDoubleClickDataGrid()
    {
        SelectedTrack = (MediaFile)_dataGrid!.SelectedItem;
        SelectedTrack.State = PlaybackState.Playing;
        MusicLibrary.MainLibrary.FirstOrDefault(x => x.Id == SelectedTrack.Id)!.State = PlaybackState.Playing;
        MusicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack.Id;
        ActiveTrack = SelectedTrack;
        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));
    }

    public async Task NewPlaylistAsync()
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

        Playlist newPlaylist = await MusicLibrary.AddNewPlaylistAsync(playlistName);
        AddPlaylistTab(newPlaylist);
        _tabControl!.SelectedIndex = TabList.Count - 1;
    }

    public async Task LoadPlaylistAsync()
    {
        OpenFileDialog openFileDialog = new()
        {
            Filter = SupportedPlaylistFilter,
            Multiselect = false,
            Title = "Select file(s)"
        };
        bool? result = openFileDialog.ShowDialog();
        if (result == false) return;

        await LoadPlaylistFileAsync(openFileDialog.FileName);
    }

    public async Task AddFolderAsync()
    {
        OpenFolderDialog folderDialog = new();
        bool? result = folderDialog.ShowDialog();
        if (result is null or false) return;

        string selectedFolderPath = folderDialog.FolderName;
        DirectoryInfo dirInfo = new(selectedFolderPath);
        IEnumerable<FileInfo> files = dirInfo.GetFiles("*.*", SearchOption.AllDirectories)
            .Where(file => _supportedAudioExtensions.Any(ext => file.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase)));

        foreach (FileInfo file in files)
        {
            await LoadAudioFileAsync(file.FullName, SelectedPlaylist!.Name);
        }

        _dataGrid!.ItemsSource = SelectedTab!.Tracks;
        await MusicLibrary.SaveToDatabaseAsync();
    }

    public async Task NewPlaylistFromFolderAsync()
    {
        OpenFolderDialog folderDialog = new()
        {
            FolderName = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        };
        bool? result = folderDialog.ShowDialog();
        if (result is null or false) return;

        string selectedFolderPath = folderDialog.FolderName;
        DirectoryInfo dirInfo = new(selectedFolderPath);
        List<FileInfo> files = dirInfo.GetFiles("*.*", SearchOption.AllDirectories)
            .Where(file => _supportedAudioExtensions.Any(ext => file.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase))).ToList();
        string playlistName = dirInfo.Name;

        Log.Information($"NewPlaylistFromFolderAsync: Selected folder {selectedFolderPath}, found {files.Count} files");

        Playlist playlist = await MusicLibrary.AddNewPlaylistAsync(playlistName);
        PlaylistTab playlistTab = AddPlaylistTab(playlist);
        SelectedTab = playlistTab;
        _tabControl!.SelectedIndex = _tabControl.Items.IndexOf(playlistTab);

        Stopwatch timer = new();
        timer.Reset();
        timer.Start();

        foreach (FileInfo file in files)
        {
            await LoadAudioFileAsync(file.FullName, playlistName, saveImmediately: false);
        }

        await MusicLibrary.SaveToDatabaseAsync();
        await MusicLibrary.CleanOrphanedTracksAsync();
        _dataGrid!.ItemsSource = SelectedTab!.Tracks;
        _dataGrid.Items.Refresh();
        _dataGrid.UpdateLayout();

        timer.Stop();
        Log.Information($"{playlistName} playlist took {timer.Elapsed.TotalSeconds} seconds to load, added {SelectedTab!.Tracks.Count} tracks");
    }

    public async Task AddFilesAsync()
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
                await LoadAudioFileAsync(fileName, SelectedPlaylist!.Name);
            }
        }

        _dataGrid!.ItemsSource = SelectedTab!.Tracks;
        await MusicLibrary.SaveToDatabaseAsync();
    }

    public async Task RemovePlaylistAsync(object sender)
    {
        if (sender is MenuItem item)
        {
            PlaylistTab playlistTab = (item.DataContext as PlaylistTab)!;
            string playlistName = playlistTab.Name!;
            await MusicLibrary.RemovePlaylistAsync(playlistName);
            int tabIndex = TabList.ToList().IndexOf(playlistTab);
            if (tabIndex >= 0)
            {
                TabList.RemoveAt(tabIndex);
            }

            // Update UI state
            if (TabList.Any())
            {
                // Select the first remaining tab
                SelectedTabIndex = 0;
                SelectedTab = TabList[0];
                if (_tabControl != null)
                {
                    _tabControl.SelectedIndex = 0;
                }
                if (_dataGrid != null)
                {
                    _dataGrid.ItemsSource = SelectedTab.Tracks;
                    _dataGrid.Items.Refresh();
                    _dataGrid.UpdateLayout();
                }
                SelectedPlaylist = GetSelectedPlaylist();
            }
            else
            {
                // Create a new playlist if none remain
                await NewPlaylistAsync();
            }

            _settingsManager.Settings.SelectedTabIndex = SelectedTabIndex;
            _settingsManager.SaveSettings(nameof(AppSettings.SelectedTabIndex));
            Log.Information($"Playlist '{playlistName}' removed from UI");
        }
    }

    private void SelectTrack(Playlist playlist, MediaFile? track)
    {
        if (_dataGrid == null || _dataGrid.ItemsSource == null || track == null) return;
        if (!playlist.Name.Equals(SelectedTab!.Name) && _tabControl != null)
        {
            int index = TabList.ToList().IndexOf(TabList.FirstOrDefault(x => x.Name == playlist.Name) ?? TabList[0]);
            if (index >= 0)
            {
                _tabControl.SelectedIndex = index;
            }
        }

        SelectedTrack = track;
        SelectedTrackIndex = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()
            .FindIndex(x => x.Id == track.Id);
        MusicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack.Id;

        MediaFile musicFile = (ActiveTrack ?? SelectedTrack)!;
        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(musicFile));
    }

    public MediaFile SelectFirstTrack()
    {
        if (_dataGrid?.ItemsSource.Cast<MediaFile>().ToList().Count == 0 || TabList.Count == 0) return null!;

        SelectedTrackIndex = 0;
        SelectedTrack = TabList[SelectedTabIndex].Tracks[SelectedTrackIndex];
        MusicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack.Id;

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
            int newIndex;
            if (ActiveTrack != null)
            {
                ActiveTrack.State = PlaybackState.Stopped;
                MusicLibrary.ClearPlayState();
            }

            int oldIndex = SelectedTrackIndex;
            if (_shuffleMode)
            {
                if (_shuffleList.Count == 0)
                {
                    ShuffleTracks(true);
                }
                newIndex = GetPreviousShuffledIndex();
            }
            else
            {
                if (oldIndex == 0)
                    newIndex = _dataGrid.ItemsSource.Cast<MediaFile>().ToList().Count - 1;
                else
                    newIndex = oldIndex - 1;
            }

            SelectedTrack = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()[newIndex];
            SelectedTrack.State = PlaybackState.Playing;
            SelectedTrackIndex = newIndex;

            MusicLibrary.MainLibrary.FirstOrDefault(x => x.Id == SelectedTrack.Id)!.State = PlaybackState.Playing;
            MusicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack != null ? SelectedTrack.Id : SelectFirstTrack().Id;

            ActiveTrack = SelectedTrack;
            _dataGrid.SelectedIndex = newIndex;
            _dataGrid.ScrollIntoView(_dataGrid.SelectedItem!);
        }

        return ActiveTrack;
    }

    public MediaFile? NextMediaFile()
    {
        if (SelectedTrack != null && _dataGrid != null)
        {
            int newIndex;
            if (ActiveTrack != null)
            {
                ActiveTrack.State = PlaybackState.Stopped;
                MusicLibrary.ClearPlayState();
            }

            int oldIndex = SelectedTrackIndex;
            if (_shuffleMode)
            {
                if (_shuffleList.Count == 0)
                {
                    ShuffleTracks(true);
                }
                newIndex = GetNextShuffledIndex();
            }
            else
            {
                if (oldIndex == _dataGrid.ItemsSource.Cast<MediaFile>().ToList().Count - 1)
                    newIndex = 0;
                else
                    newIndex = oldIndex + 1;
            }

            SelectedTrack = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()[newIndex];
            SelectedTrack.State = PlaybackState.Playing;
            SelectedTrackIndex = newIndex;

            MusicLibrary.MainLibrary.FirstOrDefault(x => x.Id == SelectedTrack.Id)!.State = PlaybackState.Playing;
            MusicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack != null ? SelectedTrack.Id : SelectFirstTrack().Id;

            ActiveTrack = SelectedTrack;
            _dataGrid.SelectedItem = ActiveTrack;
            _dataGrid.ScrollIntoView(ActiveTrack!);
        }

        return ActiveTrack;
    }

    private Playlist? GetSelectedPlaylist()
    {
        if (SelectedTabIndex < 0 || SelectedTabIndex >= TabList.Count || TabList.Count == 0)
        {
            Log.Warning("GetSelectedPlaylist: Invalid SelectedTabIndex or empty TabList");
            return null;
        }

        SelectedTab = TabList[SelectedTabIndex];
        SelectedPlaylist = MusicLibrary.Playlists.FirstOrDefault(x => x.Name == SelectedTab.Name);
        if (SelectedPlaylist == null)
        {
            Log.Warning($"GetSelectedPlaylist: Playlist '{SelectedTab.Name}' not found in MusicLibrary.Playlists");
            return null;
        }

        SelectPlaylistByName(SelectedTab.Name!);
        SelectedTrack = MusicLibrary.GetTracksFromPlaylist(SelectedTab.Name)
            .FirstOrDefault(s => s.Id == SelectedPlaylist.SelectedTrack) ?? SelectFirstTrack();

        if (SelectedTrack == null) return SelectedPlaylist;

        SelectTrack(SelectedPlaylist, SelectedTrack);
        if (_dataGrid != null)
        {
            _dataGrid.SelectedItem = SelectedTrack;
            _dataGrid.SelectedIndex = SelectedTrackIndex;
            _dataGrid.Items.Refresh();
            _dataGrid.UpdateLayout();
            _dataGrid.ScrollIntoView(SelectedTrack);
        }

        return SelectedPlaylist;
    }

    public void LoadPlaylistTabs()
    {
        TabList.Clear();
        List<Playlist> playlists = MusicLibrary.GetPlaylists();
        foreach (Playlist p in playlists)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            PlaylistTab tab = AddPlaylistTab(p);
            Log.Information($"LoadPlaylistTabs - added PlaylistTab {tab.Name}");
        }
        Log.Information($"Loaded {TabList.Count} playlists in UI");
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
        _shuffleList.Clear();

        if (shuffleMode)
        {
            List<MediaFile> tempList = _dataGrid.ItemsSource.Cast<MediaFile>().ToList();
            Random random = new();

            while (tempList.Count > 0)
            {
                int index = random.Next(0, tempList.Count);
                _shuffleList.Add(tempList[index]);
                tempList.RemoveAt(index);
            }

            if (ActiveTrack != null && _shuffleList.Count > 0)
            {
                _shuffledIndex = _shuffleList.FirstOrDefault(x => x.Id == ActiveTrack.Id) != null
                    ? _shuffleList.IndexOf(_shuffleList.First(x => x.Id == ActiveTrack.Id))
                    : SelectedTrackIndex;
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
            _shuffleList.Clear();
        }
    }

    private int GetNextShuffledIndex()
    {
        if (_dataGrid == null) return 0;

        if (_shuffledIndex == _shuffleList.Count - 1)
            _shuffledIndex = 0;
        else
            _shuffledIndex += 1;

        int newIndex = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()
            .FindIndex(x => x.Id == _shuffleList[_shuffledIndex].Id);
        Log.Information($"GetNextShuffledIndex: {newIndex}");

        return newIndex;
    }

    private int GetPreviousShuffledIndex()
    {
        if (_dataGrid == null) return 0;

        if (_shuffledIndex == 0)
            _shuffledIndex = _shuffleList.Count - 1;
        else
            _shuffledIndex -= 1;

        int newIndex = _dataGrid.ItemsSource.Cast<MediaFile>().ToList()
            .FindIndex(x => x.Id == _shuffleList[_shuffledIndex].Id);
        Log.Information($"GetPreviousShuffledIndex: {newIndex}");

        return newIndex;
    }

    private static ObservableCollection<MediaFile> LoadPlaylistTracks(string? playlistName)
    {
        ObservableCollection<MediaFile> tracks = [];
        List<MediaFile> songs = MusicLibrary.GetTracksFromPlaylist(playlistName);

        foreach (MediaFile song in songs)
        {
            tracks.Add(song);
        }

        return tracks;
    }

    private async Task LoadAudioFileAsync(string fileName, string playlistName, bool saveImmediately = true)
    {
        if (File.Exists(fileName))
        {
            try
            {
                MediaFile track = new(fileName);
                MediaFile? addedTrack = await MusicLibrary.AddTrackToLibraryAsync(track, saveImmediately);
                if (addedTrack != null)
                {
                    await MusicLibrary.AddTrackToPlaylistAsync(addedTrack.Id, playlistName, saveImmediately: saveImmediately);
                    SelectedTab!.Tracks.Add(addedTrack);
                    _dataGrid!.Items.Refresh();
                    //                    Log.Information($"Added track {fileName} to playlist {playlistName}");
                }
                else
                {
                    Log.Warning($"Failed to add track {fileName} to library");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error loading audio file {fileName}");
            }
        }
        else
        {
            Log.Information($"File {fileName} does not exist.");
        }
    }

    private async Task LoadPlaylistFileAsync(string fileName)
    {
        if (File.Exists(fileName))
        {
            string directoryName = Path.GetDirectoryName(fileName)!;
            string playlistName = Path.GetFileNameWithoutExtension(fileName);
            Playlist playlist = await MusicLibrary.AddNewPlaylistAsync(playlistName);
            SelectedPlaylist = playlist;

            PlaylistTab playlistTab = AddPlaylistTab(playlist);
            _tabControl!.SelectedIndex = TabList.Count - 1;
            SelectPlaylistByName(playlistTab.Name!);

            List<string> paths = [];
            if (fileName.EndsWith("m3u"))
            {
                M3uContent content = new();
                using FileStream stream = File.OpenRead(fileName);
                M3uPlaylist m3UPlaylist = content.GetFromStream(stream);
                paths = m3UPlaylist.GetTracksPaths();
            }
            else if (fileName.EndsWith("pls"))
            {
                PlsContent content = new();
                using FileStream stream = File.OpenRead(fileName);
                PlsPlaylist plsPlaylist = content.GetFromStream(stream);
                paths = plsPlaylist.GetTracksPaths();
            }
            else if (fileName.EndsWith("wpl"))
            {
                WplContent content = new();
                using FileStream stream = File.OpenRead(fileName);
                WplPlaylist wplPlaylist = content.GetFromStream(stream);
                paths = wplPlaylist.GetTracksPaths();
            }
            else if (fileName.EndsWith("zpl"))
            {
                ZplContent content = new();
                using FileStream stream = File.OpenRead(fileName);
                ZplPlaylist zplPlaylist = content.GetFromStream(stream);
                paths = zplPlaylist.GetTracksPaths();
            }

            if (paths.Count > 0)
            {
                foreach (string path in paths)
                {
                    string mediaFilePath = Path.Combine(directoryName, path);
                    string mediaFileDirectory = Path.GetDirectoryName(mediaFilePath)!;
                    if (!Directory.Exists(mediaFileDirectory))
                    {
                        Log.Error("Directory not found: {0}", mediaFileDirectory);
                        continue;
                    }

                    mediaFilePath = Uri.UnescapeDataString(mediaFilePath);
                    try
                    {
                        await LoadAudioFileAsync(mediaFilePath, playlistName);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Failed to load media file: {0} {1}", mediaFilePath, ex.Message);
                    }
                }

                _dataGrid!.ItemsSource = SelectedTab!.Tracks;
                await MusicLibrary.SaveToDatabaseAsync();
            }
        }
    }

    public async Task RemoveTrackAsync()
    {
        if (_dataGrid is { SelectedItem: null }) return;

        ObservableCollection<MediaFile> tracks = TabList[SelectedTabIndex].Tracks;
        if (SelectedTrackIndex >= 0 && SelectedTrackIndex < tracks.Count)
        {
            int indexToRemove = SelectedTrackIndex;
            string songId = tracks[SelectedTrackIndex].Id;

            await MusicLibrary.RemoveTrackFromPlaylistAsync(SelectedPlaylist!.Name, songId);
            if (_dataGrid!.SelectedIndex == tracks.Count - 1)
            {
                indexToRemove = SelectedTrackIndex;
                _dataGrid.SelectedIndex = SelectedTrackIndex - 1;
            }

            tracks.RemoveAt(indexToRemove);
            _dataGrid.Items.Refresh();
            _dataGrid.UpdateLayout();
        }
    }

    public void RightMouseDownTabSelect(string tabName)
    {
        int index = TabList.ToList().IndexOf(TabList.FirstOrDefault(p => p.Name == tabName) ?? TabList[0]);
        if (index >= 0)
        {
            _tabControl!.SelectedIndex = index;
        }
    }

    public async Task ChangeSelectedPlaylistNameAsync(PlaylistTab tab, string oldName, string newPlaylistName)
    {
        if (string.IsNullOrWhiteSpace(newPlaylistName))
        {
            Log.Warning("ChangeSelectedPlaylistName: New playlist name is empty or whitespace");
            tab.Name = oldName;
            return;
        }

        if (newPlaylistName == oldName)
        {
            Log.Information($"ChangeSelectedPlaylistName: New name '{newPlaylistName}' is same as old name, no change needed");
            return;
        }

        try
        {
            // Update database
            using (var context = new MusicLibraryDbContext(new DbContextOptionsBuilder<MusicLibraryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LinkerPlayer", "music_library.db")}")
                .Options))
            {
                var dbPlaylist = await context.Playlists.FirstOrDefaultAsync(p => p.Name == oldName);
                if (dbPlaylist == null)
                {
                    Log.Warning($"ChangeSelectedPlaylistName: Playlist '{oldName}' not found in database");
                    return;
                }

                // Check for duplicate name in database
                if (await context.Playlists.AnyAsync(p => p.Name == newPlaylistName && p.Name != oldName))
                {
                    Log.Warning($"ChangeSelectedPlaylistName: Playlist name '{newPlaylistName}' already exists");
                    MessageBox.Show($"A playlist named '{newPlaylistName}' already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    tab.Name = oldName;
                    return;
                }

                dbPlaylist.Name = newPlaylistName;
                await context.SaveChangesAsync();
            }

            // Sync Playlists
            var playlist = MusicLibrary.Playlists.FirstOrDefault(p => p.Name == oldName);
            if (playlist == null)
            {
                MusicLibrary.Playlists.Clear();
                await MusicLibrary.LoadFromDatabaseAsync();
                playlist = MusicLibrary.Playlists.FirstOrDefault(p => p.Name == newPlaylistName);
                if (playlist == null)
                {
                    Log.Error($"ChangeSelectedPlaylistName: Failed to find playlist '{newPlaylistName}' after reload");
                    tab.Name = oldName;
                    return;
                }
            }
            else
            {
                playlist.Name = newPlaylistName;
                var playlistIndex = MusicLibrary.Playlists.IndexOf(playlist);
                if (playlistIndex >= 0)
                {
                    MusicLibrary.Playlists[playlistIndex] = playlist;
                }
            }

            // Update UI
            tab.Name = newPlaylistName;
            var selectedTabIndex = TabList.IndexOf(tab);
            if (selectedTabIndex >= 0)
            {
                TabList[selectedTabIndex].Name = newPlaylistName;
                if (SelectedTab == tab)
                {
                    SelectedTab.Name = newPlaylistName;
                    SelectedPlaylist = playlist;
                }
            }

            await MusicLibrary.SaveToDatabaseAsync();
            Log.Information($"Playlist renamed from '{oldName}' to '{newPlaylistName}' in database");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to rename playlist from '{oldName}' to '{newPlaylistName}'");
            MessageBox.Show($"Failed to rename playlist to '{newPlaylistName}'. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            tab.Name = oldName;
            var selectedTabIndex = TabList.IndexOf(tab);
            if (selectedTabIndex >= 0)
            {
                TabList[selectedTabIndex].Name = oldName;
                if (SelectedTab == tab)
                {
                    SelectedTab.Name = oldName;
                    SelectedPlaylist = MusicLibrary.Playlists.FirstOrDefault(p => p.Name == oldName);
                }
            }
        }
    }

    public async Task ChangeSelectedPlaylistName(string newPlaylistName)
    {
        if (SelectedTab != null && SelectedPlaylist != null)
        {
            await ChangeSelectedPlaylistNameAsync(SelectedTab, SelectedPlaylist.Name!, newPlaylistName);
        }
    }

    private void SelectPlaylistByName(string name)
    {
        List<Playlist> playlists = MusicLibrary.GetPlaylists();
        if (SelectedPlaylist != null && string.Equals(SelectedPlaylist.Name, name))
        {
            if (_dataGrid != null)
            {
                _dataGrid.SelectedIndex = playlists.FirstOrDefault(x => x.Name == name) != null
                    ? playlists.IndexOf(playlists.First(x => x.Name == name))
                    : -1;
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