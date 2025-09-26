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
}