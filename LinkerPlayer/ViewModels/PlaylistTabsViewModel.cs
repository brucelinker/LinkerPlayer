using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging; // retained for other messages
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.Windows;
using LinkerPlayer.UserControls;
using ManagedBass;
using Microsoft.Extensions.DependencyInjection;
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
using System.Windows.Media;

namespace LinkerPlayer.ViewModels;

public interface IPlaylistTabsViewModel
{
    PlaylistTab? SelectedTab { get; }
    int SelectedTabIndex { get; }
    Playlist? SelectedPlaylist { get; }
    PlaybackState State { get; }
    ObservableCollection<PlaylistTab> TabList { get; }
    bool AllowDrop { get; }
    ProgressData ProgressInfo { get; }
    MediaFile? ActiveTrack { get; set; }
    int SelectedTrackIndex { get; set; }
    MediaFile? SelectedTrack { get; set; }
    void LoadPlaylistTabs();
    Task LoadSelectedPlaylistTracksAsync();
    Task LoadOtherPlaylistTracksAsync();
}

public partial class PlaylistTabsViewModel : ObservableObject, IPlaylistTabsViewModel
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

    private readonly IMusicLibrary _musicLibrary;
    private readonly ILogger<PlaylistTabsViewModel> _logger;

    // New services
    private readonly IFileImportService _fileImportService;
    private readonly IPlaylistManagerService _playlistManagerService;
    private readonly ITrackNavigationService _trackNavigationService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IDatabaseSaveService _databaseSaveService; // NEW: Debounced save service
    private readonly ISelectionService _selectionService; // new
    private readonly ISharedDataModel _sharedDataModel; // switch to interface
    private readonly ISettingsManager _settingsManager; // restore

    // Expose for legacy consumers if needed
    public ISharedDataModel SharedDataModel => _sharedDataModel;

    // ADD missing internal UI/state fields
    private TabControl? _tabControl; // holds TabControl reference
    private DataGrid? _dataGrid;     // holds current DataGrid reference
    private bool _shuffleMode;       // shuffle flag

    // ADD back filter constants used by dialogs
    private const string SupportedAudioFilter = "(*.mp3; *.flac; *.ape; *.ac3; *.dts; *.m4k; *.mka; *.mp4; *.mpc; *.ofr; *.ogg; *.opus; *.wav; *.wma; *.wv)|*.mp3; *.flac; *.ape; *.ac3; *.dts; *.m4k; *.mka; *.mp4; *.mpc; *.ofr; *.ogg; *.opus; *.wav; *.wma; *.wv";
    private const string SupportedPlaylistFilter = "(*.m3u;*.pls;*.wpl;*.zpl)|*.m3u;*.pls;*.wpl;*.zpl";
    private const string SupportedFilters = $"Audio Formats {SupportedAudioFilter}|Playlist Files {SupportedPlaylistFilter}|All files (*.*)|*.*"; // restore

    // SINGLE canonical selection properties (remove duplicates below)
    public MediaFile? ActiveTrack
    {
        get => _sharedDataModel.ActiveTrack;
        set { if (!ReferenceEquals(_sharedDataModel.ActiveTrack, value) && value != null) { _sharedDataModel.UpdateActiveTrack(value); OnPropertyChanged(nameof(ActiveTrack)); } }
    }

    public int SelectedTrackIndex
    {
        get => _sharedDataModel.SelectedTrackIndex;
        set { _sharedDataModel.UpdateSelectedTrackIndex(value); OnPropertyChanged(nameof(SelectedTrackIndex)); }
    }

    public MediaFile? SelectedTrack
    {
        get => _selectionService.CurrentTrack;
        set
        {
            if (value == null)
            {
                _selectionService.SetTrack(null, -1);
                _sharedDataModel.UpdateSelectedTrackIndex(-1);
            }
            else
            {
                int idx = SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count ? TabList[SelectedTabIndex].Tracks.IndexOf(value) : -1;
                _selectionService.SetTrack(value, idx);
                if (idx >= 0)
                {
                    _sharedDataModel.UpdateSelectedTrackIndex(idx);
                }
            }
            OnPropertyChanged(nameof(SelectedTrack));
        }
    }

    public PlaylistTabsViewModel(
        IMusicLibrary musicLibrary,
        ISharedDataModel sharedDataModel,
        ISettingsManager settingsManager,
        IFileImportService fileImportService,
        IPlaylistManagerService playlistManagerService,
        ITrackNavigationService trackNavigationService,
        IUiDispatcher uiDispatcher,
        IDatabaseSaveService databaseSaveService,
        ISelectionService selectionService,
        ILogger<PlaylistTabsViewModel> logger)
    {
        _musicLibrary = musicLibrary ?? throw new ArgumentNullException(nameof(musicLibrary));
        _fileImportService = fileImportService ?? throw new ArgumentNullException(nameof(fileImportService));
        _playlistManagerService = playlistManagerService ?? throw new ArgumentNullException(nameof(playlistManagerService));
        _trackNavigationService = trackNavigationService ?? throw new ArgumentNullException(nameof(trackNavigationService));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _databaseSaveService = databaseSaveService ?? throw new ArgumentNullException(nameof(databaseSaveService));
        _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        _sharedDataModel = sharedDataModel ?? throw new ArgumentNullException(nameof(sharedDataModel));
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        try
        {
            _shuffleMode = _settingsManager.Settings.ShuffleMode;
            AllowDrop = true;
            RegisterMessages();
            _logger.LogInformation("PlaylistTabsViewModel initialized successfully (SelectionService)");
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

    public void OnDataGridLoaded(object sender, RoutedEventArgs _)
    {
        if (sender is DataGrid dataGrid)
        {
            _dataGrid = dataGrid;
            if (SelectedTrack != null && SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count)
            {
                if (SelectedTrackIndex < 0)
                {
                    SelectedTrackIndex = TabList[SelectedTabIndex].Tracks.IndexOf(SelectedTrack);
                }
                _dataGrid.SelectedItem = SelectedTrack;
                _dataGrid.SelectedIndex = SelectedTrackIndex;
                _selectionService.SetTrack(SelectedTrack, SelectedTrackIndex);
            }
            else
            {
                SelectedPlaylist = GetSelectedPlaylist();
                if (SelectedTrack != null)
                {
                    _dataGrid.SelectedItem = SelectedTrack;
                    _dataGrid.SelectedIndex = SelectedTrackIndex;
                    _selectionService.SetTrack(SelectedTrack, SelectedTrackIndex);
                }
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
        if (_tabControl == null && sender is TabControl tc)
        {
            _tabControl = tc;
        }
        if (SelectedTabIndex < 0 || SelectedTabIndex >= TabList.Count)
        {
            return;
        }
        PlaylistTab tab = TabList[SelectedTabIndex];
        _selectionService.SetTab(tab);
        if (_dataGrid == null)
        {
            SelectedPlaylist = GetSelectedPlaylist();
            return;
        }
        _selectionService.SetMultiSelection(Enumerable.Empty<MediaFile>());
        if (tab.Tracks.Count == 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    IEnumerable<MediaFile> tracks = _playlistManagerService.LoadPlaylistTracks(tab.Name);
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (MediaFile t in tracks)
                        {
                            tab.Tracks.Add(t);
                        }

                        if (tab.Tracks.Any())
                        {
                            MediaFile first = tab.Tracks.First();
                            int idx = 0;
                            _selectionService.SetTrack(first, idx);
                            SelectedTrackIndex = idx;
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load tracks for tab '{TabName}'", tab.Name);
                }
            });
        }
        SelectedPlaylist = GetSelectedPlaylist();
    }

    public void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs _)
    {
        _dataGrid = sender as DataGrid;
        if (_dataGrid != null && _dataGrid.SelectedItems.Count > 0)
        {
            List<MediaFile> selectedTracks = _dataGrid.SelectedItems.Cast<MediaFile>().ToList();
            _selectionService.SetMultiSelection(selectedTracks);
            MediaFile selectedTrack = selectedTracks.First();
            int index = _dataGrid.Items.IndexOf(selectedTrack);
            _selectionService.SetTrack(selectedTrack, index);
            SelectedTrack = selectedTrack; // property updates index in shared model
            if (SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count)
            {
                PlaylistTab tab = TabList[SelectedTabIndex];
                tab.SelectedTrack = SelectedTrack;
                tab.SelectedIndex = index;
            }
            _settingsManager.Settings.SelectedTrackId = SelectedTrack.Id;
            _settingsManager.SaveSettings(nameof(AppSettings.SelectedTrackId));
        }
        else
        {
            _selectionService.SetMultiSelection(Enumerable.Empty<MediaFile>());
            _selectionService.SetTrack(null, -1);
            SelectedTrack = null;
        }
    }

    public void OnDataGridSorted(string propertyName, ListSortDirection direction)
    {
        if (_dataGrid?.SelectedItem is not MediaFile saveSelectedItem)
        {
            return;
        }

        try
        {
            List<MediaFile> sortedList = TabList[SelectedTabIndex].Tracks.ToList();
            sortedList.Sort((x, y) =>
            {
                object? propX = x.GetType().GetProperty(propertyName)?.GetValue(x);
                object? propY = y.GetType().GetProperty(propertyName)?.GetValue(y);

                if (propX == null && propY == null)
                {
                    return 0;
                }

                if (propX == null)
                {
                    return direction == ListSortDirection.Ascending ? -1 : 1;
                }

                if (propY == null)
                {
                    return direction == ListSortDirection.Ascending ? 1 : -1;
                }

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
        {
            return;
        }

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
            // Centralize through SelectedTrack property (which invokes SelectionService)
            SelectedTrack = selectedTrack;
            ActiveTrack = selectedTrack;
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
                {
                    continue;
                }
                PlaylistTab tab = new PlaylistTab { Name = playlist.Name }; // FIX: removed read-only Tracks assignment
                TabList.Add(tab);
            }
            _logger.LogInformation("Created {Count} playlist tabs (tracks not yet loaded)", TabList.Count);

            int savedIndex = _settingsManager.Settings.SelectedTabIndex;
            if (savedIndex < 0 || savedIndex >= TabList.Count)
            {
                savedIndex = TabList.Count > 0 ? 0 : -1;
            }

            if (savedIndex >= 0)
            {
                PlaylistTab initialTab = TabList[savedIndex];

                // Preload tracks for initial tab synchronously so selection can resolve before DataGrid loads
                if (initialTab.Tracks.Count == 0)
                {
                    IEnumerable<MediaFile> preloadTracks = _playlistManagerService.LoadPlaylistTracks(initialTab.Name);
                    foreach (MediaFile track in preloadTracks)
                    {
                        initialTab.Tracks.Add(track);
                    }
                    _logger.LogInformation("Preloaded {Count} tracks for initial tab '{Name}'", initialTab.Tracks.Count, initialTab.Name);
                }

                // Restore previously selected track (by Id) if possible, else fallback to first
                string? selectedTrackId = _musicLibrary.Playlists[savedIndex].SelectedTrackId;
                MediaFile? restored = null;
                if (!string.IsNullOrWhiteSpace(selectedTrackId))
                {
                    restored = initialTab.Tracks.FirstOrDefault(t => string.Equals(t.Id, selectedTrackId, StringComparison.Ordinal));
                }

                SelectedTabIndex = savedIndex;
                if (restored != null)
                {
                    SelectedTrack = restored;
                    SelectedTrackIndex = initialTab.Tracks.IndexOf(restored);
                }
                else if (initialTab.Tracks.Any())
                {
                    SelectedTrackIndex = 0;
                    SelectedTrack = initialTab.Tracks[0];
                    _musicLibrary.Playlists[savedIndex].SelectedTrackId = SelectedTrack.Id;
                }
                // Removed SelectedTrackChangedMessage broadcast; TrackInfo now listens to SelectionService
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load playlist tabs");
        }
    }

    /// <summary>
    /// Loads tracks for the selected playlist (called when SelectedTabIndex changes)
    /// </summary>
    public async Task LoadSelectedPlaylistTracksAsync()
    {
        try
        {
            if (SelectedTabIndex < 0 || SelectedTabIndex >= TabList.Count)
            {
                return;
            }

            PlaylistTab selectedTab = TabList[SelectedTabIndex];

            // Skip if already loaded
            if (selectedTab.Tracks.Count > 0)
            {
                return;
            }

            // Load on background thread to avoid blocking UI
            IEnumerable<MediaFile> tracks = await Task.Run(() =>
                _playlistManagerService.LoadPlaylistTracks(selectedTab.Name)
            );

            foreach (MediaFile track in tracks)
            {
                selectedTab.Tracks.Add(track);
            }

            _logger.LogInformation("Loaded {Count} tracks for playlist '{Name}'", selectedTab.Tracks.Count, selectedTab.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load selected playlist tracks");
        }
    }

    /// <summary>
    /// Loads tracks for non-selected playlists in the background
    /// </summary>
    public async Task LoadOtherPlaylistTracksAsync()
    {
        try
        {
            for (int i = 0; i < TabList.Count; i++)
            {
                // Skip the selected playlist (already loaded)
                if (i == SelectedTabIndex)
                {
                    continue;
                }

                PlaylistTab tab = TabList[i];

                // Skip if already loaded
                if (tab.Tracks.Count > 0)
                {
                    continue;
                }

                // Load on background thread
                IEnumerable<MediaFile> tracks = await Task.Run(() =>
                    _playlistManagerService.LoadPlaylistTracks(tab.Name)
                );

                // Use Dispatcher to add tracks on UI thread
                await _uiDispatcher.InvokeAsync(() =>
                {
                    foreach (MediaFile track in tracks)
                    {
                        tab.Tracks.Add(track);
                    }
                });

                _logger.LogInformation("Loaded {Count} tracks for background playlist '{Name}'", tab.Tracks.Count, tab.Name);

                // Yield to UI thread occasionally
                await Task.Delay(10);
            }

            _logger.LogInformation("Finished loading all playlist tracks in background");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load other playlist tracks");
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
                if (_tabControl != null)
                {
                    _tabControl.SelectedIndex = TabList.Count - 1;
                }
                SelectedTabIndex = TabList.Count - 1;
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
        // Ensure we have a selected playlist
        if (SelectedPlaylist == null)
        {
            SelectedPlaylist = GetSelectedPlaylist();
        }

        // Try to determine selected item if DataGrid reference is missing
        if (_dataGrid?.SelectedItem == null && SelectedTrack != null && SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count)
        {
            int idxFromSelected = TabList[SelectedTabIndex].Tracks.IndexOf(SelectedTrack);
            if (idxFromSelected >= 0)
            {
                SelectedTrackIndex = idxFromSelected;
                if (_dataGrid != null)
                {
                    _dataGrid.SelectedIndex = idxFromSelected;
                }
            }
        }

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
            // Notify PlayerControls (uses WeakReferenceMessenger)
            WeakReferenceMessenger.Default.Send(new DataGridPlayMessage(PlaybackState.Playing));
        }
    }

    [RelayCommand]
    private void SelectFirstTrackCommand()
    {
        SelectFirstTrack();
    }

    [RelayCommand]
    public void ShowProperties()
    {
        try
        {
            IPropertiesViewModel propertiesVm = App.AppHost.Services.GetRequiredService<IPropertiesViewModel>();
            // Reuse the single PropertiesWindow from DI so only one instance exists
            PropertiesWindow window = App.AppHost.Services.GetRequiredService<PropertiesWindow>();
            window.DataContext = propertiesVm;

            // Show or activate on UI thread
            _ = _uiDispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (Application.Current?.MainWindow != null && !window.IsLoaded)
                    {
                        window.Owner = Application.Current.MainWindow;
                    }

                    if (window.IsVisible)
                    {
                        // Bring existing window to front
                        try
                        { window.Activate(); }
                        catch { }
                    }
                    else
                    {
                        window.Show();
                        try
                        { window.Activate(); }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to show or activate Properties window");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show Properties window");
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
        if (SelectedTab.Tracks.Any())
        {
            string? selectedTrackId = SelectedPlaylist.SelectedTrackId;
            MediaFile? trackInTab = null;
            if (!string.IsNullOrWhiteSpace(selectedTrackId))
            {
                trackInTab = SelectedTab.Tracks.FirstOrDefault(t => t.Id == selectedTrackId);
            }
            if (trackInTab != null)
            {
                SelectedTrack = trackInTab;
                SelectedTrackIndex = SelectedTab.Tracks.IndexOf(trackInTab);
                if (_dataGrid != null)
                {
                    _dataGrid.SelectedItem = SelectedTrack;
                    _dataGrid.SelectedIndex = SelectedTrackIndex;
                }
            }
            // Fallback: select first track if nothing selected
            if (SelectedTrack == null)
            {
                SelectedTrack = SelectedTab.Tracks[0];
                SelectedTrackIndex = 0;
                SelectedPlaylist.SelectedTrackId = SelectedTrack.Id;
                if (_dataGrid != null)
                {
                    _dataGrid.SelectedItem = SelectedTrack;
                    _dataGrid.SelectedIndex = 0;
                }
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
                        {
                            continue;
                        }

                        if (line.StartsWith("#"))
                        {
                            continue; // comment or directive
                        }

                        results.Add(line);
                    }

                    // If we successfully read any entries, break
                    if (results.Count > 0)
                    {
                        break;
                    }
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
        {
            return string.Empty;
        }

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
        {
            return 0;
        }

        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

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
                {
                    continue;
                }

                string name = Path.GetFileName(file);
                string nameNorm = NormalizeForCompare(name);
                string nameNoExtNorm = NormalizeForCompare(Path.GetFileNameWithoutExtension(name));
                if (nameNorm == targetNorm || nameNoExtNorm == targetNoExtNorm)
                {
                    return file;
                }

                int dist = LevenshteinDistance(nameNoExtNorm, targetNoExtNorm);
                if (dist < bestScore)
                {
                    bestScore = dist;
                    bestPath = file;
                    if (bestScore <= 2)
                    {
                        return bestPath;
                    }
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
                {
                    continue;
                }

                string name = Path.GetFileName(file);
                string nameNorm = NormalizeForCompare(name);
                string nameNoExtNorm = NormalizeForCompare(Path.GetFileNameWithoutExtension(name));
                if (nameNorm == targetNorm || nameNoExtNorm == targetNoExtNorm)
                {
                    return file;
                }

                int dist = LevenshteinDistance(nameNoExtNorm, targetNoExtNorm);
                if (dist < bestScore)
                {
                    bestScore = dist;
                    bestPath = file;
                    if (bestScore <= 1)
                    {
                        return bestPath;
                    }
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
                {
                    return dir;
                }

                int dist = LevenshteinDistance(nameNorm, targetNorm);
                if (dist < bestScore)
                {
                    bestScore = dist;
                    bestPath = dir;
                    if (bestScore <= 2)
                    {
                        return bestPath;
                    }
                }
            }
            return bestScore <= 3 ? bestPath : null;
        }
        catch { return null; }
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
        SelectedTrack = newTrack; // triggers selection service
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
        if (_dataGrid?.ItemsSource?.Cast<MediaFile>().Any() != true || !TabList.Any())
        {
            return null;
        }

        if (!TabList[SelectedTabIndex].Tracks.Any())
        {
            return null;
        }

        try
        {
            SelectedTrackIndex = 0;
            SelectedTrack = TabList[SelectedTabIndex].Tracks[SelectedTrackIndex];
            if (SelectedTabIndex < _musicLibrary.Playlists.Count)
            { _musicLibrary.Playlists[SelectedTabIndex].SelectedTrackId = SelectedTrack.Id; }
            _dataGrid.SelectedItem = SelectedTrack;
            _dataGrid.SelectedIndex = SelectedTrackIndex;
            _dataGrid.Dispatcher.BeginInvoke(new Action(() => { _dataGrid.ScrollIntoView(SelectedTrack); }), System.Windows.Threading.DispatcherPriority.Background);
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
                if (_tabControl != null)
                {
                    _tabControl.SelectedIndex = SelectedTabIndex;
                }
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
    public async Task RenamePlaylistAsync(PlaylistTab tab)
    {
        if (tab == null || string.IsNullOrWhiteSpace(tab.Name))
        {
            return;
        }

        string? oldName = _musicLibrary.Playlists.FirstOrDefault(p => p.Name == tab.Name)?.Name;
        // Attempt to find original playlist by SelectedPlaylist if same reference
        if (SelectedPlaylist != null && SelectedPlaylist.Name != tab.Name && oldName == null)
        {
            oldName = SelectedPlaylist.Name;
        }

        // If oldName is null we cannot rename (no previous value). Just exit.
        if (oldName == null) // || oldName.Equals(tab.Name, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            bool success = await _playlistManagerService.RenamePlaylistAsync(oldName, tab.Name);
            if (!success)
            {
                // Revert
                tab.Name = oldName;
                _logger.LogWarning("Failed to rename playlist from '{OldName}' to '{NewName}'", oldName, tab.Name);
            }
            else
            {
                SelectedPlaylist = _musicLibrary.Playlists.FirstOrDefault(p => p.Name == tab.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RenamePlaylistAsync failed for '{OldName}' to '{NewName}'", oldName, tab.Name);
            tab.Name = oldName;
        }
    }

    [RelayCommand]
    private async Task ReorderTabs((int FromIndex, int ToIndex) indices)
    {
        int count = TabList.Count;
        if (count == 0)
        {
            return;
        }

        // Normalize indices into valid bounds; allow ToIndex == count (drop after last) by clamping to last
        int fromIndex = Math.Clamp(indices.FromIndex, 0, count - 1);
        int toIndex = Math.Clamp(indices.ToIndex, 0, count - 1);

        if (fromIndex != toIndex)
        {
            try
            {
                // Remember the currently selected tab
                PlaylistTab? currentlySelectedTab = SelectedTabIndex >= 0 && SelectedTabIndex < count
                    ? TabList[SelectedTabIndex]
                    : null;

                // Reorder in the UI
                PlaylistTab movedTab = TabList[fromIndex];
                await _uiDispatcher.InvokeAsync(() =>
                {
                    TabList.RemoveAt(fromIndex);
                    TabList.Insert(toIndex, movedTab);
                });

                // Reorder in the database/service
                bool success = await _playlistManagerService.ReorderPlaylistsAsync(fromIndex, toIndex);

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
                        fromIndex, toIndex);
                }
                else
                {
                    // Revert the UI change if database update failed
                    _logger.LogError("Failed to reorder tabs in database, reverting UI changes");
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        TabList.RemoveAt(toIndex);
                        TabList.Insert(fromIndex, movedTab);
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reordering tabs from {FromIndex} to {ToIndex}",
                    fromIndex, toIndex);
            }
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

    [RelayCommand]
    private void BeginRenameTab(PlaylistTab tab)
    {
        if (tab == null)
        {
            return;
        }
        // Find the EditableTabHeaderControl for this tab and set edit mode
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            foreach (Window window in Application.Current.Windows)
            {
                foreach (System.Windows.Controls.TabControl? tabControl in FindVisualChildren<System.Windows.Controls.TabControl>(window))
                {
                    foreach (System.Windows.Controls.TabItem? tabItem in FindVisualChildren<System.Windows.Controls.TabItem>(tabControl))
                    {
                        if (tabItem.DataContext == tab)
                        {
                            EditableTabHeaderControl? header = FindVisualChild<EditableTabHeaderControl>(tabItem);
                            if (header != null)
                            {
                                header.SetEditMode(true);
                                return;
                            }
                        }
                    }
                }
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj != null)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T t)
                {
                    yield return t;
                }
                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject depObj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
            if (child is T t)
            {
                return t;
            }
            T? childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
            {
                return childOfChild;
            }
        }
        return null;
    }
}
