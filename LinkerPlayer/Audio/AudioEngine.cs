using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.BassLibs;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using ManagedBass;
using ManagedBass.Wasapi;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming

namespace LinkerPlayer.Audio;

public partial class AudioEngine : ObservableObject, ISpectrumPlayer, IDisposable
{
    private readonly IOutputDeviceManager _outputDeviceManager;
    private readonly ISettingsManager _settingsManager;
    private readonly ILogger<AudioEngine> _logger;
    private readonly IUiNotifier _uiNotifier;

    [ObservableProperty] private bool _isBassInitialized;
    [ObservableProperty] private int _currentStream;
    [ObservableProperty] private string _pathToMusic = string.Empty;
    [ObservableProperty] private double _currentTrackLength;
    [ObservableProperty] private double _currentTrackPosition;
    [ObservableProperty] private float _musicVolume =0.5f;
    [ObservableProperty] private bool _isPlaying;

    private OutputMode _currentMode;
    private Device _currentDevice;

    private bool _wasapiInitialized;
    private WasapiProcedure _wasapiProc;

    private readonly System.Timers.Timer _positionTimer;

    private int _decodeStream =0; // decode stream (file)
    private int _mixerStream =0; // mixer stream (WASAPI output)
    private int _endSyncHandle; // track end-of-stream sync handle

    // Native add-on library handles
    private IntPtr _bassFxHandle = IntPtr.Zero;
    private IntPtr _bassMixHandle = IntPtr.Zero;

    // Audio device error tracking
    private Errors _lastAudioError = Errors.OK;
    private int _consecutiveAudioErrors =0;
    private const int MAX_AUDIO_ERROR_COUNT =10;
    private bool _audioDeviceLost = false;

    // Track mixer format
    private bool _mixerIsFloat = true;

    // Serialize all engine operations to avoid races across threads
    private readonly object _engineSync = new();

    public event Action? OnPlaybackStopped;
    public event Action<float[]>? OnFftCalculated;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string? lpPathName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetLastError();

