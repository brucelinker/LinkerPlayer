using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.BassLibs;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Wasapi;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
// ReSharper disable InconsistentNaming

namespace LinkerPlayer.Audio;

public partial class AudioEngine : ObservableObject, ISpectrumPlayer, IDisposable
{
    private readonly IOutputDeviceManager _outputDeviceManager;
    private readonly ISettingsManager _settingsManager;
    private readonly ILogger<AudioEngine> _logger;
    [ObservableProperty] private bool _isBassInitialized;
    [ObservableProperty] private int _currentStream;
    [ObservableProperty] private string _pathToMusic = string.Empty;
    [ObservableProperty] private double _currentTrackLength;
    [ObservableProperty] private double _currentTrackPosition;
    [ObservableProperty] private float _musicVolume = 0.5f;
    [ObservableProperty] private bool _isPlaying;

    private OutputMode _currentMode;
    private Device _currentDevice;

    private bool _wasapiInitialized;
    private WasapiProcedure _wasapiProc;

    private readonly float[] _fftBuffer = new float[2048];
    private readonly System.Timers.Timer _positionTimer;

    private readonly List<EqualizerBandSettings> _equalizerBands =
    [
        new(32.0f, 0f, 1.0f),
        new(64.0f, 0f, 1.0f),
        new(125.0f, 0f, 1.0f),
        new(250.0f, 0f, 1.0f),
        new(500.0f, 0f, 1.0f),
        new(1000.0f, 0f, 1.0f),
        new(2000.0f, 0f, 1.0f),
        new(4000.0f, 0f, 1.0f),
        new(8000.0f, 0f, 1.0f),
        new(16000.0f, 0f, 1.0f)
    ];

    private bool _eqInitialized;
    private int[] _eqFxHandles = [];
    private int _endSyncHandle;
    private IntPtr _bassFxHandle = IntPtr.Zero; // No longer needed - BassAudioEngine handles plugin loading
    private IntPtr _bassMixHandle = IntPtr.Zero; // No longer needed - BassAudioEngine handles plugin loading

    private int _decodeStream = 0; // Holds the decode stream (file)
    private int _mixerStream = 0; // Holds the mixer stream (WASAPI output)

    // Audio device error tracking (works for both DirectSound and WASAPI)
    private Errors _lastAudioError = Errors.OK;
    private int _consecutiveAudioErrors = 0;
    private const int MAX_AUDIO_ERROR_COUNT = 10;
    private bool _audioDeviceLost = false;

    // Timer tick counter for periodic logging
    private int _handleFftCallCount = 0;

    public event Action? OnPlaybackStopped;
    public event Action<float[]>? OnFftCalculated;

    [ObservableProperty] private bool _eqEnabled = true;

    public bool IsEqualizerInitialized => _eqInitialized;

    public float[] FftUpdate { get; private set; }
    public double NoiseFloorDb { get; set; } = -60;
    public int ExpectedFftSize => 2048;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string? lpPathName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetLastError();

