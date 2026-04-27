namespace LinkerPlayer.Models;

using LinkerPlayer.Audio;

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
    public string LibrarySortColumn { get; set; } = string.Empty;      // SortMemberPath of the sorted column
    public string LibrarySortDirection { get; set; } = string.Empty;   // "Ascending" | "Descending"

    // Watched folders — scanned on startup to auto-import audio files
    public List<string> WatchedFolders { get; set; } = new();

    [Serializable]
    public class ColumnInfo
    {
        public double Width { get; set; } = 100;
        public int Position { get; set; } = -1;   // -1 = far right
    }
}
