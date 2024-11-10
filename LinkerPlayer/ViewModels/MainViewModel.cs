using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;

namespace LinkerPlayer.ViewModels;

public class MainViewModel : ObservableObject
{
    private static readonly ThemeManager ThemeMgr = new();

    public MainViewModel()
    {
        ThemeColors selectedTheme;

        OutputDeviceManager.InitializeOutputDevice();

        if (!string.IsNullOrEmpty(Properties.Settings.Default.SelectedTheme))
        {
            selectedTheme = ThemeMgr.StringToThemeColor(Properties.Settings.Default.SelectedTheme);
        }
        else
        {
            selectedTheme = ThemeColors.Dark;
        }

        // Sets the theme
        selectedTheme = ThemeMgr.ModifyTheme(selectedTheme);

        Properties.Settings.Default.Save();
    }

    public void OnWindowLoaded()
    {
    }

    public void OnWindowClosing()
    {
        MusicLibrary.ClearPlayState();
        MusicLibrary.SaveToJson();

        Properties.Settings.Default.MainOutputDevice = OutputDeviceManager.GetCurrentDeviceName();
        Properties.Settings.Default.Save();
    }
}