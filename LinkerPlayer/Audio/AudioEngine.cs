using LinkerPlayer.Models;
using ManagedBass;
using ManagedBass.Fx;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
// ReSharper disable InconsistentNaming

namespace LinkerPlayer.Audio;

public class AudioEngine : ISpectrumPlayer, IDisposable
{
    private static readonly Lazy<AudioEngine> _instance = new(() => new AudioEngine(), isThreadSafe: true);
    private static bool _isBassInitialized;
    private static readonly Lock _initLock = new();
    private int _currentStream;
    private string _pathToMusic = string.Empty;
    private double _currentTrackLength;
    private double _currentTrackPosition;
    private bool _isPlaying;
    private float _musicVolume = 1.0f;
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

    public static AudioEngine Instance => _instance.Value;

    // ReSharper disable once InconsistentlySynchronizedField
    public static bool IsInitialized => _isBassInitialized;

    public event Action OnPlaybackStopped;
    public event Action<float[]> OnFftCalculated;
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsEqualizerInitialized => _eqInitialized;

    public float[] FftUpdate { get; private set; }
    public double NoiseFloorDb { get; set; } = -60;
    public int ExpectedFftSize => 2048;

        private AudioEngine()
    {
        Initialize();

        // Load plugins
        LoadBassPlugins();

        // Configure buffer and update period
        Bass.Configure(Configuration.PlaybackBufferLength, 500);
        Bass.Configure(Configuration.UpdatePeriod, 50);

        // Log BASS and BASS_FX versions
        Log.Information($"BASS version: {Bass.Version}");
        Log.Information($"BASS_FX version: {BassFx.Version}");

        var info = Bass.Info;
        Log.Information($"Actual device sample rate: {info.SampleRate} Hz");
        if (info.SampleRate != 44100)
        {
            Log.Warning($"Device sample rate ({info.SampleRate} Hz) does not match requested sample rate (44100 Hz)");
        }

        _positionTimer = new System.Timers.Timer(50);
        _positionTimer.Elapsed += (_, _) => HandleFftCalculated();
        _positionTimer.AutoReset = true;

        FftUpdate = new float[ExpectedFftSize];
    }

    public string PathToMusic
    {
        get => _pathToMusic;
        set
        {
            _pathToMusic = value;
            OnPropertyChanged(nameof(PathToMusic));
        }
    }

    public double CurrentTrackLength
    {
        get => _currentTrackLength;
        private set
        {
            _currentTrackLength = value;
            OnPropertyChanged(nameof(CurrentTrackLength));
        }
    }

    public double CurrentTrackPosition
    {
        get => _currentTrackPosition;
        set
        {
            _currentTrackPosition = value;
            OnPropertyChanged(nameof(CurrentTrackPosition));
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            _isPlaying = value;
            //Log.Information($"IsPlaying changed to: {_isPlaying}, PositionTimer enabled: {_isPlaying}");
            if (_isPlaying)
                _positionTimer.Start();
            else
                _positionTimer.Stop();
            OnPropertyChanged(nameof(IsPlaying));
        }
    }

    public float MusicVolume
    {
        get => _musicVolume;
        set
        {
            _musicVolume = Math.Clamp(value, 0.0f, 1.0f);
            if (_currentStream != 0)
            {
                Bass.ChannelSetAttribute(_currentStream, ChannelAttribute.Volume, _musicVolume);
                Log.Information($"Volume set to: {_musicVolume}");
            }
        }
    }

    public static void Initialize()
    {
        lock (_initLock)
        {
            if (_isBassInitialized)
            {
                Log.Information("BASS already initialized (flag set), skipping");
                return;
            }

            if (Bass.CurrentDevice != -1)
            {
                Log.Information("BASS already initialized (CurrentDevice set), skipping");
                _isBassInitialized = true;
                return;
            }

            try
            {
                if (!Bass.Init(-1, 48000)) // default args DeviceInitFlags.Default))
                {
                    string errorMessage = $"BASS initialization failed: {Bass.LastError}";
                    Log.Error(errorMessage);
                    throw new BassException(Bass.LastError);
                }
                Log.Information("BASS initialized");
                _isBassInitialized = true;
            }
            catch (BassException ex) when (ex.ErrorCode == Errors.Already)
            {
                Log.Information("BASS already initialized (caught Errors.Already), continuing");
                _isBassInitialized = true;
            }
        }
    }

