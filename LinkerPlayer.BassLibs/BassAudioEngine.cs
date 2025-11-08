using ManagedBass;
using ManagedBass.Wasapi;
using Microsoft.Extensions.Logging;

namespace LinkerPlayer.BassLibs;

/// <summary>
/// Configuration options for BASS initialization
/// </summary>
public class BassInitializationOptions
{
    public int SampleRate { get; set; } = 44100;
    public DeviceInitFlags InitFlags { get; set; } = DeviceInitFlags.Default;
    public bool EnableWasapi { get; set; } = true;
    public WasapiInitFlags WasapiFlags { get; set; } = WasapiInitFlags.Shared;
    public int UpdatePeriod { get; set; } = 10;
    public int PlaybackBufferLength { get; set; } = 500;
    public int UpdatePeriodConfig { get; set; } = 50;
    public bool LoadEssentialPluginsOnly { get; set; } = true;
}

/// <summary>
/// Result of BASS initialization
/// </summary>
public class BassInitializationResult
{
    public bool IsSuccess
    {
        get; set;
    }
    public bool IsBassInitialized
    {
        get; set;
    }
    public bool IsWasapiInitialized
    {
        get; set;
    }
    public List<string> LoadedPlugins { get; set; } = new();
    public List<string> FailedPlugins { get; set; } = new();
    public string? ErrorMessage
    {
        get; set;
    }
    public Exception? Exception
    {
        get; set;
    }
}

/// <summary>
/// High-level BASS audio engine manager
/// </summary>
public class BassAudioEngine : IDisposable
{
    private bool _isInitialized = false;
    private readonly ILogger<BassAudioEngine> _logger;
    private BassInitializationResult? _initializationResult;

