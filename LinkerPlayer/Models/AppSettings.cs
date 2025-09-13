namespace LinkerPlayer.Models;

public class AppSettings
{
    public bool EqualizerEnabled { get; set; }
    public string EqualizerPresetName { get; set; } = "Flat";
    public string SelectedOutputDevice { get; set; } = string.Empty;
    public OutputMode SelectedOutputMode { get; set; } = OutputMode.DirectSound;
    public int SelectedTabIndex { get; set; }
    public string SelectedTheme { get; set; } = "Slate";
    public bool ShuffleMode { get; set; }
    public double VolumeSliderValue { get; set; }
}