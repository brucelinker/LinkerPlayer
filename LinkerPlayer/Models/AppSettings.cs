namespace LinkerPlayer.Models;

public class AppSettings
{
    public bool EqualizerEnabled { get; set; }
    public string EqualizerPresetName { get; set; } = "Flat";
    public Device SelectedOutputDevice { get; set; } = new Device("Default", OutputDeviceType.DirectSound, -1, true);
    public OutputMode SelectedOutputMode { get; set; } = OutputMode.DirectSound;
    public int SelectedTabIndex { get; set; }
    public string SelectedTrackId { get; set; } = string.Empty;
    public string SelectedTheme { get; set; } = "Dark";
    public bool ShuffleMode { get; set; }
    public double VolumeSliderValue { get; set; }

    // New: Persisted splitter positions
    // Key: logical name (e.g., "MainTrackInfoRows", "PlaylistColumns")
    // Value: list of star ratios for the adjustable definitions in order
    public Dictionary<string, List<double>> SplitterLayouts { get; set; } = new();

    // New: Remember which monitor the MainWindow was on last close to position Splash on the same screen next launch
    public string? LastMainWindowMonitorDeviceName { get; set; }
}
