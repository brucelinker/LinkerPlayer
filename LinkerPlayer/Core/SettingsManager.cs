using LinkerPlayer.Models;
using Serilog;
using System;
using System.IO;
using System.Text.Json;
using System.Timers;

namespace LinkerPlayer.Core;

public class SettingsManager
{
    private readonly string _settingsPath;
    public AppSettings Settings { get; private set; } = new AppSettings();
    private readonly Timer _saveTimer;
    public event Action<string>? SettingsChanged;

    public SettingsManager()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(appData, "LinkerPlayer");
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "settings.json");

        _saveTimer = new Timer(1000) { AutoReset = false };
        _saveTimer.Elapsed += (_, _) => SaveSettingsInternal();

        LoadSettings();
    }

    public void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load settings from {Path}", _settingsPath);
            Settings = new AppSettings();
        }
    }

    public void SaveSettings(string propertyName)
    {
        _saveTimer.Stop();
        _saveTimer.Start();
        SettingsChanged?.Invoke(propertyName);
    }

    private void SaveSettingsInternal()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(Settings, options);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings to {Path}", _settingsPath);
        }
    }
}