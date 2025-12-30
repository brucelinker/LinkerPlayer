namespace LinkerPlayer.Models;

public class AppSettings
{
    public bool EqualizerEnabled
    {
        get; set;
    }
    public string EqualizerPresetName { get; set; } = "Flat";
    public Device SelectedOutputDevice { get; set; } = new Device("Default", OutputDeviceType.DirectSound, -1, true);
    public OutputMode SelectedOutputMode { get; set; } = OutputMode.DirectSound;
    public int SelectedTabIndex { get; set; } = 0;
    public string SelectedTrackId { get; set; } = string.Empty;
    public string SelectedTheme { get; set; } = "Dark";
    public bool ShuffleMode { get; set; } = false;
    public double VolumeSliderValue { get; set; } = 0.0;

    // New: Persisted splitter positions
    // Key: logical name (e.g., "MainTrackInfoRows", "PlaylistColumns")
    // Value: list of star ratios for the adjustable definitions in order
    public Dictionary<string, List<double>> SplitterLayouts { get; set; } = new();

    // New: Remember which monitor the MainWindow was on last close to position Splash on the same screen next launch
    public string LastMainWindowMonitorDeviceName { get; set; } = string.Empty;
    public List<string> VisibleColumns { get; set; } = new();
    public Dictionary<string, ColumnInfo> ColumnSettings { get; set; } = new();

    // Configurable settings for silence-based auto-advance
    public double AutoAdvanceTailWindowSeconds { get; set; } = 10.0;
    public double AutoAdvanceSilenceThresholdDb { get; set; } = -40.0;
    public double AutoAdvanceSilenceHoldSeconds { get; set; } = 0.75;
    public double AutoAdvanceHardEndSeconds { get; set; } = 0.5;

    [Serializable]
    public class ColumnInfo
    {
        public double Width { get; set; } = 100;
        public int Position { get; set; } = -1;   // -1 = far right
    }
}