    public AudioEngine(
    IOutputDeviceManager outputDeviceManager,
    ISettingsManager settingsManager,
    ILogger<AudioEngine> logger,
    IUiNotifier uiNotifier)
    {
        _outputDeviceManager = outputDeviceManager;
        _settingsManager = settingsManager;
        _logger = logger;
        _uiNotifier = uiNotifier;

        try
        {
            _currentMode = _settingsManager.Settings.SelectedOutputMode;
            _currentDevice = _settingsManager.Settings.SelectedOutputDevice
            ?? new Device("Default", OutputDeviceType.DirectSound, -1, true);

            // Initialize BASS Native Library Manager
            BassNativeLibraryManager.Initialize(_logger);

            // Set DLL directory so native DLLs are found
            string bassLibPath = BassNativeLibraryManager.GetNativeLibraryPath();
            _logger.LogInformation($"Setting DLL directory to: {bassLibPath}");
            if (!SetDllDirectory(bassLibPath))
            {
                _logger.LogWarning("Failed to set DLL directory for BASS libraries");
            }

            // Load add-ons (bass_fx, bassmix)
            LoadBassAddOns();

            // Refresh device list (doesn't open devices)
            _ = _outputDeviceManager.RefreshOutputDeviceList();

            // Create WASAPI callback
            _wasapiProc = new WasapiProcedure(WasapiProc);

            _positionTimer = new System.Timers.Timer(100)
            {
                AutoReset = true
            };
            _positionTimer.Elapsed += (_, _) => HandleFftCalculated();

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
        lock (_engineSync)
        {
            if (IsBassInitialized)
            {
                return;
            }

            try
            {
                bool success;

                if (_currentMode == OutputMode.DirectSound)
                {
                    success = Bass.Init(-1,44100, DeviceInitFlags.DirectSound);
                    if (success || Bass.LastError == Errors.Already)
                    {
                        IsBassInitialized = true;
                        _sampleRate =44100;
                        _logger.LogDebug("Initialized DirectSound on first play");
                    }
                    else
                    {
                        _logger.LogError($"Failed to initialize DirectSound: {Bass.LastError}");
                    }
                }
                else
                {
                    // WASAPI modes: initialize BASS for decoding
                    if (!BassWasapi.GetDeviceInfo(_currentDevice.Index, out WasapiDeviceInfo deviceInfo))
                    {
                        _logger.LogError($"Failed to get device info: {Bass.LastError}");
                        return;
                    }

                    // Enable mixer low-pass filter for better resampling quality
                    Bass.Configure((Configuration)0x10600, true);

                    _sampleRate = deviceInfo.MixFrequency;

                    int[] ratesToTry = { deviceInfo.MixFrequency,44100,48000 };
                    foreach (int rate in ratesToTry)
                    {
                        success = Bass.Init(0, rate, DeviceInitFlags.Default);
                        if (success)
                        {
                            IsBassInitialized = true;
                            _logger.LogDebug($"Initialized BASS for decoding at {rate} Hz on first play (WASAPI mode: {_currentMode})");
                            return;
                        }
                        else
                        {
                            // If Already, treat as success (another thread may have initialized)
                            if (Bass.LastError == Errors.Already)
                            {
                                IsBassInitialized = true;
                                _logger.LogDebug("BASS was already initialized by another call");
                                return;
                            }
                            _logger.LogDebug($"Failed to initialize BASS at {rate} Hz: {Bass.LastError}, trying next rate");
                        }
                    }

                    _logger.LogError($"Failed to initialize BASS for decoding with all sample rates: {Bass.LastError}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initializing audio device: {ex.Message}");
            }
        }
    }

    public IEnumerable<Device> DirectSoundDevices => _outputDeviceManager.GetDirectSoundDevices();
    public IEnumerable<Device> WasapiDevices => _outputDeviceManager.GetWasapiDevices();

    public OutputMode GetCurrentOutputMode() => _currentMode;
    public Device GetCurrentOutputDevice() => _currentDevice;

    private int _sampleRate;

    public void SetOutputMode(OutputMode selectedOutputMode, Device? device)
    {
        if (device == null)
        {
            device = new Device("Default", OutputDeviceType.DirectSound, -1, true);
        }

        bool needsReinit = IsBassInitialized;

        if (needsReinit)
        {
            Stop();
            FreeResources();
        }

        _currentMode = selectedOutputMode;
        _currentDevice = device;

        _settingsManager.Settings.SelectedOutputMode = selectedOutputMode;
        _settingsManager.Settings.SelectedOutputDevice = device;
        _settingsManager.SaveSettings(nameof(_settingsManager.Settings.SelectedOutputMode));
        _settingsManager.SaveSettings(nameof(_settingsManager.Settings.SelectedOutputDevice));

        if (needsReinit)
        {
            IsBassInitialized = false;
            InitializeAudioDevice();
        }

        WeakReferenceMessenger.Default.Send(new OutputModeChangedMessage(_currentMode));
    }

    public void LoadAudioFile(string pathToMusic)
    {
        lock (_engineSync)
        {
            try
            {
                // Free previous streams and clean up EQ
                CleanupEqualizer();
                if (_mixerStream !=0)
                {
                    Bass.StreamFree(_mixerStream);
                    _mixerStream =0;
                }
                if (_decodeStream !=0)
                {
                    Bass.StreamFree(_decodeStream);
                    _decodeStream =0;
                }
                if (CurrentStream !=0)
                {
                    Bass.StreamFree(CurrentStream);
                    CurrentStream =0;
                }

                if (_currentMode == OutputMode.DirectSound)
                {
                    CurrentStream = Bass.CreateStream(pathToMusic,0,0, Flags: BassFlags.Default);
                    _decodeStream =0;
                    _mixerStream =0;
                }
                else
                {
                    // WASAPI: create decode stream and mixer
                    if (!BassWasapi.GetDeviceInfo(_currentDevice.Index, out WasapiDeviceInfo deviceInfo))
                    {
                        _logger.LogError($"Failed to get device info for mixer creation: {Bass.LastError}");
                        return;
                    }

                    int deviceFreq = deviceInfo.MixFrequency;
                    int deviceChans = deviceInfo.MixChannels;

                    _decodeStream = Bass.CreateStream(pathToMusic,0,0, Flags: BassFlags.Decode);
                    _logger.LogDebug($"WASAPI: _decodeStream handle: {_decodeStream}, Bass.LastError: {Bass.LastError}");
                    if (_decodeStream ==0)
                    {
                        _logger.LogError($"Failed to load file: {Bass.LastError}. File: {pathToMusic}");
                        return;
                    }

                    Bass.ChannelGetInfo(_decodeStream, out ChannelInfo decodeInfo);
                    int decodeChans = decodeInfo.Channels;

                    _mixerStream = ManagedBass.Mix.BassMix.CreateMixerStream(deviceFreq, deviceChans, BassFlags.Float | BassFlags.Decode);
                    bool mixerIsFloat = _mixerStream !=0;
                    if (_mixerStream ==0)
                    {
                        _logger.LogWarning("Falling back to16-bit PCM mixer");
                        _mixerStream = ManagedBass.Mix.BassMix.CreateMixerStream(deviceFreq, deviceChans, BassFlags.Decode);
                        mixerIsFloat = false;
                        if (_mixerStream ==0)
                        {
                            _logger.LogError($"Failed to create mixer: {Bass.LastError}");
                            Bass.StreamFree(_decodeStream);
                            _decodeStream =0;
                            return;
                        }
                    }

                    BassFlags addFlags = BassFlags.Default;
                    if (decodeChans != deviceChans)
                        addFlags |= BassFlags.MixerChanDownMix;
                    if (decodeInfo.Frequency != deviceFreq)
                        addFlags |= BassFlags.MixerChanNoRampin;

                    if (!ManagedBass.Mix.BassMix.MixerAddChannel(_mixerStream, _decodeStream, addFlags))
                    {
                        _logger.LogError($"Failed to add decode stream to mixer: {Bass.LastError}");
                        Bass.StreamFree(_mixerStream);
                        _mixerStream =0;
                        Bass.StreamFree(_decodeStream);
                        _decodeStream =0;
                        return;
                    }

                    CurrentStream = _mixerStream;
                    _mixerIsFloat = mixerIsFloat;
                }

                if (CurrentStream == 0)
                {
                    _logger.LogError($"Failed to create stream for playback - {Path.GetFileName(pathToMusic)}: {Bass.LastError}");
                    return;
                }

                long lengthBytes = Bass.ChannelGetLength(CurrentStream);
                if (lengthBytes <0)
                {
                    _logger.LogError($"Failed to get track length: {Bass.LastError}");
                    Bass.StreamFree(CurrentStream);
                    CurrentStream = 0;
                    return;
                }

                CurrentTrackLength = Bass.ChannelBytes2Seconds(CurrentStream, lengthBytes);
                CurrentTrackPosition =0;

                if (EqEnabled)
                {
                    InitializeEqualizer();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading file: {ex.Message}");
                if (_mixerStream !=0)
                {
                    Bass.StreamFree(_mixerStream);
                    _mixerStream =0;
                }
                if (_decodeStream !=0)
                {
                    Bass.StreamFree(_decodeStream);
                    _decodeStream =0;
                }
                if (CurrentStream !=0)
                {
                    try
                    {
                        Bass.StreamFree(CurrentStream);
                    }
                    catch { }
                    CurrentStream = 0;
                }
            }
        }
    }

    public void Play()
    {
        lock (_engineSync)
        {
            _logger.LogDebug("Play() called - no path parameter");
            if (string.IsNullOrEmpty(PathToMusic))
            {
                _logger.LogError("Cannot play: PathToMusic is null or empty");
                return;
            }
            Play(PathToMusic);
        }
    }

    public void Play(string pathToMusic, double position =0)
    {
        lock (_engineSync)
        {
            _logger.LogDebug("Play() called with path: {Path}, position: {Position}", pathToMusic, position);

            if (string.IsNullOrEmpty(pathToMusic))
            {
                _logger.LogError("Play called with null or empty pathToMusic");
                return;
            }

            // Reset audio device lost flag when user manually tries to play
            if (_audioDeviceLost)
            {
                _logger.LogDebug("Resetting audio device lost flag - user is attempting to play again");
                _audioDeviceLost = false;
                _consecutiveAudioErrors =0;
                _lastAudioError = Errors.OK;
            }

            // Initialize BASS on first play if not already initialized
            if (!IsBassInitialized)
            {
                _logger.LogDebug("First play - initializing audio device (Bass.Init or BassWasapi.Init)");
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
                _logger.LogDebug("Resuming paused track");
                ResumePlay();
                return;
            }

            _logger.LogDebug("Stopping current playback before loading new track");
            Stop();

            _logger.LogDebug("Loading audio file: {FileName}", Path.GetFileName(pathToMusic));
            LoadAudioFile(pathToMusic);

            if (CurrentStream !=0)
            {
                _logger.LogDebug("CurrentStream is valid ({StreamHandle}), starting playback", CurrentStream);

                // Set end-of-track sync
                _endSyncHandle = Bass.ChannelSetSync(CurrentStream, SyncFlags.End,0, EndTrackSyncProc);
                if (_endSyncHandle ==0)
                {
                    _logger.LogWarning($"Failed to set end-of-track sync: {Bass.LastError}");
                }

                // Set position if specified
                if (position >0)
                {
                    long bytePosition = Bass.ChannelSeconds2Bytes(CurrentStream, position);
                    if (!Bass.ChannelSetPosition(CurrentStream, bytePosition))
                    {
                        _logger.LogWarning($"Failed to set initial position: {Bass.LastError}");
                    }
                }

                // Set volume
                Bass.ChannelSetAttribute(CurrentStream, ChannelAttribute.Volume, MusicVolume);

                bool playbackStarted = _currentMode == OutputMode.DirectSound
                ? StartDirectSoundPlayback()
                : StartWasapiPlayback();

                if (playbackStarted)
                {
                    IsPlaying = true;
                    PathToMusic = pathToMusic;
                    _logger.LogDebug("Playback started successfully. IsPlaying = {IsPlaying}", IsPlaying);
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
    }

    public void Stop()
    {
        lock (_engineSync)
        {
            // Push an immediate zeroed FFT frame so UI drops instantly
            try
            {
                int zeroLen = Math.Max(1, ExpectedFftSize /2);
                OnFftCalculated?.Invoke(new float[zeroLen]);
            }
            catch { /* ignore UI listeners exceptions */ }

            if (CurrentStream !=0)
            {
                if (_endSyncHandle !=0)
                {
                    Bass.ChannelRemoveSync(CurrentStream, _endSyncHandle);
                    _endSyncHandle =0;
                }

                Bass.ChannelStop(CurrentStream);
                Bass.ChannelSetPosition(CurrentStream,0);

                IsPlaying = false;
                CurrentTrackPosition =0;

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
    }

    private void FreeResources()
    {
        lock (_engineSync)
        {
            // Free all streams - Bass.StreamFree handles invalid handles gracefully
            Bass.StreamFree(_mixerStream);
            _mixerStream =0;

            Bass.StreamFree(_decodeStream);
            _decodeStream =0;

            Bass.StreamFree(CurrentStream);
            CurrentStream =0;

            // Free WASAPI - these calls are safe even if not initialized
            _logger.LogDebug("FreeResources: Freeing WASAPI");
            BassWasapi.Stop();
            BassWasapi.Free();
            _wasapiInitialized = false;

            // Free BASS - safe to call even if not initialized
            Bass.Free();
            IsBassInitialized = false; // Reset flag
        }
    }

    public void Pause()
    {
        lock (_engineSync)
        {
            if (CurrentStream !=0 && IsPlaying)
            {
                if (_currentMode == OutputMode.DirectSound)
                {
                    PauseDirectSound();
                }
                else
                {
                    PauseWasapi();
                }

                IsPlaying = false;
            }
        }
    }

    public void ResumePlay()
    {
        lock (_engineSync)
        {
            if (CurrentStream !=0 && !IsPlaying)
            {
                bool resumed = _currentMode == OutputMode.DirectSound
                ? ResumeDirectSound()
                : ResumeWasapi();

                if (!resumed)
                {
                    return;
                }

                IsPlaying = true;

                _endSyncHandle = Bass.ChannelSetSync(CurrentStream, SyncFlags.End,0, EndTrackSyncProc);
                if (_endSyncHandle ==0)
                {
                    _logger.LogWarning($"Failed to set end-of-track sync: {Bass.LastError}");
                }
            }
        }
    }

    private void EndTrackSyncProc(int handle, int channel, int data, IntPtr user)
    {
        Stop();
    }

    private bool CheckAudioDeviceLost()
    {
        if (_audioDeviceLost)
        {
            return true; // Already handled
        }

        Errors currentError = Bass.LastError;

        if (currentError == Errors.Busy)
        {
            _audioDeviceLost = true;
            _logger.LogError("Audio device is busy (BASS_ERROR_BUSY). Another application has taken exclusive control of the audio device.");

            Stop();
            ShowDeviceBusyWarning("Playback has been stopped.");
            return true;
        }

        if (currentError != Errors.OK && currentError != Errors.Unknown && currentError != Errors.Ended)
        {
            if (currentError == _lastAudioError)
            {
                _consecutiveAudioErrors++;
            }
            else
            {
                _lastAudioError = currentError;
                _consecutiveAudioErrors =1;
            }

            if (_consecutiveAudioErrors >= MAX_AUDIO_ERROR_COUNT)
            {
                _audioDeviceLost = true;
                _logger.LogError("Audio device lost after {Count} consecutive errors. Last error: {Error}", _consecutiveAudioErrors, currentError);

                _uiNotifier.ShowWarning("Audio Device Error", $"Audio device error detected. Playback has been stopped.\n\nError: {currentError}");
                return true;
            }
        }
        else
        {
            _consecutiveAudioErrors =0;
            _lastAudioError = Errors.OK;
        }

        return false;
    }

    // Start/stop position/FFT timer on IsPlaying changes
    partial void OnIsPlayingChanged(bool value)
    {
        if (value)
        {
            _positionTimer.Start();
        }
        else
        {
            _positionTimer.Stop();
        }
    }

    // Volume changes per-backend
    partial void OnMusicVolumeChanged(float value)
    {
        if (_currentMode == OutputMode.DirectSound)
        {
            if (CurrentStream !=0)
            {
                Bass.ChannelSetAttribute(CurrentStream, ChannelAttribute.Volume, value);
            }
        }
        else if (_currentMode == OutputMode.WasapiShared || _currentMode == OutputMode.WasapiExclusive)
        {
            BassWasapi.SetVolume(WasapiVolumeTypes.Session, value);
        }
    }

    private void OnMainWindowClosing(bool value)
    {
        Stop();
    }

    private void ShowDeviceBusyWarning(string suffix)
    {
        _uiNotifier.ShowWarning(
        "Audio Device Busy",
        "Audio device is being used exclusively by another application. " +
        (string.IsNullOrWhiteSpace(suffix) ? string.Empty : suffix + "\n\n") +
        "The other application has taken exclusive control of the audio device.\n" +
        "Please close the other application or switch to a different audio device in Settings.");
    }

    private void MarkDeviceBusyAndNotify(string suffix)
    {
        _audioDeviceLost = true;
        ShowDeviceBusyWarning(suffix);
    }

    public void Dispose()
    {
        Stop();

        // Clean up equalizer
        CleanupEqualizer();

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

        // Free loaded add-on libraries
        try
        {
            if (_bassFxHandle != IntPtr.Zero)
            {
                FreeLibrary(_bassFxHandle);
                _bassFxHandle = IntPtr.Zero;
            }
            if (_bassMixHandle != IntPtr.Zero)
            {
                FreeLibrary(_bassMixHandle);
                _bassMixHandle = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error freeing add-on libraries");
        }

        // Reset DLL directory
        SetDllDirectory(null);

        GC.SuppressFinalize(this);
    }

    // Explicitly load non-plugin add-ons (bass_fx, bassmix)
    private void LoadBassAddOns()
    {
        try
        {
            if (BassNativeLibraryManager.IsDllAvailable("bass_fx.dll"))
            {
                string fxPath = BassNativeLibraryManager.GetDllPath("bass_fx.dll");
                _bassFxHandle = LoadLibrary(fxPath);
                if (_bassFxHandle == IntPtr.Zero)
                {
                    _logger.LogWarning($"LoadLibrary failed for bass_fx.dll (GetLastError={GetLastError()})");
                }
                else
                {
                    _logger.LogInformation("bass_fx.dll loaded successfully");
                }
            }
            else
            {
                _logger.LogWarning("bass_fx.dll not available in extracted DLLs");
            }

            if (BassNativeLibraryManager.IsDllAvailable("bassmix.dll"))
            {
                string mixPath = BassNativeLibraryManager.GetDllPath("bassmix.dll");
                _bassMixHandle = LoadLibrary(mixPath);
                if (_bassMixHandle == IntPtr.Zero)
                {
                    _logger.LogWarning($"LoadLibrary failed for bassmix.dll (GetLastError={GetLastError()})");
                }
                else
                {
                    _logger.LogInformation("bassmix.dll loaded successfully");
                }
            }
            else
            {
                _logger.LogWarning("bassmix.dll not available in extracted DLLs");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading add-on DLLs");
        }
    }

    public void StopAndPlayFromPosition(double position)
    {
        lock (_engineSync)
        {
            Stop();
            if (!string.IsNullOrEmpty(PathToMusic))
            {
                Play(PathToMusic, position);
            }
        }
    }

    public void SeekAudioFile(double position)
    {
        lock (_engineSync)
        {
            if (CurrentStream ==0)
            {
                _logger.LogError("Cannot seek: Current stream is invalid");
                return;
            }

            if (position <0 || position > CurrentTrackLength)
            {
                _logger.LogError($"Invalid seek position {position}: must be between0 and {CurrentTrackLength} seconds");
                return;
            }

            bool ok = _currentMode == OutputMode.DirectSound
            ? SeekDirectSound(position)
            : SeekWasapi(position);

            if (!ok)
            {
                _logger.LogError("Seek failed");
            }
        }
    }
}
