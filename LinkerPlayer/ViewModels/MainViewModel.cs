using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;

namespace LinkerPlayer.ViewModels;

public class MainViewModel : ObservableObject
{
    private static readonly ThemeManager ThemeMgr = new();
    private readonly SettingsManager _settingsManager;
    private readonly PlayerControlsViewModel _playerControlsViewModel;
    private readonly PlaylistTabsViewModel _playlistTabsViewModel;

    public MainViewModel(
        SettingsManager settingsManager,
        PlayerControlsViewModel playerControlsViewModel,
        PlaylistTabsViewModel playlistTabsViewModel)
    {
        Serilog.Log.Information("MainViewModel: Initializing");
        _settingsManager = settingsManager;
        _playerControlsViewModel = playerControlsViewModel;
        _playlistTabsViewModel = playlistTabsViewModel;

        ThemeColors selectedTheme;
        OutputDeviceManager.InitializeOutputDevice();

        if (!string.IsNullOrEmpty(_settingsManager.Settings.SelectedTheme))
        {
            selectedTheme = ThemeMgr.StringToThemeColor(_settingsManager.Settings.SelectedTheme);
        }
        else
        {
            selectedTheme = ThemeColors.Dark;
        }

        selectedTheme = ThemeMgr.ModifyTheme(selectedTheme);
    }

    public PlayerControlsViewModel PlayerControlsViewModel => _playerControlsViewModel;
    public PlaylistTabsViewModel PlaylistTabsViewModel => _playlistTabsViewModel;

    public void OnWindowLoaded()
    {
    }

    public void OnWindowClosing()
    {
        MusicLibrary.ClearPlayState();
        MusicLibrary.SaveToJson();
        _settingsManager.Settings.MainOutputDevice = OutputDeviceManager.GetCurrentDeviceName();
        _settingsManager.SaveSettings(nameof(AppSettings.MainOutputDevice));
    }
}
