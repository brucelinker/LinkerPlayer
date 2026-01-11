using LinkerPlayer.Core;
using LinkerPlayer.Models;

namespace LinkerPlayer.Tests.Fakes;

public sealed class FakeSettingsManager : ISettingsManager
{
    public AppSettings Settings { get; set; } = new AppSettings();

    public event Action<string>? SettingsChanged;

    public void LoadSettings()
    {
    }

    public void SaveSettings(string propertyName)
    {
        SettingsChanged?.Invoke(propertyName);
    }
}
