using ATL;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.BassLibs;
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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace LinkerPlayer.ViewModels;

public interface IMediaTabViewModel
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
    int SelectedIndex { get; set; }
    MediaFile? SelectedTrack { get; set; }
    MediaFile? SelectedMediaFile { get; set; }
    void LoadPlaylistTabs();
    Task LoadSelectedPlaylistTracksAsync();
    Task LoadOtherPlaylistTracksAsync();
}

public partial class MediaTabViewModel : ObservableObject, IMediaTabViewModel
{
    // Manually implement SelectedTab and TabList to avoid source generator type conflicts
    private ITabData? _selectedTab; // Can be PlaylistTab or MusicLibraryTab
    private readonly IMusicBrainzRatingService _mbService;

    public ITabData? SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    private ObservableCollection<ITabData> _tabList = new ObservableCollection<ITabData>(); // Contains MusicLibraryTab + PlaylistTab instances
    public ObservableCollection<ITabData> TabList
    {
        get => _tabList;
        set => SetProperty(ref _tabList, value);
    }

    [ObservableProperty] private int _selectedTabIndex = -1;
    [ObservableProperty] private string? _activeTabName; // The tab name from PlaybackCursor (playback source - either Library or Playlist)
    [ObservableProperty] private Playlist? _selectedPlaylist;
    [ObservableProperty] private PlaybackState _state;
    [ObservableProperty] private string? _activeTrackId; // Cached ID of the currently playing track (avoids expensive property access in bindings)
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
    private readonly ILogger<MediaTabViewModel> _logger;

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
    private readonly IPlaylistFileService _playlistFileService;
    private readonly IImportCancellationService _importCancellationService;

    private readonly LibraryTabViewModel _libraryTabViewModel;

    // Shared selection/active-track state surfaced for consumers that bind to the shared model.
    public ISharedDataModel SharedDataModel => _sharedDataModel;

    // Expose LibraryTabViewModel for UI binding (e.g., loading state)
    public LibraryTabViewModel LibraryTabViewModel => _libraryTabViewModel;

    private DataGrid? _dataGrid;     // holds current DataGrid reference
    private bool _shuffleMode;       // shuffle flag
    private LibraryTab? _musicLibraryTab; // The permanent Music Library tab (always first)


    // Debounce timer — collapses rapid dirty-state bursts (e.g. editing 25 tracks at once)
    // into a single HasDirtyTracks notification so the Save button updates promptly but
    // doesn't thrash on every individual PropertyChanged event.
    private System.Windows.Threading.DispatcherTimer? _dirtyDebounceTimer;

    // ADD back filter constants used by dialogs
    private const string SupportedAudioFilter = "(*.mp3; *.flac; *.ape; *.ac3; *.dsd; *.dsf; *.dts; *.m4k; *.mka; *.mp4; *.mpc; *.ofr; *.ogg; *.opus; *.wav; *.wma; *.wv)|*.mp3; *.flac; *.ape; *.ac3; *.dts; *.m4k; *.mka; *.mp4; *.mpc; *.ofr; *.ogg; *.opus; *.wav; *.wma; *.wv";
    private const string SupportedPlaylistFilter = "(*.m3u;*.m3u8;*.pls;*.wpl;*.zpl)|*.m3u;*.m3u8;*.pls;*.wpl;*.zpl";
    private const string SupportedFilters = $"Audio Formats {SupportedAudioFilter}|Playlist Files {SupportedPlaylistFilter}|All files (*.*)|*.*";

    private List<string> _selectedColumnNames = new()
    {
        "Playing",
        "Title",
        "Artist"
    };

