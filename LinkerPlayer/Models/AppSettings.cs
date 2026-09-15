using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using System.Text.Json.Serialization;   // ← for JsonIgnore

namespace LinkerPlayer.Models;

public class AppSettings
{
    public bool EqualizerEnabled
    {
        get; set;
    }
    public string EqualizerPresetName { get; set; } = "Flat";
    public Device SelectedOutputDevice { get; set; } = new Device("Primary Sound Driver", OutputDeviceType.DirectSound, -1, true);
    public OutputMode SelectedOutputMode { get; set; } = OutputMode.DirectSound;
    public int SelectedTabIndex { get; set; } = 0;
    public string SelectedTrackId { get; set; } = string.Empty;
    public string SelectedTheme { get; set; } = "Dark";
    public bool ShuffleMode { get; set; } = false;
    public double VolumeSliderValue { get; set; } = 0.0;

    // Crossfade settings
    public bool CrossfadeEnabled { get; set; } = false;
    public int CrossfadeFadeInMs { get; set; } = 400;
    public int CrossfadeFadeOutMs { get; set; } = 400;
    public FadeCurveShape CrossfadeCurveShape { get; set; } = FadeCurveShape.Cosine;

    // Smooth fade when stopping
    public bool SmoothStopFadeEnabled { get; set; } = true;
    public int SmoothStopFadeMs { get; set; } = 150;

    // New: Persisted splitter positions
    // Key: logical name (e.g., "MainTrackInfoRows", "PlaylistColumns")
    // Value: list of star ratios for the adjustable definitions in order
    public Dictionary<string, List<double>> SplitterLayouts { get; set; } = new();

    // New: Remember which monitor the MainWindow was on last close to position Splash on the same screen next launch
    public string LastMainWindowMonitorDeviceName { get; set; } = string.Empty;
    public Dictionary<string, WindowBoundsSettings> WindowBounds { get; set; } = new();
    public List<string> VisibleColumns { get; set; } = new();
    public Dictionary<string, ColumnInfo> ColumnSettings { get; set; } = new();

    // Per-library column visibility and layout (separate from playlists)
    public List<string> LibraryVisibleColumns { get; set; } = new();
    public Dictionary<string, ColumnInfo> LibraryColumnSettings { get; set; } = new();

    // New: SkipSilence analysis settings
    public bool SkipSilenceEnabled { get; set; } = false;
    public int SkipSilenceMinimumDurationMs { get; set; } = 5000;
    public int SkipSilenceLeaveInitialMs { get; set; } = 200;
    public int SkipSilenceThresholdDb { get; set; } = -60;

    // Library tab state persistence
    public string LastLibrarySelectedTrackId { get; set; } = string.Empty;
    public List<string> LastLibrarySelectedGenres { get; set; } = new();
    public List<string> LastLibrarySelectedArtists { get; set; } = new();
    public List<string> LastLibrarySelectedAlbums { get; set; } = new();
    public List<string> LastLibrarySelectedCodecs { get; set; } = new();

    // Library column sort state
    public string LibrarySortColumn { get; set; } = string.Empty;      // Backward-compat primary SortMemberPath
    public string LibrarySortDirection { get; set; } = string.Empty;   // Backward-compat primary direction: "Ascending" | "Descending"
    public List<SortDescriptionState> LibrarySortDescriptions { get; set; } = new();

    // Library query/filter state
    public string LastLibraryKeywordSearch { get; set; } = string.Empty;
    public List<FilterCriteriaSettings> LastLibraryActiveFilters { get; set; } = new();

    // Per-playlist column sort state — keyed by playlist name
    public Dictionary<string, PlaylistSortState> PlaylistSortStates { get; set; } = new();

    // Watched folders — scanned on startup to auto-import audio files
    public List<string> WatchedFolders { get; set; } = new();

    // UTC timestamp of the most recent successful full diff-scan completion
    public DateTime? LastScanCompletedUtc { get; set; } = null;

    // Whether to automatically rescan watched folders on startup (default: false for better performance)
    public bool AutomaticallyRescanWatchedFolders { get; set; } = false;

    public string? EncryptedMusicBrainzUsername { get; set; }
    public string? EncryptedMusicBrainzPassword { get; set; }

    // Helper properties (not serialized)
    [JsonIgnore]
    public string MusicBrainzUsername
    {
        get => EncryptedMusicBrainzUsername?.Unprotect() ?? string.Empty;
        set => EncryptedMusicBrainzUsername = value.Protect();
    }

    [JsonIgnore]
    public string MusicBrainzPassword
    {
        get => EncryptedMusicBrainzPassword?.Unprotect() ?? string.Empty;
        set => EncryptedMusicBrainzPassword = value.Protect();
    }

    [Serializable]
    public class ColumnInfo
    {
        public double Width { get; set; } = 100;
        public int Position { get; set; } = -1;   // -1 = far right
    }

    [Serializable]
    public class WindowBoundsSettings
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    /// <summary>Serializable snapshot of a single FilterCriteria row.</summary>
    public class FilterCriteriaSettings
    {
        public string Type { get; set; } = string.Empty;       // FilterType enum name
        public string Operator { get; set; } = string.Empty;   // FilterOperator enum name
        public string Value { get; set; } = string.Empty;
        public string? ValueSecondary { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    public class SortDescriptionState
    {
        public string SortColumn { get; set; } = string.Empty;    // SortMemberPath
        public string SortDirection { get; set; } = string.Empty; // "Ascending" | "Descending"
    }

    /// <summary>Serializable sort state for a single playlist's DataGrid.</summary>
    public class PlaylistSortState
    {
        public string SortColumn { get; set; } = string.Empty;    // Backward-compat primary SortMemberPath
        public string SortDirection { get; set; } = string.Empty; // Backward-compat primary direction: "Ascending" | "Descending"
        public List<SortDescriptionState> SortDescriptions { get; set; } = new();
    }
}
