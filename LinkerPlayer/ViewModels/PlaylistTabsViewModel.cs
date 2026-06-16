using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Core;
using LinkerPlayer.Interop;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.Services.Playback;
using LinkerPlayer.Windows;
using ManagedBass;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PlaylistsNET.Content;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace LinkerPlayer.ViewModels;

public interface IPlaylistTabsViewModel
{
    ITabData? SelectedTab { get; } // Can be PlaylistTab or MusicLibraryTab
    int SelectedTabIndex { get; }
    Playlist? SelectedPlaylist { get; }
    PlaybackState State { get; }
    ObservableCollection<ITabData> TabList { get; } // Contains MusicLibraryTab + PlaylistTab instances
    ObservableCollection<DataGridColumn> VisibleColumns { get; }
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
    // Manually implement SelectedTab and TabList to avoid source generator type conflicts
    private ITabData? _selectedTab; // Can be PlaylistTab or MusicLibraryTab
    private readonly IMusicBrainzRatingService _mbService;

    public ITabData? SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    private ObservableCollection<ITabData> _tabList = []; // Contains MusicLibraryTab + PlaylistTab instances
    public ObservableCollection<ITabData> TabList
    {
        get => _tabList;
        set => SetProperty(ref _tabList, value);
    }

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private Playlist? _selectedPlaylist;
    [ObservableProperty] private PlaybackState _state;
    [ObservableProperty] private bool _allowDrop;
    private int _saveProgressCount;
    [ObservableProperty]
    private ProgressData _progressInfo = new()
    {
        IsProcessing = false,
        ProcessedTracks = 0,
        TotalTracks = 1,
        Status = string.Empty
    };

    public ObservableCollection<DataGridColumn> VisibleColumns { get; } = new ObservableCollection<DataGridColumn>();

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
    private readonly IPlaybackCoordinator _playbackCoordinator;
    private readonly IImportCancellationService _importCancellationService;

    // Expose for legacy consumers if needed
    public ISharedDataModel SharedDataModel => _sharedDataModel;

    // ADD missing internal UI/state fields
    private TabControl? _tabControl; // holds TabControl reference
    private DataGrid? _dataGrid;     // holds current DataGrid reference
    private bool _shuffleMode;       // shuffle flag
    private MusicLibraryTab? _musicLibraryTab; // The permanent Music Library tab (always first)

    // Debounce timer — collapses rapid dirty-state bursts (e.g. editing 25 tracks at once)
    // into a single HasDirtyTracks notification so the Save button updates promptly but
    // doesn't thrash on every individual PropertyChanged event.
    private System.Windows.Threading.DispatcherTimer? _dirtyDebounceTimer;

    // ADD back filter constants used by dialogs
    private const string SupportedAudioFilter = "(*.mp3; *.flac; *.ape; *.ac3; *.dsd; *.dsf; *.dts; *.m4k; *.mka; *.mp4; *.mpc; *.ofr; *.ogg; *.opus; *.wav; *.wma; *.wv)|*.mp3; *.flac; *.ape; *.ac3; *.dts; *.m4k; *.mka; *.mp4; *.mpc; *.ofr; *.ogg; *.opus; *.wav; *.wma; *.wv";
    private const string SupportedPlaylistFilter = "(*.m3u;*.pls;*.wpl;*.zpl)|*.m3u;*.pls;*.wpl;*.zpl";
    private const string SupportedFilters = $"Audio Formats {SupportedAudioFilter}|Playlist Files {SupportedPlaylistFilter}|All files (*.*)|*.*";

    private List<string> _selectedColumnNames = new()
    {
        "Playing",
        "Title",
        "Artist"
    };

    // SINGLE canonical selection properties (remove duplicates below)
    public MediaFile? ActiveTrack
    {
        get => _sharedDataModel.ActiveTrack;
        set
        {
            if (!ReferenceEquals(_sharedDataModel.ActiveTrack, value))
            {
                _sharedDataModel.UpdateActiveTrack(value);
                OnPropertyChanged(nameof(ActiveTrack));
            }
        }
    }

    public int SelectedTrackIndex
    {
        get => _sharedDataModel.SelectedTrackIndex;
        set { _sharedDataModel.UpdateSelectedTrackIndex(value); OnPropertyChanged(nameof(SelectedTrackIndex)); }
    }

    public MediaFile? SelectedTrack
    {
        get => _selectionService.CurrentTrack;   // derived from service
        set
        {
            if (value == null)
            {
                _selectionService.SetTrack(null, -1);
                _sharedDataModel.UpdateSelectedTrackIndex(-1);
            }
            else
            {
                int idx = SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count
                    ? TabList[SelectedTabIndex].Tracks.IndexOf(value)
                    : -1;

                _selectionService.SetTrack(value, idx);
                if (idx >= 0)
                {
                    _sharedDataModel.UpdateSelectedTrackIndex(idx);
                }
            }
            OnPropertyChanged(nameof(SelectedTrack));
        }
    }

