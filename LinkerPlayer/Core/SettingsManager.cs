using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;

namespace LinkerPlayer.Core;

public interface ISettingsManager
{
    AppSettings Settings
    {
        get;
    }
    void LoadSettings();
    void SaveSettings(string propertyName);
    event Action<string>? SettingsChanged;
}

public class SettingsManager : ISettingsManager
{
    private readonly string _settingsPath = string.Empty;
    private readonly ILogger<ISettingsManager> _logger;
    public AppSettings Settings { get; private set; } = new();
    private readonly System.Timers.Timer _saveTimer = new();
    public event Action<string>? SettingsChanged;
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly bool _isInitialized;
    private readonly object _fileLock = new();

    public SettingsManager(ILogger<ISettingsManager> logger)
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

            // Debounced save timer
            _saveTimer = new System.Timers.Timer(1000) { AutoReset = false };
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
            lock (_fileLock)
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
            lock (_fileLock)
            {
                JsonSerializerOptions options = new()
                {
                    WriteIndented = true
                };
                string json = JsonSerializer.Serialize(Settings, options);
                File.WriteAllText(_settingsPath, json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", _settingsPath);
        }
    }
}
