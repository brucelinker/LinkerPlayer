using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.Services.Playback;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace LinkerPlayer.ViewModels;

/// <summary>
/// ViewModel for the Library tab. Owns Library-specific state, column/filter restore,
/// and selection persistence/restore. This is the single place for Library selection logic.
/// </summary>
public partial class LibraryTabViewModel : BaseTabViewModel
{
    private readonly IMusicLibrary _musicLibrary;
    private readonly ISettingsManager _settingsManager;
    private readonly ISelectionService _selectionService;

    [ObservableProperty]
    private LibraryTab? _libraryTab;

    [ObservableProperty]
    private MediaFile? _selectedTrack;

    [ObservableProperty]
    private int _selectedTrackIndex = -1;

    private MediaFile? _lastSessionSelectedTrack; // in-memory for tab switches during this run

    public LibraryTabViewModel(
        IMusicLibrary musicLibrary,
        ISettingsManager settingsManager,
        ISelectionService selectionService,
        IPlaybackCoordinator playbackCoordinator,
        ILogger<LibraryTabViewModel> logger)
        : base(playbackCoordinator, logger)
    {
        _musicLibrary = musicLibrary ?? throw new ArgumentNullException(nameof(musicLibrary));
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));

        RegisterMessages();   // safe to call here now
        _logger.LogInformation("LibraryTabViewModel initialized");
    }

    public MediaFile? LastSessionSelectedTrack
    {
        get => _lastSessionSelectedTrack;
        set => _lastSessionSelectedTrack = value;
    }

    /// <summary>
    /// Initializes the Library tab with the main library collection and restores saved state.
    /// Call this once at startup.
    /// </summary>
    public void InitializeLibraryTab(ObservableCollection<MediaFile> mainLibrary)
    {
        if (LibraryTab != null)
        {
            _logger.LogWarning("LibraryTab already initialized");
            return;
        }

        LibraryTab = new LibraryTab(mainLibrary);

        // Restore visible columns
        if (_settingsManager.Settings.LibraryVisibleColumns != null &&
            _settingsManager.Settings.LibraryVisibleColumns.Count > 0)
        {
            LibraryTab.VisibleColumns = new List<string>(_settingsManager.Settings.LibraryVisibleColumns);
        }

        // Migration: ensure "Year" column exists
        if (!LibraryTab.VisibleColumns.Contains("Year"))
        {
            LibraryTab.VisibleColumns.Add("Year");
            _settingsManager.Settings.LibraryVisibleColumns = LibraryTab.VisibleColumns;
            _settingsManager.SaveSettings(nameof(AppSettings.LibraryVisibleColumns));
        }

        // Restore faceted filter selections
        LibraryTab.RestoreFilterSelections(_settingsManager.Settings);

        _logger.LogInformation("LibraryTab initialized with {Count} tracks", mainLibrary.Count);
    }

    /// <summary>
    /// Called when this tab becomes the active/selected tab.
    /// </summary>
    public override async Task ActivateAsync()
    {
        await RestoreSelectionAsync();
    }

    /// <summary>
    /// Restores the last selected track in the Library from Settings.json.
    /// This is the SINGLE place Library selection restore logic lives.
    /// </summary>
    public override Task RestoreSelectionAsync()
    {
        // Prefer in-memory session selection first (for tab switches)
        MediaFile? toRestore = _lastSessionSelectedTrack ??
            _musicLibrary.MainLibrary.FirstOrDefault(t =>
                string.Equals(t.Id, _settingsManager.Settings.LastLibrarySelectedTrackId, StringComparison.Ordinal));

        if (toRestore == null)
        {
            _logger.LogDebug("RestoreSelectionAsync (Library): No saved or session track");
            return Task.CompletedTask;
        }

        SelectedTrack = toRestore;
        SelectedTrackIndex = _musicLibrary.MainLibrary.IndexOf(toRestore);
        _lastSessionSelectedTrack = toRestore; // keep for next tab switch

        if (LibraryTab != null)
        {
            LibraryTab.SelectedTrack = toRestore;
            LibraryTab.SelectedIndex = SelectedTrackIndex;
        }

        _selectionService.SetTrack(toRestore, SelectedTrackIndex);

        _logger.LogInformation("Library selection restored to '{Title}' (ID: {Id}) [session: {Session}]",
            toRestore.Title, toRestore.Id, _lastSessionSelectedTrack != null);
        return Task.CompletedTask;
    }

    // TODO: Move these commands here from the big VM in a later step
    // [RelayCommand] private async Task RescanSelectedTracks() { ... }
    // [RelayCommand] private async Task AnalyzeSilence() { ... }
}
