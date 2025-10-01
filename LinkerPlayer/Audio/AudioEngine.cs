using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.BassLibs;
using LinkerPlayer.Core;
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

    private OutputMode _currentMode = OutputMode.DirectSound;
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
    private IntPtr _bassFxHandle = IntPtr.Zero; // Handle to explicitly loaded bass_fx.dll
    private IntPtr _bassMixHandle = IntPtr.Zero;

    private int _decodeStream = 0; // Holds the decode stream (file)
    private int _mixerStream = 0; // Holds the mixer stream (WASAPI output)

    public bool IsInitialized => IsBassInitialized;

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
            _logger.LogInformation("Initializing AudioEngine");

            // Initialize BASS Native Library Manager first
            BassNativeLibraryManager.Initialize(_logger);

            // Initialize BASS
            InitializeBass();

            // Explicitly load bass_fx.dll
            LoadBassFxLibrary();

            // Explicitly load bassmix.dll
            LoadBassMixLibrary();

            IEnumerable<Device> devices = _outputDeviceManager.RefreshOutputDeviceList();

            _currentMode = _settingsManager.Settings.SelectedOutputMode;
            _currentDevice = _settingsManager.Settings.SelectedOutputDevice ?? new Device("Default", OutputDeviceType.DirectSound, -1, true);

            _wasapiProc = new WasapiProcedure(WasapiProc);

            _positionTimer = new System.Timers.Timer(100);
            _positionTimer.Elapsed += (_, _) => HandleFftCalculated();
            _positionTimer.AutoReset = true;

            FftUpdate = new float[ExpectedFftSize];
            _logger.LogInformation("AudioEngine initialized successfully");
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

    private void LoadBassFxLibrary()
    {
        try
        {
            if (!BassNativeLibraryManager.IsDllAvailable("bass_fx.dll"))
            {
                _logger.LogError("bass_fx.dll not available for explicit loading");
                return;
            }

            string bassFxPath = BassNativeLibraryManager.GetDllPath("bass_fx.dll");
            _bassFxHandle = LoadLibrary(bassFxPath);

            if (_bassFxHandle == IntPtr.Zero)
            {
                uint error = GetLastError();
                _logger.LogError($"Failed to load bass_fx.dll. Error code: {error}");
            }
            else
            {
                _logger.LogInformation("Successfully loaded bass_fx.dll");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while loading bass_fx.dll");
        }
    }

    private void LoadBassMixLibrary()
    {
        try
        {
            if (!BassNativeLibraryManager.IsDllAvailable("bassmix.dll"))
            {
                _logger.LogError("bassmix.dll not available for explicit loading");
                return;
            }

            string bassMixPath = BassNativeLibraryManager.GetDllPath("bassmix.dll");
            _bassMixHandle = LoadLibrary(bassMixPath);

            if (_bassMixHandle == IntPtr.Zero)
            {
                uint error = GetLastError();
                _logger.LogError($"Failed to load bassmix.dll. Error code: {error}");
            }
            else
            {
                _logger.LogInformation("Successfully loaded bassmix.dll");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while loading bassmix.dll");
        }
    }

    public void InitializeBass()
    {
        if (IsBassInitialized)
        {
            return;
        }

        try
        {
            // Set DLL directory to the extracted BASS libraries
            string bassLibPath = BassNativeLibraryManager.GetNativeLibraryPath();
            _logger.LogInformation($"Setting DLL directory to: {bassLibPath}");

            if (!SetDllDirectory(bassLibPath))
            {
                _logger.LogWarning("Failed to set DLL directory for BASS libraries");
            }

            SetOutputMode(_currentMode, _currentDevice);

        }
        catch (Exception ex)
        {
            _logger.LogError($"Error initializing BASS: {ex.Message}");
        }
    }

    public IEnumerable<Device> DirectSoundDevices => _outputDeviceManager.GetDirectSoundDevices();
    public IEnumerable<Device> WasapiDevices => _outputDeviceManager.GetWasapiDevices();

    private int _sampleRate;

    public void SetOutputMode(OutputMode selectedOutputMode, Device? device)
    {
        // Stop current playback first
        Stop();

        // Free current resources
        FreeResources();

        // Initialize new mode
        bool success;

        if (device == null)
        {
            device = new Device("Default", OutputDeviceType.DirectSound, -1, true);
        }

        _currentMode = selectedOutputMode;
        _currentDevice = device;

        _settingsManager.Settings.SelectedOutputMode = selectedOutputMode;
        _settingsManager.Settings.SelectedOutputDevice = device;
        _settingsManager.SaveSettings(nameof(_settingsManager.Settings.SelectedOutputMode));
        _settingsManager.SaveSettings(nameof(_settingsManager.Settings.SelectedOutputDevice));

        if (_currentMode == OutputMode.DirectSound)
        {
            success = Bass.Init(-1, 44100, DeviceInitFlags.DirectSound);
            if (success)
            {
                _logger.LogInformation("Switched to DirectSound mode");
                IsBassInitialized = true;
                _sampleRate = 44100; // keep in sync with init
            }
            else
            {
                _logger.LogError($"Failed to initialize DirectSound: {Bass.LastError}");
            }
        }
        else
        {
            // For WASAPI modes, we still need BASS for decoding
            bool exclusive = (_currentMode == OutputMode.WasapiExclusive);

            // Get device info for mixer creation
            if (!BassWasapi.GetDeviceInfo(_currentDevice.Index, out var deviceInfo))
            {
                _logger.LogError($"Failed to get device info for mixer creation: {Bass.LastError}");
                return;
            }

            // Enable mixer low-pass filter for better resampling quality
            Bass.Configure((Configuration)0x10600, true);

            // Store for use elsewhere
            _sampleRate = deviceInfo.MixFrequency;

            // Initialize BASS for decoding only (no device)
            success = Bass.Init(0, deviceInfo.MixFrequency, DeviceInitFlags.Default); // Use no-sound device for decoding
            if (success)
            {
                _logger.LogInformation($"Bass.Init for {selectedOutputMode} mode succeeded!");
                IsBassInitialized = true;
            }
            else
            {
                _logger.LogError($"Failed to initialize BASS for decoding: {Bass.LastError}");
                return; // Early exit if decoding init fails
            }

            // Now initialize WASAPI with buffering
            var flags = exclusive ? WasapiInitFlags.Exclusive : WasapiInitFlags.Shared;
            flags |= WasapiInitFlags.Buffer; // Enable buffering to prevent data stealing

            float bufferLength = 0.10f; // 100ms buffer for stability
            float period = 0f; // Use default period

            success = BassWasapi.Init(_currentDevice.Index, deviceInfo.MixFrequency, deviceInfo.MixChannels, flags, bufferLength, period, _wasapiProc, IntPtr.Zero);

            if (!success)
            {
                _logger.LogError($"WASAPI init failed with buffer: {Bass.LastError} - Retrying without buffer");
                flags &= ~WasapiInitFlags.Buffer; // Fallback without buffer if unsupported
                success = BassWasapi.Init(_currentDevice.Index, deviceInfo.MixFrequency, deviceInfo.MixChannels, flags, bufferLength, period, _wasapiProc, IntPtr.Zero);
            }

            if (success)
            {
                _wasapiInitialized = true;
                _logger.LogInformation($"Switched to {_currentMode} mode with buffer {(flags.HasFlag(WasapiInitFlags.Buffer) ? "enabled" : "disabled (fallback)")}");
            }
            else
            {
                _logger.LogError($"Failed to initialize WASAPI: {Bass.LastError}");
            }
        }

        if (!success)
        {
            _logger.LogError($"Failed to initialize {selectedOutputMode} mode");
            MessageBox.Show($"Failed to initialize {selectedOutputMode} mode", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void LoadAudioFile(string pathToMusic)
    {
        _logger.LogDebug($"LoadAudioFile called with path: {pathToMusic}");
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

            if (_currentMode == OutputMode.DirectSound)
            {
                CurrentStream = Bass.CreateStream(pathToMusic, 0, 0, Flags: BassFlags.Default | BassFlags.Prescan);
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
                _decodeStream = Bass.CreateStream(pathToMusic, 0, 0, Flags: BassFlags.Decode | BassFlags.Float);
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
                _logger.LogInformation($"WASAPI Device: {deviceInfo.Name}, MixFrequency: {deviceInfo.MixFrequency}, MixChannels: {deviceInfo.MixChannels}");

                // Try float mixer first
                _mixerStream = ManagedBass.Mix.BassMix.CreateMixerStream(deviceFreq, deviceChans, BassFlags.Float | BassFlags.Decode);
                bool mixerIsFloat = _mixerStream != 0;
                if (_mixerStream != 0)
                {
                    _logger.LogInformation($"File Rate: {decodeInfo.Frequency} Hz, Channels: {decodeInfo.Channels}");
                    _logger.LogInformation($"Device Rate: {deviceFreq} Hz, Channels: {deviceChans}");
                    Bass.ChannelGetInfo(_mixerStream, out var mixerInfo);
                    _logger.LogInformation($"Mixer Rate: {mixerInfo.Frequency} Hz");
                    _logger.LogDebug($"Mixer Stream: Freq: {mixerInfo.Frequency}, Channels: {mixerInfo.Channels}, Flags: {mixerInfo.Flags}");
                }
                else
                {
                    // Fallback to 16-bit PCM mixer
                    _logger.LogWarning("Falling back to 16-bit PCM mixer");
                    _mixerStream = ManagedBass.Mix.BassMix.CreateMixerStream(deviceFreq, deviceChans, BassFlags.Decode);
                    mixerIsFloat = false;
                    if (_mixerStream != 0)
                    {
                        _logger.LogInformation($"File Rate: {decodeInfo.Frequency} Hz, Channels: {decodeInfo.Channels}");
                        _logger.LogInformation($"Device Rate: {deviceFreq} Hz, Channels: {deviceChans}");
                        Bass.ChannelGetInfo(_mixerStream, out var mixerInfo);
                        _logger.LogInformation($"Mixer Rate: {mixerInfo.Frequency} Hz");
                        _logger.LogDebug($"PCM Mixer Stream: Freq: {mixerInfo.Frequency}, Channels: {mixerInfo.Channels}, Flags: {mixerInfo.Flags}");
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

                _logger.LogInformation($"Adding channel with flags: {addFlags}, File Rate: {decodeInfo.Frequency} Hz, Device Rate: {deviceFreq} Hz");

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
                _logger.LogError($"Failed to create stream for playback: {Bass.LastError}");
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

            _logger.LogInformation($"Successfully loaded audio file: {Path.GetFileName(pathToMusic)}");
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
        if (string.IsNullOrEmpty(PathToMusic))
        {
            _logger.LogError("Cannot play: PathToMusic is null or empty");
            return;
        }
        Play(PathToMusic);
    }

    public void Play(string pathToMusic, double position = 0)
    {
        _logger.LogDebug($"Play called with path: {pathToMusic}, position: {position}");
        if (string.IsNullOrEmpty(pathToMusic))
        {
            _logger.LogError("Play called with null or empty pathToMusic");
            return;
        }
        PlaybackState playbackState = Bass.ChannelIsActive(CurrentStream);

        if (!string.IsNullOrEmpty(pathToMusic) && playbackState == PlaybackState.Paused)
        {
            ResumePlay();
            return;
        }

        Stop();

        LoadAudioFile(pathToMusic);

        if (CurrentStream != 0)
        {
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
            if (_currentMode == OutputMode.DirectSound)
            {
                // DirectSound play
                Bass.ChannelPlay(CurrentStream);
            }
            else
            {
                // WASAPI playback - always try to initialize WASAPI when we play
                if (!InitializeWasapiForPlayback())
                {
                    _logger.LogError("Failed to initialize WASAPI for playback");
                    return;
                }

                if (_mixerStream != 0)
                {
                    _logger.LogDebug("Calling Bass.ChannelPlay on mixer stream before WASAPI start");
                    Bass.ChannelPlay(_mixerStream, false);
                }

                bool wasapiStarted = BassWasapi.Start();
                if (!wasapiStarted)
                    _logger.LogError($"BassWasapi.Start failed: {Bass.LastError}");
                else
                    _logger.LogInformation("BassWasapi.Start succeeded");
            }

            IsPlaying = true;
            PathToMusic = pathToMusic;
        }
        else
        {
            _logger.LogError("Failed to create stream, cannot play");
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

            if (_wasapiInitialized)
            {
                BassWasapi.Stop();
            }
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
        _logger.LogInformation("Track ended");
        Stop();
    }

    private bool InitializeWasapiForPlayback()
    {
        try
        {
            // Always free any existing WASAPI initialization first
            if (_wasapiInitialized)
            {
                _logger.LogInformation("Freeing existing WASAPI initialization");
                BassWasapi.Stop();
                BassWasapi.Free();
                _wasapiInitialized = false;
            }

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
                if (BassWasapi.GetInfo(out var info))
                {
                    _logger.LogInformation($"WASAPI initialized successfully. Buffered={(info.BufferLength > 0 ? "Yes" : "No")}, Format={(_mixerIsFloat ? "float" : "16-bit PCM")}");
                }
                else
                {
                    _logger.LogInformation($"WASAPI initialized successfully. Format={(_mixerIsFloat ? "float" : "16-bit PCM")}");
                }
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

    #region Equalizer
    public bool InitializeEqualizer()
    {
        CleanupEqualizer();

        if (CurrentStream == 0)
        {
            _logger.LogWarning("Cannot initialize equalizer: No stream loaded");
            return false;
        }

        if (_bassFxHandle == IntPtr.Zero)
        {
            _logger.LogError("Cannot initialize equalizer: bass_fx.dll not loaded");
            return false;
        }

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
        if (_eqInitialized)
        {
            _logger.LogInformation($"Equalizer initialized with {successCount}/{_equalizerBands.Count} bands");
        }

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

    public double GetDecibelLevel()
    {
        int level;
        if (_currentMode == OutputMode.DirectSound)
        {
            level = Bass.ChannelGetLevel(CurrentStream);
        }
        else
        {
            level = BassWasapi.GetLevel();
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
        }
        else
        {
            level = BassWasapi.GetLevel();
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
        }
        else
        {
            // IMPORTANT: In WASAPI use device GetData to avoid starving the mixer decode stream
            bytesRead = BassWasapi.GetData(fftDataBuffer, (int)DataFlags.FFT2048);
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
        if (CurrentStream != 0)
        {
            double positionSeconds = Bass.ChannelBytes2Seconds(CurrentStream, Bass.ChannelGetPosition(CurrentStream));
            if (!double.IsNaN(positionSeconds) && positionSeconds >= 0)
            {
                CurrentTrackPosition = positionSeconds;
            }

            PlaybackState state = Bass.ChannelIsActive(CurrentStream);
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
        }
        else // WASAPI modes
        {
            bytesRead = BassWasapi.GetData(_fftBuffer, (int)DataFlags.FFT2048);
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
        if (_wasapiInitialized)
        {
            _logger.LogInformation("FreeResources: Freeing WASAPI");
            BassWasapi.Stop();
            BassWasapi.Free();
            _wasapiInitialized = false;
        }
        Bass.Free();
    }

    public void Dispose()
    {
        Stop();

        // Clean up equalizer
        CleanupEqualizer();

        // Free explicitly loaded bass_fx.dll
        if (_bassFxHandle != IntPtr.Zero)
        {
            FreeLibrary(_bassFxHandle);
            _bassFxHandle = IntPtr.Zero;
        }

        // Free explicitly loaded bassmix.dll
        if (_bassMixHandle != IntPtr.Zero)
        {
            FreeLibrary(_bassMixHandle);
            _bassMixHandle = IntPtr.Zero;
        }

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