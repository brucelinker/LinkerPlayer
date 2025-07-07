using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Core;
using LinkerPlayer.Database;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using ManagedBass;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PlaylistsNET.Content;
using PlaylistsNET.Models;
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
    [ObservableProperty]
    private ProgressData _progressInfo = new()
    {
        IsProcessing = false,
        ProcessedTracks = 0,
        TotalTracks = 1,
        Status = string.Empty
    };

    private readonly SharedDataModel _sharedDataModel;
    private readonly SettingsManager _settingsManager;
    private readonly MusicLibrary _musicLibrary;
    private readonly ILogger<PlaylistTabsViewModel> _logger;

    private TabControl? _tabControl;
    private DataGrid? _dataGrid;

    private bool _shuffleMode;
    private readonly List<MediaFile> _shuffleList = [];
    private int _shuffledIndex;

    private readonly string[] _supportedAudioExtensions = [".mp3", ".flac", ".wav"];
    private const string SupportedAudioFilter = "(*.mp3; *.flac)|*.mp3;*.flac";
    private const string SupportedPlaylistFilter = "(*.m3u;*.pls;*.wpl;*.zpl)|*.m3u;*.pls;*.wpl;*.zpl";
    private const string SupportedFilters = $"Audio Formats {SupportedAudioFilter}|Playlist Files {SupportedPlaylistFilter}|All files (*.*)|*.*";

    public PlaylistTabsViewModel(
        MusicLibrary musicLibrary,
        SharedDataModel sharedDataModel,
        SettingsManager settingsManager,
        ILogger<PlaylistTabsViewModel> logger)
    {
        _musicLibrary = musicLibrary;
        _logger = logger;

        try
        {
            _logger.LogInformation("Initializing PlaylistTabsViewModel");
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
            _logger.LogInformation("PlaylistTabsViewModel initialized successfully");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error in PlaylistTabsViewModel constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in PlaylistTabsViewModel constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
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

        if (!SelectedPlaylist.TrackIds.Any())
        {
            SelectedPlaylist.SelectedTrack = null;
            SelectedTrack = null;
            SelectedTrackIndex = -1;
            return;
        }

        SelectedPlaylist.SelectedTrack = SelectedPlaylist.TrackIds.FirstOrDefault();
        SelectedTrack = _musicLibrary.MainLibrary.FirstOrDefault(x => x.Id == SelectedPlaylist.SelectedTrack);

        if (SelectedTrack == null) return;
        _musicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack.Id;
        if (_shuffleMode)
        {
            ShuffleTracks(_shuffleMode);
        }

        _logger.LogInformation("OnTabSelectionChanged: SelectedTabIndex={Index}, TabName={Name}", SelectedTabIndex, SelectedTab?.Name ?? "none");
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

            if (_musicLibrary.Playlists.Count > 0 && SelectedTabIndex < _musicLibrary.Playlists.Count)
            {
                _musicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack!.Id;
            }

            //_logger.LogInformation("OnTrackSelectionChanged : ScrollIntoView");
            _dataGrid.ScrollIntoView(SelectedTrack!);

            if (ActiveTrack == null) // || ActiveTrack == SelectedTrack)
            {
                WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));
            }
        }
        else
        {
            SelectedTab!.SelectedTrack = null;
            SelectedTab.SelectedIndex = -1;
            WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(null));
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
            _dataGrid.ScrollIntoView((_dataGrid.SelectedIndex >= 0 ? _dataGrid.SelectedItem : null) ?? SelectFirstTrack());
            SelectedTrackIndex = _dataGrid.SelectedIndex;
            SelectedTrack = _dataGrid.SelectedItem as MediaFile;
            _musicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack!.Id;
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
        _musicLibrary.MainLibrary.FirstOrDefault(x => x.Id == SelectedTrack.Id)!.State = PlaybackState.Playing;
        _musicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack.Id;
        ActiveTrack = SelectedTrack;
        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));
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
        //_logger.LogInformation("DragOver triggered with effect: {Effect}", args.Effects);
    }

    [RelayCommand]
    private async Task Drop(DragEventArgs args)
    {
        if (!args.Data.GetDataPresent(DataFormats.FileDrop))
        {
            args.Handled = true;
            _logger.LogWarning("Drop event triggered without FileDrop data");
            return;
        }

        string[] droppedItems = (string[])args.Data.GetData(DataFormats.FileDrop)!;
        bool isControlPressed = (args.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        _logger.LogInformation($"Drop triggered with {droppedItems.Length} items, Control pressed: {isControlPressed}, Items: {string.Join(", ", droppedItems)}");

        Progress<ProgressData> progress = new (data =>
        {
            // This runs on the UI thread
            WeakReferenceMessenger.Default.Send(new ProgressValueMessage(data));
        });

        foreach (string item in droppedItems)
        {
            try
            {
                if (File.Exists(item) && IsAudioFile(item))
                {
                    await AddFileToCurrentPlaylistAsync(item);
                    _logger.LogInformation($"Added file {item} to current playlist");
                }
                else if (Directory.Exists(item))
                {
                    await HandleFolderDropAsync(item, isControlPressed, progress);
                    _logger.LogInformation($"Processed folder {item}");
                }
                else
                {
                    _logger.LogWarning($"Invalid drop item: {item}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to process drop item: {item}");
            }
        }

        args.Handled = true;
    }

    private async Task HandleFolderDropAsync(string folderPath, bool createNewPlaylist, IProgress<ProgressData> progress)
    {
        if (createNewPlaylist)
        {
            await CreatePlaylistFromFolderAsync(folderPath, progress);
        }
        else
        {
            await AddFolderToCurrentPlaylistAsync(folderPath, progress);
        }
    }

    private bool IsAudioFile(string path)
    {
        return _supportedAudioExtensions.Contains(Path.GetExtension(path).ToLower());
    }

    private async Task AddFileToCurrentPlaylistAsync(string filePath)
    {
        if (SelectedTab == null)
        {
            Playlist newPlaylist = await _musicLibrary.AddNewPlaylistAsync("Default Playlist");
            AddPlaylistTab(newPlaylist);
            SelectedTab = TabList.Last();
            SelectedTabIndex = TabList.Count - 1;
        }

        MediaFile mediaFile = new() { Path = filePath, Title = Path.GetFileNameWithoutExtension(filePath) };
        MediaFile? addedTrack = await _musicLibrary.AddTrackToLibraryAsync(mediaFile);
        if (addedTrack != null)
        {
            await _musicLibrary.AddTrackToPlaylistAsync(addedTrack.Id, SelectedTab.Name);
            SelectedTab.Tracks.Add(addedTrack);
            _logger.LogInformation($"Added track {addedTrack.Title} to playlist {SelectedTab.Name}");
        }
    }

    private async Task AddFolderToCurrentPlaylistAsync(string folderPath, IProgress<ProgressData>? progress = null)
    {
        if(_dataGrid == null) 
        {
            _logger.LogError("DataGrid is null, cannot add folder to current playlist");
            return;
        }

        ProgressInfo.IsProcessing = true;
        ProgressInfo.TotalTracks = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories).Count(IsAudioFile);
        ProgressInfo.ProcessedTracks = 0;
        ProgressInfo.Phase = "Adding";
        ProgressInfo.Status = "Starting import...";
        progress?.Report(ProgressInfo);

        try
        {
            if (SelectedTab == null)
            {
                Playlist newPlaylist = await _musicLibrary.AddNewPlaylistAsync("Default Playlist");
                AddPlaylistTab(newPlaylist);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SelectedTab = TabList.Last();
                    SelectedTabIndex = TabList.Count - 1;
                    _dataGrid.ItemsSource = SelectedTab.Tracks;
                    _dataGrid.Items.Refresh();
                });
            }

            Playlist? playlist = _musicLibrary.Playlists.FirstOrDefault(p => p.Name == SelectedTab!.Name);
            if (playlist == null)
            {
                _logger.LogError($"Playlist {SelectedTab!.Name} not found in _musicLibrary.Playlists");
                progress?.Report(new ProgressData { IsProcessing = false, Status = "Error: Playlist not found" });
                return;
            }

            List<MediaFile> tracksToAdd = new();
            int batchSize = Math.Max(1, ProgressInfo.TotalTracks / 100); // ~5 for 507 tracks
            int processedCount = 0;
            HashSet<string> addedPaths = new(StringComparer.OrdinalIgnoreCase);

            await Task.Run(async () =>
            {
                List<MediaFile> batchTracks = new();
                foreach (string file in Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories))
                {
                    if (IsAudioFile(file) && addedPaths.Add(file))
                    {
                        MediaFile mediaFile = new()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Path = file,
                            Title = Path.GetFileNameWithoutExtension(file),
                            FileName = Path.GetFileName(file),
                            State = PlaybackState.Stopped,
                            Duration = TimeSpan.FromSeconds(1),
                            Album = "<Unknown>",
                            Artist = "<Unknown>"
                        };

                        MediaFile? existingTrack = _musicLibrary.IsTrackInLibrary(mediaFile);
                        MediaFile clonedTrack;
                        if (existingTrack != null)
                        {
                            clonedTrack = existingTrack.Clone();
                            _logger.LogDebug($"Using existing track: {file}");
                        }
                        else
                        {
                            clonedTrack = mediaFile.Clone();
                            _musicLibrary.MainLibrary.Add(clonedTrack);
                        }

                        tracksToAdd.Add(clonedTrack);
                        batchTracks.Add(clonedTrack);
                        processedCount++;

                        if (batchTracks.Count >= batchSize || processedCount == ProgressInfo.TotalTracks)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                foreach (MediaFile track in batchTracks)
                                {
                                    if (SelectedTab!.Tracks.All(t => t.Id != track.Id))
                                    {
                                        SelectedTab.Tracks.Add(track);
                                    }
                                }
                                _dataGrid.Items.Refresh();
                                ProgressInfo.ProcessedTracks = processedCount;
                                ProgressInfo.Status = $"Adding: {clonedTrack.Title}";
                                progress?.Report(ProgressInfo);
                            });
                            batchTracks.Clear();
                        }
                    }
                }
            });

            _logger.LogInformation($"Added {tracksToAdd.Count} tracks to playlist {playlist.Name}");

            _ = Task.Run(async () =>
            {
                try
                {
                    Task databaseTask = _musicLibrary.SaveTracksBatchAsync(tracksToAdd);

                    ProgressInfo.Phase = "Metadata";
                    ProgressInfo.ProcessedTracks = 0;
                    await Parallel.ForEachAsync(tracksToAdd, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (track, _) =>
                    {
                        try
                        {
                            track.UpdateFromFileMetadata(false, minimal: true);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to extract metadata for {track.Path}");
                            track.Duration = TimeSpan.FromSeconds(1);
                            track.Album = "<Unknown>";
                            track.Title = Path.GetFileNameWithoutExtension(track.Path);
                        }

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            int index = SelectedTab!.Tracks.IndexOf(track);
                            if (index >= 0)
                            {
                                SelectedTab.Tracks[index] = track;
                            }
                            ProgressInfo.ProcessedTracks++;
                            if (ProgressInfo.ProcessedTracks % batchSize == 0 || ProgressInfo.ProcessedTracks == tracksToAdd.Count)
                            {
                                _dataGrid.Items.Refresh();
                                ProgressInfo.Status = $"Metadata updated: {track.Title}";
                                progress?.Report(ProgressInfo);
                            }
                        });
                    });

                    await databaseTask;

                    ProgressInfo.Phase = "Saving";
                    ProgressInfo.ProcessedTracks = 0;
                    List<string> trackIds = tracksToAdd.Select(t => t.Id).ToList();
                    await _musicLibrary.AddTracksToPlaylistAsync(trackIds, SelectedTab!.Name, saveImmediately: false);
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ProgressInfo.ProcessedTracks = trackIds.Count;
                        ProgressInfo.Status = $"Saved {trackIds.Count} tracks to playlist";
                        progress?.Report(ProgressInfo);
                    });

                    if (playlist.TrackIds.Any() && playlist.SelectedTrack == null)
                    {
                        playlist.SelectedTrack = playlist.TrackIds.First();
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (_dataGrid.SelectedItem == null && SelectedTab.Tracks.Any())
                            {
                                _dataGrid.SelectedIndex = 0;
                                _dataGrid.ScrollIntoView(_dataGrid.SelectedItem!);
                            }
                        });
                    }

                    await _musicLibrary.SaveToDatabaseAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save tracks or update metadata");
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ProgressInfo.Status = "Error saving to database";
                        progress?.Report(ProgressInfo);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to process folder: {folderPath}");
            ProgressInfo.Status = "Error processing folder";
            progress?.Report(ProgressInfo);
        }
        finally
        {
            ProgressInfo.IsProcessing = false;
            ProgressInfo.TotalTracks = 1;
            ProgressInfo.ProcessedTracks = 0;
            ProgressInfo.Phase = ""; // Idle
            ProgressInfo.Status = ""; //"Finished";
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                progress?.Report(ProgressInfo);
                _dataGrid.Items.Refresh();
            });
        }
    }

    private async Task CreatePlaylistFromFolderAsync(string folderPath, IProgress<ProgressData>? progress = null)
    {
        ProgressInfo.IsProcessing = true;
        ProgressInfo.TotalTracks = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories).Count(IsAudioFile);
        ProgressInfo.ProcessedTracks = 0;
        ProgressInfo.Status = "Starting import...";
        progress?.Report(ProgressInfo);

        try
        {
            string baseFolderName = Path.GetFileName(folderPath);
            string folderName = baseFolderName;
            int suffix = 1;
            while (_musicLibrary.Playlists.Any(p => p.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
            {
                folderName = $"{baseFolderName} ({suffix++})";
            }

            Playlist playlist = await _musicLibrary.AddNewPlaylistAsync(folderName);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PlaylistTab tab = AddPlaylistTab(playlist);
                SelectedTab = tab;
                SelectedTabIndex = TabList.Count - 1;
                _dataGrid!.ItemsSource = SelectedTab.Tracks;
                _dataGrid.Items.Refresh();
            });

            List<MediaFile> tracksToAdd = new ();
            int batchSize = Math.Max(1, ProgressInfo.TotalTracks / 100);
            int processedCount = 0;

            await Task.Run(async () =>
            {
                foreach (string file in Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories))
                {
                    if (IsAudioFile(file))
                    {
                        _logger.LogDebug("Processing file {File} for playlist {PlaylistName}", file, playlist.Name);
                        MediaFile mediaFile = new () { Path = file, Title = Path.GetFileNameWithoutExtension(file) };
                        MediaFile? addedTrack = await _musicLibrary.AddTrackToLibraryAsync(mediaFile, saveImmediately: false);
                        if (addedTrack != null)
                        {
                            tracksToAdd.Add(addedTrack);
                            processedCount++;
                            if (processedCount % batchSize == 0 || processedCount == ProgressInfo.TotalTracks)
                            {
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    ProgressInfo.ProcessedTracks = processedCount;
                                    ProgressInfo.Status = $"Adding: {addedTrack.Title}";
                                    progress?.Report(ProgressInfo);
                                });
                            }
                        }
                    }
                }
            });

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (MediaFile track in tracksToAdd)
                {
                    SelectedTab!.Tracks.Add(track);
                }
                _dataGrid!.Items.Refresh();
            });

            await Task.Run(async () =>
            {
                await _musicLibrary.SaveTracksBatchAsync(tracksToAdd);
                foreach (MediaFile track in tracksToAdd)
                {
                    await _musicLibrary.AddTrackToPlaylistAsync(track.Id, playlist.Name, saveImmediately: false);
                }
                if (tracksToAdd.Any())
                {
                    playlist.SelectedTrack = tracksToAdd.First().Id;
                }
                await _musicLibrary.SaveToDatabaseAsync();
            });
        }
        finally
        {
            ProgressInfo.IsProcessing = false;
            ProgressInfo.TotalTracks = 1;
            ProgressInfo.ProcessedTracks = 0;
            ProgressInfo.Status = ""; //"Finished";
            progress?.Report(ProgressInfo);
        }
    }

    [RelayCommand]
    public async Task RenamePlaylistAsync((PlaylistTab Tab, string? OldName) args)
    {
        if (string.IsNullOrEmpty(args.Tab.Name) || args.OldName == null) return;

        string oldName = args.OldName;
        string newName = args.Tab.Name;
        try
        {
            await ChangeSelectedPlaylistNameAsync(args.Tab, oldName, newName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"RenamePlaylistAsync failed for '{oldName}' to '{newName}'");
        }
    }

    public async Task NewPlaylistAsync()
    {
        string playlistName = "New Playlist";
        int index = 0;
        bool found = true;

        while (found)
        {
            found = false;
            foreach (Playlist playlist in _musicLibrary.GetPlaylists())
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

        Playlist newPlaylist = await _musicLibrary.AddNewPlaylistAsync(playlistName);
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
        await _musicLibrary.SaveToDatabaseAsync();
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

        _logger.LogInformation($"NewPlaylistFromFolderAsync: Selected folder {selectedFolderPath}, found {files.Count} files");

        Playlist playlist = await _musicLibrary.AddNewPlaylistAsync(playlistName);
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

        await _musicLibrary.SaveToDatabaseAsync();
        await _musicLibrary.CleanOrphanedTracksAsync();
        _dataGrid!.ItemsSource = SelectedTab!.Tracks;
        _dataGrid.Items.Refresh();
        _dataGrid.UpdateLayout();

        timer.Stop();
        _logger.LogInformation($"{playlistName} playlist took {timer.Elapsed.TotalSeconds} seconds to load, added {SelectedTab!.Tracks.Count} tracks");
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
        await _musicLibrary.SaveToDatabaseAsync();
    }

    public async Task RemovePlaylistAsync(object sender)
    {
        if (sender is not MenuItem { DataContext: PlaylistTab playlistTab })
        {
            return;
        }

        string playlistName = playlistTab.Name;
        const int maxRetries = 3;

        for (int retryCount = 0; retryCount < maxRetries; retryCount++)
        {
            try
            {
                await _musicLibrary.RemovePlaylistAsync(playlistName);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    int tabIndex = TabList.IndexOf(playlistTab);
                    if (tabIndex >= 0)
                    {
                        TabList.RemoveAt(tabIndex);
                    }

                    if (TabList.Any())
                    {
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
                        NewPlaylistAsync().GetAwaiter().GetResult();
                    }

                    _settingsManager.Settings.SelectedTabIndex = SelectedTabIndex;
                    _settingsManager.SaveSettings(nameof(AppSettings.SelectedTabIndex));
                    _logger.LogInformation($"Playlist '{playlistName}' removed from UI");
                });
                return;
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 5)
            {
                if (retryCount == maxRetries - 1)
                {
                    _logger.LogError(ex, $"Failed to remove playlist '{playlistName}' after {maxRetries} retries");
                    MessageBox.Show($"Failed to remove playlist '{playlistName}'. Please try again later.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                _logger.LogWarning($"Database locked, retrying ({retryCount + 1}/{maxRetries}) after delay");
                await Task.Delay(1000 * (retryCount + 1));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to remove playlist '{playlistName}'");
                MessageBox.Show($"Failed to remove playlist '{playlistName}'. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
    }

    private async Task SelectTrackAsync(Playlist playlist, MediaFile? track)
    {
        if (_dataGrid == null || _dataGrid.ItemsSource == null || track == null || SelectedTab == null || _tabControl == null)
        {
            _logger.LogWarning($"Cannot select track {track?.Id ?? "null"}: Invalid DataGrid, ItemsSource, track, SelectedTab, or TabControl");
            return;
        }

        //_logger.LogInformation($"Selecting track {track.Id} for playlist {playlist.Name}, SelectedTab: {SelectedTab.Name}, TabList count: {TabList.Count}, Playlists count: {_musicLibrary.Playlists.Count}, TabControl items: {_tabControl.Items.Count}");

        // Validate playlist exists
        if (_musicLibrary.Playlists.All(p => p.Name != playlist.Name))
        {
            _logger.LogWarning($"Playlist {playlist.Name} not found in _musicLibrary.Playlists; skipping selection");
            return;
        }

        // Ensure correct tab is selected
        if (!playlist.Name.Equals(SelectedTab.Name))
        {
            PlaylistTab? targetTab = TabList.FirstOrDefault(x => x.Name == playlist.Name);
            if (targetTab != null)
            {
                int index = TabList.ToList().IndexOf(targetTab);
                if (index >= 0)
                {
                    _tabControl.SelectedIndex = index;
                    SelectedTab = targetTab;
                    SelectedTabIndex = index;
                    _logger.LogInformation($"Switched to tab {playlist.Name} at index {index}");
                    await Task.Delay(100);
                }
                else
                {
                    _logger.LogWarning($"Tab for playlist {playlist.Name} not found in TabList");
                    return;
                }
            }
            else
            {
                _logger.LogWarning($"Tab for playlist {playlist.Name} not found in TabList");
                return;
            }
        }

        // Retry until track is in DataGrid or timeout (10 seconds)
        int retries = 100;
        int delayMs = 100;
        while (retries > 0)
        {
            if (SelectedTab.Tracks.Any(t => t.Id == track.Id))
            {
                List<MediaFile> items = _dataGrid.ItemsSource.Cast<MediaFile>().ToList();
                SelectedTrackIndex = items.FindIndex(x => x.Id == track.Id);
                if (SelectedTrackIndex >= 0)
                {
                    SelectedTrack = track;
                    _musicLibrary.Playlists[SelectedTabIndex].SelectedTrack = track.Id;
                    _logger.LogInformation($"Selected track {track.Title} with Id {track.Id} in playlist {SelectedTab.Name} at index {SelectedTrackIndex}");

                    return;
                }
            }
            _logger.LogDebug($"Track {track.Id} not yet in {SelectedTab.Name} DataGrid; retrying ({retries} left)");
            await Task.Delay(delayMs);
            retries--;
        }

        _logger.LogWarning($"Failed to select track {track.Id} in {SelectedTab.Name} after {100 * delayMs}ms");
    }

    public MediaFile SelectFirstTrack()
    {
        if (_dataGrid?.ItemsSource.Cast<MediaFile>().ToList().Count == 0 || TabList.Count == 0) return null!;

        SelectedTrackIndex = 0;
        SelectedTrack = TabList[SelectedTabIndex].Tracks[SelectedTrackIndex];
        _musicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack.Id;

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
                _musicLibrary.ClearPlayState();
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

            _musicLibrary.MainLibrary.FirstOrDefault(x => x.Id == SelectedTrack.Id)!.State = PlaybackState.Playing;
            _musicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack != null ? SelectedTrack.Id : SelectFirstTrack().Id;

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
                _musicLibrary.ClearPlayState();
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

            _musicLibrary.MainLibrary.FirstOrDefault(x => x.Id == SelectedTrack.Id)!.State = PlaybackState.Playing;
            _musicLibrary.Playlists[SelectedTabIndex].SelectedTrack = SelectedTrack != null ? SelectedTrack.Id : SelectFirstTrack().Id;

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
            _logger.LogWarning("GetSelectedPlaylist: Invalid SelectedTabIndex or empty TabList");
            return null;
        }

        SelectedTab = TabList[SelectedTabIndex];
        SelectedPlaylist = _musicLibrary.Playlists.FirstOrDefault(x => x.Name == SelectedTab.Name);
        if (SelectedPlaylist == null)
        {
            _logger.LogWarning($"GetSelectedPlaylist: Playlist '{SelectedTab.Name}' not found in _musicLibrary.Playlists");
            return null;
        }

        SelectPlaylistByName(SelectedTab.Name);
        SelectedTrack = _musicLibrary.GetTracksFromPlaylist(SelectedTab.Name)
            .FirstOrDefault(s => s.Id == SelectedPlaylist.SelectedTrack) ?? SelectFirstTrack();

        if (SelectedTrack == null) return SelectedPlaylist;

        SelectTrackAsync(SelectedPlaylist, SelectedTrack).GetAwaiter().GetResult();
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
        List<Playlist> playlists = _musicLibrary.GetPlaylists();
        foreach (Playlist p in playlists)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            PlaylistTab tab = AddPlaylistTab(p);
            _logger.LogInformation($"LoadPlaylistTabs - added PlaylistTab {tab.Name}");
        }
        _logger.LogInformation($"Loaded {TabList.Count} playlists in UI");
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
        _logger.LogInformation($"GetNextShuffledIndex: {newIndex}");

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
        _logger.LogInformation($"GetPreviousShuffledIndex: {newIndex}");

        return newIndex;
    }

    private ObservableCollection<MediaFile> LoadPlaylistTracks(string? playlistName)
    {
        ObservableCollection<MediaFile> tracks = [];
        List<MediaFile> songs = _musicLibrary.GetTracksFromPlaylist(playlistName);

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
                MediaFile? addedTrack = await _musicLibrary.AddTrackToLibraryAsync(track, saveImmediately);
                if (addedTrack != null)
                {
                    await _musicLibrary.AddTrackToPlaylistAsync(addedTrack.Id, playlistName, saveImmediately: saveImmediately);
                    SelectedTab!.Tracks.Add(addedTrack);
                    _dataGrid!.Items.Refresh();
                    //                    _logger.LogInformation($"Added track {fileName} to playlist {playlistName}");
                }
                else
                {
                    _logger.LogWarning($"Failed to add track {fileName} to library");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading audio file {fileName}");
            }
        }
        else
        {
            _logger.LogInformation($"File {fileName} does not exist.");
        }
    }

    private async Task LoadPlaylistFileAsync(string fileName)
    {
        if (File.Exists(fileName))
        {
            string directoryName = Path.GetDirectoryName(fileName)!;
            string playlistName = Path.GetFileNameWithoutExtension(fileName);
            Playlist playlist = await _musicLibrary.AddNewPlaylistAsync(playlistName);
            SelectedPlaylist = playlist;

            PlaylistTab playlistTab = AddPlaylistTab(playlist);
            _tabControl!.SelectedIndex = TabList.Count - 1;
            SelectPlaylistByName(playlistTab.Name);

            List<string> paths = [];
            if (fileName.EndsWith("m3u"))
            {
                M3uContent content = new();
                await using FileStream stream = File.OpenRead(fileName);
                M3uPlaylist m3UPlaylist = content.GetFromStream(stream);
                paths = m3UPlaylist.GetTracksPaths();
            }
            else if (fileName.EndsWith("pls"))
            {
                PlsContent content = new();
                await using FileStream stream = File.OpenRead(fileName);
                PlsPlaylist plsPlaylist = content.GetFromStream(stream);
                paths = plsPlaylist.GetTracksPaths();
            }
            else if (fileName.EndsWith("wpl"))
            {
                WplContent content = new();
                await using FileStream stream = File.OpenRead(fileName);
                WplPlaylist wplPlaylist = content.GetFromStream(stream);
                paths = wplPlaylist.GetTracksPaths();
            }
            else if (fileName.EndsWith("zpl"))
            {
                ZplContent content = new();
                await using FileStream stream = File.OpenRead(fileName);
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
                        _logger.LogError("Directory not found: {0}", mediaFileDirectory);
                        continue;
                    }

                    mediaFilePath = Uri.UnescapeDataString(mediaFilePath);
                    try
                    {
                        await LoadAudioFileAsync(mediaFilePath, playlistName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Failed to load media file: {0} {1}", mediaFilePath, ex.Message);
                    }
                }

                _dataGrid!.ItemsSource = SelectedTab!.Tracks;
                await _musicLibrary.SaveToDatabaseAsync();
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

            await _musicLibrary.RemoveTrackFromPlaylistAsync(SelectedPlaylist!.Name, songId);
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
            _logger.LogWarning("ChangeSelectedPlaylistName: New playlist name is empty or whitespace");
            tab.Name = oldName;
            return;
        }

        if (newPlaylistName == oldName)
        {
            _logger.LogInformation($"ChangeSelectedPlaylistName: New name '{newPlaylistName}' is same as old name, no change needed");
            return;
        }

        try
        {
            // Update database
            await using (MusicLibraryDbContext context = new (new DbContextOptionsBuilder<MusicLibraryDbContext>()
                             .UseSqlite($"Data Source={Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LinkerPlayer", "music_library.db")}")
                             .Options))
            {
                Playlist? dbPlaylist = await context.Playlists.FirstOrDefaultAsync(p => p.Name == oldName);
                if (dbPlaylist == null)
                {
                    _logger.LogWarning($"ChangeSelectedPlaylistName: Playlist '{oldName}' not found in database");
                    return;
                }

                // Check for duplicate name in database
                if (await context.Playlists.AnyAsync(p => p.Name == newPlaylistName && p.Name != oldName))
                {
                    _logger.LogWarning($"ChangeSelectedPlaylistName: Playlist name '{newPlaylistName}' already exists");
                    MessageBox.Show($"A playlist named '{newPlaylistName}' already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    tab.Name = oldName;
                    return;
                }

                dbPlaylist.Name = newPlaylistName;
                await context.SaveChangesAsync();
            }

            // Sync Playlists
            Playlist? playlist = _musicLibrary.Playlists.FirstOrDefault(p => p.Name == oldName);
            if (playlist == null)
            {
                _musicLibrary.Playlists.Clear();
                await _musicLibrary.LoadFromDatabaseAsync();
                playlist = _musicLibrary.Playlists.FirstOrDefault(p => p.Name == newPlaylistName);
                if (playlist == null)
                {
                    _logger.LogError($"ChangeSelectedPlaylistName: Failed to find playlist '{newPlaylistName}' after reload");
                    tab.Name = oldName;
                    return;
                }
            }
            else
            {
                playlist.Name = newPlaylistName;
                int playlistIndex = _musicLibrary.Playlists.IndexOf(playlist);
                if (playlistIndex >= 0)
                {
                    _musicLibrary.Playlists[playlistIndex] = playlist;
                }
            }

            // Update UI
            tab.Name = newPlaylistName;
            int selectedTabIndex = TabList.IndexOf(tab);
            if (selectedTabIndex >= 0)
            {
                TabList[selectedTabIndex].Name = newPlaylistName;
                if (SelectedTab == tab)
                {
                    SelectedTab.Name = newPlaylistName;
                    SelectedPlaylist = playlist;
                }
            }

            await _musicLibrary.SaveToDatabaseAsync();
            _logger.LogInformation($"Playlist renamed from '{oldName}' to '{newPlaylistName}' in database");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to rename playlist from '{oldName}' to '{newPlaylistName}'");
            MessageBox.Show($"Failed to rename playlist to '{newPlaylistName}'. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            tab.Name = oldName;
            int selectedTabIndex = TabList.IndexOf(tab);
            if (selectedTabIndex >= 0)
            {
                TabList[selectedTabIndex].Name = oldName;
                if (SelectedTab == tab)
                {
                    SelectedTab.Name = oldName;
                    SelectedPlaylist = _musicLibrary.Playlists.FirstOrDefault(p => p.Name == oldName);
                }
            }
        }
    }

    private void SelectPlaylistByName(string name)
    {
        List<Playlist> playlists = _musicLibrary.GetPlaylists();
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