    private void LoadBassPlugins()
    {
        string basePath = AppDomain.CurrentDomain.BaseDirectory;
        string[] pluginFiles = new[]
        {
            "bassflac.dll",    // FLAC support
            "bassalac.dll",    // Apple Lossless
            "basswv.dll",      // WavPack
            "basswma.dll",     // WMA
            "bassmidi.dll",    // MIDI
            "bass_aac.dll",    // AAC
            "bass_ac3.dll",    // AC3
            "bassape.dll"      // Monkey's Audio
        };

        foreach (string plugin in pluginFiles)
        {
            string path = Path.Combine(basePath, plugin);
            if (File.Exists(path))
            {
                int handle = Bass.PluginLoad(path);
                if (handle != 0)
                    Log.Information($"Loaded plugin: {plugin}");
                else
                    Log.Error($"Failed to load plugin: {plugin}, Error={Bass.LastError}");
            }
            else
            {
                Log.Error($"Plugin not found: {path}");
            }
        }
    }

    public double GetDecibelLevel()
    {
        int level = Bass.ChannelGetLevel(_currentStream);
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

    public void LoadAudioFile(string pathToMusic, double position = 0)
    {
        Log.Information($"Loading audio file: {pathToMusic} at position {position}");

        _currentStream = Bass.CreateStream(pathToMusic, Flags: BassFlags.Decode | BassFlags.Prescan | BassFlags.Float);
        if (_currentStream == 0)
        {
            Log.Error($"Failed to create decode stream: {Bass.LastError}");
            return;
        }

        _currentStream = BassFx.TempoCreate(_currentStream, BassFlags.Default);
        if (_currentStream == 0)
        {
            Log.Error($"Failed to create tempo stream: {Bass.LastError}");
            return;
        }

        //var info = Bass.ChannelGetInfo(_currentStream);
        //Log.Information($"Stream format: flags={info.Flags}, type={info.ChannelType}, freq={info.Frequency}");
        //Log.Information($"Stream type: {info.ChannelType}");

        long lengthBytes = Bass.ChannelGetLength(_currentStream);
        if (lengthBytes < 0)
        {
            Log.Error($"Failed to get track length: {Bass.LastError}");
            Bass.StreamFree(_currentStream);
            _currentStream = 0;
            _eqFxHandles = [];
            _eqInitialized = false;
            return;
        }
        CurrentTrackLength = Bass.ChannelBytes2Seconds(_currentStream, lengthBytes);
        Log.Information($"Track length set to {CurrentTrackLength} seconds");

        CurrentTrackPosition = 0;
        SeekAudioFile(position);

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

        var playbackState = Bass.ChannelIsActive(_currentStream);
        if (!string.IsNullOrEmpty(pathToMusic) && playbackState == PlaybackState.Paused)
        {
            ResumePlay();
            return;
        }

        //Log.Information($"Track changed or no stream loaded, stopping current stream and loading: {pathToMusic}");
        Stop();

        LoadAudioFile(pathToMusic, position);

        if (_currentStream != 0)
        {
            _endSyncHandle = Bass.ChannelSetSync(_currentStream, SyncFlags.End, 0, EndTrackSyncProc);
            if (_endSyncHandle == 0)
            {
                Log.Error($"Failed to set end-of-track sync: {Bass.LastError}");
            }

            InitializeEqualizer();
            foreach (var band in _equalizerBands)
            {
                SetBandGain(band.Frequency, band.Gain);
            }

            if (!Bass.ChannelPlay(_currentStream))
            {
                Log.Error($"Failed to play stream: {Bass.LastError}");
                return;
            }
            //Log.Information($"Playing stream: {_currentStream}");
            //var state = Bass.ChannelIsActive(_currentStream);
            //Log.Information($"Stream state after play: {state}");
            IsPlaying = true;
            //Log.Information("Playback started successfully");

            Bass.ChannelSetAttribute(_currentStream, ChannelAttribute.Volume, _musicVolume);
        }
        else
        {
            Log.Error("Failed to create stream, cannot play");
        }
    }

    public void Stop()
    {
        if (_currentStream != 0)
        {
            if (_endSyncHandle != 0)
            {
                Bass.ChannelRemoveSync(_currentStream, _endSyncHandle);
                _endSyncHandle = 0;
            }

            Bass.ChannelStop(_currentStream);
            Bass.StreamFree(_currentStream);
            _currentStream = 0;
            _eqFxHandles = [];
            _eqInitialized = false;
            //Log.Information("Stream stopped and freed");
        }
        IsPlaying = false;
        CurrentTrackPosition = 0;
        //Log.Information("Stop: Invoking OnPlaybackStopped");
        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        OnPlaybackStopped?.Invoke();
    }

    public void Pause()
    {
        if (_currentStream != 0 && IsPlaying)
        {
            if (!Bass.ChannelPause(_currentStream))
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
        if (_currentStream != 0 && !IsPlaying)
        {
            if (!Bass.ChannelPlay(_currentStream))
            {
                Log.Error($"Failed to resume stream: {Bass.LastError}");
                return;
            }
            Log.Information("Stream resumed");
            var state = Bass.ChannelIsActive(_currentStream);
            Log.Information($"Stream state after resume: {state}");
            IsPlaying = true;

            _endSyncHandle = Bass.ChannelSetSync(_currentStream, SyncFlags.End, 0, EndTrackSyncProc);
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
        if (_currentStream == 0)
        {
            Log.Error("Cannot seek: Current stream is invalid");
            return;
        }

        if (position < 0 || position > CurrentTrackLength)
        {
            Log.Error($"Invalid seek position {position}: must be between 0 and {CurrentTrackLength} seconds");
            return;
        }

        var playbackState = Bass.ChannelIsActive(_currentStream);
        bool wasPlaying = playbackState == PlaybackState.Playing;
        if (wasPlaying)
        {
            if (!Bass.ChannelPause(_currentStream))
            {
                Log.Error($"Failed to pause stream before seek: {Bass.LastError}");
            }
            //else
            //{
            //Log.Information("Paused stream before seek");
            //}
        }

        long bytePosition = Bass.ChannelSeconds2Bytes(_currentStream, position);
        if (bytePosition < 0)
        {
            Log.Error($"Failed to convert position {position} to bytes: {Bass.LastError}");
            return;
        }

        if (!Bass.ChannelSetPosition(_currentStream, bytePosition))
        {
            Log.Error($"Failed to seek to position {position}: {Bass.LastError}");
            return;
        }

        double actualPosition = Bass.ChannelBytes2Seconds(_currentStream, Bass.ChannelGetPosition(_currentStream));
        if (double.IsNaN(actualPosition) || actualPosition < 0)
        {
            Log.Error($"Invalid position after seek: {actualPosition}");
            return;
        }
        //Log.Information($"Actual position after seek: {actualPosition:F2}s");

        CurrentTrackPosition = actualPosition;

        if (wasPlaying)
        {
            if (!Bass.ChannelPlay(_currentStream))
            {
                Log.Error($"Failed to resume stream after seek: {Bass.LastError}");
            }
            //Log.Information("Resumed stream after seek");
        }

        //Log.Information($"Seeked to position {actualPosition:F2}s");
    }

    public void ReselectOutputDevice(string deviceName)
    {
        int deviceId = -1;
        for (int i = 0; ; i++)
        {
            var device = Bass.GetDeviceInfo(i);
            if (string.IsNullOrEmpty(device.Name)) break;
            if (device.Name == deviceName)
            {
                deviceId = i;
                break;
            }
        }

        if (deviceId == -1)
        {
            Log.Error($"Output device not found: {deviceName}");
            return;
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

    public void InitializeEqualizer()
    {
        _eqInitialized = false;

        if (_currentStream == 0)
        {
            Log.Error("Cannot initialize equalizer: No stream loaded");
            return;
        }

        if (_eqFxHandles.Length > 0)
        {
            foreach (int fxHandle in _eqFxHandles)
            {
                if (fxHandle != 0)
                    Bass.ChannelRemoveFX(_currentStream, fxHandle);
            }
        }

        _eqFxHandles = new int[_equalizerBands.Count];
        for (int i = 0; i < _equalizerBands.Count; i++)
        {
            float freq = _equalizerBands[i].Frequency;
            int fxHandle = Bass.ChannelSetFX(_currentStream, EffectType.PeakEQ, 0);
            if (fxHandle == 0)
            {
                Log.Error($"Failed to set FX for {freq}Hz: {Bass.LastError}");
                continue;
            }

            _eqFxHandles[i] = fxHandle;

            var eqParams = new PeakEQParameters
            {
                fCenter = freq,
                fGain = _equalizerBands[i].Gain,
                fBandwidth = _equalizerBands[i].Bandwidth,
                lChannel = FXChannelFlags.All
            };

            if (!Bass.FXSetParameters(fxHandle, eqParams))
            {
                Log.Error($"Failed to set EQ params for {freq}Hz: {Bass.LastError}");
                Bass.ChannelRemoveFX(_currentStream, fxHandle);
                _eqFxHandles[i] = 0;
            }

            //Log.Information($"EQ band {freq} Hz initialized (gain={eqParams.fGain} dB)");
        }

        _eqInitialized = true;
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

    public void SetBandGain(int index, float gain)
    {
        SetBandGain(_equalizerBands[index].Frequency, gain);
    }

    public void SetBandGain(float frequency, float gain)
    {
        if (!_eqInitialized || !_eqFxHandles.Any() || _currentStream == 0)
        {
            Log.Warning($"SetBandGain skipped: EQ not initialized or stream invalid.");
            return;
        }

        int bandIndex = _equalizerBands.FindIndex(b => Math.Abs(b.Frequency - frequency) < .1);
        if (bandIndex == -1 || bandIndex >= _eqFxHandles.Length || _eqFxHandles[bandIndex] == 0)
        {
            Log.Warning($"SetBandGain skipped: FX handle invalid for {frequency} Hz");
            return;
        }

        var eqParams = new PeakEQParameters
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

        if (_currentStream == 0)
        {
            Array.Clear(fftDataBuffer, 0, fftDataBuffer.Length);
            return false;
        }

        if (FftUpdate.Length == ExpectedFftSize)
        {
            Array.Copy(FftUpdate, fftDataBuffer, ExpectedFftSize);
            return true;
        }

        int bytesRead = Bass.ChannelGetData(_currentStream, fftDataBuffer, (int)DataFlags.FFT2048);
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
        if (_currentStream != 0)
        {
            double positionSeconds = Bass.ChannelBytes2Seconds(_currentStream, Bass.ChannelGetPosition(_currentStream));
            if (!double.IsNaN(positionSeconds) && positionSeconds >= 0)
            {
                CurrentTrackPosition = positionSeconds;
            }

            var state = Bass.ChannelIsActive(_currentStream);
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

        int bytesRead = Bass.ChannelGetData(_currentStream, _fftBuffer, (int)DataFlags.FFT2048);
        if (bytesRead < 0)
        {
            Log.Error($"HandleFftCalculated: Failed to get FFT data: {Bass.LastError}");
            FftUpdate = new float[ExpectedFftSize];
            OnFftCalculated.Invoke(FftUpdate);
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

        OnFftCalculated.Invoke(FftUpdate);
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        Stop();
        Bass.Free();
        _positionTimer.Dispose();
    }
}