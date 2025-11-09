using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Services; // for IDatabaseSaveService
using Microsoft.Extensions.Logging;
using System.IO;

namespace LinkerPlayer.ViewModels;

public class MainViewModel : ObservableObject
{
    private static readonly ThemeManager ThemeMgr = new();
    private readonly ISettingsManager _settingsManager;
    private readonly IMusicLibrary _musicLibrary;
    private readonly IDatabaseSaveService _databaseSaveService; // flush pending debounced saves
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModel(
        ISettingsManager settingsManager,
        PlayerControlsViewModel playerControlsViewModel,
        PlaylistTabsViewModel playlistTabsViewModel,
        IMusicLibrary musicLibrary,
        IDatabaseSaveService databaseSaveService,
        ILogger<MainViewModel> logger)
    {
        _musicLibrary = musicLibrary;
        _databaseSaveService = databaseSaveService;
        _logger = logger;

        try
        {
            _logger.Log(LogLevel.Information, "Initializing MainViewModel");
            _settingsManager = settingsManager;
            PlayerControlsViewModel = playerControlsViewModel;
            PlaylistTabsViewModel = playlistTabsViewModel;

            ThemeColors selectedTheme;

            if (!string.IsNullOrEmpty(_settingsManager.Settings.SelectedTheme))
            {
                selectedTheme = ThemeMgr.StringToThemeColor(_settingsManager.Settings.SelectedTheme);
            }
            else
            {
                selectedTheme = ThemeColors.Dark;
            }

            // Theme system now properly preserves fonts via caching in ThemeManager
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

    public PlayerControlsViewModel PlayerControlsViewModel
    {
        get;
    }

    public PlaylistTabsViewModel PlaylistTabsViewModel
    {
        get;
    }

    public void OnWindowLoaded()
    {
    }

    public void OnWindowClosing()
    {
        // Flush any pending debounced playlist/selection changes first.
        _databaseSaveService.SaveImmediately();

        // Perform a final synchronous save to ensure persistence before process exit.
        _musicLibrary.SaveToDatabase();

        // Persist last selected output device (and any other immediate settings).
        _settingsManager.SaveSettings(nameof(AppSettings.SelectedOutputDevice));
    }
}
