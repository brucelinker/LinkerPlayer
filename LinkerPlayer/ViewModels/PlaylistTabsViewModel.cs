using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using ManagedBass;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PlaylistsNET.Content;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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

    public readonly SharedDataModel SharedDataModel;
    private readonly ISettingsManager _settingsManager;
    private readonly IMusicLibrary _musicLibrary;
    private readonly ILogger<PlaylistTabsViewModel> _logger;

    // New services
    private readonly IFileImportService _fileImportService;
    private readonly IPlaylistManagerService _playlistManagerService;
    private readonly ITrackNavigationService _trackNavigationService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IDatabaseSaveService _databaseSaveService; // NEW: Debounced save service

    private TabControl? _tabControl;
    private DataGrid? _dataGrid;

    private bool _shuffleMode;

    private const string SupportedAudioFilter = "(*.mp3; *.flac; *.ape; *.ac3; *.dts; *.m4k; *.mka; *.mp4; *.mpc; *.ofr; *.ogg; *.opus; *.wav; *.wma; *.wv)|*.mp3; *.flac; *.ape; *.ac3; *.dts; *.m4k; *.mka; *.mp4; *.mpc; *.ofr; *.ogg; *.opus; *.wav; *.wma; *.wv";
    private const string SupportedPlaylistFilter = "(*.m3u;*.pls;*.wpl;*.zpl)|*.m3u;*.pls;*.wpl;*.zpl";
    private const string SupportedFilters = $"Audio Formats {SupportedAudioFilter}|Playlist Files {SupportedPlaylistFilter}|All files (*.*)|*.*";

    public PlaylistTabsViewModel(
        IMusicLibrary musicLibrary,
        SharedDataModel sharedDataModel,
        ISettingsManager settingsManager,
        IFileImportService fileImportService,
        IPlaylistManagerService playlistManagerService,
        ITrackNavigationService trackNavigationService,
        IUiDispatcher uiDispatcher,
        IDatabaseSaveService databaseSaveService, // NEW: Inject debounced save service
        ILogger<PlaylistTabsViewModel> logger)
    {
        _musicLibrary = musicLibrary ?? throw new ArgumentNullException(nameof(musicLibrary));
        _fileImportService = fileImportService ?? throw new ArgumentNullException(nameof(fileImportService));
        _playlistManagerService = playlistManagerService ?? throw new ArgumentNullException(nameof(playlistManagerService));
        _trackNavigationService = trackNavigationService ?? throw new ArgumentNullException(nameof(trackNavigationService));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _databaseSaveService = databaseSaveService ?? throw new ArgumentNullException(nameof(databaseSaveService)); // NEW
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        try
        {
            SharedDataModel = sharedDataModel ?? throw new ArgumentNullException(nameof(sharedDataModel));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _shuffleMode = _settingsManager.Settings.ShuffleMode;
            AllowDrop = true;

            RegisterMessages();
            _logger.LogInformation("PlaylistTabsViewModel initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PlaylistTabsViewModel constructor: {Message}", ex.Message);
            throw;
        }
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (_, m) =>
        {
            OnPlaybackStateChanged(m.Value);
        });

        WeakReferenceMessenger.Default.Register<ShuffleModeMessage>(this, (_, m) =>
        {
            OnShuffleChanged(m.Value);
        });
    }

    public int SelectedTrackIndex
    {
        get => SharedDataModel.SelectedTrackIndex;
        set => SharedDataModel.UpdateSelectedTrackIndex(value);
    }

    public MediaFile? SelectedTrack
    {
        get => SharedDataModel.SelectedTrack;
        set
        {
            SharedDataModel.UpdateSelectedTrack(value!);
            OnPropertyChanged(nameof(SelectedTrack));
        }
    }

    public MediaFile? ActiveTrack
    {
        get => SharedDataModel.ActiveTrack;
        set => SharedDataModel.UpdateActiveTrack(value!);
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

            if (SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count)
            {
                PlaylistTab tab = TabList[SelectedTabIndex];

                SelectedPlaylist = GetSelectedPlaylist();

                WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));
            }
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        try
        {
            if (value < 0 || value >= TabList.Count)
            {
                return;
            }

            // Keep TabControl selection in sync if available
            if (_tabControl != null && _tabControl.SelectedIndex != value)
            {
                _tabControl.SelectedIndex = value;
            }

            // Update selection-sensitive state
            SelectedTab = TabList[value];
            SelectedPlaylist = GetSelectedPlaylist();
            // Do not force DataGrid refresh here; preserves scroll/selection

            // Persist setting asynchronously to avoid blocking UI
            _ = Task.Run(() =>
            {
                _settingsManager.Settings.SelectedTabIndex = value;
                _settingsManager.SaveSettings(nameof(AppSettings.SelectedTabIndex));
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnSelectedTabIndexChanged failed for value {Value}", value);
        }
    }

    public void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tabControl == null && sender is TabControl tabControl)
        {
            _tabControl = tabControl;
        }

        if (SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count)
        {
            _settingsManager.Settings.SelectedTabIndex = SelectedTabIndex;
            _settingsManager.SaveSettings(nameof(AppSettings.SelectedTabIndex));
        }

        if (SelectedTabIndex < 0 || SelectedTabIndex >= TabList.Count)
        {
            _logger.LogWarning("OnTabSelectionChanged: Invalid SelectedTabIndex {Index}", SelectedTabIndex);
            return;
        }

        PlaylistTab tab = TabList[SelectedTabIndex];

        if (_dataGrid == null || tab == null)
        {
            // Even if _dataGrid is not yet available, update playlist and return
            SelectedPlaylist = GetSelectedPlaylist();
            return;
        }

        // Clear SelectedTracks when switching tabs
        SharedDataModel.UpdateSelectedTracks(Enumerable.Empty<MediaFile>());

        SelectedPlaylist = GetSelectedPlaylist();
        // Avoid Items.Refresh to prevent scroll jumps

        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));
    }

    public void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs _)
    {
        _dataGrid = sender as DataGrid;

        // Handle multi-selection
        if (_dataGrid != null && _dataGrid.SelectedItems.Count > 0)
        {
            List<MediaFile> selectedTracks = _dataGrid.SelectedItems.Cast<MediaFile>().ToList();
            SharedDataModel.UpdateSelectedTracks(selectedTracks);

            // Use the first selected item as the primary selection for backward compatibility
            MediaFile selectedTrack = selectedTracks.First();

            if (SelectedTrack?.Id == selectedTrack.Id && selectedTracks.Count == 1)
            {
                return;
            }

            // Save the user's selection
            if (!SetLastSelectedTrack(selectedTrack))
            {
                // SelectedPlaylist is null
                return;
            }

            SelectedTrack = selectedTrack;
            SelectedTrackIndex = _dataGrid.Items.IndexOf(selectedTrack);

            if (SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count)
            {
                PlaylistTab tab = TabList[SelectedTabIndex];
                tab.SelectedTrack = SelectedTrack;
                tab.SelectedIndex = SelectedTrackIndex;
            }

            _settingsManager.Settings.SelectedTrackId = SelectedTrack.Id;
            _settingsManager.SaveSettings(nameof(AppSettings.SelectedTrackId));

            _dataGrid.ScrollIntoView(SelectedTrack);

            if (ActiveTrack == null)
            {
                WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));
            }
        }
        else
        {
            SharedDataModel.UpdateSelectedTracks(Enumerable.Empty<MediaFile>());
            WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(null));
        }
    }

    private bool SetLastSelectedTrack(MediaFile selectedTrack)
    {
        if (SelectedPlaylist == null)
            return false;

        if (SelectedTabIndex >= 0 && SelectedTabIndex < _musicLibrary.Playlists.Count)
        {
            _musicLibrary.Playlists[SelectedTabIndex].SelectedTrackId = selectedTrack.Id;

            // Request deferred database save instead of immediate save
            // This batches changes and saves every 2 seconds
            _databaseSaveService.RequestSave();
        }
        else
        {
            _logger.LogError("SetLastSelectedTrack: Invalid SelectedTabIndex {Index}", SelectedTabIndex);
            return false;
        }

        return true;
    }

    public void OnDataGridSorted(string propertyName, ListSortDirection direction)
    {
        if (_dataGrid?.SelectedItem is not MediaFile saveSelectedItem)
            return;

        try
        {
            List<MediaFile> sortedList = TabList[SelectedTabIndex].Tracks.ToList();
            sortedList.Sort((x, y) =>
            {
                object? propX = x.GetType().GetProperty(propertyName)?.GetValue(x);
                object? propY = y.GetType().GetProperty(propertyName)?.GetValue(y);

                if (propX == null && propY == null)
                    return 0;
                if (propX == null)
                    return direction == ListSortDirection.Ascending ? -1 : 1;
                if (propY == null)
                    return direction == ListSortDirection.Ascending ? 1 : -1;

                int comparison = Comparer.Default.Compare(propX, propY);
                return direction == ListSortDirection.Ascending ? comparison : -comparison;
            });

            // Update the collection
            TabList[SelectedTabIndex].Tracks.Clear();
            foreach (MediaFile track in sortedList)
            {
                TabList[SelectedTabIndex].Tracks.Add(track);
            }

            // Restore selection (no ItemsSource override)
            int newSelectedIndex = TabList[SelectedTabIndex].Tracks.ToList()
                .FindIndex(t => t.FileName.Equals(saveSelectedItem.FileName, StringComparison.OrdinalIgnoreCase));

            if (newSelectedIndex >= 0)
            {
                _dataGrid.SelectedIndex = newSelectedIndex;
                SelectedTrackIndex = newSelectedIndex;
                SelectedTrack = _dataGrid.SelectedItem as MediaFile;

                if (SelectedTrack != null)
                {
                    _musicLibrary.Playlists[SelectedTabIndex].SelectedTrackId = SelectedTrack.Id;
                }
            }

            _dataGrid.Items.Refresh(); // Keep this for sorting - needed to update view

            // Scroll into view at lower priority
            _dataGrid.Dispatcher.BeginInvoke(new Action(() =>
            {
                _dataGrid.ScrollIntoView(_dataGrid.SelectedItem ?? SelectFirstTrack());
            }), System.Windows.Threading.DispatcherPriority.Background);

            // Reinitialize shuffle if enabled
            if (_shuffleMode)
            {
                OnShuffleChanged(_shuffleMode);
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sorting DataGrid by {PropertyName}", propertyName);
        }
    }

    public void OnDoubleClickDataGrid()
    {
        if (_dataGrid?.SelectedItem is not MediaFile selectedTrack)
            return;

        try
        {
            selectedTrack.State = PlaybackState.Playing;

            MediaFile? libraryTrack = _musicLibrary.MainLibrary.FirstOrDefault(x => x.Id == selectedTrack.Id);
            if (libraryTrack != null)
            {
                libraryTrack.State = PlaybackState.Playing;
            }

            if (SelectedTabIndex >= 0 && SelectedTabIndex < _musicLibrary.Playlists.Count)
            {
                _musicLibrary.Playlists[SelectedTabIndex].SelectedTrackId = selectedTrack.Id;
            }

            SelectedTrack = selectedTrack;
            ActiveTrack = selectedTrack;

            WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(selectedTrack));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling double-click on DataGrid");
        }
    }

    // Public methods called by UI controls - keep these for backward compatibility
    public void LoadPlaylistTabs()
    {
        try
        {
            TabList.Clear();

            List<Playlist> playlists = _musicLibrary.GetPlaylists();

            foreach (Playlist playlist in playlists)
            {
                if (string.IsNullOrWhiteSpace(playlist.Name))
                    continue;

                PlaylistTab tab = CreatePlaylistTabFromPlaylist(playlist);
                TabList.Add(tab);
            }

            //_logger.LogInformation(
            //    "Loaded {Count} playlists in UI (TabList: {TabCount})",
            //TabList.Count,
            //TabList.Count);

            if (TabList.Any())
            {
                PlaylistTab firstTab = TabList[0];
                //_logger.LogInformation(
                //    "First Tab: Name='{Name}', Tracks={TrackCount}, SelectedTrack='{SelectedTrack}', SelectedIndex={SelectedIndex}",
                //    firstTab.Name,
                //    firstTab.Tracks.Count,
                //    firstTab.SelectedTrack?.Title ?? "null",
                //    firstTab.SelectedIndex);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load playlist tabs");
        }
    }

    // Replace the existing public async Task methods with proper RelayCommands
    [RelayCommand]
    private async Task NewPlaylist()
    {
        try
        {
            PlaylistTab newTab = await _playlistManagerService.CreatePlaylistTabAsync("New Playlist");

            await _uiDispatcher.InvokeAsync(() =>
            {
                TabList.Add(newTab);
                _tabControl!.SelectedIndex = TabList.Count - 1;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create new playlist");
        }
    }

    [RelayCommand]
    private async Task LoadPlaylist()
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Filter = SupportedPlaylistFilter,
            Multiselect = false,
            Title = "Select playlist file"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            await LoadPlaylistFileAsync(openFileDialog.FileName);
        }
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        OpenFolderDialog folderDialog = new OpenFolderDialog();
        if (folderDialog.ShowDialog() == true)
        {
            try
            {
                await EnsureSelectedTabExistsAsync();

                // Create progress callback to send ProgressValueMessage
                Progress<ProgressData> progress = new Progress<ProgressData>(data =>
                {
                    WeakReferenceMessenger.Default.Send(new ProgressValueMessage(data));
                });

                List<MediaFile> importedTracks = await _fileImportService.ImportFolderAsync(folderDialog.FolderName, progress);

                if (importedTracks.Any())
                {
                    bool success = await _playlistManagerService.AddTracksToPlaylistAsync(SelectedPlaylist!.Name, importedTracks);

                    if (success && SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count)
                    {
                        PlaylistTab tab = TabList[SelectedTabIndex];
                        await _uiDispatcher.InvokeAsync(() =>
                        {
                            foreach (MediaFile track in importedTracks)
                            {
                                tab.Tracks.Add(track); // ObservableCollection auto-notifies!
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add folder to playlist");
            }
        }
    }

    [RelayCommand]
    private async Task AddFiles()
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Filter = SupportedFilters,
            Multiselect = true,
            Title = "Select files"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                await EnsureSelectedTabExistsAsync();

                // Create progress callback to send ProgressValueMessage
                Progress<ProgressData> progress = new Progress<ProgressData>(data =>
                {
                    WeakReferenceMessenger.Default.Send(new ProgressValueMessage(data));
                });

                List<MediaFile> importedTracks = await _fileImportService.ImportFilesAsync(openFileDialog.FileNames, progress);

                if (importedTracks.Any() && SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count)
                {
                    PlaylistTab tab = TabList[SelectedTabIndex];
                    bool success = await _playlistManagerService.AddTracksToPlaylistAsync(tab.Name, importedTracks);

                    if (success)
                    {
                        await _uiDispatcher.InvokeAsync(() =>
                        {
                            foreach (MediaFile track in importedTracks)
                            {
                                tab.Tracks.Add(track); // ObservableCollection auto-notifies!
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add files to playlist");
            }
        }
    }

    [RelayCommand]
    private async Task NewPlaylistFromFolder()
    {
        OpenFolderDialog folderDialog = new OpenFolderDialog
        {
            FolderName = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        };

        if (folderDialog.ShowDialog() == true)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                Progress<ProgressData> progress = new Progress<ProgressData>(data =>
                {
                    WeakReferenceMessenger.Default.Send(new ProgressValueMessage(data));
                });

                await CreatePlaylistFromFolderAsync(folderDialog.FolderName, progress);

                stopwatch.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create playlist from folder");
            }
        }
    }

    [RelayCommand]
    private async Task RemovePlaylist(PlaylistTab? playlistTab)
    {
        if (playlistTab == null)
        {
            _logger.LogWarning("RemovePlaylistCommand called with null playlist tab");
            return;
        }

        try
        {
            bool success = await _playlistManagerService.RemovePlaylistAsync(playlistTab.Name);

            if (success)
            {
                await _uiDispatcher.InvokeAsync(async () =>
                {
                    int tabIndex = TabList.IndexOf(playlistTab);
                    if (tabIndex >= 0)
                    {
                        TabList.RemoveAt(tabIndex);
                    }

                    // Handle tab selection after removal
                    if (TabList.Any())
                    {
                        SelectedTabIndex = 0;
                        SelectedTab = TabList[0];
                        if (_tabControl != null)
                        {
                            _tabControl.SelectedIndex = 0;
                        }
                        SelectedPlaylist = GetSelectedPlaylist();
                    }
                    else
                    {
                        // Create a new playlist if none exist
                        await NewPlaylist();
                    }

                    _settingsManager.Settings.SelectedTabIndex = SelectedTabIndex;
                    _settingsManager.SaveSettings(nameof(AppSettings.SelectedTabIndex));
                });
            }
            else
            {
                _logger.LogError("Failed to remove playlist: {PlaylistName}", playlistTab.Name);
                MessageBox.Show($"Failed to remove playlist '{playlistTab.Name}'. Please try again.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing playlist: {PlaylistName}", playlistTab.Name);
            MessageBox.Show($"Failed to remove playlist '{playlistTab.Name}'. Please try again.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task RemoveTrack()
    {
        if (_dataGrid?.SelectedItem == null || SelectedPlaylist == null)
        {
            _logger.LogWarning("Cannot remove track - no track or playlist selected");
            return;
        }

        try
        {
            ObservableCollection<MediaFile> tracks = TabList[SelectedTabIndex].Tracks;
            if (SelectedTrackIndex >= 0 && SelectedTrackIndex < tracks.Count)
            {
                MediaFile trackToRemove = tracks[SelectedTrackIndex];
                bool success = await _playlistManagerService.RemoveTrackFromPlaylistAsync(SelectedPlaylist.Name, trackToRemove.Id);

                if (success)
                {
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        // Adjust selection if removing last item
                        if (_dataGrid.SelectedIndex == tracks.Count - 1)
                        {
                            _dataGrid.SelectedIndex = Math.Max(0, SelectedTrackIndex - 1);
                        }

                        tracks.RemoveAt(SelectedTrackIndex);
                        // Rely on ObservableCollection change; no Items.Refresh to avoid scroll jump
                        _dataGrid.UpdateLayout();
                    });
                }
                else
                {
                    _logger.LogError("Failed to remove track from playlist");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing track from playlist");
        }
    }

    [RelayCommand]
    private void PlayTrack()
    {
        if (_dataGrid?.SelectedItem is MediaFile selectedTrack)
        {
            OnDoubleClickDataGrid();
        }
    }

    [RelayCommand]
    private void SelectFirstTrackCommand()
    {
        SelectFirstTrack();
    }

    // Keep the old methods for backward compatibility but mark them as obsolete
    [Obsolete("Use NewPlaylistCommand instead")]
    public async Task NewPlaylistAsync() => await NewPlaylist();

    [Obsolete("Use LoadPlaylistCommand instead")]
    public async Task LoadPlaylistAsync() => await LoadPlaylist();

    [Obsolete("Use AddFolderCommand instead")]
    public async Task AddFolderAsync() => await AddFolder();

    [Obsolete("Use AddFilesCommand instead")]
    public async Task AddFilesAsync() => await AddFiles();

    [Obsolete("Use NewPlaylistFromFolderCommand instead")]
    public async Task NewPlaylistFromFolderAsync() => await NewPlaylistFromFolder();

    [Obsolete("Use RemoveTrackCommand instead")]
    public async Task RemoveTrackAsync() => await RemoveTrack();

    // Convert this to a more proper command handler
    public async Task RemovePlaylistAsync(object sender)
    {
        if (sender is MenuItem { DataContext: PlaylistTab playlistTab })
        {
            await RemovePlaylist(playlistTab);
        }
    }

    private void OnPlaybackStateChanged(PlaybackState state)
    {
        State = state;
        if (ActiveTrack != null)
        {
            ActiveTrack.State = state;
        }
    }

    private Playlist? GetSelectedPlaylist()
    {
        if (SelectedTabIndex < 0 || SelectedTabIndex >= TabList.Count || !TabList.Any())
        {
            _logger.LogWarning("GetSelectedPlaylist: Invalid SelectedTabIndex or empty TabList");
            return null;
        }

        SelectedTab = TabList[SelectedTabIndex];
        SelectedPlaylist = _musicLibrary.Playlists.FirstOrDefault(x => x.Name == SelectedTab.Name);

        if (SelectedPlaylist == null)
        {
            _logger.LogWarning("GetSelectedPlaylist: Playlist '{TabName}' not found in _musicLibrary.Playlists", SelectedTab.Name);
            return null;
        }

        // Make sure it is not an empty playlist
        if (SelectedPlaylist.PlaylistTracks.Count > 0)
        {
            string selectedTrackId = _musicLibrary.Playlists[SelectedTabIndex].SelectedTrackId!;

            SelectedTrack = _musicLibrary.GetTracksFromPlaylist(SelectedTab.Name)
                .FirstOrDefault(s => s.Id == selectedTrackId) ?? SelectFirstTrack();

            if (SelectedTrack != null && _dataGrid != null)
            {
                _dataGrid.SelectedItem = SelectedTrack;
                SelectedTrackIndex = _dataGrid.SelectedIndex;

                // Scroll into view at lower priority to avoid blocking
                _dataGrid.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _dataGrid.ScrollIntoView(SelectedTrack);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        return SelectedPlaylist;
    }

    private async Task LoadPlaylistFileAsync(string fileName)
    {
        if (!File.Exists(fileName))
        {
            _logger.LogWarning("Playlist file does not exist: {FileName}", fileName);
            return;
        }

        try
        {
            string directoryName = Path.GetDirectoryName(fileName)!;
            string playlistName = Path.GetFileNameWithoutExtension(fileName);

            PlaylistTab newTab = await _playlistManagerService.CreatePlaylistTabAsync(playlistName);

            await _uiDispatcher.InvokeAsync(() =>
            {
                TabList.Add(newTab);
                SelectedTab = newTab;
                SelectedTabIndex = TabList.Count - 1;
                _tabControl!.SelectedIndex = SelectedTabIndex;
            });

            List<string> paths = ExtractPathsFromPlaylistFile(fileName);
            List<string> validPaths = new List<string>();

            foreach (string path in paths)
            {
                try
                {
                    string candidatePath = path;

                    // Convert file:// URIs to local paths
                    if (Uri.TryCreate(candidatePath, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                    {
                        candidatePath = uri.LocalPath;
                    }

                    // Unescape any percent-encoded characters
                    candidatePath = Uri.UnescapeDataString(candidatePath);

                    // If relative, combine with playlist directory
                    if (!Path.IsPathRooted(candidatePath))
                    {
                        candidatePath = Path.GetFullPath(Path.Combine(directoryName, candidatePath));
                    }

                    if (File.Exists(candidatePath))
                    {
                        validPaths.Add(candidatePath);
                        continue;
                    }

                    // Fallbacks: fuzzy recovery
                    string? fileNameOnly = Path.GetFileName(candidatePath);
                    string? immediateDir = null;
                    try
                    {
                        immediateDir = Path.GetDirectoryName(candidatePath);
                    }
                    catch { /* ignore */ }

                    // Case A: directory exists but file name is wrong -> fuzzy file match in the directory
                    if (!string.IsNullOrEmpty(immediateDir) && Directory.Exists(immediateDir))
                    {
                        string? recovered = TryFindClosestFileInDirectory(immediateDir, fileNameOnly);
                        if (!string.IsNullOrEmpty(recovered))
                        {
                            _logger.LogInformation("Recovered missing file by fuzzy match: '{Orig}' => '{Match}'", candidatePath, recovered);
                            validPaths.Add(recovered);
                            continue;
                        }
                    }
                    else
                    {
                        // Case B: the directory segment itself is wrong (e.g., diacritics replaced by '?')
                        // Try recover the album directory within its parent (one level up), then search recursively for the file
                        try
                        {
                            // candidatePath = ...\\<AlbumDir>\\<SubDir?>\\<FileName>
                            // albumDirPath = ...\\<AlbumDir>
                            string? albumDirPath = string.IsNullOrEmpty(immediateDir) ? null : Path.GetDirectoryName(immediateDir);
                            if (!string.IsNullOrEmpty(albumDirPath))
                            {
                                string? artistDir = Path.GetDirectoryName(albumDirPath);
                                string albumDirName = Path.GetFileName(albumDirPath);

                                if (!string.IsNullOrEmpty(artistDir) && Directory.Exists(artistDir))
                                {
                                    string? recoveredAlbum = TryFindClosestDirectoryInParent(artistDir, albumDirName);
                                    if (!string.IsNullOrEmpty(recoveredAlbum) && Directory.Exists(recoveredAlbum))
                                    {
                                        // Search recursively under the recovered album directory for the closest file
                                        string? recoveredDeep = TryFindClosestFileRecursively(recoveredAlbum, fileNameOnly);
                                        if (!string.IsNullOrEmpty(recoveredDeep))
                                        {
                                            _logger.LogInformation("Recovered missing file by directory+file fuzzy match: '{Orig}' => '{Match}'", candidatePath, recoveredDeep);
                                            validPaths.Add(recoveredDeep);
                                            continue;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Directory fuzzy recovery failed for {Path}", candidatePath);
                        }
                    }

                    _logger.LogWarning("Playlist file references missing file: {FilePath}", candidatePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve path from playlist entry: {Entry}", path);
                }
            }

            if (validPaths.Any())
            {
                // Create progress callback to send ProgressValueMessage so the bottom progress bar updates
                Progress<ProgressData> progress = new Progress<ProgressData>(data =>
                {
                    WeakReferenceMessenger.Default.Send(new ProgressValueMessage(data));
                });

                List<MediaFile> importedTracks = await _fileImportService.ImportFilesAsync(validPaths.ToArray(), progress);

                if (importedTracks.Any())
                {
                    await _playlistManagerService.AddTracksToPlaylistAsync(playlistName, importedTracks);

                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        foreach (MediaFile track in importedTracks)
                        {
                            SelectedTab!.Tracks.Add(track);
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load playlist file: {FileName}", fileName);
        }
    }

    // Helpers restored and used across the class
    private static List<string> ExtractPathsFromPlaylistFile(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();

        // Prefer robust manual parsing for M3U/M3U8 with encoding detection
        if (extension is ".m3u" or ".m3u8")
        {
            return ExtractPathsFromM3u(fileName);
        }

        using FileStream stream = File.OpenRead(fileName);

        return extension switch
        {
            ".pls" => new PlsContent().GetFromStream(stream).GetTracksPaths(),
            ".wpl" => new WplContent().GetFromStream(stream).GetTracksPaths(),
            ".zpl" => new ZplContent().GetFromStream(stream).GetTracksPaths(),
            _ => new List<string>()
        };
    }

    private static List<string> ExtractPathsFromM3u(string fileName)
    {
        List<string> results = new List<string>();

        try
        {
            // Try UTF-8 with BOM detection, then Latin1, then UTF-16
            foreach (Encoding? encoding in new[] { new UTF8Encoding(false, false), Encoding.Latin1, Encoding.Unicode })
            {
                try
                {
                    using StreamReader reader = new StreamReader(fileName, encoding, detectEncodingFromByteOrderMarks: true);
                    string? line;
                    results.Clear();

                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (string.IsNullOrEmpty(line))
                            continue;
                        if (line.StartsWith("#"))
                            continue; // comment or directive

                        results.Add(line);
                    }

                    // If we successfully read any entries, break
                    if (results.Count > 0)
                        break;
                }
                catch
                {
                    // Try next encoding
                    results.Clear();
                }
            }
        }
        catch
        {
            // Fallback to PlaylistsNET if manual parsing fails
            try
            {
                using FileStream stream = File.OpenRead(fileName);
                results = new M3uContent().GetFromStream(stream).GetTracksPaths();
            }
            catch
            {
                // ignore
            }
        }

        return results;
    }

    // --- String normalization and distance helpers ---
    private static string NormalizeForCompare(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Lowercase
        string s = input.ToLowerInvariant();

        // Replace curly quotes and similar punctuation with ASCII
        s = s.Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u201C', '"')
            .Replace('\u201D', '"');

        // Remove diacritics
        string formD = s.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder(formD.Length);
        foreach (char ch in formD)
        {
            UnicodeCategory uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        s = sb.ToString().Normalize(NormalizationForm.FormC);

        // Remove punctuation (keep letters, digits, spaces), collapse spaces
        s = Regex.Replace(s, "[^a-z0-9 ]", string.Empty);
        s = Regex.Replace(s, "\\s+", " ").Trim();
        return s;
    }

    // Correct small spacing issues in helpers
    private static int LevenshteinDistance(string a, string b)
    {
        if (a == b)
            return 0;
        if (a.Length == 0)
            return b.Length;
        if (b.Length == 0)
            return a.Length;

        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
            d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++)
            d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                            Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                            d[i - 1, j - 1] + cost);
            }
        }
        return d[a.Length, b.Length];
    }


    private static string? TryFindClosestFileInDirectory(string directory, string targetFileName)
    {
        try
        {
            string targetNoExtNorm = NormalizeForCompare(Path.GetFileNameWithoutExtension(targetFileName));
            string targetNorm = NormalizeForCompare(Path.GetFileName(targetFileName));
            HashSet<string> allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mp3", ".flac", ".ape", ".ac3", ".dts", ".m4k", ".mka", ".mp4", ".mpc",
            ".ofr", ".ogg", ".opus", ".wav", ".wma", ".wv" };
            string? bestPath = null;
            int bestScore = int.MaxValue;
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                string ext = Path.GetExtension(file);
                if (!allowed.Contains(ext))
                    continue;
                string name = Path.GetFileName(file);
                string nameNorm = NormalizeForCompare(name);
                string nameNoExtNorm = NormalizeForCompare(Path.GetFileNameWithoutExtension(name));
                if (nameNorm == targetNorm || nameNoExtNorm == targetNoExtNorm)
                    return file;
                int dist = LevenshteinDistance(nameNoExtNorm, targetNoExtNorm);
                if (dist < bestScore)
                {
                    bestScore = dist;
                    bestPath = file;
                    if (bestScore <= 2)
                        return bestPath;
                }
            }
            return bestScore <= 3 ? bestPath : null;
        }
        catch { return null; }
    }

    private static string? TryFindClosestFileRecursively(string baseDir, string targetFileName)
    {
        try
        {
            string targetNoExtNorm = NormalizeForCompare(Path.GetFileNameWithoutExtension(targetFileName));
            string targetNorm = NormalizeForCompare(Path.GetFileName(targetFileName));
            HashSet<string> allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mp3", ".flac", ".ape", ".ac3", ".dts", ".m4k", ".mka", ".mp4", ".mpc",
            ".ofr", ".ogg", ".opus", ".wav", ".wma", ".wv" };
            string? bestPath = null;
            int bestScore = int.MaxValue;
            foreach (string file in Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file);
                if (!allowed.Contains(ext))
                    continue;
                string name = Path.GetFileName(file);
                string nameNorm = NormalizeForCompare(name);
                string nameNoExtNorm = NormalizeForCompare(Path.GetFileNameWithoutExtension(name));
                if (nameNorm == targetNorm || nameNoExtNorm == targetNoExtNorm)
                    return file;
                int dist = LevenshteinDistance(nameNoExtNorm, targetNoExtNorm);
                if (dist < bestScore)
                {
                    bestScore = dist;
                    bestPath = file;
                    if (bestScore <= 1)
                        return bestPath;
                }
            }
            return bestScore <= 2 ? bestPath : null;
        }
        catch { return null; }
    }

    private static string? TryFindClosestDirectoryInParent(string parentDir, string targetDirName)
    {
        try
        {
            string targetNorm = NormalizeForCompare(targetDirName);
            string? bestPath = null;
            int bestScore = int.MaxValue;
            foreach (string dir in Directory.EnumerateDirectories(parentDir))
            {
                string name = Path.GetFileName(dir);
                string nameNorm = NormalizeForCompare(name);
                if (nameNorm == targetNorm)
                    return dir;
                int dist = LevenshteinDistance(nameNorm, targetNorm);
                if (dist < bestScore)
                {
                    bestScore = dist;
                    bestPath = dir;
                    if (bestScore <= 2)
                        return bestPath;
                }
            }
            return bestScore <= 3 ? bestPath : null;
        }
        catch { return null; }
    }

    private PlaylistTab CreatePlaylistTabFromPlaylist(Playlist playlist)
    {
        IEnumerable<MediaFile> tracks = _playlistManagerService.LoadPlaylistTracks(playlist.Name);

        return new PlaylistTab
        {
            Name = playlist.Name,
            Tracks = new ObservableCollection<MediaFile>(tracks)
        };
    }

    private async Task EnsureSelectedTabExistsAsync()
    {
        if (SelectedTab == null)
        {
            PlaylistTab defaultTab = await _playlistManagerService.CreatePlaylistTabAsync("Default Playlist");
            await _uiDispatcher.InvokeAsync(() =>
            {
                TabList.Add(defaultTab);
                SelectedTab = defaultTab;
                SelectedTabIndex = TabList.Count - 1;
            });
        }
    }

    private MediaFile NavigateToTrack(IList<MediaFile> tracks, int newIndex)
    {
        if (newIndex < 0 || newIndex >= tracks.Count)
        {
            _logger.LogError("Invalid track index: {NewIndex} (Total tracks: {TotalTracks})", newIndex, tracks.Count);
            return SelectFirstTrack()!;
        }

        MediaFile newTrack = tracks[newIndex];

        // Update track state
        newTrack.State = PlaybackState.Playing;

        // Update UI selection
        SelectedTrack = newTrack;
        SelectedTrackIndex = newIndex;
        ActiveTrack = newTrack;

        // Update library state
        MediaFile? libraryTrack = _musicLibrary.MainLibrary.FirstOrDefault(x => x.Id == newTrack.Id);
        if (libraryTrack != null)
        {
            libraryTrack.State = PlaybackState.Playing;
        }

        // Update playlist state
        if (SelectedTabIndex >= 0 && SelectedTabIndex < _musicLibrary.Playlists.Count)
        {
            _musicLibrary.Playlists[SelectedTabIndex].SelectedTrackId = newTrack.Id;
        }

        // Update UI
        _dataGrid!.SelectedIndex = newIndex;
        _dataGrid.ScrollIntoView(newTrack);

        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));

        return newTrack;
    }

    private void OnShuffleChanged(bool shuffle)
    {
        _shuffleMode = shuffle;

        if (_dataGrid?.ItemsSource != null)
        {
            List<MediaFile> currentTracks = _dataGrid.ItemsSource.Cast<MediaFile>().ToList();
            if (shuffle && currentTracks.Any())
            {
                string? currentTrackId = ActiveTrack?.Id ?? SelectedTrack?.Id;
                _trackNavigationService.InitializeShuffle(currentTracks, currentTrackId);
            }
            else
            {
                _trackNavigationService.ClearShuffle();
            }
        }
    }

    public MediaFile? SelectFirstTrack()
    {
        if (_dataGrid?.ItemsSource.Cast<MediaFile>().Any() != true || !TabList.Any())
            return null;

        if (!TabList[SelectedTabIndex].Tracks.Any())
        {
            // Empty Playlist
            return null;
        }

        try
        {
            SelectedTrackIndex = 0;
            SelectedTrack = TabList[SelectedTabIndex].Tracks[SelectedTrackIndex];

            if (SelectedTabIndex < _musicLibrary.Playlists.Count)
            {
                _musicLibrary.Playlists[SelectedTabIndex].SelectedTrackId = SelectedTrack.Id;
            }

            _dataGrid.SelectedItem = SelectedTrack;
            _dataGrid.SelectedIndex = SelectedTrackIndex;

            // Scroll into view at lower priority
            _dataGrid.Dispatcher.BeginInvoke(new Action(() =>
            {
                _dataGrid.ScrollIntoView(SelectedTrack);
            }), System.Windows.Threading.DispatcherPriority.Background);

            WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));
            return SelectedTrack;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error selecting first track");
            return null!;
        }
    }

    private async Task CreatePlaylistFromFolderAsync(string folderPath, IProgress<ProgressData>? progress = null)
    {
        try
        {
            string folderName = Path.GetFileName(folderPath);
            string uniqueName = _playlistManagerService.GetUniquePlaylistName(folderName);

            PlaylistTab newTab = await _playlistManagerService.CreatePlaylistTabAsync(uniqueName);

            await _uiDispatcher.InvokeAsync(() =>
            {
                TabList.Add(newTab);
                SelectedTab = newTab;
                SelectedTabIndex = TabList.Count - 1;
                _tabControl!.SelectedIndex = SelectedTabIndex;
                // Do not override ItemsSource; XAML binding will pick up Tracks automatically
            });

            List<MediaFile> importedTracks = await _fileImportService.ImportFolderAsync(folderPath, progress);

            if (importedTracks.Any())
            {
                bool success = await _playlistManagerService.AddTracksToPlaylistAsync(uniqueName, importedTracks);

                if (success)
                {
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        foreach (MediaFile track in importedTracks)
                        {
                            SelectedTab!.Tracks.Add(track); // ObservableCollection auto-notifies!
                        }

                        // Set selected track if needed
                        if (_dataGrid!.SelectedItem == null && SelectedTab!.Tracks.Any())
                        {
                            _dataGrid.SelectedIndex = 0;
                            _dataGrid.ScrollIntoView(_dataGrid.SelectedItem!);
                        }
                    });

                    // Set first track as selected
                    Playlist? playlist = _musicLibrary.Playlists.FirstOrDefault(p => p.Name == uniqueName);
                    if (playlist != null && importedTracks.Any())
                    {
                        playlist.SelectedTrackId = importedTracks.First().Id;
                    }
                }
                else
                {
                    _logger.LogError("Failed to add tracks to new playlist");
                }
            }
            else
            {
                _logger.LogWarning("No tracks found in folder: {FolderPath}", folderPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create playlist from folder: {FolderPath}", folderPath);
            await _uiDispatcher.InvokeAsync(() =>
            {
                progress?.Report(new ProgressData
                {
                    IsProcessing = false,
                    Status = "Error creating playlist from folder"
                });
            });
        }
    }

    public void RightMouseDownTabSelect(string tabName)
    {
        try
        {
            PlaylistTab? targetTab = TabList.FirstOrDefault(p => p.Name == tabName);
            if (targetTab != null)
            {
                int index = TabList.IndexOf(targetTab);
                if (index >= 0 && _tabControl != null)
                {
                    _tabControl.SelectedIndex = index;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error selecting tab by right-click: {TabName}", tabName);
        }
    }

    [RelayCommand]
    public async Task RenamePlaylistAsync((PlaylistTab Tab, string? OldName) args)
    {
        if (string.IsNullOrEmpty(args.Tab.Name) || args.OldName == null)
            return;

        try
        {
            bool success = await _playlistManagerService.RenamePlaylistAsync(args.OldName, args.Tab.Name);
            if (!success)
            {
                // Revert the tab name if rename failed
                await _uiDispatcher.InvokeAsync(() =>
                {
                    args.Tab.Name = args.OldName;
                });
                _logger.LogWarning("Failed to rename playlist from '{OldName}' to '{NewName}'", args.OldName, args.Tab.Name);
            }
            else
            {
                // Update the selected playlist reference
                SelectedPlaylist = _musicLibrary.Playlists.FirstOrDefault(p => p.Name == args.Tab.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RenamePlaylistAsync failed for '{OldName}' to '{NewName}'", args.OldName, args.Tab.Name);

            // Revert the tab name on error
            await _uiDispatcher.InvokeAsync(() =>
            {
                args.Tab.Name = args.OldName;
            });
        }
    }

    [RelayCommand]
    private async Task ReorderTabs((int FromIndex, int ToIndex) indices)
    {
        if (indices.FromIndex < 0 || indices.ToIndex < 0 ||
            indices.FromIndex >= TabList.Count || indices.ToIndex >= TabList.Count)
        {
            _logger.LogWarning("ReorderTabs called with invalid indices: from {FromIndex} to {ToIndex}",
                indices.FromIndex, indices.ToIndex);
            return;
        }

        if (indices.FromIndex == indices.ToIndex)
        {
            return; // Nothing to do
        }

        try
        {
            // Remember the currently selected tab
            PlaylistTab? currentlySelectedTab = SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count
                ? TabList[SelectedTabIndex]
                : null;

            // Reorder in the UI
            PlaylistTab movedTab = TabList[indices.FromIndex];
            await _uiDispatcher.InvokeAsync(() =>
            {
                TabList.RemoveAt(indices.FromIndex);
                TabList.Insert(indices.ToIndex, movedTab);
            });

            // Reorder in the database/service
            bool success = await _playlistManagerService.ReorderPlaylistsAsync(indices.FromIndex, indices.ToIndex);

            if (success)
            {
                // Update the selected tab index if needed
                if (currentlySelectedTab != null)
                {
                    int newSelectedIndex = TabList.IndexOf(currentlySelectedTab);
                    if (newSelectedIndex >= 0)
                    {
                        SelectedTabIndex = newSelectedIndex;
                        _settingsManager.Settings.SelectedTabIndex = newSelectedIndex;
                        _settingsManager.SaveSettings(nameof(AppSettings.SelectedTabIndex));
                    }
                }

                _logger.LogInformation("Successfully reordered tab from index {FromIndex} to {ToIndex}",
                    indices.FromIndex, indices.ToIndex);
            }
            else
            {
                // Revert the UI change if database update failed
                _logger.LogError("Failed to reorder tabs in database, reverting UI changes");
                await _uiDispatcher.InvokeAsync(() =>
                {
                    TabList.RemoveAt(indices.ToIndex);
                    TabList.Insert(indices.FromIndex, movedTab);
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering tabs from {FromIndex} to {ToIndex}",
                indices.FromIndex, indices.ToIndex);
        }
    }

    // PlayerControlsViewModel helpers
    public MediaFile? PreviousMediaFile()
    {
        try
        {
            if (SelectedTabIndex < 0 || SelectedTabIndex >= TabList.Count)
            {
                _logger.LogWarning("PreviousMediaFile: Invalid SelectedTabIndex {Index} (TabList count: {Count})",
                    SelectedTabIndex, TabList.Count);
                return null;
            }

            PlaylistTab tab = TabList[SelectedTabIndex];

            if (tab?.Tracks == null || !tab.Tracks.Any())
            {
                _logger.LogWarning("PreviousMediaFile: No tracks available in TabList for tab {Index}",
                    SelectedTabIndex);
                return null;
            }

            List<MediaFile> currentTracks = tab.Tracks.ToList();
            int currentIndex = SelectedTrackIndex;

            if (currentIndex < 0 || currentIndex >= currentTracks.Count)
            {
                _logger.LogWarning("PreviousMediaFile: Invalid current track index {Index}", currentIndex);
                currentIndex = 0;
            }

            int previousIndex = _trackNavigationService.GetPreviousTrackIndex(currentTracks, currentIndex, _shuffleMode);

            if (previousIndex < 0 || previousIndex >= currentTracks.Count)
            {
                _logger.LogError("PreviousMediaFile: Invalid previous track index {Index}", previousIndex);
                return null;
            }

            return NavigateToTrack(currentTracks, previousIndex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting previous media file");
            return null;
        }
    }

    public MediaFile? NextMediaFile()
    {
        try
        {
            if (SelectedTabIndex < 0 || SelectedTabIndex >= TabList.Count)
            {
                _logger.LogWarning("NextMediaFile: Invalid SelectedTabIndex {Index} (TabList count: {Count})",
                    SelectedTabIndex, TabList.Count);
                return null;
            }

            PlaylistTab tab = TabList[SelectedTabIndex];

            if (tab?.Tracks == null || !tab.Tracks.Any())
            {
                _logger.LogWarning("NextMediaFile: No tracks available in TabList for tab {Index}",
                    SelectedTabIndex);
                return null;
            }

            List<MediaFile> currentTracks = tab.Tracks.ToList();
            int currentIndex = SelectedTrackIndex;

            if (currentIndex < 0 || currentIndex >= currentTracks.Count)
            {
                _logger.LogWarning("NextMediaFile: Invalid current track index {Index}", currentIndex);
                currentIndex = 0;
            }

            int nextIndex = _trackNavigationService.GetNextTrackIndex(currentTracks, currentIndex, _shuffleMode);

            if (nextIndex < 0 || nextIndex >= currentTracks.Count)
            {
                _logger.LogError("NextMediaFile: Invalid next track index {Index}", nextIndex);
                return null;
            }

            return NavigateToTrack(currentTracks, nextIndex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting next media file");
            return null;
        }
    }
}