    // Canonical shared selection properties.
    public MediaFile? ActiveTrack
    {
        get => _sharedDataModel.ActiveTrack;
        set
        {
            if (!ReferenceEquals(_sharedDataModel.ActiveTrack, value))
            {
                _sharedDataModel.UpdateActiveTrack(value);
                ActiveTrackId = value?.Id; // Update the cached ID
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
            OnPropertyChanged(nameof(SelectedMediaFile));
        }
    }

    public int SelectedIndex
    {
        get => SelectedTrackIndex;
        set => SelectedTrackIndex = value;
    }

    public MediaFile? SelectedMediaFile
    {
        get => SelectedTrack;
        set => SelectedTrack = value;
    }

    public List<string> SelectedColumnNames => _selectedColumnNames;

    public MediaTabViewModel(
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
        IPlaylistFileService playlistFileService,
        IImportCancellationService importCancellationService,
        IMusicBrainzRatingService mbService,
        LibraryTabViewModel libraryTabViewModel,
        ILogger<MediaTabViewModel> logger)
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
        _playlistFileService = playlistFileService ?? throw new ArgumentNullException(nameof(playlistFileService));
        _importCancellationService = importCancellationService ?? throw new ArgumentNullException(nameof(importCancellationService));
        _libraryTabViewModel = libraryTabViewModel ?? throw new ArgumentNullException(nameof(libraryTabViewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to library loaded event to refresh playlist tabs when library finishes loading
        _musicLibrary.LibraryLoaded += OnLibraryLoaded;

        try
        {
            _shuffleMode = _settingsManager.Settings.ShuffleMode;
            AllowDrop = true;
            RegisterMessages();

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

            // Initialize Library tab immediately so UI shows with loading indicator
            _libraryTabViewModel.InitializeLibraryTab(_musicLibrary.MainLibrary);
            _musicLibraryTab = _libraryTabViewModel.LibraryTab ?? new LibraryTab(_musicLibrary.MainLibrary);
            TabList.Add(_musicLibraryTab);

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

            // Update cached ActiveTrackId to avoid expensive property access in bindings during scroll
            ActiveTrackId = m.Value?.Id;

            // Update the active tab name to reflect the playback source
            if (_playbackCoordinator?.PlaybackCursor != null)
            {
                ActiveTabName = _playbackCoordinator.PlaybackCursor.PlaylistName;
            }

            // When playback changes to a new track, update selection in the current tab to match,
            // but do NOT switch tabs. This keeps the user on their chosen tab while showing where
            // the active track is (via play icon). Playback continues from the source playlist.
            // Marshal to UI thread since this may come from a background thread.
            if (m.Value != null)
            {
                _uiDispatcher.InvokeAsync(() =>
                {
                    // Only update selection if the active track is in the current tab
                    if (SelectedTabIndex >= 0 && SelectedTabIndex < TabList.Count)
                    {
                        ITabData currentTab = TabList[SelectedTabIndex];
                        int trackIndex = currentTab.Tracks.IndexOf(m.Value);
                        if (trackIndex >= 0)
                        {
                            // The active track is in the current tab, show it as selected
                            SelectedTrack = m.Value;
                            SelectedTrackIndex = trackIndex;
                            currentTab.SelectedMediaFile = m.Value;
                            currentTab.SelectedIndex = trackIndex;
                        }
                        // If active track is not in current tab, leave selection unchanged
                        // (it stays on the original playlist's track range)
                    }
                });
            }
        });

        _logger.LogInformation("Constructor end - LastLibrarySelectedTrackId = {Id}", _settingsManager.Settings.LastLibrarySelectedTrackId);
    }

    private void OnLibraryLoaded(object? sender, EventArgs e)
    {
        _logger.LogInformation("MediaTabViewModel: Library loaded, refreshing playlist tabs");

        _uiDispatcher.InvokeAsync(() =>
        {
            LoadPlaylistTabs();
            _ = LoadSelectedPlaylistTracksAsync();
            _ = LoadOtherPlaylistTracksAsync();
        });
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
        if (sender is DataGrid dataGrid)
        {
            _dataGrid = dataGrid;
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        try
        {
            if (value < 0 || value >= TabList.Count)
                return;

            SelectedTab = TabList[value];
            SelectedPlaylist = GetSelectedPlaylist();
            _selectionService.SetTab(SelectedTab as PlaylistTab);

            // Always persist the index
            _settingsManager.Settings.SelectedTabIndex = value;
            _settingsManager.SaveSettings(nameof(AppSettings.SelectedTabIndex));

            if (SelectedTab is LibraryTab)
            {
                _ = _libraryTabViewModel.ActivateAsync();
            }
            else if (SelectedTab is PlaylistTab playlistTab)
            {
                _selectionService.SetMultiSelection(Enumerable.Empty<MediaFile>());

                if (playlistTab.Tracks.Count == 0)
                {
                    _ = LoadPlaylistTracksAndRestoreSelectionAsync(playlistTab);
                }
                else
                {
                    RestorePlaylistSelection(playlistTab);

                    // NOTE: Tab switch restores the tab's selected track, but does NOT change playback.
                    // Selection and active track are independent; only play/skip commands change the active track.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnSelectedTabIndexChanged failed");
        }
    }

    private void RestorePlaylistSelection(PlaylistTab playlistTab)
    {
        Playlist? playlist = GetPlaylistForTab(playlistTab);

        string? desiredTrackId = null;
        string? activePlaylistName = _playbackCoordinator.PlaybackCursor?.PlaylistName ?? ActiveTabName;

        if (!string.IsNullOrWhiteSpace(activePlaylistName) &&
            string.Equals(playlistTab.Name, activePlaylistName, StringComparison.Ordinal))
        {
            desiredTrackId = ActiveTrack?.Id;
        }

        if (string.IsNullOrWhiteSpace(desiredTrackId))
        {
            desiredTrackId = playlistTab.SelectedMediaFile?.Id;
        }

        if (string.IsNullOrWhiteSpace(desiredTrackId))
        {
            desiredTrackId = playlist?.SelectedTrackId;
        }

        MediaFile? restored = !string.IsNullOrWhiteSpace(desiredTrackId)
            ? playlistTab.Tracks.FirstOrDefault(t => string.Equals(t.Id, desiredTrackId, StringComparison.Ordinal))
            : null;

        if (restored == null)
        {
            playlistTab.SelectedMediaFile = null;
            playlistTab.SelectedIndex = -1;
            SelectedTrack = null;
            return;
        }

        int restoredIndex = playlistTab.Tracks.IndexOf(restored!);
        playlistTab.SelectedMediaFile = restored;
        playlistTab.SelectedIndex = restoredIndex;

        SelectedTrack = restored;

        // NOTE: Restoring selection does not change playback.
        // Only playback commands (play, skip) should change the active track.

        if (playlist != null && !string.Equals(playlist.SelectedTrackId, restored!.Id, StringComparison.Ordinal))
        {
            playlist.SelectedTrackId = restored.Id;
            _databaseSaveService.RequestSave();
        }

    }

    private async Task LoadPlaylistTracksForTabAsync(PlaylistTab playlistTab)
    {
        if (playlistTab.Tracks.Count > 0)
            return;

        try
        {
            IEnumerable<MediaFile> tracks = await Task.Run(() =>
                _playlistManagerService.LoadPlaylistTracks(playlistTab.Name));

            await _uiDispatcher.InvokeAsync(() =>
            {
                foreach (MediaFile track in tracks)
                {
                    playlistTab.Tracks.Add(track);
                }
            });

            _logger.LogInformation("Loaded {Count} tracks for playlist '{Name}'", playlistTab.Tracks.Count, playlistTab.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tracks for playlist {Name}", playlistTab.Name);
        }
    }

    private async Task LoadPlaylistTracksAndRestoreSelectionAsync(PlaylistTab playlistTab)
    {
        try
        {
            IEnumerable<MediaFile> tracks = await Task.Run(() =>
                _playlistManagerService.LoadPlaylistTracks(playlistTab.Name));

            await _uiDispatcher.InvokeAsync(() =>
            {
                foreach (MediaFile track in tracks)
                {
                    playlistTab.Tracks.Add(track);
                }

                // Now restore selection
                RestorePlaylistSelection(playlistTab);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tracks for playlist '{Name}'", playlistTab.Name);
        }
    }

    public void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid dataGrid || dataGrid.DataContext is not ITabData senderTab)
            return;

        // Ignore selection changes from non-active tabs. Background loads and hidden DataGrids
        // can fire SelectionChanged and must not overwrite the active tab's persisted selection.
        if (SelectedTab == null || !ReferenceEquals(SelectedTab, senderTab))
            return;

        _dataGrid = dataGrid;

        if (_dataGrid.SelectedItems.Count > 0)
        {
            List<MediaFile> selectedTracks = _dataGrid.SelectedItems.Cast<MediaFile>().ToList();
            MediaFile selectedTrack = _dataGrid.SelectedItem as MediaFile ?? selectedTracks[0];
            if (selectedTrack == null)
                return;

            int index = _dataGrid.Items.IndexOf(selectedTrack);

            _logger.LogInformation("=== OnTrackSelectionChanged: '{Title}' (tab: {TabName}, multi: {Count})",
                selectedTrack.Title, senderTab.Name, selectedTracks.Count);

            senderTab.SelectedMediaFile = selectedTrack;

            // SINGLE SOURCE OF TRUTH
            _selectionService.SetMultiSelection(selectedTracks);

            // Keep shared selection in sync for consumers that observe the VM directly.
            // SelectedTrack setter updates selection service for single-selection state.
            SelectedTrack = selectedTrack;

            // NOTE: User selection does NOT change the active track (what's playing).
            // If the user is playing Track A and selects Track B, Track A should still be playing
            // and show the play icon. Only play/skip commands change the active track.

            // Guard tab-owned index writes together to avoid transient -1 churn.
            if (index >= 0)
            {
                senderTab.SelectedIndex = index;
            }

            // Persist based on the sender tab (authoritative source for this event)
            if (senderTab is LibraryTab)
            {
                _settingsManager.Settings.LastLibrarySelectedTrackId = selectedTrack!.Id;
                _settingsManager.SaveSettings(nameof(AppSettings.LastLibrarySelectedTrackId));

                _libraryTabViewModel.SelectedTrack = selectedTrack;
                _libraryTabViewModel.LastSessionSelectedTrack = selectedTrack;
            }
            else if (senderTab is PlaylistTab playlistTab)
            {
                Playlist? playlist = GetPlaylistForTab(playlistTab);
                if (playlist != null)
                {
                    playlist.SelectedTrackId = selectedTrack!.Id;
                    _databaseSaveService.RequestSave();
                }
            }

            CheckHealthForTracks(selectedTracks);
        }
        else
        {
            // Do not clear tab selections on empty SelectionChanged events.
            // Tab switch/layout refresh can transiently emit empty selection and would wipe persistence.
            if (senderTab is LibraryTab || senderTab is PlaylistTab)
            {
                return;
            }

            senderTab.SelectedMediaFile = null;
            senderTab.SelectedIndex = -1;

            _selectionService.SetMultiSelection(Enumerable.Empty<MediaFile>());
            SelectedTrack = null;
        }
    }

    private Playlist? GetPlaylistForTab(PlaylistTab playlistTab)
    {
        return _musicLibrary.Playlists.FirstOrDefault(p =>
            string.Equals(p.Name, playlistTab.Name, StringComparison.Ordinal));
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

        Task.Run(async () =>
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
                    try
                    {
                        await _uiDispatcher.InvokeAsync(() =>
                        {
                            track.HealthStatus = status;
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        return;
                    }
                }
            }
        });
    }

    public void OnDoubleClickDataGrid()
    {
        if (_dataGrid?.SelectedItem is not MediaFile selectedTrack)
            return;

        _logger.LogInformation("OnDoubleClickDataGrid CALLED for track {TrackTitle} ({TrackId}) in tab {TabName}", 
            selectedTrack.Title, selectedTrack.Id, SelectedTab?.Name ?? "Unknown");

        SelectedTrack = selectedTrack; // This already triggers everything

        if (_playbackCoordinator != null && selectedTrack != null && SelectedTab != null)
        {
            string tabName = SelectedTab.Name ?? "Music Library";
            _logger.LogInformation("OnDoubleClickDataGrid: Calling SetSelection with Playlist={Playlist}, Index={Index}", 
                tabName, _dataGrid?.SelectedIndex ?? 0);
            _playbackCoordinator.SetSelection(
                tabName,
                _dataGrid?.SelectedIndex ?? 0,
                selectedTrack);
        }

        Playlist? playlist = GetSelectedPlaylist();
        if (playlist != null)
        {
            playlist.SelectedTrackId = selectedTrack!.Id;
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
            _logger.LogInformation("LoadPlaylistTabs called - adding playlist tabs");

            // Don't clear TabList - Library tab is already there from constructor
            // Just remove any existing playlist tabs and add fresh ones
            for (int i = TabList.Count - 1; i >= 1; i--)
            {
                TabList.RemoveAt(i);
            }

            // Create Playlist tabs
            List<Playlist> playlists = _musicLibrary.GetPlaylists();
            _logger.LogInformation("LoadPlaylistTabs: Found {Count} playlists", playlists.Count);
            foreach (Playlist playlist in playlists)
            {
                if (string.IsNullOrWhiteSpace(playlist.Name))
                    continue;
                _logger.LogInformation("LoadPlaylistTabs: Adding playlist tab '{Name}'", playlist.Name);
                TabList.Add(new PlaylistTab { Name = playlist.Name });
            }

            _logger.LogInformation("LoadPlaylistTabs: TabList now has {Count} tabs", TabList.Count);

            // Restore the last selected tab + selection
            int savedIndex = _settingsManager.Settings.SelectedTabIndex;
            if (savedIndex < 0 || savedIndex >= TabList.Count)
                savedIndex = 0;

            SelectedTabIndex = savedIndex;

            // Force UI refresh
            if (SelectedTabIndex >= 0)
            {
                SelectedTabIndex = SelectedTabIndex; // trigger property changed
            }

            // Force initial selection
            if (SelectedTabIndex < 0 && TabList.Count > 0)
            {
                SelectedTabIndex = 0;
            }

            _logger.LogInformation("LoadPlaylistTabs completed. TabCount={Count}, SelectedTabIndex={Index}", TabList.Count, SelectedTabIndex);

            foreach (ITabData tab in TabList)
            {
                if (tab is PlaylistTab pt)
                {
                    _logger.LogInformation("Playlist '{Name}' has {TrackCount} tracks", pt.Name, pt.Tracks.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tabs");
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
                return;

            ITabData selectedTab = TabList[SelectedTabIndex];

            if (selectedTab is LibraryTab)
            {
                _logger.LogInformation("Music Library tab selected - tracks already loaded from MainLibrary");
                return;
            }

            if (selectedTab is not PlaylistTab playlistTab)
                return;

            // ALWAYS load if empty - remove the "already loaded" skip for now to debug
            if (playlistTab.Tracks.Count > 0)
            {
                _logger.LogInformation("Playlist '{Name}' already has tracks", playlistTab.Name);
                return;
            }

            _logger.LogInformation("Loading tracks for selected playlist '{Name}'", playlistTab.Name);

            IEnumerable<MediaFile> tracks = await Task.Run(() =>
                _playlistManagerService.LoadPlaylistTracks(playlistTab.Name));

            await _uiDispatcher.InvokeAsync(() =>
            {
                playlistTab.Tracks.Clear(); // ensure clean
                foreach (MediaFile track in tracks)
                {
                    playlistTab.Tracks.Add(track);
                }
            });

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
                if (tab is LibraryTab)
                {
                    continue;
                }

                // Only process PlaylistTab instances
                if (tab is not PlaylistTab playlistTab)
                {
                    continue;
                }

                // ALWAYS attempt to load if empty
                if (playlistTab.Tracks.Count > 0)
                {
                    _logger.LogInformation("Playlist '{Name}' already has tracks - skipping", playlistTab.Name);
                    continue;
                }

                _logger.LogInformation("Loading tracks for background playlist '{Name}'", playlistTab.Name);

                _logger.LogInformation("Attempting to load playlist '{Name}' - Service exists: {ServiceExists}",
                    playlistTab.Name, _playlistManagerService != null);

                // Load on background thread
                IEnumerable<MediaFile> tracks = await Task.Run(() =>
                    _playlistManagerService!.LoadPlaylistTracks(playlistTab.Name)
                );

                // Use Dispatcher to add tracks on UI thread
                await _uiDispatcher.InvokeAsync(() =>
                {
                    playlistTab.Tracks.Clear(); // ensure clean state
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

        try
        {
            if (openFileDialog.ShowDialog() == true)
            {
                await _playlistFileService.LoadPlaylistFileAsync(openFileDialog.FileName, this);
            }
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            _logger.LogWarning(ex, "Playlist file dialog failed with COM error (HRESULT: 0x{HResult:X8})", ex.HResult);
            MessageBox.Show(
                "The playlist file dialog could not be opened due to a Windows shell error. Please try again.",
                "Open Playlist",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Playlist file dialog failed to initialize");
            MessageBox.Show(
                "The playlist file dialog could not be opened. Please try again.",
                "Open Playlist",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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

        bool isLibraryTab = SelectedTab is LibraryTab;
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
                        if (SelectedTab is not LibraryTab)
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
        List<MediaFile> selected = [];

        if (_dataGrid != null)
        {
            selected = _dataGrid.SelectedItems.Cast<MediaFile>().ToList();

            if (selected.Count == 0 && _dataGrid.SelectedItem is MediaFile single)
            {
                selected = [single];
            }
        }

        IReadOnlyList<MediaFile> multiSelection = _selectionService.MultiSelection;
        if (selected.Count == 0 && multiSelection.Count > 0)
        {
            selected = multiSelection.ToList();
        }

        if (selected.Count == 0 && SelectedTrack != null)
        {
            selected = [SelectedTrack];
        }

        if (selected.Count == 0)
            return;

        IRescanLogger? rescanLogger = App.AppHost?.Services?.GetService<IRescanLogger>();
        rescanLogger?.BeginSession();

        try
        {
            // Determine what to do by checking the file system directly — do not rely on
            // HealthStatus being up to date, because FSW may have missed events.
            List<MediaFile> missing = selected.Where(t => !File.Exists(t.Path)).ToList();
            List<MediaFile> present = selected.Where(t => File.Exists(t.Path)).ToList();

            rescanLogger?.LogInfo($"Rescanning selected track(s): total={selected.Count}, present={present.Count}, missing={missing.Count}");

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
                    foreach (MediaFile track in missing)
                        rescanLogger?.LogRemoved(track.Path);
                }
            }

            // Refresh metadata for tracks that do exist
            if (present.Count > 0)
            {
                List<MediaFile> updates = [];
                foreach (MediaFile track in present)
                {
                    try
                    {
                        MediaFile work = new() { Path = track.Path, Id = track.Id };
                        work.UpdateFromFileMetadata(raisePropertyChanged: true);
                        updates.Add(work);
                        rescanLogger?.LogUpdated($"{track.Path}  [ReplayGain={work.ReplayGain}]");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to refresh metadata for {Path}", track.Path);
                        rescanLogger?.LogWarning($"Metadata refresh failed: {track.Path} — {ex.Message}");
                    }
                }

                if (updates.Count > 0)
                {
                    await _musicLibrary.UpdateTracksAsync(updates).ConfigureAwait(false);

                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        foreach (MediaFile track in present)
                            track.HealthStatus = TrackHealthStatus.Ok;
                    });
                }
            }

            rescanLogger?.LogInfo($"Selected-track rescan complete: {selected.Count} track(s)");
        }
        finally
        {
            rescanLogger?.EndSession();
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

    private static readonly string[] ReplayGainTrackGainKeys = ["REPLAYGAIN_TRACK_GAIN", "----:com.apple.iTunes:REPLAYGAIN_TRACK_GAIN", "R128_TRACK_GAIN"];
    private static readonly string[] ReplayGainTrackPeakKeys = ["REPLAYGAIN_TRACK_PEAK", "----:com.apple.iTunes:REPLAYGAIN_TRACK_PEAK"];
    private static readonly string[] ReplayGainAlbumGainKeys = ["REPLAYGAIN_ALBUM_GAIN", "----:com.apple.iTunes:REPLAYGAIN_ALBUM_GAIN", "R128_ALBUM_GAIN"];
    private static readonly string[] ReplayGainAlbumPeakKeys = ["REPLAYGAIN_ALBUM_PEAK", "----:com.apple.iTunes:REPLAYGAIN_ALBUM_PEAK"];

    private List<MediaFile> ResolveSelectedTracksForBatchAction()
    {
        List<MediaFile> targets = [];

        if (_dataGrid != null)
        {
            targets = _dataGrid.SelectedItems.Cast<MediaFile>().ToList();
            if (targets.Count == 0 && _dataGrid.SelectedItem is MediaFile single)
                targets = [single];
        }

        if (targets.Count == 0 && _selectionService.MultiSelection.Count > 0)
            targets = _selectionService.MultiSelection.ToList();

        if (targets.Count == 0 && SelectedTrack != null)
            targets = [SelectedTrack];

        return targets
            .Where(t => !string.IsNullOrWhiteSpace(t.Path) && File.Exists(t.Path))
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
    }

    private static void SetReplayGainField(Track track, IEnumerable<string> keys, string? value)
    {
        foreach (string key in keys)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                track.AdditionalFields.Remove(key);

                string? match = track.AdditionalFields.Keys.FirstOrDefault(k =>
                    string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    track.AdditionalFields.Remove(match);
            }
            else
            {
                track.AdditionalFields[key] = value;
            }
        }
    }

    private static string FormatDb(double value)
        => string.Create(CultureInfo.InvariantCulture, $"{value:+0.00;-0.00} dB");

    [RelayCommand]
    private async Task AnalyzeReplayGainTrack()
    {
        List<MediaFile> targets = ResolveSelectedTracksForBatchAction();
        if (targets.Count == 0)
            return;

        IReplayGainCalculator calculator = App.AppHost.Services.GetRequiredService<IReplayGainCalculator>();
        List<MediaFile> updated = [];

        WeakReferenceMessenger.Default.Send(new ProgressValueMessage(new ProgressData
        {
            IsProcessing = true,
            ProcessedTracks = 0,
            TotalTracks = targets.Count,
            Status = "Analyzing ReplayGain (Track)...",
            Phase = "ReplayGain"
        }));

        int processed = 0;
        foreach (MediaFile track in targets)
        {
            ReplayGainResult result = await calculator.CalculateReplayGainAsync(track.Path);
            if (result.Success)
            {
                Track atl = new(track.Path);
                SetReplayGainField(atl, ReplayGainTrackGainKeys, FormatDb(result.TrackGain));
                SetReplayGainField(atl, ReplayGainTrackPeakKeys, result.TrackPeak.ToString("F6", CultureInfo.InvariantCulture));
                SetReplayGainField(atl, ReplayGainAlbumGainKeys, null);
                SetReplayGainField(atl, ReplayGainAlbumPeakKeys, null);

                bool saved = await atl.SaveAsync(writeProgress: null);
                if (saved)
                {
                    track.UpdateFromFileMetadata();
                    updated.Add(track);
                }
            }

            processed++;
            WeakReferenceMessenger.Default.Send(new ProgressValueMessage(new ProgressData
            {
                IsProcessing = true,
                ProcessedTracks = processed,
                TotalTracks = targets.Count,
                Status = "Analyzing ReplayGain (Track)...",
                Phase = "ReplayGain"
            }));
        }

        if (updated.Count > 0)
            await _musicLibrary.UpdateTracksAsync(updated, updateMetadata: true, updateAnalysis: false);

        WeakReferenceMessenger.Default.Send(new ProgressValueMessage(new ProgressData
        {
            IsProcessing = false,
            ProcessedTracks = updated.Count,
            TotalTracks = targets.Count,
            Status = $"ReplayGain Track analysis complete ({updated.Count}/{targets.Count})",
            Phase = "ReplayGain"
        }));
    }

    [RelayCommand]
    private async Task AnalyzeReplayGainAlbum()
    {
        List<MediaFile> targets = ResolveSelectedTracksForBatchAction();
        if (targets.Count == 0)
            return;

        IReplayGainCalculator calculator = App.AppHost.Services.GetRequiredService<IReplayGainCalculator>();
        List<(MediaFile Track, ReplayGainResult Result)> measured = [];

        WeakReferenceMessenger.Default.Send(new ProgressValueMessage(new ProgressData
        {
            IsProcessing = true,
            ProcessedTracks = 0,
            TotalTracks = targets.Count,
            Status = "Analyzing ReplayGain (Album)...",
            Phase = "ReplayGain"
        }));

        int processed = 0;
        foreach (MediaFile track in targets)
        {
            ReplayGainResult result = await calculator.CalculateReplayGainAsync(track.Path);
            if (result.Success)
                measured.Add((track, result));

            processed++;
            WeakReferenceMessenger.Default.Send(new ProgressValueMessage(new ProgressData
            {
                IsProcessing = true,
                ProcessedTracks = processed,
                TotalTracks = targets.Count,
                Status = "Analyzing ReplayGain (Album)...",
                Phase = "ReplayGain"
            }));
        }

        if (measured.Count == 0)
        {
            WeakReferenceMessenger.Default.Send(new ProgressValueMessage(new ProgressData
            {
                IsProcessing = false,
                ProcessedTracks = 0,
                TotalTracks = targets.Count,
                Status = "ReplayGain Album analysis failed",
                Phase = "ReplayGain"
            }));
            return;
        }

        double albumGain = measured.Average(m => m.Result.TrackGain);
        double albumPeak = measured.Max(m => m.Result.TrackPeak);
        string albumGainValue = FormatDb(albumGain);
        string albumPeakValue = albumPeak.ToString("F6", CultureInfo.InvariantCulture);

        List<MediaFile> updated = [];
        foreach ((MediaFile track, ReplayGainResult result) in measured)
        {
            Track atl = new(track.Path);
            SetReplayGainField(atl, ReplayGainTrackGainKeys, FormatDb(result.TrackGain));
            SetReplayGainField(atl, ReplayGainTrackPeakKeys, result.TrackPeak.ToString("F6", CultureInfo.InvariantCulture));
            SetReplayGainField(atl, ReplayGainAlbumGainKeys, albumGainValue);
            SetReplayGainField(atl, ReplayGainAlbumPeakKeys, albumPeakValue);

            bool saved = await atl.SaveAsync(writeProgress: null);
            if (saved)
            {
                track.UpdateFromFileMetadata();
                updated.Add(track);
            }
        }

        if (updated.Count > 0)
            await _musicLibrary.UpdateTracksAsync(updated, updateMetadata: true, updateAnalysis: false);

        WeakReferenceMessenger.Default.Send(new ProgressValueMessage(new ProgressData
        {
            IsProcessing = false,
            ProcessedTracks = updated.Count,
            TotalTracks = targets.Count,
            Status = $"ReplayGain Album analysis complete ({updated.Count}/{targets.Count})",
            Phase = "ReplayGain"
        }));
    }

    [RelayCommand]
    private async Task RemoveReplayGainAnalysis()
    {
        List<MediaFile> targets = ResolveSelectedTracksForBatchAction();
        if (targets.Count == 0)
            return;

        List<MediaFile> updated = [];
        foreach (MediaFile track in targets)
        {
            try
            {
                Track atl = new(track.Path);
                SetReplayGainField(atl, ReplayGainTrackGainKeys, null);
                SetReplayGainField(atl, ReplayGainTrackPeakKeys, null);
                SetReplayGainField(atl, ReplayGainAlbumGainKeys, null);
                SetReplayGainField(atl, ReplayGainAlbumPeakKeys, null);

                bool saved = await atl.SaveAsync(writeProgress: null);
                if (saved)
                {
                    track.UpdateFromFileMetadata();
                    updated.Add(track);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove ReplayGain analysis for {Path}", track.Path);
            }
        }

        if (updated.Count > 0)
            await _musicLibrary.UpdateTracksAsync(updated, updateMetadata: true, updateAnalysis: false);
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

        if (currentTab is LibraryTab)
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

            // NOTE: Selecting the first track (e.g., on tab load) does NOT change the active track.
            // If Track A is playing, selecting the first track in the current tab should not make the
            // first track become active.

            if (SelectedTabIndex < _musicLibrary.Playlists.Count)
            {
                _musicLibrary.Playlists[SelectedTabIndex].SelectedTrackId = SelectedTrack!.Id;
            }

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
                                {
                                    // Vorbis-family: write raw RATING field (Popularity rounds to whole numbers in ATL).
                                    double ratingScale = Models.MediaFileHelper.RatingScaleForPath(mediaFile.Path);
                                    if (ratingScale <= 1.0)
                                    {
                                        if (mediaFile.Rating > 0.0)
                                        {
                                            double val = Math.Round(mediaFile.Rating * ratingScale / 5.0, 2);
                                            atlTrack.AdditionalFields["RATING"] = val.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                                        }
                                        else
                                        {
                                            atlTrack.AdditionalFields.Remove("RATING");
                                        }
                                    }
                                    else
                                    {
                                        atlTrack.Popularity = Models.MediaFileHelper.StarsToPopularity(mediaFile.Rating, mediaFile.Path);
                                    }
                                    break;
                                }
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
