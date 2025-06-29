using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO;
using System.Text.Json;
using System.Timers;

namespace LinkerPlayer.Core;

public class SettingsManager
{
    private readonly string _settingsPath;
    private readonly ILogger<SettingsManager> _logger;
    public AppSettings Settings { get; private set; } = new();
    private readonly Timer _saveTimer;
    public event Action<string>? SettingsChanged;
    private readonly Guid _instanceId = Guid.NewGuid();
    private bool _isInitialized;

    public SettingsManager(ILogger<SettingsManager> logger)
    {
        _logger = logger;
        _logger.Log(LogLevel.Information,"Initializing SettingsManager, InstanceId: {InstanceId}", _instanceId);
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "LinkerPlayer");
            _logger.Log(LogLevel.Information, "Creating directory: {AppFolder}", appFolder);
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }
            _settingsPath = Path.Combine(appFolder, "settings.json");

            _saveTimer = new Timer(1000) { AutoReset = false };
            _saveTimer.Elapsed += (_, _) => SaveSettingsInternal();

            _logger.Log(LogLevel.Information, "Loading settings from: {SettingsPath}", _settingsPath);
            LoadSettings();
            _logger.Log(LogLevel.Information, "SettingsManager initialized successfully");
            _isInitialized = true;
        }
        catch (IOException ex)
        {
            _logger.Log(LogLevel.Error, ex, "IO error in SettingsManager constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            Settings = new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, ex, "Unexpected error in SettingsManager constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            Settings = new AppSettings();
        }
    }

    public void LoadSettings()
    {
        if (_isInitialized)
        {
            _logger.Log(LogLevel.Information, "SettingsManager already initialized, skipping LoadSettings, InstanceId: {InstanceId}", _instanceId);
            return;
        }

        try
        {
            if (File.Exists(_settingsPath))
            {
                _logger.Log(LogLevel.Information, "Reading settings file: {SettingsPath}", _settingsPath);
                string json = File.ReadAllText(_settingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                _logger.Log(LogLevel.Information, "Settings file not found, using defaults: {SettingsPath}", _settingsPath);
                Settings = new AppSettings();
            }
        }
        catch (IOException ex)
        {
            _logger.Log(LogLevel.Error, ex, "IO error loading settings from {Path}: {Message}\n{StackTrace}", _settingsPath, ex.Message, ex.StackTrace);
            Settings = new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, ex, "Unexpected error loading settings from {Path}: {Message}\n{StackTrace}", _settingsPath, ex.Message, ex.StackTrace);
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
        JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(Settings, options);
        File.WriteAllText(_settingsPath, json);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to save settings to {Path}", _settingsPath);
    }
}
}