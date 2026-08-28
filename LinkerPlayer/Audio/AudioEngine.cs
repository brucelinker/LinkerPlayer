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

namespace LinkerPlayer.Audio;

public partial class AudioEngine : ObservableObject, IAudioEngine, IChannelLevelProvider
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
    [ObservableProperty] private float _musicVolume = 0.5f;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _loadedTrackPath = string.Empty;

    private OutputMode _currentMode;
    private Device _currentDevice;

    private bool _wasapiInitialized;
    private WasapiProcedure _wasapiProc;

    private readonly System.Timers.Timer _positionTimer;

    private int _decodeStream = 0; // decode stream (file)
    private int _mixerStream = 0; // mixer stream (WASAPI output)
    private int _endSyncHandle; // track end-of-stream sync handle

    // Native add-on library handles
    private IntPtr _bassFxHandle = IntPtr.Zero;
    private IntPtr _bassMixHandle = IntPtr.Zero;

    // Audio device error tracking
    private Errors _lastAudioError = Errors.OK;
    private int _consecutiveAudioErrors = 0;
    private const int MAX_AUDIO_ERROR_COUNT = 10;
    private bool _audioDeviceLost = false;

    // Track mixer format
    private bool _mixerIsFloat = true;

    // Serialize all engine operations to avoid races across threads
    private readonly object _engineSync = new();

    public event Action? OnPlaybackStopped;
    public event Action? OnTrackEnded;
    public event Action<float[]>? OnFftCalculated;

    public event Action<string>? OnCrossfadeCommitted;

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

            // Ensure native libs are extracted before any path usage
            BassNativeLibraryManager.Initialize(_logger);

            // Set DLL directory so native DLLs are found
            string bassLibPath = BassNativeLibraryManager.GetNativeLibraryPath();
            _logger.LogInformation($"Setting DLL directory to: {bassLibPath}");
            if (!SetDllDirectory(bassLibPath))
            {
                _logger.LogWarning("Failed to set DLL directory for BASS libraries");
            }

            // Load add-ons (bass_fx, bassmix) - these are loaded synchronously as they're needed for playback
            LoadBassAddOns();

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
                    int requestedDeviceIndex = _currentDevice.Type == OutputDeviceType.DirectSound ? _currentDevice.Index : -1;

                    success = Bass.Init(requestedDeviceIndex, 44100, DeviceInitFlags.DirectSound);
                    if (success || Bass.LastError == Errors.Already)
                    {
                        IsBassInitialized = true;
                        _sampleRate = 44100;

                        try
                        {
                            int actualDeviceIndex = Bass.CurrentDevice;
                            DeviceInfo actualInfo = Bass.GetDeviceInfo(actualDeviceIndex);
                            _logger.LogInformation(
                                "Initialized DirectSound (RequestedDeviceIndex={RequestedDeviceIndex}, ActualDeviceIndex={ActualDeviceIndex}, ActualDeviceName={ActualDeviceName})",
                                requestedDeviceIndex,
                                actualDeviceIndex,
                                actualInfo.Name);
                        }
                        catch
                        {
                            _logger.LogInformation("Initialized DirectSound (RequestedDeviceIndex={RequestedDeviceIndex})", requestedDeviceIndex);
                        }

                        _logger.LogDebug("Initialized DirectSound on first play");
                    }
                    else
                    {
                        _logger.LogError("Failed to initialize DirectSound (RequestedDeviceIndex={RequestedDeviceIndex}): {Error}", requestedDeviceIndex, Bass.LastError);
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

                    int[] ratesToTry = { deviceInfo.MixFrequency, 44100, 48000 };
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

    private IEnumerable<Device>? _devices;

    private void RefreshDevicesCached()
    {
        // Always refresh when called to avoid stale device index mapping after hotplug/default-device changes.
        _devices = _outputDeviceManager.RefreshOutputDeviceList().ToList();
    }

    public OutputMode GetCurrentOutputMode() => _currentMode;
    public Device GetCurrentOutputDevice() => _currentDevice;

    private int _sampleRate;

    public void SetOutputMode(OutputMode selectedOutputMode, Device? device)
    {
        if (device == null)
        {
            device = new Device("Primary Sound Driver", OutputDeviceType.DirectSound, -1, true);
        }

        bool needsReinit = IsBassInitialized;

        if (needsReinit)
        {
            Stop();
            FreeResources();

            // Device changes (especially DirectSound) require full BASS teardown to actually switch endpoints.
            try
            {
                if (_wasapiInitialized)
                {
                    BassWasapi.Stop();
                    BassWasapi.Free();
                    _wasapiInitialized = false;
                }
            }
            catch
            {
            }

            try
            {
                Bass.Free();
            }
            catch
            {
            }

            IsBassInitialized = false;
        }

        _currentMode = selectedOutputMode;
        _currentDevice = device;

        // Force re-init of WASAPI on mode/device change so the next Play binds to the new endpoint.
        _wasapiInitialized = false;

        // Refresh cached devices so future name->device mapping stays accurate.
        RefreshDevicesCached();

        _settingsManager.Settings.SelectedOutputMode = selectedOutputMode;
        _settingsManager.Settings.SelectedOutputDevice = device;
        _settingsManager.SaveSettings(nameof(_settingsManager.Settings.SelectedOutputMode));
        _settingsManager.SaveSettings(nameof(_settingsManager.Settings.SelectedOutputDevice));

        // Re-init eagerly so failures are surfaced immediately.
        InitializeAudioDevice();

        WeakReferenceMessenger.Default.Send(new OutputModeChangedMessage(_currentMode));
    }

    [ObservableProperty] private int _channelCount = 2;

    public void LoadAudioFile(string pathToMusic)
    {
        lock (_engineSync)
        {
            try
            {
                // Free previous streams and clean up EQ
                CleanupEqualizer();

                StopCrossfadeTimer("load");

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

                // Always use decode+mixer topology so crossfade is available in all output modes.
                int mixerFreq;
                int mixerChans;

                if (_currentMode == OutputMode.DirectSound)
                {
                    mixerFreq = _sampleRate > 0 ? _sampleRate : 44100;

                    // Detect channel count from audio file
                    int tempDecodeStream = Bass.CreateStream(pathToMusic, 0, 0, Flags: BassFlags.Decode);
                    if (tempDecodeStream != 0)
                    {
                        Bass.ChannelGetInfo(tempDecodeStream, out ChannelInfo tempDecodeInfo);
                        mixerChans = tempDecodeInfo.Channels;
                        Bass.StreamFree(tempDecodeStream);
                    }
                    else
                    {
                        mixerChans = 2; // fallback to stereo
                    }
                }
                else
                {
                    if (!BassWasapi.GetDeviceInfo(_currentDevice.Index, out WasapiDeviceInfo deviceInfo))
                    {
                        _logger.LogError($"Failed to get device info for mixer creation: {Bass.LastError}");
                        return;
                    }

                    mixerFreq = deviceInfo.MixFrequency;
                    mixerChans = deviceInfo.MixChannels;
                }

                _decodeStream = Bass.CreateStream(pathToMusic, 0, 0, Flags: BassFlags.Decode);
                _logger.LogDebug($"Decode stream handle: {_decodeStream}, Bass.LastError: {Bass.LastError}");
                if (_decodeStream == 0)
                {
                    _logger.LogError($"Failed to load file: {Bass.LastError}. File: {pathToMusic}");
                    return;
                }

                Bass.ChannelGetInfo(_decodeStream, out ChannelInfo decodeInfo);
                int decodeChans = decodeInfo.Channels;

                // Set channel count - this will fire PropertyChanged via ObservableProperty
                int oldChannelCount = ChannelCount; // Capture old value before setting
                ChannelCount = decodeChans;
                _logger.LogDebug("AudioEngine: Set ChannelCount to {ChannelCount} (was {OldCount})", decodeChans, oldChannelCount);

                if (mixerFreq <= 0)
                {
                    mixerFreq = decodeInfo.Frequency > 0 ? decodeInfo.Frequency : 44100;
                }

                if (mixerChans <= 0)
                {
                    mixerChans = decodeChans > 0 ? decodeChans : 2;
                }

                BassFlags mixerFlags;
                if (_currentMode == OutputMode.DirectSound)
                {
                    // DirectSound needs a PLAYABLE mixer (no Decode flag), otherwise ChannelPlay produces no sound.
                    mixerFlags = BassFlags.Float;
                }
                else
                {
                    // WASAPI pulls data via ChannelGetData in WasapiProc, so mixer must be decode-only.
                    mixerFlags = BassFlags.Float | BassFlags.Decode;
                }

                _mixerStream = ManagedBass.Mix.BassMix.CreateMixerStream(mixerFreq, mixerChans, mixerFlags);
                bool mixerIsFloat = _mixerStream != 0;
                if (_mixerStream == 0)
                {
                    _logger.LogWarning("Falling back to 16-bit PCM mixer");

                    if (_currentMode == OutputMode.DirectSound)
                    {
                        mixerFlags = BassFlags.Default;
                    }
                    else
                    {
                        mixerFlags = BassFlags.Decode;
                    }

                    _mixerStream = ManagedBass.Mix.BassMix.CreateMixerStream(mixerFreq, mixerChans, mixerFlags);
                    mixerIsFloat = false;
                    if (_mixerStream == 0)
                    {
                        _logger.LogWarning("Mixer fallback failed for {Freq}Hz/{Chans}ch: {Error}. Retrying safe stereo format.",
                            mixerFreq,
                            mixerChans,
                            Bass.LastError);

                        int safeFreq = decodeInfo.Frequency > 0 ? decodeInfo.Frequency : 44100;
                        int safeChans = 2;
                        _mixerStream = ManagedBass.Mix.BassMix.CreateMixerStream(safeFreq, safeChans, mixerFlags);
                        mixerIsFloat = false;
                        mixerFreq = safeFreq;
                        mixerChans = safeChans;

                        if (_mixerStream == 0)
                        {
                            _logger.LogError($"Failed to create mixer: {Bass.LastError}");
                            Bass.StreamFree(_decodeStream);
                            _decodeStream = 0;
                            return;
                        }
                    }
                }

                BassFlags addFlags = BassFlags.Default;
                if (decodeChans != mixerChans)
                {
                    addFlags |= BassFlags.MixerChanDownMix;
                }

                if (decodeInfo.Frequency != mixerFreq)
                {
                    addFlags |= BassFlags.MixerChanNoRampin;
                }

                // Enable buffering so we can read levels from the source channel
                addFlags |= BassFlags.MixerChanBuffer;

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
                _mixerIsFloat = mixerIsFloat;

                if (CurrentStream == 0)
                {
                    _logger.LogError($"Failed to create stream for playback - {Path.GetFileName(pathToMusic)}: {Bass.LastError}");
                    return;
                }

                // DirectSound: explicitly bind the output stream to the selected device.
                if (_currentMode == OutputMode.DirectSound)
                {
                    int deviceIndex = _currentDevice.Type == OutputDeviceType.DirectSound ? _currentDevice.Index : -1;

                    // Device -1 is the system default/primary device; ChannelSetDevice does not accept -1.
                    if (deviceIndex >= 0)
                    {
                        if (!Bass.ChannelSetDevice(CurrentStream, deviceIndex))
                        {
                            _logger.LogWarning("DirectSound: Bass.ChannelSetDevice failed (DeviceIndex={DeviceIndex}): {Error}", deviceIndex, Bass.LastError);
                        }
                    }
                }

                int lengthStream = _decodeStream != 0 ? _decodeStream : CurrentStream;

                long lengthBytes = Bass.ChannelGetLength(lengthStream);
                if (lengthBytes < 0)
                {
                    _logger.LogError($"Failed to get track length: {Bass.LastError}");
                    Bass.StreamFree(CurrentStream);
                    CurrentStream = 0;
                    return;
                }

                CurrentTrackLength = Bass.ChannelBytes2Seconds(lengthStream, lengthBytes);
                CurrentTrackPosition = 0;

                _logger.LogDebug("Track length set (LengthSeconds={LengthSeconds}, LengthStream={LengthStream}, CurrentStream={CurrentStream}, DecodeStream={DecodeStream}, Mode={Mode})",
                    CurrentTrackLength,
                    lengthStream,
                    CurrentStream,
                    _decodeStream,
                    _currentMode);

                if (EqEnabled)
                {
                    InitializeEqualizer();
                }

                // Set the LoadedTrackPath only after successfully loading the file
                LoadedTrackPath = pathToMusic;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading file: {ex.Message}");
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

    public void Play(string pathToMusic, double position = 0)
    {
        lock (_engineSync)
        {
            _logger.LogDebug("Play() called with path: {Path}, position: {Position}", pathToMusic, position);
            _logger.LogInformation("Play requested (Mode={Mode}, SelectedDeviceType={DeviceType}, SelectedDeviceIndex={DeviceIndex}, SelectedDeviceName={DeviceName}, BassCurrentDevice={BassDevice})",
                _currentMode, _currentDevice.Type, _currentDevice.Index, _currentDevice.Name, Bass.CurrentDevice);

            if (string.IsNullOrEmpty(pathToMusic))
            {
                _logger.LogError("Play called with null or empty pathToMusic");
                return;
            }

            CurrentTrackPosition = 0;
            CurrentTrackLength = 0;

            if (_audioDeviceLost)
            {
                _logger.LogDebug("Resetting audio device lost flag");
                _audioDeviceLost = false;
                _consecutiveAudioErrors = 0;
                _lastAudioError = Errors.OK;
            }

            if (!IsBassInitialized)
            {
                _logger.LogDebug("First play - initializing audio device");
                InitializeAudioDevice();
                if (!IsBassInitialized)
                    return;
            }

            // Fast resume if same track and paused
            PlaybackState currentState = Bass.ChannelIsActive(CurrentStream);
            if (string.Equals(LoadedTrackPath, pathToMusic, StringComparison.OrdinalIgnoreCase) &&
                currentState == PlaybackState.Paused)
            {
                _logger.LogDebug("Fast resume of paused track");
                ResumePlay();
                return;
            }

            _logger.LogDebug("Stopping current playback before loading new track");
            Stop();                    // This is now lighter

            _logger.LogDebug("Loading audio file: {FileName}", Path.GetFileName(pathToMusic));
            LoadAudioFile(pathToMusic);

            if (CurrentStream != 0)
            {
                _logger.LogDebug("CurrentStream is valid, starting playback");

                // Set end-of-track sync
                int syncTargetStream = CurrentStream;
                if (_decodeStream != 0)
                {
                    syncTargetStream = _decodeStream;
                }

                double syncLenSeconds = 0;
                try
                {
                    long lenBytes = Bass.ChannelGetLength(syncTargetStream);
                    syncLenSeconds = Bass.ChannelBytes2Seconds(syncTargetStream, lenBytes);
                }
                catch
                {
                }

                double syncPosSeconds = 0;
                try
                {
                    long posBytes = Bass.ChannelGetPosition(syncTargetStream);
                    syncPosSeconds = Bass.ChannelBytes2Seconds(syncTargetStream, posBytes);
                }
                catch
                {
                }

                PlaybackState syncState = PlaybackState.Stopped;
                try
                {
                    syncState = Bass.ChannelIsActive(syncTargetStream);
                }
                catch
                {
                }

                currentState = PlaybackState.Stopped;
                try
                {
                    currentState = Bass.ChannelIsActive(CurrentStream);
                }
                catch
                {
                }

                PlaybackState decodeState = PlaybackState.Stopped;
                try
                {
                    if (_decodeStream != 0)
                    {
                        decodeState = Bass.ChannelIsActive(_decodeStream);
                    }
                }
                catch
                {
                }

                _logger.LogDebug(
                    "EndSync register (TargetStream={TargetStream}, EndSyncHandle={EndSyncHandle}, CurrentStream={CurrentStream}, DecodeStream={DecodeStream}, MixerStream={MixerStream}, Mode={Mode}, LoadedPath={LoadedPath}, TargetLenSeconds={TargetLenSeconds}, TargetPosSeconds={TargetPosSeconds}, TargetState={TargetState}, CurrentState={CurrentState}, DecodeState={DecodeState})",
                    syncTargetStream,
                    _endSyncHandle,
                    CurrentStream,
                    _decodeStream,
                    _mixerStream,
                    _currentMode,
                    LoadedTrackPath,
                    syncLenSeconds,
                    syncPosSeconds,
                    syncState,
                    currentState,
                    decodeState);

                _endSyncHandle = Bass.ChannelSetSync(syncTargetStream, SyncFlags.End, 0, EndTrackSyncProc);
                if (_endSyncHandle == 0)
                {
                    _logger.LogWarning("Failed to set end-of-track sync (TargetStream={TargetStream}, CurrentStream={CurrentStream}, DecodeStream={DecodeStream}, Mode={Mode}): {Error}",
                        syncTargetStream,
                        CurrentStream,
                        _decodeStream,
                        _currentMode,
                        Bass.LastError);
                }
                else
                {
                    _logger.LogDebug("EndSync registered (TargetStream={TargetStream}, EndSyncHandle={EndSyncHandle})", syncTargetStream, _endSyncHandle);
                }

                // Set position if specified
                if (position > 0)
                {
                    int positionTarget = _decodeStream != 0 ? _decodeStream : CurrentStream;
                    long bytePos = Bass.ChannelSeconds2Bytes(positionTarget, position);
                    Bass.ChannelSetPosition(positionTarget, bytePos);
                }

                Bass.ChannelSetAttribute(CurrentStream, ChannelAttribute.Volume, MusicVolume);

                bool started = _currentMode == OutputMode.DirectSound
                    ? StartDirectSoundPlayback()
                    : StartWasapiPlayback();

                if (started)
                {
                    IsPlaying = true;
                    PathToMusic = pathToMusic;
                    _positionTimer.Start();
                }
            }
        }
    }

    public void Stop()
    {
        lock (_engineSync)
        {
            StopCrossfadeTimer("stop");

            _logger.LogTrace("Stop() entered (Mode={Mode}, CurrentStream={CurrentStream}, DecodeStream={DecodeStream}, MixerStream={MixerStream}, EndSyncHandle={EndSyncHandle}, LoadedPath={LoadedPath}, IsPlaying={IsPlaying})",
                _currentMode,
                CurrentStream,
                _decodeStream,
                _mixerStream,
                _endSyncHandle,
                LoadedTrackPath,
                IsPlaying);

            // Push an immediate zeroed FFT frame so UI drops instantly
            try
            {
                int zeroLen = Math.Max(1, ExpectedFftSize / 2);
                OnFftCalculated?.Invoke(new float[zeroLen]);
            }
            catch { /* ignore UI listeners exceptions */ }

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

            // Ensure UI resets immediately even if a subsequent Play is still loading.
            CurrentTrackPosition = 0;
            if (CurrentStream == 0)
            {
                CurrentTrackLength = 0;
            }

            OnPlaybackStopped?.Invoke();
        }
    }

    private void FreeResources()
    {
        lock (_engineSync)
        {
            _logger.LogTrace("FreeResources() entered (CurrentStream={CurrentStream}, DecodeStream={DecodeStream}, MixerStream={MixerStream}, EndSyncHandle={EndSyncHandle}, LoadedPath={LoadedPath})",
                CurrentStream,
                _decodeStream,
                _mixerStream,
                _endSyncHandle,
                LoadedTrackPath);

            // Free all streams - Bass.StreamFree handles invalid handles gracefully
            Bass.StreamFree(_mixerStream);
            _mixerStream = 0;

            Bass.StreamFree(_decodeStream);
            _decodeStream = 0;

            Bass.StreamFree(CurrentStream);
            CurrentStream = 0;

            // Do not free WASAPI/BASS here. Normal Stop/Pause should keep the device
            // initialized (especially in WASAPI Exclusive) to avoid BASS_ERROR_BUSY
            // on rapid stop/resume cycles.

            _logger.LogTrace("FreeResources() completed (CurrentStream={CurrentStream}, DecodeStream={DecodeStream}, MixerStream={MixerStream}, EndSyncHandle={EndSyncHandle})",
                CurrentStream,
                _decodeStream,
                _mixerStream,
                _endSyncHandle);
        }
    }

    public void Pause()
    {
        lock (_engineSync)
        {
            if (CurrentStream != 0 && IsPlaying)
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
            if (CurrentStream != 0 && !IsPlaying)
            {
                bool resumed = _currentMode == OutputMode.DirectSound
                ? ResumeDirectSound()
                : ResumeWasapi();

                if (!resumed)
                {
                    return;
                }

                IsPlaying = true;

                int syncTargetStream = CurrentStream;
                if (_decodeStream != 0)
                {
                    syncTargetStream = _decodeStream;
                }

                _endSyncHandle = Bass.ChannelSetSync(syncTargetStream, SyncFlags.End, 0, EndTrackSyncProc);
                if (_endSyncHandle == 0)
                {
                    _logger.LogWarning("Failed to set end-of-track sync (TargetStream={TargetStream}): {Error}", syncTargetStream, Bass.LastError);
                }
            }
        }
    }

    private void EndTrackSyncProc(int handle, int channel, int data, IntPtr user)
    {
        try
        {
            double channelLenSeconds = 0;
            double channelPosSeconds = 0;
            try
            {
                long lenBytes = Bass.ChannelGetLength(channel);
                channelLenSeconds = Bass.ChannelBytes2Seconds(channel, lenBytes);
            }
            catch
            {
            }

            try
            {
                long posBytes = Bass.ChannelGetPosition(channel);
                channelPosSeconds = Bass.ChannelBytes2Seconds(channel, posBytes);
            }
            catch
            {
            }

            PlaybackState channelState = PlaybackState.Stopped;
            try
            {
                channelState = Bass.ChannelIsActive(channel);
            }
            catch
            {
            }

            PlaybackState currentState = PlaybackState.Stopped;
            try
            {
                currentState = CurrentStream != 0 ? Bass.ChannelIsActive(CurrentStream) : PlaybackState.Stopped;
            }
            catch
            {
            }

            PlaybackState decodeState = PlaybackState.Stopped;
            try
            {
                decodeState = _decodeStream != 0 ? Bass.ChannelIsActive(_decodeStream) : PlaybackState.Stopped;
            }
            catch
            {
            }

            PlaybackState mixerState = PlaybackState.Stopped;
            try
            {
                mixerState = _mixerStream != 0 ? Bass.ChannelIsActive(_mixerStream) : PlaybackState.Stopped;
            }
            catch
            {
            }

            _logger.LogDebug(
                "EndTrackSyncProc fired (Handle={Handle}, Channel={Channel}, Data={Data}, Mode={Mode}, CurrentStream={CurrentStream}, DecodeStream={DecodeStream}, MixerStream={MixerStream}, LoadedPath={LoadedPath}, WasapiStarted={WasapiStarted}, ChannelState={ChannelState}, CurrentState={CurrentState}, DecodeState={DecodeState}, MixerState={MixerState}, ChannelPosSeconds={ChannelPosSeconds}, ChannelLenSeconds={ChannelLenSeconds})",
                handle,
                channel,
                data,
                _currentMode,
                CurrentStream,
                _decodeStream,
                _mixerStream,
                LoadedTrackPath,
                _currentMode == OutputMode.DirectSound ? (bool?)null : BassWasapi.IsStarted,
                channelState,
                currentState,
                decodeState,
                mixerState,
                channelPosSeconds,
                channelLenSeconds);

            // Never call user code synchronously from a BASS sync callback.
            // The coordinator may call back into AudioEngine (Stop/Play) which can deadlock the BASS callback thread.
            _ = Task.Run(() =>
            {
                try
                {
                    OnTrackEnded?.Invoke();
                }
                catch
                {
                }
            });
        }
        catch
        {
        }
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
            _logger.LogError("Audio device is busy (BASS_ERROR_BUSY). Another application has taken exclusive control of the audio device.");
            if (!_audioDeviceLost)
            {
                MarkDeviceBusyAndNotify("Playback has been stopped.");
            }

            Stop();
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
                _consecutiveAudioErrors = 1;
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
            _consecutiveAudioErrors = 0;
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
            if (CurrentStream != 0)
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

        CleanupEqualizer();

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
            if (CurrentStream == 0)
            {
                _logger.LogError("Cannot seek: Current stream is invalid");
                return;
            }

            if (position < 0 || position > CurrentTrackLength)
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

    // Crossfade implementation has been moved to the partial class file 'AudioEngine.Crossfade.cs'.
    // Do NOT duplicate crossfade fields/methods here to avoid conflicts between partial class definitions.

    public bool TryGetChannelDecibelLevels(out double[] levels)
    {
        const double MinDbValue = -60.0;
        const double MaxDbValue = 10.0;
        levels = Array.Empty<double>();

        // Use BassMix.ChannelGetLevel to read levels from the decode stream
        // This is the correct API for reading levels from source channels feeding a mixer
        if (_decodeStream == 0 || CurrentStream == 0)
            return false;

        int channelCount = ChannelCount;
        if (channelCount < 1)
            return false;

        // For multichannel (>2): use the extended level API to get all channels
        if (channelCount > 2)
        {
            float[] levelEx = new float[channelCount];

            // BASS_Mixer_ChannelGetLevel: reads levels from a mixer source channel
            // 0.02f = 20ms window for level calculation (good balance of responsiveness and smoothness)
            // Returns number of samples processed, or -1 on error
            int result = ManagedBass.Mix.BassMix.ChannelGetLevel(_decodeStream, levelEx, 0.02f, LevelRetrievalFlags.RMS);

            if (result == -1)
            {
                return false;
            }

            levels = new double[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                double linear = levelEx[i];
                double db = linear > 0 ? 20.0 * Math.Log10(linear) : MinDbValue;
                if (db < MinDbValue)
                    db = MinDbValue;
                if (db > MaxDbValue)
                    db = MaxDbValue;
                levels[i] = db;
            }

            return true;
        }
        else
        {
            // Stereo: use the simple integer-based API
            int level = ManagedBass.Mix.BassMix.ChannelGetLevel(_decodeStream);

            if (level == -1)
            {
                return false;
            }

            int left = level & 0xFFFF;
            int right = (level >> 16) & 0xFFFF;
            levels = new double[2];
            levels[0] = left > 0 ? 20.0 * Math.Log10(left / 32768.0) : MinDbValue;
            levels[1] = right > 0 ? 20.0 * Math.Log10(right / 32768.0) : MinDbValue;

            return true;
        }
    }
}