    public BassAudioEngine(ILogger<BassAudioEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initialize the BASS audio engine with the specified options
    /// </summary>
    /// <param name="options">Initialization options</param>
    /// <returns>Initialization result</returns>
    public BassInitializationResult Initialize(BassInitializationOptions? options = null)
    {
        if (_isInitialized && _initializationResult != null)
            return _initializationResult;

        options ??= new BassInitializationOptions();
        _initializationResult = new BassInitializationResult();

        try
        {
            _logger.LogInformation("Initializing BASS Audio Engine");

            // Step 1: Initialize native library manager
            BassNativeLibraryManager.Initialize(_logger);

            // Step 2: Set DLL directory for BASS to find native libraries
            string nativeLibPath = BassNativeLibraryManager.GetNativeLibraryPath();
            SetDllDirectory(nativeLibPath);
            _logger.LogInformation($"Set DLL directory to: {nativeLibPath}");

            // Step 3: Log BASS version
            Version? version = ManagedBass.Bass.Version;
            _logger.LogInformation($"BASS Version: {version}");

            // Step 4: Initialize BASS
            if (!ManagedBass.Bass.Init(-1, options.SampleRate, options.InitFlags))
            {
                Errors error = ManagedBass.Bass.LastError;
                _initializationResult.ErrorMessage = $"Failed to initialize BASS: {error}";
                _logger.LogError(_initializationResult.ErrorMessage);
                return _initializationResult;
            }

            _initializationResult.IsBassInitialized = true;
            _logger.LogInformation("BASS initialized successfully");

            // Step 5: Initialize WASAPI (optional)
            if (options.EnableWasapi)
            {
                try
                {
                    BassWasapi.Init(-1, options.SampleRate, 2, options.WasapiFlags);
                    _initializationResult.IsWasapiInitialized = true;
                    _logger.LogInformation("WASAPI initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "WASAPI initialization failed, continuing without WASAPI");
                }
            }

            // Step 6: Configure BASS
            ManagedBass.Bass.Configure(Configuration.PlaybackBufferLength, options.PlaybackBufferLength);
            ManagedBass.Bass.Configure(Configuration.UpdatePeriod, options.UpdatePeriodConfig);
            ManagedBass.Bass.UpdatePeriod = options.UpdatePeriod;

            // Step 7: Load plugins
            if (options.LoadEssentialPluginsOnly)
            {
                LoadEssentialPlugins(_initializationResult);
            }
            else
            {
                LoadAllPlugins(_initializationResult);
            }

            // Step 8: Log device info
            DeviceInfo deviceInfo = ManagedBass.Bass.GetDeviceInfo(ManagedBass.Bass.CurrentDevice);
            _logger.LogInformation($"Using audio device: {deviceInfo.Name}");

            // Reset DLL directory
            SetDllDirectory(null);

            _initializationResult.IsSuccess = true;
            _isInitialized = true;
            _logger.LogInformation($"BASS Audio Engine initialized successfully. Loaded {_initializationResult.LoadedPlugins.Count} plugins.");

            return _initializationResult;
        }
        catch (Exception ex)
        {
            _initializationResult.Exception = ex;
            _initializationResult.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to initialize BASS Audio Engine");
            return _initializationResult;
        }
    }

    /// <summary>
    /// Gets the current initialization result
    /// </summary>
    public BassInitializationResult? GetInitializationResult() => _initializationResult;

    /// <summary>
    /// Checks if BASS is currently initialized
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Shutdown the BASS audio engine
    /// </summary>
    public void Shutdown()
    {
        if (!_isInitialized)
            return;

        _logger.LogInformation("Shutting down BASS Audio Engine");

        try
        {
            if (_initializationResult?.IsWasapiInitialized == true)
            {
                BassWasapi.Free();
            }

            ManagedBass.Bass.Free();
            BassNativeLibraryManager.Cleanup();

            _isInitialized = false;
            _initializationResult = null;
            _logger.LogInformation("BASS Audio Engine shutdown complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during BASS Audio Engine shutdown");
        }
    }

    private void LoadEssentialPlugins(BassInitializationResult result)
    {
        // Load only essential plugins that require explicit loading
        // Defer codec loading to background task for faster startup
        string[] essentialPlugins = new[]
        {
            "bass_aac.dll",   // AAC
            "bass_mpc.dll",   // MPC
            "bassalac.dll",   // Apple Lossless
            "bassape.dll",    // Monkey's Audio
            "bassflac.dll",   // FLAC support
            "bassopus.dll",   // Opus
            "basswebm.dll",   // WebM/Opus
            "basswv.dll",     // WavPack
        };

        // Start plugin loading in background without blocking
        Task.Run(() =>
        {
            _logger.LogInformation("Loading plugins in background");
            LoadSpecificPluginsBackground(essentialPlugins, result);
        });
    }

    private void LoadAllPlugins(BassInitializationResult result)
    {
        // Load all available plugins
        string[] allPlugins = BassNativeLibraryManager.GetAvailableDlls()
            .Where(dll => dll != "bass.dll") // Don't try to load the main BASS library as a plugin
            .ToArray();

        // Start plugin loading in background without blocking
        Task.Run(() =>
        {
            _logger.LogInformation("Loading all plugins in background");
            LoadSpecificPluginsBackground(allPlugins, result);
        });
    }

    private void LoadSpecificPlugins(string[] pluginNames, BassInitializationResult result)
    {
        _logger.LogInformation($"Loading {pluginNames.Length} BASS plugins");

        foreach (string pluginName in pluginNames)
        {
            try
            {
                if (!BassNativeLibraryManager.IsDllAvailable(pluginName))
                {
                    _logger.LogWarning($"Plugin not available: {pluginName}");
                    result.FailedPlugins.Add($"{pluginName} (not available)");
                    continue;
                }

                string pluginPath = BassNativeLibraryManager.GetDllPath(pluginName);
                int handle = Bass.PluginLoad(pluginPath);

                if (handle != 0)
                {
                    result.LoadedPlugins.Add(pluginName);
                    _logger.LogInformation($"Loaded plugin: {pluginName}; Handle: {handle}");
                }
                else
                {
                    Errors error = Bass.LastError;
                    result.FailedPlugins.Add($"{pluginName} ({error})");
                    _logger.LogWarning($"Failed to load plugin: {pluginName}, Error: {error}");
                }
            }
            catch (Exception ex)
            {
                result.FailedPlugins.Add($"{pluginName} (exception: {ex.Message})");
                _logger.LogError(ex, $"Exception loading plugin: {pluginName}");
            }
        }

        _logger.LogInformation($"Plugin loading complete. Loaded: {result.LoadedPlugins.Count}, Failed: {result.FailedPlugins.Count}");
    }

    private void LoadSpecificPluginsBackground(string[] pluginNames, BassInitializationResult result)
    {
        foreach (string pluginName in pluginNames)
        {
            try
            {
                if (!BassNativeLibraryManager.IsDllAvailable(pluginName))
                {
                    _logger.LogWarning($"Plugin not available: {pluginName}");
                    result.FailedPlugins.Add($"{pluginName} (not available)");
                    continue;
                }

                string pluginPath = BassNativeLibraryManager.GetDllPath(pluginName);
                int handle = Bass.PluginLoad(pluginPath);

                if (handle != 0)
                {
                    result.LoadedPlugins.Add(pluginName);
                    _logger.LogDebug($"Loaded plugin in background: {pluginName}; Handle: {handle}");
                }
                else
                {
                    Errors error = Bass.LastError;
                    result.FailedPlugins.Add($"{pluginName} ({error})");
                    _logger.LogDebug($"Failed to load plugin in background: {pluginName}, Error: {error}");
                }
            }
            catch (Exception ex)
            {
                result.FailedPlugins.Add($"{pluginName} (exception: {ex.Message})");
                _logger.LogError(ex, $"Exception loading plugin in background: {pluginName}");
            }
        }

        _logger.LogInformation($"Background plugin loading complete. Loaded: {result.LoadedPlugins.Count}, Failed: {result.FailedPlugins.Count}");
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string? lpPathName);

    public void Dispose()
    {
        Shutdown();
        GC.SuppressFinalize(this);
    }
}