    public AudioEngine(IOutputDeviceManager outputDeviceManager, ISettingsManager settingsManager, ILogger<AudioEngine> logger)
    {
        _outputDeviceManager = outputDeviceManager;
        _settingsManager = settingsManager;
        _logger = logger;

        try
        {
            _currentMode = _settingsManager.Settings.SelectedOutputMode;
            _currentDevice = _settingsManager.Settings.SelectedOutputDevice ?? new Device("Default", OutputDeviceType.DirectSound, -1, true);

            // Initialize BASS Native Library Manager first
            BassNativeLibraryManager.Initialize(_logger);

            // Set DLL directory to the extracted BASS libraries (safe at startup)
            string bassLibPath = BassNativeLibraryManager.GetNativeLibraryPath();
            _logger.LogInformation($"Setting DLL directory to: {bassLibPath}");
            if (!SetDllDirectory(bassLibPath))
            {
                _logger.LogWarning("Failed to set DLL directory for BASS libraries");
            }

            // DON'T explicitly load bass_fx.dll or bassmix.dll here - let BassAudioEngine handle it
            // Those DLLs need to be loaded as BASS plugins, not just via LoadLibrary
            
            // Refresh device list (safe at startup - doesn't open devices)
            IEnumerable<Device> devices = _outputDeviceManager.RefreshOutputDeviceList();

            // Create WASAPI callback (safe at startup)
            _wasapiProc = new WasapiProcedure(WasapiProc);

            _positionTimer = new System.Timers.Timer(100);
            _positionTimer.Elapsed += (_, _) => HandleFftCalculated();
            _positionTimer.AutoReset = true;

            FftUpdate = new float[ExpectedFftSize];

            WeakReferenceMessenger.Default.Register<MainWindowClosingMessage>(this, (_, m) =>
            {
                OnMainWindowClosing(m.Value);
            });

            _logger.LogInformation("AudioEngine initialized successfully (audio device init deferred until Play)");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error in AudioEngine constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in AudioEngine constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
    }

    public void InitializeAudioDevice()
    {
        if (IsBassInitialized)
        {
            return;
        }

        try
        {
            // Directly initialize the audio device based on current mode
            // Do NOT call SetOutputMode - that's for configuration changes only
            bool success;

            if (_currentMode == OutputMode.DirectSound)
            {
                success = Bass.Init(-1, 44100, DeviceInitFlags.DirectSound);
                if (success)
                {
                    IsBassInitialized = true;
                    _sampleRate = 44100;
                    
                    _logger.LogInformation("Initialized DirectSound on first play");
                }
                else
                {
                    _logger.LogError($"Failed to initialize DirectSound: {Bass.LastError}");
                }
            }
            else
            {
                // For WASAPI modes, we need BASS for decoding
                // Get device info for mixer creation
                if (!BassWasapi.GetDeviceInfo(_currentDevice.Index, out var deviceInfo))
                {
                    _logger.LogError($"Failed to get device info: {Bass.LastError}");
                    return;
                }

                // Enable mixer low-pass filter for better resampling quality
                Bass.Configure((Configuration)0x10600, true);

                // Store for use elsewhere
                _sampleRate = deviceInfo.MixFrequency;

                // Initialize BASS for decoding only (no device)
                // Try device's native rate first, then fall back to common rates
                int[] ratesToTry = { deviceInfo.MixFrequency, 44100, 48000 };
                
                foreach (int rate in ratesToTry)
                {
                    success = Bass.Init(0, rate, DeviceInitFlags.Default);
                    if (success)
                    {
                        IsBassInitialized = true;
                        
                        _logger.LogInformation($"Initialized BASS for decoding at {rate} Hz on first play (WASAPI mode: {_currentMode})");
                        return;
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to initialize BASS at {rate} Hz: {Bass.LastError}, trying next rate");
                    }
                }
            
                // If all rates failed, log final error
                _logger.LogError($"Failed to initialize BASS for decoding with all sample rates: {Bass.LastError}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error initializing audio device: {ex.Message}");
        }
    }

    public IEnumerable<Device> DirectSoundDevices => _outputDeviceManager.GetDirectSoundDevices();
    public IEnumerable<Device> WasapiDevices => _outputDeviceManager.GetWasapiDevices();

    public OutputMode GetCurrentOutputMode() { return _currentMode; }
    public Device GetCurrentOutputDevice() { return _currentDevice; }

    private int _sampleRate;

    public void SetOutputMode(OutputMode selectedOutputMode, Device? device)
    {
        // This method is called from Settings dialog
        // It should update mode/device settings and only reinitialize if already playing
        
        if (device == null)
        {
            device = new Device("Default", OutputDeviceType.DirectSound, -1, true);
        }

        // Check if we need to reinitialize (only if currently initialized)
        bool needsReinit = IsBassInitialized;
        
        if (needsReinit)
        {
            // Stop current playback first
            Stop();

            // Free current resources
            FreeResources();
        }

        // Update mode and device
        _currentMode = selectedOutputMode;
        _currentDevice = device;

        // Save to settings
        _settingsManager.Settings.SelectedOutputMode = selectedOutputMode;
        _settingsManager.Settings.SelectedOutputDevice = device;
        _settingsManager.SaveSettings(nameof(_settingsManager.Settings.SelectedOutputMode));
        _settingsManager.SaveSettings(nameof(_settingsManager.Settings.SelectedOutputDevice));

        // If we were initialized, reinitialize with new settings
        if (needsReinit)
        {
            IsBassInitialized = false; // Reset flag so InitializeAudioDevice will run
            InitializeAudioDevice(); // Reinitialize with new mode/device
        }

        WeakReferenceMessenger.Default.Send(new OutputModeChangedMessage(_currentMode));
    }

    public void LoadAudioFile(string pathToMusic)
    {
        //_logger.LogDebug($"LoadAudioFile called with path: {pathToMusic}");

        try
        {
            // Free previous streams and clean up EQ
            CleanupEqualizer();
            if (_mixerStream != 0)
            {
                Bass.StreamFree(_mixerStream);
                _mixerStream = 0;
            }
            if (_decodeStream != 0)
            {
                Bass.StreamFree(_decodeStream);
                _decodeStream = 0;
            }
            if (CurrentStream != 0)
            {
                Bass.StreamFree(CurrentStream);
                CurrentStream = 0;
            }

            //int apePlugin = Bass.PluginLoad("bassape.dll");
            //_logger.LogInformation($"APE plugin handle: {apePlugin}");

            //_logger.LogInformation($"Trying to play: {pathToMusic}");
            //_logger.LogInformation($"File exists: {File.Exists(pathToMusic)}");
            //_logger.LogInformation($"Bass version: {Bass.Version}");
            //_logger.LogInformation($"Loaded plugins: {string.Join(", ", Bass.PluginGetInfo(apePlugin))}");

            if (_currentMode == OutputMode.DirectSound)
            {
                //CurrentStream = Bass.CreateStream(pathToMusic, 0, 0, Flags: BassFlags.Default | BassFlags.Prescan);
                CurrentStream = Bass.CreateStream(pathToMusic, 0, 0, Flags: BassFlags.Default);
                _decodeStream = 0;
                _mixerStream = 0;
            }
            else
            {
                // WASAPI: always create decode stream and mixer

                // Get device info for mixer creation
                if (!BassWasapi.GetDeviceInfo(_currentDevice.Index, out var deviceInfo))
                {
                    _logger.LogError($"Failed to get device info for mixer creation: {Bass.LastError}");
                    return;
                }

                int deviceFreq = deviceInfo.MixFrequency;
                int deviceChans = deviceInfo.MixChannels;

                // Try float first
                //_decodeStream = Bass.CreateStream(pathToMusic, 0, 0, Flags: BassFlags.Decode | BassFlags.Float);
                _decodeStream = Bass.CreateStream(pathToMusic, 0, 0, Flags: BassFlags.Decode);
                _logger.LogDebug($"WASAPI: _decodeStream handle: {_decodeStream}, Bass.LastError: {Bass.LastError}");
                if (_decodeStream == 0)
                {
                    _logger.LogError($"Failed to load file: {Bass.LastError}. File: {pathToMusic}");
                    return;
                }

                // Get decode stream info for channel count
                Bass.ChannelGetInfo(_decodeStream, out var decodeInfo);
                int decodeChans = decodeInfo.Channels;
                _logger.LogDebug($"Decode Stream: Freq: {decodeInfo.Frequency}, Channels: {decodeChans}, Flags: {decodeInfo.Flags}");

                // Log WASAPI device info
                //_logger.LogInformation($"WASAPI Device: {deviceInfo.Name}, MixFrequency: {deviceInfo.MixFrequency}, MixChannels: {deviceInfo.MixChannels}");

                // Try float mixer first
                _mixerStream = ManagedBass.Mix.BassMix.CreateMixerStream(deviceFreq, deviceChans, BassFlags.Float | BassFlags.Decode);
                bool mixerIsFloat = _mixerStream != 0;
                if (_mixerStream != 0)
                {
                    Bass.ChannelGetInfo(_mixerStream, out var mixerInfo);

                    //_logger.LogInformation($"File Rate: {decodeInfo.Frequency} Hz, Channels: {decodeInfo.Channels}");
                    //_logger.LogInformation($"Device Rate: {deviceFreq} Hz, Channels: {deviceChans}");
                    //_logger.LogInformation($"Mixer Rate: {mixerInfo.Frequency} Hz");
                    //_logger.LogDebug($"Mixer Stream: Freq: {mixerInfo.Frequency}, Channels: {mixerInfo.Channels}, Flags: {mixerInfo.Flags}");
                }
                else
                {
                    // Fallback to 16-bit PCM mixer
                    _logger.LogWarning("Falling back to 16-bit PCM mixer");
                    _mixerStream = ManagedBass.Mix.BassMix.CreateMixerStream(deviceFreq, deviceChans, BassFlags.Decode);
                    mixerIsFloat = false;
                    if (_mixerStream != 0)
                    {
                        Bass.ChannelGetInfo(_mixerStream, out var mixerInfo);

                        //_logger.LogInformation($"File Rate: {decodeInfo.Frequency} Hz, Channels: {decodeInfo.Channels}");
                        //_logger.LogInformation($"Device Rate: {deviceFreq} Hz, Channels: {deviceChans}");
                        //_logger.LogInformation($"Mixer Rate: {mixerInfo.Frequency} Hz");
                        //_logger.LogDebug($"PCM Mixer Stream: Freq: {mixerInfo.Frequency}, Channels: {mixerInfo.Channels}, Flags: {mixerInfo.Flags}");
                    }
                    else
                    {
                        _logger.LogError($"Failed to create mixer: {Bass.LastError}");
                        Bass.StreamFree(_decodeStream);
                        _decodeStream = 0;
                        return;
                    }
                }

                // Use MixerChanDownMix if channel counts do not match
                BassFlags addFlags = BassFlags.Default;
                if (decodeChans != deviceChans) addFlags |= BassFlags.MixerChanDownMix;
                if (decodeInfo.Frequency != deviceFreq) addFlags |= BassFlags.MixerChanNoRampin; // Critical for rate mismatches

                //_logger.LogInformation($"Adding channel with flags: {addFlags}, File Rate: {decodeInfo.Frequency} Hz, Device Rate: {deviceFreq} Hz");

                if (!ManagedBass.Mix.BassMix.MixerAddChannel(_mixerStream, _decodeStream, addFlags))
                {
                    _logger.LogError($"Failed to add decode stream to mixer: {Bass.LastError}");
                    Bass.StreamFree(_mixerStream);
                    _mixerStream = 0;
                    Bass.StreamFree(_decodeStream);
                    _decodeStream = 0;
                    return;
                }

                CurrentStream = _mixerStream;
                // Store if mixer is float for WASAPI init
                _mixerIsFloat = mixerIsFloat;
            }

            if (CurrentStream == 0)
            {
                _logger.LogError($"Failed to create stream for playback - {Path.GetFileName(pathToMusic)}: {Bass.LastError}");
                return;
            }

            long lengthBytes = Bass.ChannelGetLength(CurrentStream);
            if (lengthBytes < 0)
            {
                _logger.LogError($"Failed to get track length: {Bass.LastError}");
                Bass.StreamFree(CurrentStream);
                CurrentStream = 0;
                return;
            }

            CurrentTrackLength = Bass.ChannelBytes2Seconds(CurrentStream, lengthBytes);
            CurrentTrackPosition = 0;

            // Initialize equalizer for the new stream
            if (EqEnabled)
            {
                InitializeEqualizer();
            }

            //_logger.LogInformation($"Successfully loaded audio file: {Path.GetFileName(pathToMusic)}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading file: {ex.Message}");
            // Ensure we clean up if there's an exception
            if (_mixerStream != 0)
            {
                Bass.StreamFree(_mixerStream);
                _mixerStream = 0;
            }
            if (_decodeStream != 0)
            {
                Bass.StreamFree(_decodeStream);
                _decodeStream = 0;
            }
            if (CurrentStream != 0)
            {
                try { Bass.StreamFree(CurrentStream); } catch { }
                CurrentStream = 0;
            }
        }
    }

    public void Play()
    {
        _logger.LogInformation("Play() called - no path parameter");
        if (string.IsNullOrEmpty(PathToMusic))
        {
            _logger.LogError("Cannot play: PathToMusic is null or empty");
            return;
        }
        Play(PathToMusic);
    }

    public void Play(string pathToMusic, double position = 0)
    {
        _logger.LogInformation("Play() called with path: {Path}, position: {Position}", pathToMusic, position);
        
        if (string.IsNullOrEmpty(pathToMusic))
        {
            _logger.LogError("Play called with null or empty pathToMusic");
            return;
        }
        
        // Reset audio device lost flag when user manually tries to play
        if (_audioDeviceLost)
        {
            _logger.LogInformation("Resetting audio device lost flag - user is attempting to play again");
            _audioDeviceLost = false;
            _consecutiveAudioErrors = 0;
            _lastAudioError = Errors.OK;
        }
        
        // Initialize BASS on first play if not already initialized
        if (!IsBassInitialized)
        {
            _logger.LogInformation("First play - initializing audio device (Bass.Init or BassWasapi.Init)");
            InitializeAudioDevice();
            
            // If initialization failed, don't continue
            if (!IsBassInitialized)
            {
                _logger.LogError("Failed to initialize audio device - cannot play");
                return;
            }
        }
        
        PlaybackState playbackState = Bass.ChannelIsActive(CurrentStream);

        if (!string.IsNullOrEmpty(pathToMusic) && playbackState == PlaybackState.Paused)
        {
            _logger.LogInformation("Resuming paused track");
            ResumePlay();
            return;
        }

        _logger.LogInformation("Stopping current playback before loading new track");
        Stop();

        _logger.LogInformation("Loading audio file: {FileName}", Path.GetFileName(pathToMusic));
        LoadAudioFile(pathToMusic);

        if (CurrentStream != 0)
        {
            _logger.LogInformation("CurrentStream is valid ({StreamHandle}), starting playback", CurrentStream);
            
            // Set end-of-track sync
            _endSyncHandle = Bass.ChannelSetSync(CurrentStream, SyncFlags.End, 0, EndTrackSyncProc);
            if (_endSyncHandle == 0)
            {
                _logger.LogWarning($"Failed to set end-of-track sync: {Bass.LastError}");
            }

            // Set position if specified
            if (position > 0)
            {
                long bytePosition = Bass.ChannelSeconds2Bytes(CurrentStream, position);
                if (!Bass.ChannelSetPosition(CurrentStream, bytePosition))
                {
                    _logger.LogWarning($"Failed to set initial position: {Bass.LastError}");
                }
            }

            // Set volume
            Bass.ChannelSetAttribute(CurrentStream, ChannelAttribute.Volume, MusicVolume);

            // Start playback
            bool playbackStarted = false;
            
            if (_currentMode == OutputMode.DirectSound)
            {
                _logger.LogInformation("Starting DirectSound playback");
                bool playSuccess = Bass.ChannelPlay(CurrentStream);
                
                if (!playSuccess && Bass.LastError == Errors.Busy)
                {
                    _logger.LogError("Failed to start DirectSound playback - device is busy");
                    _audioDeviceLost = true;
                    
                    Window? owner = Application.Current.MainWindow;
                    if (owner != null)
                    {
                        MessageBox.Show(
                            owner,
                            "Audio device is being used exclusively by another application. Playback cannot start.\n\n" +
                            "The other application has taken exclusive control of the audio device.\n" +
                            "Please close the other application or switch to a different audio device in Settings.",
                            "Audio Device Busy",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show(
                            "Audio device is being used exclusively by another application. Playback cannot start.\n\n" +
                            "The other application has taken exclusive control of the audio device.\n" +
                            "Please close the other application or switch to a different audio device in Settings.",
                            "Audio Device Busy",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    return; // Exit early - IsPlaying stays false
                }
                
                playbackStarted = playSuccess;
            }
            else
            {
                _logger.LogInformation("Starting WASAPI playback");
                
                // WASAPI playback - only initialize if not already initialized
                if (!_wasapiInitialized)
                {
                    if (!InitializeWasapiForPlayback())
                    {
                        _logger.LogError("Failed to initialize WASAPI for playback");
                        
                        // Check if it failed due to device being busy
                        if (Bass.LastError == Errors.Busy || Bass.LastError == Errors.Already)
                        {
                            _audioDeviceLost = true; // Set flag so user can try again later
                            
                            Window? owner = Application.Current.MainWindow;
                            if (owner != null)
                            {
                                MessageBox.Show(
                                    owner,
                                    "Audio device is being used exclusively by another application. Playback cannot start.\n\n" +
                                    "The other application has taken exclusive control of the audio device.\n" +
                                    "Please close the other application or switch to a different audio device in Settings.",
                                    "Audio Device Busy",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                            }
                            else
                            {
                                MessageBox.Show(
                                    "Audio device is being used exclusively by another application. Playback cannot start.\n\n" +
                                    "The other application has taken exclusive control of the audio device.\n" +
                                    "Please close the other application or switch to a different audio device in Settings.",
                                    "Audio Device Busy",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                            }
                        }
                        else
                        {
                            // Some other error - still set flag so they can try again
                            _audioDeviceLost = true;
                        }
                        return; // Exit early - IsPlaying stays false
                    }
                }

                if (_mixerStream != 0)
                {
                    _logger.LogInformation("Calling Bass.ChannelPlay on mixer stream (handle {MixerStream})", _mixerStream);
                    Bass.ChannelPlay(_mixerStream, false);
                }

                bool wasapiStarted = BassWasapi.Start();
                if (!wasapiStarted)
                {
                    _logger.LogError($"BassWasapi.Start failed: {Bass.LastError}");
                    
                    // Check if it failed due to device being busy
                    if (Bass.LastError == Errors.Busy)
                    {
                        _audioDeviceLost = true; // Set flag so user can try again later
                        
                        Window? owner = Application.Current.MainWindow;
                        if (owner != null)
                        {
                            MessageBox.Show(
                                owner,
                                "Audio device is being used exclusively by another application. Playback cannot start.\n\n" +
                                "The other application has taken exclusive control of the audio device.\n" +
                                "Please close the other application or switch to a different audio device in Settings.",
                                "Audio Device Busy",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                        else
                        {
                            MessageBox.Show(
                                "Audio device is being used exclusively by another application. Playback cannot start.\n\n" +
                                "The other application has taken exclusive control of the audio device.\n" +
                                "Please close the other application or switch to a different audio device in Settings.",
                                "Audio Device Busy",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        // Some other error - still set flag so they can try again
                        _audioDeviceLost = true;
                    }
                    return; // Exit early - IsPlaying stays false
                }
                else
                {
                    _logger.LogInformation("BassWasapi.Start succeeded");
                    playbackStarted = true;
                }
            }

            // ONLY set IsPlaying if playback actually started successfully
            if (playbackStarted)
            {
                IsPlaying = true;
                PathToMusic = pathToMusic;
                _logger.LogInformation("Playback started successfully. IsPlaying = {IsPlaying}", IsPlaying);
            }
            else
            {
                _logger.LogWarning("Playback did not start - IsPlaying remains false");
            }
        }
        else
        {
            _logger.LogError($"Failed to create stream, cannot play {Path.GetFileName(pathToMusic)}");
        }
    }

    public void Stop()
    {
        if (CurrentStream != 0)
        {
            if (_endSyncHandle != 0)
            {
                Bass.ChannelRemoveSync(CurrentStream, _endSyncHandle);
                _endSyncHandle = 0;
            }

            Bass.ChannelStop(CurrentStream);
            Bass.ChannelSetPosition(CurrentStream, 0);

            IsPlaying = false;
            CurrentTrackPosition = 0;

            // Stop WASAPI but DON'T free it (keep it initialized for next track)
            if (_wasapiInitialized)
            {
                BassWasapi.Stop();
            }

            FreeResources();
        }
        else
        {
            // Even if no stream, ensure IsPlaying is false
            IsPlaying = false;
        }

        OnPlaybackStopped?.Invoke();
    }

    public void Pause()
    {
        if (CurrentStream != 0 && IsPlaying)
        {
            if (_currentMode == OutputMode.DirectSound)
            {
                if (!Bass.ChannelPause(CurrentStream))
                {
                    _logger.LogError($"Failed to pause stream: {Bass.LastError}");
                    return;
                }
            }
            else
            {
                // WASAPI pause
                BassWasapi.Stop();
                if (_mixerStream != 0)
                {
                    Bass.ChannelPause(_mixerStream);
                }
            }
            
            IsPlaying = false;
        }
    }

    public void ResumePlay()
    {
        if (CurrentStream != 0 && !IsPlaying)
        {
            if (_currentMode == OutputMode.DirectSound)
            {
                if (!Bass.ChannelPlay(CurrentStream))
                {
                    _logger.LogError($"Failed to resume stream: {Bass.LastError}");
                    return;
                }
            }
            else
            {
                // WASAPI resume
                if (_mixerStream != 0)
                {
                    Bass.ChannelPlay(_mixerStream);
                }
                
                if (!BassWasapi.Start())
                {
                    _logger.LogError($"Failed to resume WASAPI: {Bass.LastError}");
                    return;
                }
            }
            
            IsPlaying = true;

            _endSyncHandle = Bass.ChannelSetSync(CurrentStream, SyncFlags.End, 0, EndTrackSyncProc);
            if (_endSyncHandle == 0)
            {
                _logger.LogWarning($"Failed to set end-of-track sync: {Bass.LastError}");
            }
        }
    }

    public void StopAndPlayFromPosition(double position)
    {
        Stop();
        if (!string.IsNullOrEmpty(PathToMusic))
        {
            Play(PathToMusic, position);
        }
    }

    public void SeekAudioFile(double position)
    {
        if (CurrentStream == 0)
        {
            _logger.LogError("Cannot seek: Current stream is invalid");
            return;
        }

        if (position < 0 || position > CurrentTrackLength)
        {
            _logger.LogError($"Invalid seek position {position}: must be between 0 and {CurrentTrackLength} seconds");
            return;
        }

        if (_currentMode == OutputMode.DirectSound)
        {
            PlaybackState playbackState = Bass.ChannelIsActive(CurrentStream);
            bool wasPlaying = playbackState == PlaybackState.Playing;
            if (wasPlaying)
            {
                Bass.ChannelPause(CurrentStream);
            }

            long bytePosition = Bass.ChannelSeconds2Bytes(CurrentStream, position);
            if (bytePosition < 0)
            {
                _logger.LogError($"Failed to convert position {position} to bytes: {Bass.LastError}");
                return;
            }

            if (!Bass.ChannelSetPosition(CurrentStream, bytePosition))
            {
                _logger.LogError($"Failed to seek to position {position}: {Bass.LastError}");
                return;
            }

            double actualPosition = Bass.ChannelBytes2Seconds(CurrentStream, Bass.ChannelGetPosition(CurrentStream));
            if (!double.IsNaN(actualPosition) && actualPosition >= 0)
            {
                CurrentTrackPosition = actualPosition;
            }

            if (wasPlaying)
            {
                Bass.ChannelPlay(CurrentStream);
            }
        }
        else // WASAPI
        {
            if (_decodeStream == 0)
            {
                _logger.LogError("Cannot seek: decode stream is invalid");
                return;
            }

            long bytePosition = Bass.ChannelSeconds2Bytes(_decodeStream, position);
            if (bytePosition < 0)
            {
                _logger.LogError($"Failed to convert position {position} to bytes: {Bass.LastError}");
                return;
            }

            if (!Bass.ChannelSetPosition(_decodeStream, bytePosition))
            {
                _logger.LogError($"Failed to seek to position {position}: {Bass.LastError}");
                return;
            }

            double actualPosition = Bass.ChannelBytes2Seconds(_decodeStream, Bass.ChannelGetPosition(_decodeStream));
            if (!double.IsNaN(actualPosition) && actualPosition >= 0)
            {
                CurrentTrackPosition = actualPosition;
            }
        }
    }

    // Add a private field to track mixer format
    private bool _mixerIsFloat = true;

    private int WasapiProc(IntPtr buffer, int length, IntPtr user)
    {
        if (_mixerStream == 0) 
        {
            unsafe
            {
                byte* ptr = (byte*)buffer.ToPointer();
                for (int i = 0; i < length; i++)
                {
                    ptr[i] = 0;
                }
            }
            return length;
        }

        int bytesRead;
        if (_mixerIsFloat)
        {
            // Request float data
            bytesRead = Bass.ChannelGetData(_mixerStream, buffer, length | (int)DataFlags.Float);
        }
        else
        {
            // Request 16-bit PCM data
            bytesRead = Bass.ChannelGetData(_mixerStream, buffer, length);
        }

        if (bytesRead > 0)
        {
            if (bytesRead < length)
            {
                unsafe
                {
                    byte* ptr = (byte*)buffer.ToPointer();
                    for (int i = bytesRead; i < length; i++)
                    {
                        ptr[i] = 0;
                    }
                }
            }
            return length;
        }
        else
        {
            unsafe
            {
                byte* ptr = (byte*)buffer.ToPointer();
                for (int i = 0; i < length; i++)
                {
                    ptr[i] = 0;
                }
            }
            return length;
        }
    }

    private void EndTrackSyncProc(int handle, int channel, int data, IntPtr user)
    {
        //_logger.LogInformation("Track ended");
        Stop();
    }

    private bool InitializeWasapiForPlayback()
    {
        try
        {
            // Only reset error tracking if we're recovering from a lost device
            // (not on every song change)
            if (_audioDeviceLost)
            {
                _logger.LogInformation("Recovering from audio device loss - resetting error tracking");
                _audioDeviceLost = false;
                _consecutiveAudioErrors = 0;
                _lastAudioError = Errors.OK;
            }

            // If already initialized AND running, don't re-initialize
            if (_wasapiInitialized && BassWasapi.IsStarted)
            {
                _logger.LogInformation("WASAPI already initialized and running - skipping re-init");
                return true;
            }

            // ALWAYS free WASAPI before initialization - BassWasapi.Free() is safe to call even if not init
            // This prevents "Already" errors from stale WASAPI state
            _logger.LogInformation("Freeing any existing WASAPI state before re-init");
            BassWasapi.Stop();
            BassWasapi.Free();
            _wasapiInitialized = false;

            // Get device info
            if (!BassWasapi.GetDeviceInfo(_currentDevice.Index, out var deviceInfo))
            {
                _logger.LogError($"Failed to get WASAPI device info for device {_currentDevice.Index}: {Bass.LastError}");
                return false;
            }

            bool exclusive = (_currentMode == OutputMode.WasapiExclusive);
            WasapiInitFlags baseFlags = exclusive ? WasapiInitFlags.Exclusive : WasapiInitFlags.Shared;

            // Prefer buffered mode so GetData/GetLevel work reliably outside the callback
            WasapiInitFlags bufferedFlags = baseFlags | WasapiInitFlags.Buffer;
            const WasapiInitFlags FLOAT_FLAG = (WasapiInitFlags)0x100; // enable float when supported

            float bufferLength = 0.10f; // ~100ms allows UI FFT/VU without starving the callback
            float period = 0f; // default

            bool success = false;

            // Try float + buffered
            if (_mixerIsFloat)
            {
                _logger.LogInformation($"Initializing WASAPI (buffered): Device={deviceInfo.Name}, Freq={deviceInfo.MixFrequency}, Channels={deviceInfo.MixChannels}, Mode={_currentMode}, Float=TRUE");
                success = BassWasapi.Init(_currentDevice.Index, deviceInfo.MixFrequency, deviceInfo.MixChannels, bufferedFlags | FLOAT_FLAG, bufferLength, period, _wasapiProc, IntPtr.Zero);
            }

            // Fallbacks in order of preference
            if (!success && _mixerIsFloat)
            {
                _logger.LogWarning($"WASAPI float buffered init failed: {Bass.LastError} - trying float without buffer");
                success = BassWasapi.Init(_currentDevice.Index, deviceInfo.MixFrequency, deviceInfo.MixChannels, baseFlags | FLOAT_FLAG, 0, 0, _wasapiProc, IntPtr.Zero);
            }

            if (!success)
            {
                _logger.LogInformation("Trying PCM buffered WASAPI init");
                success = BassWasapi.Init(_currentDevice.Index, deviceInfo.MixFrequency, deviceInfo.MixChannels, bufferedFlags, bufferLength, period, _wasapiProc, IntPtr.Zero);
            }

            if (!success)
            {
                _logger.LogWarning($"WASAPI PCM buffered init failed: {Bass.LastError} - trying PCM without buffer");
                success = BassWasapi.Init(_currentDevice.Index, deviceInfo.MixFrequency, deviceInfo.MixChannels, baseFlags, 0, 0, _wasapiProc, IntPtr.Zero);
            }

            if (success)
            {
                _wasapiInitialized = true;
                _logger.LogInformation($"WASAPI initialized successfully. Format={(_mixerIsFloat ? "float" : "16-bit PCM")}");
                return true;
            }
            else
            {
                _logger.LogError($"Failed to initialize WASAPI: {Bass.LastError}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during WASAPI initialization");
            return false;
        }
    }

    partial void OnMusicVolumeChanged(float value)
    {
        if (_currentMode == OutputMode.DirectSound)
        {
            if (CurrentStream != 0)
            {
                Bass.ChannelSetAttribute(CurrentStream, ChannelAttribute.Volume, value);
            }
        }
        else if (_currentMode == OutputMode.WasapiShared || _currentMode == OutputMode.WasapiExclusive)
        {
            // Set WASAPI session volume (0.0 = mute, 1.0 = max)
            BassWasapi.SetVolume(WasapiVolumeTypes.Session, value);
        }
    }

    partial void OnIsPlayingChanged(bool value)
    {
        if (value)
            _positionTimer.Start();
        else
            _positionTimer.Stop();
    }

    private void OnMainWindowClosing(bool value)
    {
        Stop();
    }

    #region Equalizer
    public bool InitializeEqualizer()
    {
        CleanupEqualizer();

        if (CurrentStream == 0)
        {
            _logger.LogWarning("Cannot initialize equalizer: No stream loaded");
            return false;
        }

        // bass_fx.dll is loaded as a plugin by BassAudioEngine, no need to check _bassFxHandle

        _eqFxHandles = new int[_equalizerBands.Count];
        int successCount = 0;

        for (int i = 0; i < _equalizerBands.Count; i++)
        {
            float freq = _equalizerBands[i].Frequency;
            int fxHandle = Bass.ChannelSetFX(CurrentStream, EffectType.PeakEQ, 0);

            if (fxHandle == 0)
            {
                _logger.LogError($"Failed to set FX for {freq}Hz: {Bass.LastError}");
                continue;
            }

            PeakEQParameters eqParams = new()
            {
                fCenter = freq,
                fGain = _equalizerBands[i].Gain,
                fBandwidth = _equalizerBands[i].Bandwidth,
                lChannel = FXChannelFlags.All
            };

            if (!Bass.FXSetParameters(fxHandle, eqParams))
            {
                _logger.LogError($"Failed to set EQ params for {freq}Hz: {Bass.LastError}");
                Bass.ChannelRemoveFX(CurrentStream, fxHandle);
                _eqFxHandles[i] = 0;
            }
            else
            {
                _eqFxHandles[i] = fxHandle;
                successCount++;
            }
        }

        _eqInitialized = successCount > 0;
        //if (_eqInitialized)
        //{
   //    _logger.LogInformation($"Equalizer initialized with {successCount}/{_equalizerBands.Count} bands");
 //}

        return _eqInitialized;
    }

    public List<EqualizerBandSettings> GetBandsList()
    {
        // Return a COPY of the bands, not the original reference
        return _equalizerBands.Select(band => new EqualizerBandSettings(
            band.Frequency,
            band.Gain,
            band.Bandwidth
        )).ToList();
    }

    public void SetBandsList(List<EqualizerBandSettings> bands)
    {
        _equalizerBands.Clear();

        // Create NEW band objects from the input, don't store references
        foreach (EqualizerBandSettings band in bands)
        {
            _equalizerBands.Add(new EqualizerBandSettings(
                band.Frequency,
                band.Gain,
                band.Bandwidth
            ));
        }

        // Re-initialize EQ if it's already set up
        if (EqEnabled && CurrentStream != 0)
        {
            InitializeEqualizer();
        }
    }

    public float GetBandGain(int index)
    {
        if (index >= 0 && index < _equalizerBands.Count)
        {
            return _equalizerBands[index].Gain;
        }
        return 0f;
    }

    public void SetBandGainByIndex(int index, float gain)
    {
        if (index >= 0 && index < _equalizerBands.Count)
        {
            SetBandGain(_equalizerBands[index].Frequency, gain);
        }
    }

    public void SetBandGain(float frequency, float gain)
    {
        // Store the gain setting regardless of EQ state
        int bandIndex = _equalizerBands.FindIndex(b => Math.Abs(b.Frequency - frequency) < 0.1f);
        if (bandIndex != -1)
        {
            _equalizerBands[bandIndex].Gain = Math.Clamp(gain, -12f, 12f);
        }

      // Only apply to actual EQ if it's enabled and initialized
        if (!EqEnabled || !_eqInitialized || _eqFxHandles.Length == 0 || CurrentStream == 0)
        {
            return;
    }

        int bandIdx = _equalizerBands.FindIndex(b => Math.Abs(b.Frequency - frequency) < 0.1f);
        if (bandIdx == -1 || bandIdx >= _eqFxHandles.Length || _eqFxHandles[bandIdx] == 0)
     {
     return;
        }

        float clampedGain = Math.Clamp(gain, -12f, 12f);

        PeakEQParameters eqParams = new()
    {
            fCenter = frequency,
            fBandwidth = _equalizerBands[bandIdx].Bandwidth,
         fGain = clampedGain,
   lChannel = FXChannelFlags.All
        };

        if (Bass.FXSetParameters(_eqFxHandles[bandIdx], eqParams))
        {
  _equalizerBands[bandIdx].Gain = clampedGain;
    }
        else
{
    _logger.LogError($"Failed to update EQ gain for {frequency} Hz: {Bass.LastError}");
        }
    }

    private void CleanupEqualizer()
    {
        if (_eqFxHandles.Length > 0 && CurrentStream != 0)
        {
            foreach (int fxHandle in _eqFxHandles)
            {
                if (fxHandle != 0)
                    Bass.ChannelRemoveFX(CurrentStream, fxHandle);
            }
        }
        _eqFxHandles = [];
        _eqInitialized = false;
    }

    #endregion

    #region SpectrumAnalyzer

    private bool CheckAudioDeviceLost()
    {
        if (_audioDeviceLost)
        {
            return true; // Already handled
        }

        Errors currentError = Bass.LastError;
        
        // BASS_ERROR_BUSY (46) means device is being used exclusively by another application
        // This happens when:
        // 1. You're in DirectSound/WASAPI Shared and another app takes WASAPI Exclusive
        // 2. You're in WASAPI Shared and another app takes WASAPI Exclusive
        // 3. Device is otherwise unavailable
        if (currentError == Errors.Busy)
        {
            _audioDeviceLost = true;
            _logger.LogError("Audio device is busy (BASS_ERROR_BUSY). Another application has taken exclusive control of the audio device.");

            // Marshal to UI thread to stop playback and show message
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // Stop playback (this will also stop the timer via OnIsPlayingChanged)
                    Stop();

                    // Show message box with owner window so it appears on the correct monitor
                    Window? owner = Application.Current.MainWindow;
                    if (owner != null)
                    {
                        MessageBox.Show(
                            owner,
                            "Audio device is being used exclusively by another application. Playback has been stopped.\n\n" +
                            "The other application has taken exclusive control of the audio device.\n" +
                            "Please close the other application or switch to a different audio device in Settings.",
                            "Audio Device Busy",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show(
                            "Audio device is being used exclusively by another application. Playback has been stopped.\n\n" +
                            "The other application has taken exclusive control of the audio device.\n" +
                            "Please close the other application or switch to a different audio device in Settings.",
                            "Audio Device Busy",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error showing audio device busy message: {Message}", ex.Message);
                }
            });

            return true;
        }
        
        // Check for other device errors (fallback for other error types)
        if (currentError != Errors.OK && currentError != Errors.Unknown && currentError != Errors.Ended)
        {
            if (currentError == _lastAudioError)
            {
                _consecutiveAudioErrors++;
            }
            else
            {
                _lastAudioError = currentError;
                _consecutiveAudioErrors = 1;
            }

            // If we've had too many consecutive errors, the device is likely lost
            if (_consecutiveAudioErrors >= MAX_AUDIO_ERROR_COUNT)
            {
                _audioDeviceLost = true;
                _logger.LogError("Audio device lost after {Count} consecutive errors. Last error: {Error}", 
                    _consecutiveAudioErrors, currentError);

                // Marshal to UI thread to stop playback and show message
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        // Stop playback (this will also stop the timer via OnIsPlayingChanged)
                        Stop();

                        // Show message box with owner window so it appears on the correct monitor
                        Window? owner = Application.Current.MainWindow;
                        if (owner != null)
                        {
                            MessageBox.Show(
                                owner,
                                $"Audio device error detected. Playback has been stopped.\n\nError: {currentError}",
                                "Audio Device Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                        else
                        {
                            MessageBox.Show(
                                $"Audio device error detected. Playback has been stopped.\n\nError: {currentError}",
                                "Audio Device Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error showing audio device error message: {Message}", ex.Message);
                    }
                });

                return true;
            }
        }
        else
        {
            // Reset error tracking when successful
            _consecutiveAudioErrors = 0;
            _lastAudioError = Errors.OK;
        }

        return false;
    }

    public double GetDecibelLevel()
    {
        int level;
        if (_currentMode == OutputMode.DirectSound)
        {
            level = Bass.ChannelGetLevel(CurrentStream);
            
            // Check for BUSY error
            if (level == -1 && Bass.LastError == Errors.Busy)
            {
                CheckAudioDeviceLost();
                return double.NaN;
            }
        }
        else
        {
            if (_audioDeviceLost)
            {
                return double.NaN;
            }
            
            level = BassWasapi.GetLevel();
            
            // Check for BUSY error
            if (level == -1 && Bass.LastError == Errors.Busy)
            {
                CheckAudioDeviceLost();
                return double.NaN;
            }
            
            if (CheckAudioDeviceLost())
            {
                return double.NaN;
            }
        }

        if (level == -1)
        {
            return double.NaN;
        }

        int left = level & 0xFFFF;
        int right = (level >> 16) & 0xFFFF;

        double leftDb = 20 * Math.Log10(left / 32768.0);
        double rightDb = 20 * Math.Log10(right / 32768.0);
        double avgDb = (leftDb + rightDb) / 2.0;
        return avgDb;
    }

    public (double LeftDb, double RightDb) GetStereoDecibelLevels()
    {
        int level;
        if (_currentMode == OutputMode.DirectSound)
        {
            level = Bass.ChannelGetLevel(CurrentStream);
            
            // Check for BUSY error
            if (level == -1 && Bass.LastError == Errors.Busy)
            {
                CheckAudioDeviceLost();
                return (double.NaN, double.NaN);
            }
        }
        else
        {
            if (_audioDeviceLost)
            {
                return (double.NaN, double.NaN);
            }
            
            level = BassWasapi.GetLevel();
            
            // Check for BUSY error
            if (level == -1 && Bass.LastError == Errors.Busy)
            {
                CheckAudioDeviceLost();
                return (double.NaN, double.NaN);
            }
            
            if (CheckAudioDeviceLost())
            {
                return (double.NaN, double.NaN);
            }
        }

        if (level == -1)
        {
            return (double.NaN, double.NaN);
        }

        int left = level & 0xFFFF;
        int right = (level >> 16) & 0xFFFF;

        double leftDb = left > 0 ? 20 * Math.Log10(left / 32768.0) : -120.0;
        double rightDb = right > 0 ? 20 * Math.Log10(right / 32768.0) : -120.0;

        return (leftDb, rightDb);
    }

    public bool GetFftData(float[] fftDataBuffer)
    {
        if (fftDataBuffer.Length != ExpectedFftSize)
        {
            return false; // Early exit if buffer size wrong
        }

        if (CurrentStream == 0)
        {
            Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
            return false;
        }

        int bytesRead;
        if (_currentMode == OutputMode.DirectSound)
        {
            bytesRead = Bass.ChannelGetData(CurrentStream, fftDataBuffer, (int)DataFlags.FFT2048);
            
            // Check for BUSY error immediately after GetData
            if (bytesRead <= 0 && Bass.LastError == Errors.Busy)
            {
                CheckAudioDeviceLost();
                Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
                return false;
            }
        }
        else
        {
            if (_audioDeviceLost)
            {
                Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
                return false;
            }
            
            // IMPORTANT: In WASAPI use device GetData to avoid starving the mixer decode stream
            bytesRead = BassWasapi.GetData(fftDataBuffer, (int)DataFlags.FFT2048);
            
            // Check for BUSY error immediately after GetData
            if (bytesRead <= 0 && Bass.LastError == Errors.Busy)
            {
                CheckAudioDeviceLost();
                Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
                return false;
            }
            
            if (CheckAudioDeviceLost())
            {
                Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
                return false;
            }
        }

        if (bytesRead <= 0)
        {
            Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
            return false;
        }

        return true;
    }

    public int GetFftFrequencyIndex(int frequency)
    {
        const int fftSize = 2048;
        // Ensure we have a sane sample rate
        int rate = _sampleRate > 0 ? _sampleRate : 44100;
        float binWidth = rate / (float)fftSize;  // Use cached rate
        int index = (int)(frequency / binWidth);
        return Math.Clamp(index, 0, fftSize / 2 - 1);
    }

    private void HandleFftCalculated()
    {
        _handleFftCallCount++;
        
        // Log every 50 calls (5 seconds at 100ms intervals)
        //if (_handleFftCallCount % 50 == 0)
        //{
            //_logger.LogInformation("HandleFftCalculated tick #{Count} - Timer is running, IsPlaying={IsPlaying}, _audioDeviceLost={AudioDeviceLost}", 
                //_handleFftCallCount, IsPlaying, _audioDeviceLost);
        //}

        // Early exit if device is already lost
        if (_audioDeviceLost)
        {
            return;
        }

        // **DIRECTSOUND CHECK**: If we're supposed to be playing, but the stream has stopped, the device was taken
        if (_currentMode == OutputMode.DirectSound && IsPlaying && CurrentStream != 0)
        {
            PlaybackState state = Bass.ChannelIsActive(CurrentStream);
            if (state == PlaybackState.Stopped)
            {
                _audioDeviceLost = true;
                _logger.LogError("DirectSound playback has stopped unexpectedly - device taken by exclusive app!");
                
                // Marshal to UI thread to stop playback and show message
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        // Stop playback (this will also stop the timer via OnIsPlayingChanged)
                        Stop();
                        
                        // Show message box with owner window so it appears on the correct monitor
                        Window? owner = Application.Current.MainWindow;
                        if (owner != null)
                        {
                            MessageBox.Show(
                                owner,
                                "Audio device is being used exclusively by another application. Playback has been stopped.\n\n" +
                                "The other application has taken exclusive control of the audio device.\n" +
                                "Please close the other application or switch to a different audio device in Settings.",
                                "Audio Device Busy",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                        else
                        {
                            MessageBox.Show(
                                "Audio device is being used exclusively by another application. Playback has been stopped.\n\n" +
                                "The other application has taken exclusive control of the audio device.\n" +
                                "Please close the other application or switch to a different audio device in Settings.",
                                "Audio Device Busy",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error showing audio device busy message: {Message}", ex.Message);
                    }
                });
                
                return;
            }
        }

        // **WASAPI-SPECIFIC CHECK**: If we're supposed to be playing, but WASAPI has stopped, the device was taken
        if (_currentMode != OutputMode.DirectSound && IsPlaying && _wasapiInitialized)
        {
            if (!BassWasapi.IsStarted)
            {
                _audioDeviceLost = true;
                _logger.LogError("WASAPI playback has stopped unexpectedly - device taken by exclusive app!");
                
                // Marshal to UI thread to stop playback and show message
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        // Stop playback (this will also stop the timer via OnIsPlayingChanged)
                        Stop();
                        
                        // Show message box with owner window so it appears on the correct monitor
                        Window? owner = Application.Current.MainWindow;
                        if (owner != null)
                        {
                            MessageBox.Show(
                                owner,
                                "Audio device is being used exclusively by another application. Playback has been stopped.\n\n" +
                                "The other application has taken exclusive control of the audio device.\n" +
                                "Please close the other application or switch to a different audio device in Settings.",
                                "Audio Device Busy",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                        else
                        {
                            MessageBox.Show(
                                "Audio device is being used exclusively by another application. Playback has been stopped.\n\n" +
                                "The other application has taken exclusive control of the audio device.\n" +
                                "Please close the other application or switch to a different audio device in Settings.",
                                "Audio Device Busy",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error showing audio device busy message: {Message}", ex.Message);
                    }
                });
                
                return;
            }
        }

        if (CurrentStream != 0)
        {
            double positionSeconds = Bass.ChannelBytes2Seconds(CurrentStream, Bass.ChannelGetPosition(CurrentStream));
            if (!double.IsNaN(positionSeconds) && positionSeconds >= 0)
            {
                CurrentTrackPosition = positionSeconds;
            }

            PlaybackState state = Bass.ChannelIsActive(CurrentStream);
            
            // Check for device loss after querying state
            if (CheckAudioDeviceLost())
            {
                return;
            }
            
            if (state != PlaybackState.Playing)
            {
                return;
            }
        }
        else
        {
            return;
        }

        int bytesRead;
        if (_currentMode == OutputMode.DirectSound)
        {
            bytesRead = Bass.ChannelGetData(CurrentStream, _fftBuffer, (int)DataFlags.FFT2048);
            
            // Check for BUSY error immediately after GetData
            if (bytesRead < 0 && Bass.LastError == Errors.Busy)
            {
                _logger.LogError("DirectSound ChannelGetData failed: BASS_ERROR_BUSY");
                CheckAudioDeviceLost(); // This will handle the error and show message
                return;
            }
        }
        else // WASAPI modes
        {
            // Early exit if device is lost
            if (_audioDeviceLost)
            {
                return;
            }

            bytesRead = BassWasapi.GetData(_fftBuffer, (int)DataFlags.FFT2048);

            // Check for BUSY error immediately after GetData
            if (bytesRead < 0)
            {
                Errors error = Bass.LastError;
                
                if (error == Errors.Busy)
                {
                    _logger.LogError("WASAPI GetData failed: BASS_ERROR_BUSY - Device taken by exclusive app");
                    CheckAudioDeviceLost(); // This will handle the error and show message
                    return;
                }
                else if (error != Errors.OK && error != Errors.Unknown)
                {
                    _logger.LogWarning("WASAPI GetData returned {BytesRead}, Bass.LastError = {Error}", bytesRead, error);
                }
            }

            // Check if device was lost during the GetData call
            if (CheckAudioDeviceLost())
            {
                return;
            }
        }

        int fftSize; // Declare once here
        if (bytesRead < 0)
        {
            fftSize = _fftBuffer.Length / 2; // 1024 for consistency
            FftUpdate = new float[fftSize];
            Array.Clear(FftUpdate, 0, fftSize);
            OnFftCalculated?.Invoke(FftUpdate);
            return;
        }

        fftSize = _fftBuffer.Length / 2; // Assign value for success path (1024 magnitudes)
        float[] fftResult = new float[fftSize];
        for (int i = 0; i < fftSize; i++)
        {
            float real = _fftBuffer[i * 2];
            float imag = _fftBuffer[i * 2 + 1];
            float magnitude = (float)Math.Sqrt(real * real + imag * imag);
            float db = 20 * (float)Math.Log10(magnitude > 0 ? magnitude : 1e-5f);
            fftResult[i] = db < NoiseFloorDb ? 0f : Math.Max(0, (db + 120f) / 120f) * 0.5f;
        }

        const int barCount = 32;
        float[] barValues = new float[barCount];
        int binsPerBar = fftSize / barCount;
        for (int bar = 0; bar < barCount; bar++)
        {
            int startBin = bar * binsPerBar;
            int endBin = (bar == barCount - 1) ? fftSize : (bar + 1) * binsPerBar;
            float sum = 0f;
            int binCount = endBin - startBin;
            for (int bin = startBin; bin < endBin; bin++)
            {
                sum += fftResult[bin];
            }
            barValues[bar] = binCount > 0 ? sum / binCount : 0f;
        }

        for (int i = 0; i < fftSize; i++)
        {
            int barIndex = (int)((float)i / fftSize * barCount);
            barIndex = Math.Clamp(barIndex, 0, barCount - 1);
            fftResult[i] = barValues[barIndex];
        }
        FftUpdate = fftResult;

        OnFftCalculated?.Invoke(FftUpdate);
    }

    #endregion

    private void FreeResources()
    {
        // Free all streams - Bass.StreamFree handles invalid handles gracefully
        Bass.StreamFree(_mixerStream);
        _mixerStream = 0;
        
        Bass.StreamFree(_decodeStream);
        _decodeStream = 0;
        
        Bass.StreamFree(CurrentStream);
        CurrentStream = 0;

        // Free WASAPI - these calls are safe even if not initialized
        _logger.LogInformation("FreeResources: Freeing WASAPI");
        BassWasapi.Stop();
        BassWasapi.Free();
        _wasapiInitialized = false;

        // Free BASS - safe to call even if not initialized
        Bass.Free();
        IsBassInitialized = false; // ← CRITICAL: Reset this flag!
    }

    public void Dispose()
    {
        Stop();

        // Clean up equalizer
        CleanupEqualizer();

        // No longer need to free _bassFxHandle or _bassMixHandle
        // They're managed by BassAudioEngine now

        // Only call BassWasapi.Free() if WASAPI was successfully initialized
        try
        {
            if (BassNativeLibraryManager.IsDllAvailable("basswasapi.dll"))
            {
                BassWasapi.Free();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error freeing WASAPI: {ex.Message}");
        }

        Bass.Free();
        _positionTimer.Dispose();

        // Reset DLL directory
        SetDllDirectory(null);

        GC.SuppressFinalize(this);
    }
}