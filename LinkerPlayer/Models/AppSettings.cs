namespace LinkerPlayer.Models;

public class AppSettings
{
    public bool EqualizerEnabled { get; set; }
    public string EqualizerPresetName { get; set; } = "Flat";
    public string MainOutputDevice { get; set; } = string.Empty;
    public int SelectedTabIndex { get; set; }
    public string SelectedTheme { get; set; } = "Slate";
    public bool ShuffleMode { get; set; }
    public double VolumeSliderValue { get; set; }
}