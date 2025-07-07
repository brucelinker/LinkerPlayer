using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Timers;

namespace LinkerPlayer.Core;

public class SettingsManager
{
    private readonly string _settingsPath = string.Empty;
    private readonly ILogger<SettingsManager> _logger;
    public AppSettings Settings { get; private set; } = new();
    private readonly Timer _saveTimer = new();
    public event Action<string>? SettingsChanged;
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly bool _isInitialized;

    public SettingsManager(ILogger<SettingsManager> logger)
    {
        _logger = logger;
        _logger.LogInformation("Initializing SettingsManager");

        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "LinkerPlayer");

            //_logger.LogInformation("Creating directory: {AppFolder}", appFolder);

            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }

            _settingsPath = Path.Combine(appFolder, "settings.json");

            _saveTimer = new Timer(1000) { AutoReset = false };
            _saveTimer.Elapsed += (_, _) => SaveSettingsInternal();

            //_logger.LogInformation("Loading settings from: {SettingsPath}", _settingsPath);
            LoadSettings();
            _logger.LogInformation("SettingsManager initialized successfully");

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SettingsManager constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            Settings = new AppSettings();
        }
    }

    public void LoadSettings()
    {
        if (_isInitialized)
        {
            _logger.LogInformation("SettingsManager already initialized, skipping LoadSettings, InstanceId: {InstanceId}", _instanceId);
            return;
        }

        try
        {
            if (File.Exists(_settingsPath))
            {
                _logger.LogInformation("Reading settings file: {SettingsPath}", _settingsPath);
                string json = File.ReadAllText(_settingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                _logger.LogInformation("Settings file not found, using defaults: {SettingsPath}", _settingsPath);
                Settings = new AppSettings();
            }
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error loading settings from {Path}: {Message}\n{StackTrace}", _settingsPath, ex.Message, ex.StackTrace);
            Settings = new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading settings from {Path}: {Message}\n{StackTrace}", _settingsPath, ex.Message, ex.StackTrace);
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
            JsonSerializerOptions options = new() { WriteIndented = true };
            string json = JsonSerializer.Serialize(Settings, options);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", _settingsPath);
        }
    }
}