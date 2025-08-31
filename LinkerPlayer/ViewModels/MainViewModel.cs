using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace LinkerPlayer.ViewModels;

public class MainViewModel : ObservableObject
{
    private static readonly ThemeManager ThemeMgr = new();
    private readonly SettingsManager _settingsManager;
    private readonly OutputDeviceManager _outputDeviceManager;
    private readonly IMusicLibrary _musicLibrary;
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModel(
        SettingsManager settingsManager,
        PlayerControlsViewModel playerControlsViewModel,
        PlaylistTabsViewModel playlistTabsViewModel,
        OutputDeviceManager outputDeviceManager,
        IMusicLibrary musicLibrary,
        ILogger<MainViewModel> logger)
    {
        _musicLibrary = musicLibrary;
        _logger = logger;

        try
        {
            _logger.Log(LogLevel.Information, "Initializing MainViewModel"); _settingsManager = settingsManager;
            PlayerControlsViewModel = playerControlsViewModel;
            PlaylistTabsViewModel = playlistTabsViewModel;
            _outputDeviceManager = outputDeviceManager;

            ThemeColors selectedTheme;
            _outputDeviceManager.InitializeOutputDevice();

            if (!string.IsNullOrEmpty(_settingsManager.Settings.SelectedTheme))
            {
                selectedTheme = ThemeMgr.StringToThemeColor(_settingsManager.Settings.SelectedTheme);
            }
            else
            {
                selectedTheme = ThemeColors.Dark;
            }

            ThemeMgr.ModifyTheme(selectedTheme);
            _logger.Log(LogLevel.Information, "MainViewModel initialized successfully");
        }
        catch (IOException ex)
        {
            _logger.Log(LogLevel.Error, ex, "IO error in MainViewModel constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, ex, "Unexpected error in MainViewModel constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
    }

    public PlayerControlsViewModel PlayerControlsViewModel { get; }

    public PlaylistTabsViewModel PlaylistTabsViewModel { get; }

    public void OnWindowLoaded()
    {
    }

    public void OnWindowClosing()
    {
        _musicLibrary.ClearPlayState();
        Task.Run(async () =>
        {
            await _musicLibrary.SaveToDatabaseAsync();
        }).Wait();
        _settingsManager.Settings.MainOutputDevice = _outputDeviceManager.GetCurrentDeviceName();
        _settingsManager.SaveSettings(nameof(AppSettings.MainOutputDevice));
    }
}