    public List<string> SelectedColumnNames => _selectedColumnNames;
    private bool _isInitialLoad = true;

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
        IPlaybackCoordinator playbackCoordinator,
        IImportCancellationService importCancellationService,
        IMusicBrainzRatingService mbService,
        ILogger<PlaylistTabsViewModel> logger)
    {
        _musicLibrary = musicLibrary ?? throw new ArgumentNullException(nameof(musicLibrary));
        _fileImportService = fileImportService ?? throw new ArgumentNullException(nameof(fileImportService));
        _playlistManagerService = playlistManagerService ?? throw new ArgumentNullException(nameof(playlistManagerService));
        _trackNavigationService = trackNavigationService ?? throw new ArgumentNullException(nameof(trackNavigationService));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _databaseSaveService = databaseSaveService ?? throw new ArgumentNullException(nameof(databaseSaveService));
        _mbService = mbService ?? throw new ArgumentNullException(nameof(mbService));
        _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        _sharedDataModel = sharedDataModel ?? throw new ArgumentNullException(nameof(sharedDataModel));
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _playbackCoordinator = playbackCoordinator ?? throw new ArgumentNullException(nameof(playbackCoordinator));
        _importCancellationService = importCancellationService ?? throw new ArgumentNullException(nameof(importCancellationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        try
        {
            _shuffleMode = _settingsManager.Settings.ShuffleMode;
            AllowDrop = true;
            RegisterMessages();
            _musicLibrary.LibraryLoaded += OnLibraryLoaded;

            if (_settingsManager.Settings.VisibleColumns == null ||
                _settingsManager.Settings.VisibleColumns.Count == 0)
            {
                _selectedColumnNames = new List<string> { "Title", "Artist", "Album", "Year", "Duration" };
                _settingsManager.Settings.VisibleColumns = _selectedColumnNames;
                _settingsManager.SaveSettings(nameof(AppSettings.VisibleColumns));
            }
            else
            {
                _selectedColumnNames = new List<string>(_settingsManager.Settings.VisibleColumns);
            }

            _logger.LogInformation("PlaylistTabsViewModel initialized successfully (SelectionService)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PlaylistTabsViewModel constructor: {Message}", ex.Message);
            throw;
        }

        // Debounce timer for dirty-state notifications; fires 80 ms after the last change.
        // Must be created on the UI thread (DispatcherTimer requires it).
        _dirtyDebounceTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _dirtyDebounceTimer.Tick += (_, __) =>
        {
            _dirtyDebounceTimer.Stop();
            OnPropertyChanged(nameof(HasDirtyTracks));
        };

        WeakReferenceMessenger.Default.Register<ActiveTrackChangedMessage>(this, (_, m) =>
        {
            if (!ReferenceEquals(_sharedDataModel.ActiveTrack, m.Value))
            {
                _sharedDataModel.UpdateActiveTrack(m.Value);
            }
            OnPropertyChanged(nameof(ActiveTrack));
            OnPropertyChanged(nameof(State));

            if (_dataGrid != null)
            {
                _ = _uiDispatcher.InvokeAsync(() =>
                {
                    IEditableCollectionView? ecv = _dataGrid.Items as IEditableCollectionView;
                    if (ecv == null || (!ecv.IsAddingNew && !ecv.IsEditingItem))
                    {
                        _dataGrid.Items.Refresh();
                    }
                });
            }
        });

        _logger.LogInformation("Constructor end - LastLibrarySelectedTrackId = {Id}", _settingsManager.Settings.LastLibrarySelectedTrackId);

        ResetSelectionState();
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

    public void ApplySelectedColumns(List<string> columns)
    {
        List<string> newList = columns ?? new List<string>();

        if (!newList.SequenceEqual(_selectedColumnNames))
        {
            _selectedColumnNames = newList;
            _settingsManager.Settings.VisibleColumns = _selectedColumnNames;
            _settingsManager.SaveSettings(nameof(AppSettings.VisibleColumns));
        }
    }

    public void UpdateSelectedColumnNames(List<string> columns)
    {
        _selectedColumnNames = columns ?? new List<string>();

        _settingsManager.Settings.VisibleColumns = _selectedColumnNames;
        _settingsManager.SaveSettings("VisibleColumns");
    }

    public void OnDataGridLoaded(object sender, RoutedEventArgs _)
    {
        if (sender is not DataGrid dataGrid)
            return;

        _dataGrid = dataGrid;

        // Force restored selection if we have one (this runs after DataGrid is ready)
        if (SelectedTab is MusicLibraryTab && SelectedTrack != null)
        {
            _logger.LogInformation("OnDataGridLoaded: Forcing restored Library selection '{Title}'", SelectedTrack.Title);

            _isSyncingGridSelection = true;
            try
            {
                dataGrid.SelectedItem = SelectedTrack;
                dataGrid.SelectedIndex = SelectedTrackIndex;
                dataGrid.ScrollIntoView(SelectedTrack);
            }
            finally
            {
                _isSyncingGridSelection = false;
            }
            return;
        }

        // Normal fallback for other cases
        if (SelectedTrack != null && SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count)
        {
            if (SelectedTrackIndex < 0)
                SelectedTrackIndex = TabList[SelectedTabIndex].Tracks.IndexOf(SelectedTrack);

            dataGrid.SelectedItem = SelectedTrack;
            _selectionService.SetTrack(SelectedTrack, SelectedTrackIndex);
        }
    }

    private void ResetSelectionState()
    {
        _selectionService.SetMultiSelection(Enumerable.Empty<MediaFile>());
        _selectionService.SetTrack(null, -1);
        SelectedTrack = null;
        SelectedTrackIndex = -1;
        _logger.LogDebug("ResetSelectionState called");
    }

    private void OnLibraryLoaded(object? sender, EventArgs e)
    {
        string? savedId = _settingsManager.Settings.LastLibrarySelectedTrackId;
        if (string.IsNullOrWhiteSpace(savedId))
        {
            _logger.LogDebug("OnLibraryLoaded: No saved track ID");
            return;
        }

        MediaFile? restored = _musicLibrary.MainLibrary.FirstOrDefault(t =>
            string.Equals(t.Id, savedId, StringComparison.Ordinal));

        if (restored == null)
        {
            _logger.LogWarning("OnLibraryLoaded: Saved ID {Id} not found", savedId);
            return;
        }

        _logger.LogInformation("OnLibraryLoaded: FINAL restoration to '{Title}'", restored.Title);

        // This is the last word on selection
        SelectedTrack = restored;
        SelectedTrackIndex = _musicLibrary.MainLibrary.IndexOf(restored);

        if (_dataGrid != null)
        {
            _isSyncingGridSelection = true;
            try
            {
                _dataGrid.SelectedItem = restored;
                _dataGrid.SelectedIndex = SelectedTrackIndex;
                _dataGrid.ScrollIntoView(restored);
            }
            finally
            {
                _isSyncingGridSelection = false;
            }
        }

        if (_musicLibraryTab != null)
        {
            _musicLibraryTab.ViewRefreshed -= OnLibraryViewRefreshed;
            _musicLibraryTab.ViewRefreshed += OnLibraryViewRefreshed;
        }
    }

    private void OnLibraryViewRefreshed(object? sender, EventArgs e)
    {
        if (_dataGrid == null || SelectedTrack == null || SelectedTab is not MusicLibraryTab)
            return;

        // Only restore if the DataGrid lost the selection
        if (ReferenceEquals(_dataGrid.SelectedItem, SelectedTrack))
            return;

        _dataGrid.SelectedItem = SelectedTrack;
        _dataGrid.SelectedIndex = SelectedTrackIndex;

        _dataGrid.Dispatcher.BeginInvoke(() =>
        {
            _dataGrid.ScrollIntoView(SelectedTrack);
        }, System.Windows.Threading.DispatcherPriority.Background);
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
            _tabControl = tc;

        if (SelectedTabIndex < 0 || SelectedTabIndex >= TabList.Count)
            return;

        ITabData tab = TabList[SelectedTabIndex];
        _selectionService.SetTab(tab as PlaylistTab);

        if (tab is MusicLibraryTab)
            return; // Library doesn't need lazy loading

        // Clear multi-selection when switching tabs
        _selectionService.SetMultiSelection(Enumerable.Empty<MediaFile>());

        if (_dataGrid == null)
        {
            SelectedPlaylist = GetSelectedPlaylist();
            return;
        }

        if (tab is PlaylistTab playlistTab && playlistTab.Tracks.Count == 0)
        {
            // Background lazy load (existing logic is fine)
            _ = Task.Run(async () => { /* ... existing lazy load ... */ });
        }

        SelectedPlaylist = GetSelectedPlaylist();
    }

    private bool _isSyncingGridSelection;

    public void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingGridSelection)
            return;

        _dataGrid = sender as DataGrid;

        if (_dataGrid?.SelectedItems.Count > 0)
        {
            List<MediaFile> selectedTracks = _dataGrid.SelectedItems.Cast<MediaFile>().ToList();
            MediaFile selectedTrack = _dataGrid.SelectedItem as MediaFile ?? selectedTracks[0];
            int index = _dataGrid.Items.IndexOf(selectedTrack);

            _logger.LogDebug("OnTrackSelectionChanged: '{Title}' (multi: {Count})", selectedTrack.Title, selectedTracks.Count);

            // SINGLE SOURCE OF TRUTH
            _selectionService.SetMultiSelection(selectedTracks);
            _selectionService.SetTrack(selectedTrack, index);

            // Update our property (triggers setter above)
            SelectedTrack = selectedTrack;
            SelectedTrackIndex = index;

            // Persist
            if (SelectedTab is MusicLibraryTab)
            {
                _settingsManager.Settings.LastLibrarySelectedTrackId = selectedTrack.Id;
                _settingsManager.SaveSettings(nameof(AppSettings.LastLibrarySelectedTrackId));
                _logger.LogInformation("✅ Saved Library selection: '{Title}'", selectedTrack.Title);
            }
            else if (GetSelectedPlaylist() is Playlist p)
            {
                p.SelectedTrackId = selectedTrack.Id;
                _databaseSaveService.RequestSave();
            }

            SyncGridSelection(selectedTrack, index);
            CheckHealthForTracks(selectedTracks);
        }
        else
        {
            _selectionService.SetMultiSelection(Enumerable.Empty<MediaFile>());
            _selectionService.SetTrack(null, -1);
            SelectedTrack = null;
            SelectedTrackIndex = -1;
        }
    }

    /// <summary>
    /// Checks file-system health for any selected tracks that haven't been checked yet
    /// (status == Unknown). Runs on a background thread; updates HealthStatus on the UI thread.
    /// </summary>
    private void CheckHealthForTracks(List<MediaFile> tracks)
    {
        List<MediaFile> toCheck = tracks.Where(t => t.HealthStatus == TrackHealthStatus.Unknown).ToList();
        if (toCheck.Count == 0)
            return;

        Task.Run(() =>
        {
            foreach (MediaFile track in toCheck)
            {
                TrackHealthStatus status;
                if (!File.Exists(track.Path))
                {
                    status = TrackHealthStatus.Missing;
                }
                else if (track.FileLastWriteTimeUtc.HasValue)
                {
                    try
                    {
                        DateTime diskTime = File.GetLastWriteTimeUtc(track.Path);
                        status = Math.Abs((diskTime - track.FileLastWriteTimeUtc.Value).TotalSeconds) > 2
                            ? TrackHealthStatus.Changed
                            : TrackHealthStatus.Ok;
                    }
                    catch { status = TrackHealthStatus.Ok; }
                }
                else
                {
                    status = TrackHealthStatus.Ok;
                }

                if (status != TrackHealthStatus.Unknown)
                {
                    _uiDispatcher.InvokeAsync(() => track.HealthStatus = status);
                }
            }
        });
    }

    private void SyncGridSelection(MediaFile? track, int index)
    {
        if (_dataGrid == null)
            return;

        try
        {
            _isSyncingGridSelection = true;

            if (!ReferenceEquals(_dataGrid.SelectedItem, track))
                _dataGrid.SelectedItem = track;

            if (_dataGrid.SelectedIndex != index)
                _dataGrid.SelectedIndex = index;
        }
        finally
        {
            _isSyncingGridSelection = false;
        }
    }

    public void OnDoubleClickDataGrid()
    {
        if (_dataGrid?.SelectedItem is not MediaFile selectedTrack)
            return;

        SelectedTrack = selectedTrack; // This already triggers everything

        Playlist? playlist = GetSelectedPlaylist();
        if (playlist != null)
        {
            playlist.SelectedTrackId = selectedTrack.Id;
        }
    }

    public void UpdateColumns(List<string> selectedColumnNames)
    {
        VisibleColumns.Clear();

        foreach (string name in selectedColumnNames)
        {
            VisibleColumns.Add(new DataGridTextColumn { Header = name, Binding = new Binding(name) });
        }
    }

    // Public methods called by UI controls - keep these for backward compatibility
    public void LoadPlaylistTabs()
    {
        try
        {
            // Clear any stale selection from previous session
            ResetSelectionState();

            TabList.Clear();

            // ALWAYS create the Music Library tab first (permanent, cannot be removed)
            _musicLibraryTab = new MusicLibraryTab(_musicLibrary.MainLibrary);
            TabList.Add(_musicLibraryTab);
            _logger.LogInformation("Created Music Library tab with {Count} tracks", _musicLibrary.MainLibrary.Count);

            // Load saved visible columns for the library tab
            if (_settingsManager.Settings.LibraryVisibleColumns != null && _settingsManager.Settings.LibraryVisibleColumns.Count > 0)
            {
                _musicLibraryTab.VisibleColumns = new List<string>(_settingsManager.Settings.LibraryVisibleColumns);
            }
            // Ensure Year is always included
            if (!_musicLibraryTab.VisibleColumns.Contains("Year"))
            {
                _musicLibraryTab.VisibleColumns.Add("Year");
                _settingsManager.Settings.LibraryVisibleColumns = _musicLibraryTab.VisibleColumns;
                _settingsManager.SaveSettings(nameof(AppSettings.LibraryVisibleColumns));
            }

            // Restore filter selections from settings
            _musicLibraryTab.RestoreFilterSelections(_settingsManager.Settings);

            // Then load user playlists
            List<Playlist> playlists = _musicLibrary.GetPlaylists();
            foreach (Playlist playlist in playlists)
            {
                if (string.IsNullOrWhiteSpace(playlist.Name))
                    continue;

                PlaylistTab tab = new PlaylistTab { Name = playlist.Name };
                TabList.Add(tab);
            }
            _logger.LogInformation("Created {Count} playlist tabs (tracks not yet loaded)", TabList.Count - 1);

            // Adjust saved index to account for Music Library tab at index 0
            int savedIndex = _settingsManager.Settings.SelectedTabIndex;
            if (savedIndex < 0 || savedIndex >= TabList.Count)
            {
                savedIndex = TabList.Count > 1 ? 1 : 0;
            }

            if (savedIndex >= 0)
            {
                ITabData initialTab = TabList[savedIndex];

                // Handle Music Library tab selection
                if (initialTab is MusicLibraryTab libraryTab)
                {
                    SelectedTabIndex = savedIndex;
                    _logger.LogInformation("Selected Music Library tab with {Count} tracks", _musicLibrary.MainLibrary.Count);

                    // Restore last selected track in Library if available
                    if (!string.IsNullOrWhiteSpace(_settingsManager.Settings.LastLibrarySelectedTrackId))
                    {
                        MediaFile? restored = _musicLibrary.MainLibrary.FirstOrDefault(t =>
                            string.Equals(t.Id, _settingsManager.Settings.LastLibrarySelectedTrackId, StringComparison.Ordinal));

                        if (restored != null)
                        {
                            SelectedTrack = restored;
                            SelectedTrackIndex = _musicLibrary.MainLibrary.IndexOf(restored);

                            _logger.LogInformation("Restored last Library selection in LoadPlaylistTabs: '{Title}'", restored.Title);

                            // Force if DataGrid is already available
                            if (_dataGrid != null)
                            {
                                ForceLibrarySelection(restored);
                            }

                            // Final force after DataGrid is fully ready
                            _ = _uiDispatcher.InvokeAsync(() => ForceLibrarySelection(restored));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load playlist tabs");
        }
        finally
        {
            _isInitialLoad = false;
        }
    }

    // Helper method
    private void ForceLibrarySelection(MediaFile track)
    {
        if (_dataGrid == null || track == null)
            return;

        _isSyncingGridSelection = true;
        try
        {
            _dataGrid.SelectedItem = track;
            _dataGrid.SelectedIndex = SelectedTrackIndex;
            _dataGrid.ScrollIntoView(track);
            _logger.LogInformation("ForceLibrarySelection applied: '{Title}'", track.Title);
        }
        finally
        {
            _isSyncingGridSelection = false;
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

            ITabData selectedTab = TabList[SelectedTabIndex];

            // Music Library tab already has tracks via MainLibrary reference - no loading needed
            if (selectedTab is MusicLibraryTab)
            {
                _logger.LogInformation("Music Library tab selected - tracks already loaded from MainLibrary");
                return;
            }

            // Handle PlaylistTab
            if (selectedTab is not PlaylistTab playlistTab)
            {
                return;
            }

            // Skip if already loaded
            if (playlistTab.Tracks.Count > 0)
            {
                return;
            }

            // Load on background thread to avoid blocking UI
            IEnumerable<MediaFile> tracks = await Task.Run(() =>
                _playlistManagerService.LoadPlaylistTracks(playlistTab.Name)
            );

            foreach (MediaFile track in tracks)
            {
                playlistTab.Tracks.Add(track);
            }

            _logger.LogInformation("Loaded {Count} tracks for playlist '{Name}'", playlistTab.Tracks.Count, playlistTab.Name);
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

                ITabData tab = TabList[i];

                // Skip Music Library tab (always loaded)
                if (tab is MusicLibraryTab)
                {
                    continue;
                }

                // Only process PlaylistTab instances
                if (tab is not PlaylistTab playlistTab)
                {
                    continue;
                }

                // Skip if already loaded
                if (playlistTab.Tracks.Count > 0)
                {
                    continue;
                }

                // Load on background thread
                IEnumerable<MediaFile> tracks = await Task.Run(() =>
                    _playlistManagerService.LoadPlaylistTracks(playlistTab.Name)
                );

                // Use Dispatcher to add tracks on UI thread
                await _uiDispatcher.InvokeAsync(() =>
                {
                    foreach (MediaFile track in tracks)
                    {
                        playlistTab.Tracks.Add(track);
                    }
                });

                _logger.LogInformation("Loaded {Count} tracks for background playlist '{Name}'", playlistTab.Tracks.Count, playlistTab.Name);

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

                if (importedTracks.Any() && _settingsManager.Settings.SkipSilenceEnabled)
                {
                    Services.Analysis.ITrackSilenceAnalyzer analyzer = App.AppHost.Services.GetRequiredService<Services.Analysis.ITrackSilenceAnalyzer>();

                    await Task.Run(async () =>
                    {
                        List<MediaFile> updated = new List<MediaFile>();
                        foreach (MediaFile track in importedTracks)
                        {
                            try
                            {
                                Services.Analysis.TrackSilenceAnalysisResult result = await analyzer.AnalyzeAsync(track);
                                track.LeadingSilenceMs = result.LeadingSilenceMs;
                                track.TrailingSilenceMs = result.TrailingSilenceMs;
                                updated.Add(track);
                            }
                            catch
                            {
                            }
                        }

                        if (updated.Count > 0)
                        {
                            await _musicLibrary.UpdateTracksAsync(updated, updateMetadata: false, updateAnalysis: true);
                        }
                    });
                }

                if (importedTracks.Any())
                {
                    // Resolve the active playlist; SelectedPlaylist may be null if UI state wasn't initialized
                    Playlist? targetPlaylist = SelectedPlaylist ?? GetSelectedPlaylist();

                    if (targetPlaylist == null)
                    {
                        _logger.LogWarning("No selected playlist available to add imported tracks");
                    }
                    else
                    {
                        bool success = await _playlistManagerService.AddTracksToPlaylistAsync(targetPlaylist.Name, importedTracks);

                        if (success && SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count)
                        {
                            if (TabList[SelectedTabIndex] is not PlaylistTab tab)
                            {
                                return;
                            }
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

                if (importedTracks.Any() && _settingsManager.Settings.SkipSilenceEnabled)
                {
                    Services.Analysis.ITrackSilenceAnalyzer analyzer = App.AppHost.Services.GetRequiredService<Services.Analysis.ITrackSilenceAnalyzer>();

                    await Task.Run(async () =>
                    {
                        List<MediaFile> updated = new List<MediaFile>();
                        foreach (MediaFile track in importedTracks)
                        {
                            try
                            {
                                Services.Analysis.TrackSilenceAnalysisResult result = await analyzer.AnalyzeAsync(track);
                                track.LeadingSilenceMs = result.LeadingSilenceMs;
                                track.TrailingSilenceMs = result.TrailingSilenceMs;
                                updated.Add(track);
                            }
                            catch
                            {
                            }
                        }

                        if (updated.Count > 0)
                        {
                            await _musicLibrary.UpdateTracksAsync(updated, updateMetadata: false, updateAnalysis: true);
                        }
                    });
                }

                if (importedTracks.Any())
                {
                    // If the current selected tab is a PlaylistTab, also add the imported tracks to it.
                    if (SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count && TabList[SelectedTabIndex] is PlaylistTab playlistTab)
                    {
                        bool success = await _playlistManagerService.AddTracksToPlaylistAsync(playlistTab.Name, importedTracks);
                        if (success)
                        {
                            await _uiDispatcher.InvokeAsync(() =>
                            {
                                foreach (MediaFile track in importedTracks)
                                {
                                    if (playlistTab.Tracks.All(t => t.Id != track.Id))
                                    {
                                        playlistTab.Tracks.Add(track); // ObservableCollection auto-notifies!
                                    }
                                }
                            });
                        }
                        else
                        {
                            _logger.LogError("Failed to add imported files to playlist {Name}", playlistTab.Name);
                        }
                    }
                    else
                    {
                        // Selected tab is not a playlist (likely Music Library) - files have been imported into the Library only.
                        _logger.LogInformation("Imported {Count} files into Music Library (no playlist selected)", importedTracks.Count);
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

        bool isLibraryTab = SelectedTab is MusicLibraryTab;
        if (_dataGrid?.SelectedItem == null || (!isLibraryTab && SelectedPlaylist == null))
        {
            _logger.LogWarning("Cannot remove track - no track or playlist selected (IsLibrary={IsLib}, HasPlaylist={HasPlaylist})", isLibraryTab, SelectedPlaylist != null);
            return;
        }

        try
        {
            ObservableCollection<MediaFile> tracks = TabList[SelectedTabIndex].Tracks;
            if (SelectedTrackIndex >= 0 && SelectedTrackIndex < tracks.Count)
            {
                MediaFile trackToRemove = (MediaFile)_dataGrid.SelectedItem;
                bool success;
                if (isLibraryTab)
                {
                    List<string> containingPlaylists = _musicLibrary.GetPlaylistsContainingTrack(trackToRemove.Id);
                    if (containingPlaylists.Any())
                    {
                        string playlistList = string.Join(Environment.NewLine, containingPlaylists);
                        MessageBoxResult result = MessageBox.Show(
                            $"The file is in use by the following playlist(s):{Environment.NewLine}{Environment.NewLine}{playlistList}{Environment.NewLine}{Environment.NewLine}Continue deleting from Library (this will remove it from all playlists)?",
                            "Track In Use",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result != MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }

                    await _musicLibrary.RemoveTrackFromLibraryAsync(trackToRemove.Id);
                    success = true;
                }
                else
                {
                    success = await _playlistManagerService.RemoveTrackFromPlaylistAsync(SelectedPlaylist!.Name, trackToRemove.Id);
                }

                if (success)
                {
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        if (SelectedTab is not MusicLibraryTab)
                        {
                            tracks.Remove(trackToRemove);
                        }
                        // Adjust selection
                        if (_dataGrid != null)
                        {
                            if (SelectedTrackIndex >= tracks.Count)
                            {
                                _dataGrid.SelectedIndex = Math.Max(0, tracks.Count - 1);
                            }
                            else
                            {
                                _dataGrid.SelectedIndex = SelectedTrackIndex;
                            }
                        }
                        _dataGrid?.UpdateLayout();
                    });
                }
                else
                {
                    _logger.LogError("Failed to remove track from playlist/library");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing track from playlist/library");
        }
    }

    [RelayCommand]
    private async Task RescanSelectedTracks()
    {
        if (_dataGrid == null)
            return;

        List<MediaFile> selected = _dataGrid.SelectedItems.Cast<MediaFile>().ToList();
        if (selected.Count == 0)
            return;

        // Determine what to do by checking the file system directly — do not rely on
        // HealthStatus being up to date, because FSW may have missed events.
        List<MediaFile> missing = selected.Where(t => !File.Exists(t.Path)).ToList();
        List<MediaFile> present = selected.Where(t => File.Exists(t.Path)).ToList();

        // Confirm removal of tracks whose files are gone
        if (missing.Count > 0)
        {
            string names = string.Join("\n  • ", missing.Select(t => t.FileName));
            MessageBoxResult answer = MessageBox.Show(
                $"The following {missing.Count} track(s) no longer exist on disk and will be removed from the library:\n\n  • {names}\n\nProceed?",
                "Remove Missing Tracks",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer == MessageBoxResult.Yes)
            {
                await _musicLibrary.RemoveTracksAsync(missing.Select(t => t.Id)).ConfigureAwait(false);
            }
        }

        // Refresh metadata for tracks that do exist
        if (present.Count > 0)
        {
            await Task.Run(() =>
            {
                foreach (MediaFile track in present)
                {
                    try
                    {
                        track.UpdateFromFileMetadata(raisePropertyChanged: true);
                        _uiDispatcher.InvokeAsync(() => track.HealthStatus = TrackHealthStatus.Ok);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to refresh metadata for {Path}", track.Path);
                    }
                }
            }).ConfigureAwait(false);

            await _musicLibrary.UpdateTracksAsync(present).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task AnalyzeSilence()
    {
        if (_dataGrid == null)
        {
            return;
        }

        List<MediaFile> targets = _dataGrid.SelectedItems.Cast<MediaFile>().ToList();
        IReadOnlyList<MediaFile> multiSelection = _selectionService.MultiSelection;
        if (targets.Count == 0 && multiSelection.Count > 0)
        {
            targets = multiSelection.ToList();
        }

        if (targets.Count == 0 && _dataGrid.SelectedItem is MediaFile single)
        {
            targets = new List<MediaFile> { single };
        }

        if (targets.Count == 0)
        {
            return;
        }

        try
        {
            _analyzeSilenceCts?.Cancel();
            _analyzeSilenceCts?.Dispose();
        }
        catch
        {
        }
        _analyzeSilenceCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _analyzeSilenceCts.Token;

        await _uiDispatcher.InvokeAsync(() =>
        {
            WeakReferenceMessenger.Default.Send(new ProgressValueMessage(new ProgressData
            {
                IsProcessing = true,
                ProcessedTracks = 0,
                TotalTracks = targets.Count,
                Status = "Analyzing silence..."
            }));
        });

        Services.Analysis.ITrackSilenceAnalyzer analyzer = App.AppHost.Services.GetRequiredService<Services.Analysis.ITrackSilenceAnalyzer>();

        await Task.Run(async () =>
         {
             List<MediaFile> updated = new List<MediaFile>();
             int processed = 0;
             foreach (MediaFile track in targets)
             {
                 if (cancellationToken.IsCancellationRequested)
                 {
                     break;
                 }

                 try
                 {
                     Services.Analysis.TrackSilenceAnalysisResult result = await analyzer.AnalyzeAsync(track, cancellationToken);
                     track.LeadingSilenceMs = result.LeadingSilenceMs;
                     track.TrailingSilenceMs = result.TrailingSilenceMs;
                     updated.Add(track);
                 }
                 catch
                 {
                 }

                 processed++;
                 await _uiDispatcher.InvokeAsync(() =>
                 {
                     WeakReferenceMessenger.Default.Send(new ProgressValueMessage(new ProgressData
                     {
                         IsProcessing = true,
                         ProcessedTracks = processed,
                         TotalTracks = targets.Count,
                         Status = cancellationToken.IsCancellationRequested ? "Canceling..." : "Analyzing silence..."
                     }));
                 });
             }

             if (updated.Count > 0)
             {
                 await _musicLibrary.UpdateTracksAsync(updated, updateMetadata: false, updateAnalysis: true);
             }
         }, cancellationToken);

        await _uiDispatcher.InvokeAsync(() =>
        {
            WeakReferenceMessenger.Default.Send(new ProgressValueMessage(new ProgressData
            {
                IsProcessing = false,
                ProcessedTracks = 0,
                TotalTracks = 1,
                Status = string.Empty
            }));
        });
    }

    [RelayCommand]
    private async Task ClearSilenceAnalysis()
    {
        if (_dataGrid == null)
        {
            return;
        }

        List<MediaFile> targets = _dataGrid.SelectedItems.Cast<MediaFile>().ToList();
        if (targets.Count == 0 && _dataGrid.SelectedItem is MediaFile single)
        {
            targets = new List<MediaFile> { single };
        }

        if (targets.Count == 0)
        {
            return;
        }

        foreach (MediaFile track in targets)
        {
            track.LeadingSilenceMs = null;
            track.TrailingSilenceMs = null;
        }

        await _musicLibrary.UpdateTracksAsync(targets, updateMetadata: false, updateAnalysis: true);
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
    private async Task GetRatings()
    {
        IReadOnlyList<MediaFile> selection = _selectionService.MultiSelection;


        if (selection.Count == 0)
        {
            MessageBox.Show("Please select one or more tracks first.", "No Selection",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Progress<ProgressData> progress = new Progress<ProgressData>(data =>
        {
            // Update status bar, progress dialog, etc.
            // Or send a message via WeakReferenceMessenger
        });

        await _mbService.EnrichSelectedTracksAsync(selection, progress);

        await _musicLibrary.UpdateTracksAsync(selection);

        MessageBox.Show($"MusicBrainz ratings fetch complete!\n\nUpdated {selection.Count} track(s).",
            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
                        Window? owner = Application.Current?.MainWindow;
                        OwnedWindowHelper.Show(window, owner);
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
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(ActiveTrack));
    }

    private Playlist? GetSelectedPlaylist()
    {
        if (SelectedTabIndex < 0 || SelectedTabIndex >= TabList.Count || !TabList.Any())
        {
            _logger.LogDebug("GetSelectedPlaylist: Invalid tab index or empty TabList");
            return null;
        }

        ITabData currentTab = TabList[SelectedTabIndex];
        SelectedTab = currentTab; // maintain compatibility

        if (currentTab is MusicLibraryTab)
        {
            return null;
        }

        Playlist? playlist = _musicLibrary.Playlists.FirstOrDefault(p =>
            string.Equals(p.Name, currentTab.Name, StringComparison.Ordinal));

        if (playlist == null)
        {
            _logger.LogWarning("GetSelectedPlaylist: Playlist '{TabName}' not found", currentTab.Name);
        }

        return playlist;
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
                newTab.IsLoading = true;
                newTab.LoadingStatus = $"Loading \"{playlistName}\"…";
                newTab.LoadingProgress = 0;
                newTab.LoadingTotal = 1;
                TabList.Add(newTab);
                SelectedTab = newTab;
                SelectedTabIndex = TabList.Count - 1;
                if (_tabControl != null)
                    _tabControl.SelectedIndex = SelectedTabIndex;
            });

            // ---------------------------------------------------------------
            // PHASE 1 — Instant population (no I/O, pure in-memory)
            // Normalise each M3U path to an absolute candidate, resolve against
            // the in-memory library index, create a lightweight stub for anything
            // unknown, and push the full list to the DataGrid immediately.
            // Missing / unresolved rows appear red straight away via the existing
            // TrackHealthStatus.Missing DataTrigger in StylesRepository.xaml.
            // ---------------------------------------------------------------

            List<string> rawPaths = ExtractPathsFromPlaylistFile(fileName);

            Dictionary<string, MediaFile> libraryIndex = _musicLibrary.MainLibrary
                .GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Each entry: the resolved MediaFile + the original raw candidate path
            // (used in Phase 2 to attempt fuzzy recovery).
            List<(MediaFile Track, string CandidatePath)> entries = new List<(MediaFile, string)>();

            foreach (string path in rawPaths)
            {
                try
                {
                    string candidatePath = path;

                    if (Uri.TryCreate(candidatePath, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                        candidatePath = uri.LocalPath;

                    candidatePath = Uri.UnescapeDataString(candidatePath);

                    if (!Path.IsPathRooted(candidatePath))
                        candidatePath = Path.GetFullPath(Path.Combine(directoryName, candidatePath));

                    if (libraryIndex.TryGetValue(candidatePath, out MediaFile? known))
                    {
                        // Already in the library — use the live object directly.
                        entries.Add((known, candidatePath));
                    }
                    else
                    {
                        // Unknown path: create a stub so the row appears immediately.
                        // HealthStatus.Missing renders it red; Phase 2 will resolve it.
                        MediaFile stub = new MediaFile(candidatePath)
                        {
                            HealthStatus = TrackHealthStatus.Missing
                        };
                        entries.Add((stub, candidatePath));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to normalise path from playlist entry: {Entry}", path);
                }
            }

            if (entries.Count == 0)
            {
                await _uiDispatcher.InvokeAsync(() => newTab.IsLoading = false);
                return;
            }

            // Split known (already in library) from unknown (need resolution)
            List<MediaFile> knownTracks = entries
                .Where(e => e.Track.HealthStatus != TrackHealthStatus.Missing)
                .Select(e => e.Track)
                .ToList();

            List<(MediaFile Track, string CandidatePath)> stubs = entries
                .Where(e => e.Track.HealthStatus == TrackHealthStatus.Missing)
                .ToList();

            // Phase 1 — add only known tracks to the DataGrid immediately
            await _uiDispatcher.InvokeAsync(() =>
            {
                newTab.LoadingTotal = entries.Count;
                newTab.LoadingProgress = knownTracks.Count;
                newTab.LoadingStatus = stubs.Count > 0
                    ? $"Loaded {knownTracks.Count} tracks, resolving {stubs.Count} unmatched…"
                    : $"Loading {knownTracks.Count} tracks…";

                foreach (MediaFile track in knownTracks)
                    newTab.Tracks.Add(track);
            });

            await _playlistManagerService.AddTracksToPlaylistAsync(playlistName, knownTracks);

            if (stubs.Count == 0)
            {
                await _uiDispatcher.InvokeAsync(() => newTab.IsLoading = false);
                return;
            }

            // Phase 2 — attempt to resolve each unknown path; only add to DataGrid on success
            _logger.LogInformation("Playlist '{Name}': {Known} known, {Stubs} unmatched — attempting resolution", playlistName, knownTracks.Count, stubs.Count);

            // Switch overlay to resolution progress
            await _uiDispatcher.InvokeAsync(() =>
            {
                newTab.LoadingTotal = stubs.Count;
                newTab.LoadingProgress = 0;
                newTab.LoadingStatus = $"Resolving {stubs.Count} unmatched track(s)…";
            });

            Progress<ProgressData> progress = new Progress<ProgressData>(data =>
            {
                WeakReferenceMessenger.Default.Send(new ProgressValueMessage(data));
            });

            CancellationToken ct = _importCancellationService.Token;
            List<PlaylistImportLogEntry> importLog = new List<PlaylistImportLogEntry>();

            await Task.Run(async () =>
            {
                int resolved = 0;

                for (int i = 0; i < stubs.Count; i++)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    (MediaFile _, string candidatePath) = stubs[i];

                    try
                    {
                        string? resolvedPath = null;
                        string? fuzzyRecoveredPath = null;

                        if (File.Exists(candidatePath))
                        {
                            resolvedPath = candidatePath;
                        }
                        else
                        {
                            // Fuzzy recovery — same logic as the old synchronous loop.
                            string? fileNameOnly = Path.GetFileName(candidatePath);
                            string? immediateDir = null;
                            try
                            { immediateDir = Path.GetDirectoryName(candidatePath); }
                            catch { }

                            if (!string.IsNullOrEmpty(immediateDir) && Directory.Exists(immediateDir))
                            {
                                string? recovered = TryFindClosestFileInDirectory(immediateDir, fileNameOnly);
                                if (!string.IsNullOrEmpty(recovered))
                                {
                                    _logger.LogInformation("Recovered missing file by fuzzy match: '{Orig}' => '{Match}'", candidatePath, recovered);
                                    resolvedPath = recovered;
                                    fuzzyRecoveredPath = recovered;
                                }
                            }
                            else
                            {
                                try
                                {
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
                                                string? recoveredDeep = TryFindClosestFileRecursively(recoveredAlbum, fileNameOnly);
                                                if (!string.IsNullOrEmpty(recoveredDeep))
                                                {
                                                    _logger.LogInformation("Recovered missing file by directory+file fuzzy match: '{Orig}' => '{Match}'", candidatePath, recoveredDeep);
                                                    resolvedPath = recoveredDeep;
                                                    fuzzyRecoveredPath = recoveredDeep;
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
                        }

                        if (resolvedPath != null)
                        {
                            // Re-check library index with the resolved path (may differ after fuzzy recovery).
                            MediaFile? imported;
                            if (libraryIndex.TryGetValue(resolvedPath, out MediaFile? alreadyKnown))
                            {
                                imported = alreadyKnown;
                            }
                            else
                            {
                                imported = await _fileImportService.ImportFileAsync(resolvedPath).ConfigureAwait(false);
                            }

                            if (imported != null && !string.IsNullOrEmpty(imported.FileName))
                            {
                                // Add the resolved track to the DataGrid now that we know it's real
                                await _uiDispatcher.InvokeAsync(() => newTab.Tracks.Add(imported));
                                resolved++;
                                if (fuzzyRecoveredPath != null)
                                {
                                    importLog.Add(new PlaylistImportLogEntry
                                    {
                                        Action = PlaylistImportAction.Recovered,
                                        Path = candidatePath,
                                        RecoveredPath = fuzzyRecoveredPath
                                    });
                                }
                            }
                            else if (imported != null)
                            {
                                _logger.LogWarning("Playlist '{Name}': imported track has no metadata — skipped: {Path}", playlistName, resolvedPath);
                                importLog.Add(new PlaylistImportLogEntry
                                {
                                    Action = PlaylistImportAction.Skipped,
                                    Path = candidatePath
                                });
                            }
                            else
                            {
                                _logger.LogWarning("Playlist '{Name}': could not import resolved path — skipped: {Path}", playlistName, resolvedPath);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Playlist '{Name}': track not found on disk — skipped: {Path}", playlistName, candidatePath);
                            importLog.Add(new PlaylistImportLogEntry
                            {
                                Action = PlaylistImportAction.Missing,
                                Path = candidatePath
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve stub track: {Path}", candidatePath);
                    }

                    ((IProgress<ProgressData>)progress).Report(new ProgressData
                    {
                        IsProcessing = true,
                        TotalTracks = stubs.Count,
                        ProcessedTracks = i + 1,
                        Status = $"Resolving tracks… {i + 1} / {stubs.Count}",
                        Phase = "Resolving"
                    });
                    await _uiDispatcher.InvokeAsync(() => newTab.LoadingProgress = i + 1);
                }

                _logger.LogInformation(
                    "Playlist '{Name}': load complete — {Known} direct, {Resolved} recovered, {Skipped} not found",
                    playlistName, knownTracks.Count, resolved, stubs.Count - resolved);

                ((IProgress<ProgressData>)progress).Report(new ProgressData
                {
                    IsProcessing = false,
                    TotalTracks = stubs.Count,
                    ProcessedTracks = stubs.Count,
                    Status = string.Empty,
                    Phase = string.Empty
                });

                await _uiDispatcher.InvokeAsync(() =>
                {
                    newTab.IsLoading = false;

                    if (importLog.Count > 0)
                    {
                        PlaylistImportLogWindow logWindow = new PlaylistImportLogWindow(
                            playlistName,
                            knownTracks.Count + resolved,
                            resolved,
                            importLog);
                        OwnedWindowHelper.Show(logWindow, System.Windows.Application.Current.MainWindow);
                    }
                });
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load playlist file: {FileName}", fileName);
            await _uiDispatcher.InvokeAsync(() =>
            {
                if (SelectedTab is PlaylistTab pt)
                    pt.IsLoading = false;
            });
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
            UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark)
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
              { ".mp3", ".flac", ".ape", ".ac3", ".dsd", ".dsf", ".dts", ".m4k", ".mka", ".mp4", ".mpc", ".ofr", ".ogg", ".opus", ".wav", ".wma", ".wv" };
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
              { ".mp3", ".flac", ".ape", ".ac3", ".dsd", ".dsf", ".dts", ".m4k", ".mka", ".mp4", ".mpc", ".ofr", ".ogg", ".opus", ".wav", ".wma", ".wv" };
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

            List<MediaFile> importedTracks = await _fileImportService.ImportFolderAsync(folderPath, progress, _importCancellationService.Token);

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
            ITabData? targetTab = TabList.FirstOrDefault(p => p.Name == tabName);
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
    private void BeginRenameTab(object? parameter)
    {
        if (parameter is not PlaylistTab playlistTab)
        {
            _logger.LogWarning("BeginRenameTab called with invalid parameter type: {Type}", parameter?.GetType().Name ?? "null");
            return;
        }

        // Trigger edit mode via WeakReferenceMessenger
        WeakReferenceMessenger.Default.Send(new BeginEditTabMessage(playlistTab));
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
                ITabData? currentlySelectedTab = SelectedTabIndex >= 0 && SelectedTabIndex < count
                    ? TabList[SelectedTabIndex]
                    : null;

                // Reorder in the UI
                ITabData movedTab = TabList[fromIndex];
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

    private CancellationTokenSource? _analyzeSilenceCts;

    [RelayCommand]
    private void CancelAnalyzeSilence()
    {
        try
        {
            _analyzeSilenceCts?.Cancel();
        }
        catch
        {
        }
    }

    // ==================================================================
    //  Save dirty library tracks (inline editing)
    // ==================================================================

    public bool HasDirtyTracks => _musicLibrary.MainLibrary.Any(t => t.IsDirty);

    public List<MediaFile> DirtyTracks => _musicLibrary.MainLibrary.Where(t => t.IsDirty).ToList();
    /// <summary>
    /// Call this when a library track's dirty state may have changed (e.g., after editing a cell).
    /// Debounced — bursts of changes (e.g. editing 25 tracks) collapse to a single notification.
    /// </summary>
    public void NotifyDirtyStateChanged()
    {
        if (_dirtyDebounceTimer == null)
        {
            // Fallback: no timer (unit-test context), fire immediately
            OnPropertyChanged(nameof(HasDirtyTracks));
            return;
        }

        // Restart the timer so rapid back-to-back calls are coalesced.
        _dirtyDebounceTimer.Stop();
        _dirtyDebounceTimer.Start();
    }

    [RelayCommand]
    private async Task SaveDirtyTracksAsync()
    {
        List<MediaFile> dirtyTracks = _musicLibrary.MainLibrary
            .Where(t => t.IsDirty)
            .ToList();

        if (dirtyTracks.Count == 0)
            return;

        _logger.LogInformation("Saving {Count} dirty track(s) to file and database", dirtyTracks.Count);
        foreach (MediaFile t in dirtyTracks)
        {
            _logger.LogInformation("  DIRTY  [{Props}]  {Path}",
                string.Join(", ", t.DirtyProperties),
                t.Path);
        }

        void SendProgress(bool isProcessing, int processed, int total, string status)
        {
            _uiDispatcher.InvokeAsync(() =>
                WeakReferenceMessenger.Default.Send(new ProgressValueMessage(new ProgressData
                {
                    IsProcessing = isProcessing,
                    ProcessedTracks = processed,
                    TotalTracks = total,
                    Status = status,
                    Phase = isProcessing ? "Saving" : string.Empty
                })));
        }

        SendProgress(true, 0, dirtyTracks.Count, $"Saving 0/{dirtyTracks.Count} tracks...");

        // BitmapImage is UI-thread-affine — encode cover bytes before any await.
        List<(MediaFile MediaFile, HashSet<string> DirtyProps, byte[]? CoverBytes)> snapshots = [];
        foreach (MediaFile mediaFile in dirtyTracks)
        {
            byte[]? coverBytes = null;
            if (mediaFile.DirtyProperties.Contains(nameof(MediaFile.AlbumCover)) && mediaFile.AlbumCover != null)
            {
                try
                {
                    using System.IO.MemoryStream ms = new();
                    PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(mediaFile.AlbumCover));
                    encoder.Save(ms);
                    coverBytes = ms.ToArray();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to encode cover image for {Path}", mediaFile.Path);
                }
            }
            snapshots.Add((mediaFile, new HashSet<string>(mediaFile.DirtyProperties), coverBytes));
        }

        // Save tracks in parallel (up to 8 concurrent) so a full album saves
        // in a few seconds rather than many minutes over a UNC/NAS share.
        System.Collections.Concurrent.ConcurrentBag<MediaFile> savedBag = new();
        SemaphoreSlim semaphore = new(8, 8);
        _saveProgressCount = 0;

        IEnumerable<Task> saveTasks = snapshots.Select(async snapshot =>
        {
            (MediaFile mediaFile, HashSet<string> dirtyProps, byte[]? coverBytes) = snapshot;
            await semaphore.WaitAsync();
            try
            {
                // Run all ATL I/O on a thread-pool thread.  new ATL.Track(path) does a
                // synchronous full-file read which blocks for several seconds on a UNC/NAS
                // share.  Without Task.Run the async lambda resumes on the WPF dispatcher
                // (SynchronizationContext is captured at call site), so every save would
                // serialize through the UI thread regardless of the semaphore parallelism.
                bool ok = await Task.Run(async () =>
                {
                    ATL.Track atlTrack = new(mediaFile.Path);

                    foreach (string prop in dirtyProps)
                    {
                        switch (prop)
                        {
                            case nameof(MediaFile.Title):
                                atlTrack.Title = mediaFile.Title;
                                break;
                            case nameof(MediaFile.Artist):
                                atlTrack.Artist = mediaFile.Artist;
                                break;
                            case nameof(MediaFile.Album):
                                atlTrack.Album = mediaFile.Album;
                                break;
                            case nameof(MediaFile.AlbumArtist):
                                atlTrack.AlbumArtist = mediaFile.AlbumArtist;
                                break;
                            case nameof(MediaFile.Genres):
                                atlTrack.Genre = mediaFile.Genres;
                                break;
                            case nameof(MediaFile.Track):
                                atlTrack.TrackNumber = mediaFile.Track;
                                break;
                            case nameof(MediaFile.TrackCount):
                                atlTrack.TrackTotal = mediaFile.TrackCount;
                                break;
                            case nameof(MediaFile.Disc):
                                atlTrack.DiscNumber = mediaFile.Disc;
                                break;
                            case nameof(MediaFile.DiscCount):
                                atlTrack.DiscTotal = mediaFile.DiscCount;
                                break;
                            case nameof(MediaFile.Year):
                                atlTrack.Year = mediaFile.Year;
                                break;
                            case nameof(MediaFile.Composers):
                                atlTrack.Composer = mediaFile.Composers;
                                break;
                            case nameof(MediaFile.Comment):
                                atlTrack.Comment = mediaFile.Comment;
                                break;
                            case nameof(MediaFile.Copyright):
                                atlTrack.Copyright = mediaFile.Copyright;
                                break;
                            case nameof(MediaFile.Rating):
                                atlTrack.Popularity = (float)Math.Round(mediaFile.Rating * 255.0 / 5.0, 1);
                                break;
                            case nameof(MediaFile.AlbumCover):
                                atlTrack.EmbeddedPictures.Clear();
                                if (coverBytes != null)
                                {
                                    ATL.PictureInfo pic = ATL.PictureInfo.fromBinaryData(
                                        coverBytes,
                                        ATL.PictureInfo.PIC_TYPE.Front,
                                        ATL.AudioData.MetaDataIOFactory.TagType.ANY,
                                        0,
                                        0);
                                    atlTrack.EmbeddedPictures.Add(pic);
                                }
                                break;
                        }
                    }

                    return await atlTrack.SaveAsync(writeProgress: null);
                });

                if (ok)
                {
                    mediaFile.LastSavedByAppUtc = DateTime.UtcNow;
                    try
                    { mediaFile.FileLastWriteTimeUtc = File.GetLastWriteTimeUtc(mediaFile.Path); }
                    catch { }
                    savedBag.Add(mediaFile);
                    _logger.LogDebug("Saved dirty track: {Path}", mediaFile.Path);
                }
                else
                {
                    _logger.LogWarning("ATL failed to save track: {Path}", mediaFile.Path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save dirty track: {Path}", mediaFile.Path);
            }
            finally
            {
                semaphore.Release();
                int done = Interlocked.Increment(ref _saveProgressCount);
                SendProgress(true, done, dirtyTracks.Count, $"Saving {done}/{dirtyTracks.Count} tracks...");
            }
        });

        await Task.WhenAll(saveTasks);
        List<MediaFile> saved = savedBag.ToList();

        // Update HasEmbeddedCover on saved tracks based on whether cover bytes were written.
        foreach ((MediaFile mediaFile, HashSet<string> dirtyProps, byte[]? coverBytes) in snapshots)
        {
            if (!saved.Contains(mediaFile))
                continue;
            if (dirtyProps.Contains(nameof(MediaFile.AlbumCover)))
                mediaFile.HasEmbeddedCover = coverBytes != null;
        }

        // ClearDirty raises PropertyChanged — must happen on the UI thread, and BEFORE
        // UpdateTracksAsync so that the in-memory property-copy loop inside that method
        // does not re-trigger dirty tracking on the live MediaFile objects.
        foreach (MediaFile mediaFile in saved)
            mediaFile.ClearDirty();

        // Persist to database.  updateMetadata:true syncs FileLastWriteTimeUtc and all
        // scalar fields; UpdateTracksAsync now wraps the in-memory copy in
        // DisableDirtyTracking/EnableDirtyTracking so the assignments cannot re-dirty the tracks.
        try
        {
            await _musicLibrary.UpdateTracksAsync(saved, updateMetadata: true, updateAnalysis: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist dirty tracks to database");
        }

        SendProgress(false, saved.Count, dirtyTracks.Count, $"Saved {saved.Count} of {dirtyTracks.Count} track(s)");

        NotifyDirtyStateChanged();
    }
}
