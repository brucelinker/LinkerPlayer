using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.BassLibs;
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
    private readonly ILogger<AudioEngine> _logger;
    [ObservableProperty] private bool _isBassInitialized;
    [ObservableProperty] private int _currentStream;
    [ObservableProperty] private string _pathToMusic = string.Empty;
    [ObservableProperty] private double _currentTrackLength;
    [ObservableProperty] private double _currentTrackPosition;
    [ObservableProperty] private float _musicVolume = 0.5f;
    [ObservableProperty] private bool _isPlaying;

    private OutputMode _currentMode = OutputMode.DirectSound;
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

    public AudioEngine(ILogger<AudioEngine> logger)
    {
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

            _wasapiProc = new WasapiProcedure(WasapiProc);

            _positionTimer = new System.Timers.Timer(50);
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

            // Initialize BASS with simple approach like the working player
            if (!Bass.Init(-1, 44100, DeviceInitFlags.Default))
            {
                _logger.LogError("Failed to initialize BASS: " + Bass.LastError);
                return;
            }

            // Initialize WASAPI directly like the simple player
            try
            {
                if (BassNativeLibraryManager.IsDllAvailable("basswasapi.dll"))
                {
                    BassWasapi.Init(-1, 44100, 2, WasapiInitFlags.Shared, 0.1f, 0, WasapiProc);
                    _wasapiInitialized = true;
                    _logger.LogInformation("WASAPI initialized successfully");
                }
                else
                {
                    _logger.LogWarning("WASAPI DLL not available, skipping WASAPI initialization");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"WASAPI initialization failed: {ex.Message}");
                // Continue without WASAPI
            }

            // Minimal configuration like the simple player
            Bass.UpdatePeriod = 10;

            // Log device info for debugging
            DeviceInfo deviceInfo = Bass.GetDeviceInfo(Bass.CurrentDevice);
            _logger.LogInformation($"Using audio device: {deviceInfo.Name}");

            IsBassInitialized = true;
            _logger.LogInformation("BASS initialized successfully!");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error initializing BASS: {ex.Message}");
        }
    }

    private int WasapiProc(IntPtr buffer, int length, IntPtr user)
    {
        if (CurrentStream == 0)
        {
            _logger.LogDebug("WASAPI proc: No stream, returning 0");
            return 0;
        }

        int data = Bass.ChannelGetData(CurrentStream, buffer, length);
        if (data < 0)
        {
            _logger.LogWarning($"WASAPI proc: Failed to get data: {Bass.LastError}");
            return 0;
        }

        _logger.LogTrace($"WASAPI proc: Returned {data} bytes");
        return data;
    }

    public double GetDecibelLevel()
    {
        int level = Bass.ChannelGetLevel(CurrentStream);
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
        int level = Bass.ChannelGetLevel(CurrentStream);
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

    private void EndTrackSyncProc(int handle, int channel, int data, IntPtr user)
    {
        _logger.LogInformation("Track ended");
        Stop();
    }

    partial void OnMusicVolumeChanged(float value)
    {
        if (CurrentStream != 0)
        {
            Bass.ChannelSetAttribute(CurrentStream, ChannelAttribute.Volume, value);
        }
    }

    partial void OnIsPlayingChanged(bool value)
    {
        if (value)
            _positionTimer.Start();
        else
            _positionTimer.Stop();
    }

    public void LoadAudioFile(string pathToMusic)
    {
        try
        {
            // Free previous stream and clean up EQ
            if (CurrentStream != 0)
            {
                CleanupEqualizer();
                Bass.StreamFree(CurrentStream);
                CurrentStream = 0;
            }

            // Create stream from file
            CurrentStream = Bass.CreateStream(pathToMusic, 0, 0, BassFlags.Default);

            if (CurrentStream == 0)
            {
                _logger.LogError($"Failed to load file: {Bass.LastError}. File: {pathToMusic}");
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
            if (CurrentStream != 0)
            {
                try
                {
                    Bass.StreamFree(CurrentStream);
                }
                catch
                {
                    // Ignore cleanup errors
                }
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
            if (Bass.ChannelPlay(CurrentStream))
            {
                IsPlaying = true;
                PathToMusic = pathToMusic;
            }
            else
            {
                _logger.LogError($"Failed to play stream: {Bass.LastError}");
            }
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
        }

        OnPlaybackStopped?.Invoke();
    }

    public void Pause()
    {
        if (CurrentStream != 0 && IsPlaying)
        {
            if (!Bass.ChannelPause(CurrentStream))
            {
                _logger.LogError($"Failed to pause stream: {Bass.LastError}");
                return;
            }
            IsPlaying = false;
        }
    }

    public void ResumePlay()
    {
        if (CurrentStream != 0 && !IsPlaying)
        {
            if (!Bass.ChannelPlay(CurrentStream))
            {
                _logger.LogError($"Failed to resume stream: {Bass.LastError}");
                return;
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

        PlaybackState playbackState = Bass.ChannelIsActive(CurrentStream);
        bool wasPlaying = playbackState == PlaybackState.Playing;
        if (wasPlaying)
        {
            if (!Bass.ChannelPause(CurrentStream))
            {
                _logger.LogError($"Failed to pause stream before seek: {Bass.LastError}");
            }
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
            if (!Bass.ChannelPlay(CurrentStream))
            {
                _logger.LogError($"Failed to resume stream after seek: {Bass.LastError}");
            }
        }
    }

    public void ChangeOutputMode(OutputMode selectedOutputMode)
    {
        // Stop current playback first
        Stop();

        // Free current resources
        FreeResources();

        // Initialize new mode
        bool success;

        if (selectedOutputMode == OutputMode.DirectSound)
        {
            _currentMode = OutputMode.DirectSound;
            success = Bass.Init(-1, 44100, DeviceInitFlags.DirectSound);
            if (success)
            {
                _logger.LogInformation("Switched to DirectSound mode");
            }
            else
            {
                _logger.LogError($"Failed to initialize DirectSound: {Bass.LastError}");
            }
        }
        else
        {
            // For WASAPI modes, we still need BASS for decoding
            success = Bass.Init(0, 44100, DeviceInitFlags.Default);

            if (success)
            {
                _currentMode = selectedOutputMode;
                _logger.LogInformation($"Switched to {selectedOutputMode} mode");
            }
            else
            {
                _logger.LogError($"Failed to initialize BASS for WASAPI: {Bass.LastError}");
            }
        }

        if (!success)
        {
            _logger.LogError($"Failed to initialize {selectedOutputMode} mode");
            MessageBox.Show($"Failed to initialize {selectedOutputMode} mode", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ReselectOutputDevice(Device device)
    {
        _logger.LogInformation("ReselectOutputDevice called with: {DeviceName}", device.Name);

        if (device.Type != DeviceType.DirectSound)
        {
            _logger.LogWarning("ReselectOutputDevice called for a non-DirectSound device. Ignoring.");
            return;
        }

        int deviceId = device.Index;
        if (deviceId == -1)
        {
            _logger.LogWarning("Invalid device index (-1) for ReselectOutputDevice. Using default.");
        }

        try
        {
            Bass.Free();
            if (!Bass.Init(deviceId))
            {
                _logger.LogError("Failed to initialize BASS with device {DeviceName}: {LastError}", device.Name, Bass.LastError);
                // Try to re-init with default device as a fallback
                Bass.Init(-1);
                return;
            }

            _logger.LogInformation("DirectSound output device set to: {DeviceName}", device.Name);

            //if (!string.IsNullOrEmpty(PathToMusic))
            //{
            //    double position = CurrentTrackPosition;
            //    Stop();
            //    Play(PathToMusic, position);
            //}
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error switching DirectSound device");
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

    #region SpectrumAnalyzer

    public bool GetFftData(float[] fftDataBuffer)
    {
        if (fftDataBuffer.Length != ExpectedFftSize)
        {
            return false;
        }

        if (CurrentStream == 0)
        {
            Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
            return false;
        }

        if (FftUpdate.Length == ExpectedFftSize)
        {
            Array.Copy(FftUpdate, fftDataBuffer, ExpectedFftSize);
            return true;
        }

        int bytesRead = Bass.ChannelGetData(CurrentStream, fftDataBuffer, (int)DataFlags.FFT2048);
        if (bytesRead < 0)
        {
            Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
            return false;
        }

        return true;
    }

    public int GetFftFrequencyIndex(int frequency)
    {
        const int fftSize = 2048;
        const int sampleRate = 44100;
        const float binWidth = sampleRate / (float)fftSize;
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

        int bytesRead = Bass.ChannelGetData(CurrentStream, _fftBuffer, (int)DataFlags.FFT2048);
        if (bytesRead < 0)
        {
            FftUpdate = new float[ExpectedFftSize];
            OnFftCalculated?.Invoke(FftUpdate);
            return;
        }

        int fftSize = _fftBuffer.Length / 2;
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
        if (CurrentStream != 0)
        {
            Bass.StreamFree(CurrentStream);
            CurrentStream = 0;
        }

        if (_wasapiInitialized)
        {
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