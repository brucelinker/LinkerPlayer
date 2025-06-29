using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Models;
using ManagedBass;
using ManagedBass.Fx;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
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

    public bool IsInitialized => IsBassInitialized;

    public event Action? OnPlaybackStopped;
    public event Action<float[]>? OnFftCalculated;

    public bool EqEnabled;
    public bool IsEqualizerInitialized => _eqInitialized;

    public float[] FftUpdate { get; private set; }
    public double NoiseFloorDb { get; set; } = -60;
    public int ExpectedFftSize => 2048;

    public AudioEngine(ILogger<AudioEngine> logger)
    {
        _logger = logger;

        try
        {
            _logger.Log(LogLevel.Information, "Initializing AudioEngine");
            Initialize();

            // Load plugins
            LoadBassPlugins();

            // Configure buffer and update period
            Bass.Configure(Configuration.PlaybackBufferLength, 500);
            Bass.Configure(Configuration.UpdatePeriod, 50);

            _positionTimer = new System.Timers.Timer(50);
            _positionTimer.Elapsed += (_, _) => HandleFftCalculated();
            _positionTimer.AutoReset = true;

            FftUpdate = new float[ExpectedFftSize];
            _logger.Log(LogLevel.Information, "AudioEngine initialized successfully");
        }
        catch (IOException ex)
        {
            _logger.Log(LogLevel.Error, ex, "IO error in AudioEngine constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, ex, "Unexpected error in AudioEngine constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
    }

    public void Initialize()
    {
        if (IsBassInitialized)
        {
            return;
        }

        IsBassInitialized = Bass.Init(1, 48000);
        Log.Information("BASS initialized");

        if (!IsBassInitialized)
        {
            Log.Error("BASS failed to initialize.");
        }
    }

    private static void LoadBassPlugins()
    {
        string basePath = AppDomain.CurrentDomain.BaseDirectory;
        string[] pluginFiles =
        [
            "bass_aac.dll",    // AAC
            "bass_ac3.dll",    // AC3
            "bassalac.dll",    // Apple Lossless
            "bassflac.dll",    // FLAC support
            "bassmidi.dll",    // MIDI
            "bassape.dll",     // Monkey's Audio
            "basswv.dll",      // WavPack
            "basswma.dll",     // WMA
        ];

        foreach (string plugin in pluginFiles)
        {
            string path = Path.Combine(basePath, plugin);
            if (File.Exists(path))
            {
                try
                {
                    int handle = Bass.PluginLoad(path);
                    if (handle != 0)
                        Log.Information($"Loaded plugin: {plugin}");
                    else
                        Log.Error($"Failed to load plugin: {plugin}, Error={Bass.LastError}");
                }
                catch (Exception ex)
                {
                    Log.Error($"Error loading plugin {plugin}: {ex.Message}");
                }
            }
            else
            {
                Log.Error($"Plugin not found: {path}");
            }
        }
    }

    public double GetDecibelLevel()
    {
        int level = Bass.ChannelGetLevel(CurrentStream);
        if (level == -1)
        {
            //Log.Information("Decibel Level: Unknown (no level data)");
            return double.NaN;
        }

        int left = level & 0xFFFF;
        int right = (level >> 16) & 0xFFFF;

        double leftDb = 20 * Math.Log10(left / 32768.0);
        double rightDb = 20 * Math.Log10(right / 32768.0);
        double avgDb = (leftDb + rightDb) / 2.0;
        Log.Information($"Decibel Level: {avgDb}");
        return avgDb;
    }

    private void EndTrackSyncProc(int handle, int channel, int data, IntPtr user)
    {
        Log.Information("EndTrackSyncProc: Track ended, invoking OnPlaybackStopped");
        Stop();
    }

    partial void OnMusicVolumeChanged(float value)
    {
        if (CurrentStream != 0)
        {
            Bass.ChannelSetAttribute(CurrentStream, ChannelAttribute.Volume, value);
            //Log.Information($"Volume set to: {_musicVolume}");
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
        //        Log.Information($"Loading audio file: {pathToMusic} at position {position}");

        CurrentStream = Bass.CreateStream(pathToMusic, Flags: BassFlags.Decode | BassFlags.Prescan | BassFlags.Float);
        if (CurrentStream == 0)
        {
            Log.Error($"Failed to create decode stream: {Bass.LastError}");
            return;
        }

        CurrentStream = BassFx.TempoCreate(CurrentStream, BassFlags.Default);
        if (CurrentStream == 0)
        {
            Log.Error($"Failed to create tempo stream: {Bass.LastError}");
            return;
        }

        long lengthBytes = Bass.ChannelGetLength(CurrentStream);
        if (lengthBytes < 0)
        {
            Log.Error($"Failed to get track length: {Bass.LastError}");
            Bass.StreamFree(CurrentStream);
            CurrentStream = 0;
            _eqFxHandles = [];
            _eqInitialized = false;
            return;
        }
        CurrentTrackLength = Bass.ChannelBytes2Seconds(CurrentStream, lengthBytes);
        Log.Information($"Track length set to {CurrentTrackLength} seconds");

        CurrentTrackPosition = 0;

        //Log.Information($"Successfully loaded audio file: {pathToMusic}");
    }

    public void Play()
    {
        if (string.IsNullOrEmpty(PathToMusic))
        {
            Log.Error("Cannot play: PathToMusic is null or empty");
            return;
        }
        Play(PathToMusic);
    }

    public void Play(string pathToMusic, double position = 0)
    {
        //Log.Information($"Play method called for {pathToMusic} at position {position}");

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
            _endSyncHandle = Bass.ChannelSetSync(CurrentStream, SyncFlags.End, 0, EndTrackSyncProc);
            if (_endSyncHandle == 0)
            {
                Log.Error($"Failed to set end-of-track sync: {Bass.LastError}");
            }

            if (EqEnabled)
            {
                InitializeEqualizer();
                foreach (EqualizerBandSettings band in _equalizerBands)
                {
                    SetBandGain(band.Frequency, band.Gain);
                }
            }

            if (CurrentStream != 0)
            {
                long bytePosition = Bass.ChannelSeconds2Bytes(CurrentStream, position);
                if (Bass.ChannelSetPosition(CurrentStream, bytePosition))
                {
                    Bass.ChannelPlay(CurrentStream);
                }
            }

            if (!Bass.ChannelPlay(CurrentStream))
            {
                Log.Error($"Failed to play stream: {Bass.LastError}");
                return;
            }
            //Log.Information($"Playing stream: {CurrentStream}");
            //var state = Bass.ChannelIsActive(CurrentStream);
            //Log.Information($"Stream state after play: {state}");
            IsPlaying = true;
            //Log.Information("Playback started successfully");

            Bass.ChannelSetAttribute(CurrentStream, ChannelAttribute.Volume, MusicVolume);
        }
        else
        {
            Log.Error("Failed to create stream, cannot play");
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
            Bass.StreamFree(CurrentStream);
            CurrentStream = 0;
            _eqFxHandles = [];
            _eqInitialized = false;
            //Log.Information("Stream stopped and freed");
        }
        IsPlaying = false;
        CurrentTrackPosition = 0;
        //Log.Information("Stop: Invoking OnPlaybackStopped");
        OnPlaybackStopped?.Invoke();
    }

    public void Pause()
    {
        if (CurrentStream != 0 && IsPlaying)
        {
            if (!Bass.ChannelPause(CurrentStream))
            {
                Log.Error($"Failed to pause stream: {Bass.LastError}");
                return;
            }
            IsPlaying = false;
            Log.Information("Stream paused");
        }
    }

    public void ResumePlay()
    {
        if (CurrentStream != 0 && !IsPlaying)
        {
            if (!Bass.ChannelPlay(CurrentStream))
            {
                Log.Error($"Failed to resume stream: {Bass.LastError}");
                return;
            }
            Log.Information("Stream resumed");
            PlaybackState state = Bass.ChannelIsActive(CurrentStream);
            Log.Information($"Stream state after resume: {state}");
            IsPlaying = true;

            _endSyncHandle = Bass.ChannelSetSync(CurrentStream, SyncFlags.End, 0, EndTrackSyncProc);
            if (_endSyncHandle == 0)
            {
                Log.Error($"Failed to set end-of-track sync: {Bass.LastError}");
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
            Log.Error("Cannot seek: Current stream is invalid");
            return;
        }

        if (position < 0 || position > CurrentTrackLength)
        {
            Log.Error($"Invalid seek position {position}: must be between 0 and {CurrentTrackLength} seconds");
            return;
        }

        PlaybackState playbackState = Bass.ChannelIsActive(CurrentStream);
        bool wasPlaying = playbackState == PlaybackState.Playing;
        if (wasPlaying)
        {
            if (!Bass.ChannelPause(CurrentStream))
            {
                Log.Error($"Failed to pause stream before seek: {Bass.LastError}");
            }
        }

        long bytePosition = Bass.ChannelSeconds2Bytes(CurrentStream, position);
        if (bytePosition < 0)
        {
            Log.Error($"Failed to convert position {position} to bytes: {Bass.LastError}");
            return;
        }

        if (!Bass.ChannelSetPosition(CurrentStream, bytePosition))
        {
            Log.Error($"Failed to seek to position {position}: {Bass.LastError}");
            return;
        }

        double actualPosition = Bass.ChannelBytes2Seconds(CurrentStream, Bass.ChannelGetPosition(CurrentStream));
        if (double.IsNaN(actualPosition) || actualPosition < 0)
        {
            Log.Error($"Invalid position after seek: {actualPosition}");
            return;
        }

        CurrentTrackPosition = actualPosition;

        if (wasPlaying)
        {
            if (!Bass.ChannelPlay(CurrentStream))
            {
                Log.Error($"Failed to resume stream after seek: {Bass.LastError}");
            }
        }
    }

    public void ReselectOutputDevice(string deviceName)
    {
        int deviceId = -1;
        for (int i = 0; ; i++)
        {
            DeviceInfo device = Bass.GetDeviceInfo(i);
            if (string.IsNullOrEmpty(device.Name)) break;
            if (device.Name == deviceName)
            {
                deviceId = i;
                break;
            }
        }

        Bass.Free();
        if (!Bass.Init(deviceId)) // Default args 44100, DeviceInitFlags.Default))
        {
            Log.Error($"Failed to initialize BASS with device {deviceName}: {Bass.LastError}");
            return;
        }

        Log.Information($"Output device set to: {deviceName}");

        if (!string.IsNullOrEmpty(PathToMusic))
        {
            double position = CurrentTrackPosition;
            Stop();
            Play(PathToMusic, position);
        }
    }

    public bool InitializeEqualizer()
    {
        _eqInitialized = false;

        if (CurrentStream == 0)
        {
            //Log.Error("Cannot initialize equalizer: No stream loaded");
            return false;
        }

        if (_eqFxHandles.Length > 0)
        {
            foreach (int fxHandle in _eqFxHandles)
            {
                if (fxHandle != 0)
                    Bass.ChannelRemoveFX(CurrentStream, fxHandle);
            }
        }

        _eqFxHandles = new int[_equalizerBands.Count];
        for (int i = 0; i < _equalizerBands.Count; i++)
        {
            float freq = _equalizerBands[i].Frequency;
            int fxHandle = Bass.ChannelSetFX(CurrentStream, EffectType.PeakEQ, 0);
            if (fxHandle == 0)
            {
                Log.Error($"Failed to set FX for {freq}Hz: {Bass.LastError}");
                continue;
            }

            _eqFxHandles[i] = fxHandle;

            PeakEQParameters eqParams = new()
            {
                fCenter = freq,
                fGain = _equalizerBands[i].Gain,
                fBandwidth = _equalizerBands[i].Bandwidth,
                lChannel = FXChannelFlags.All
            };

            if (!Bass.FXSetParameters(fxHandle, eqParams))
            {
                Log.Error($"Failed to set EQ params for {freq}Hz: {Bass.LastError}");
                Bass.ChannelRemoveFX(CurrentStream, fxHandle);
                _eqFxHandles[i] = 0;
            }

            //Log.Information($"EQ band {freq} Hz initialized (gain={eqParams.fGain} dB)");
        }

        _eqInitialized = true;

        return true;
    }

    public List<EqualizerBandSettings> GetBandsList()
    {
        return new List<EqualizerBandSettings>(_equalizerBands);
    }

    public void SetBandsList(List<EqualizerBandSettings> bands)
    {
        _equalizerBands.Clear();
        _equalizerBands.AddRange(bands);
        InitializeEqualizer();
    }

    public float GetBandGain(int index)
    {
        EqualizerBandSettings band = _equalizerBands[index];
        return band.Gain;
    }

    public void SetBandGainByIndex(int index, float gain)
    {
        SetBandGain(_equalizerBands[index].Frequency, gain);
    }

    public void SetBandGain(float frequency, float gain)
    {
        if (!_eqInitialized || _eqFxHandles.Length == 0 || CurrentStream == 0)
        {
            Log.Warning("SetBandGain skipped: EQ not initialized or stream invalid.");
            return;
        }

        int bandIndex = _equalizerBands.FindIndex(b => Math.Abs(b.Frequency - frequency) < .1);
        if (bandIndex == -1 || bandIndex >= _eqFxHandles.Length || _eqFxHandles[bandIndex] == 0)
        {
            Log.Warning($"SetBandGain skipped: FX handle invalid for {frequency} Hz");
            return;
        }

        PeakEQParameters eqParams = new()
        {
            fCenter = frequency,
            fBandwidth = _equalizerBands[bandIndex].Bandwidth,
            fGain = Math.Clamp(gain, -12f, 12f),
            lChannel = FXChannelFlags.All
        };

        if (!Bass.FXSetParameters(_eqFxHandles[bandIndex], eqParams))
        {
            Log.Error($"Failed to update EQ gain for {frequency} Hz: {Bass.LastError}");
            return;
        }

        _equalizerBands[bandIndex].Gain = eqParams.fGain;
        //Log.Information($"EQ gain set: {frequency} Hz → {eqParams.fGain} dB");
    }

    public bool GetFftData(float[] fftDataBuffer)
    {
        if (fftDataBuffer.Length != ExpectedFftSize)
        {
            Log.Error($"GetFftData: Buffer size {fftDataBuffer.Length} does not match expected {ExpectedFftSize}");
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
            Log.Error($"GetFftData: Failed to get FFT data: {Bass.LastError}");
            Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
            return false;
        }

        return true;
    }

    public int GetFftFrequencyIndex(int frequency)
    {
        const int fftSize = 2048;
        const int sampleRate = 44100;
        float binWidth = sampleRate / (float)fftSize;
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
                Log.Information("HandleFftCalculated: Stream not playing, skipping FFT");
                return;
            }
        }
        else
        {
            Log.Information("HandleFftCalculated: No stream, skipping FFT");
            return;
        }

        int bytesRead = Bass.ChannelGetData(CurrentStream, _fftBuffer, (int)DataFlags.FFT2048);
        if (bytesRead < 0)
        {
            Log.Error($"HandleFftCalculated: Failed to get FFT data: {Bass.LastError}");
            FftUpdate = new float[ExpectedFftSize];
            OnFftCalculated!.Invoke(FftUpdate);
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

        //Log.Information($"FFT data sample: {string.Join(", ", fftResult.Take(10))}");

        OnFftCalculated!.Invoke(FftUpdate);
    }

    public void Dispose()
    {
        Stop();
        Bass.Free();
        _positionTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}