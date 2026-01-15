using LinkerPlayer.Audio;
using LinkerPlayer.Models;
using System.Collections.Generic;
using System.ComponentModel;

namespace LinkerPlayer.Tests.Fakes;

public sealed class FakeAudioEngine : IAudioEngine, IChannelLevelProvider
{
    private bool _disposed;

    private string _loadedTrackPath = string.Empty;

    public string PathToMusic { get; set; } = string.Empty;

    public string LoadedTrackPath => _loadedTrackPath;

    public float MusicVolume { get; set; }

    public bool IsPlaying { get; private set; }

    public double CurrentTrackPosition { get; private set; }

    public double CurrentTrackLength { get; set; }

    public bool EqEnabled { get; set; }

    public bool IsEqualizerInitialized => false;

    public IEnumerable<Device> DirectSoundDevices => new List<Device>();

    public IEnumerable<Device> WasapiDevices => new List<Device>();

    public float[] FftUpdate { get; } = new float[0];

    public int ExpectedFftSize => 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event System.Action<float[]>? OnFftCalculated;

    public event System.Action? OnPlaybackStopped;

    public event System.Action? OnTrackEnded;

    public event System.Action<string>? OnCrossfadeCommitted;

    public string? LastPlayPath { get; private set; }

    public double LastPlayStartSeconds { get; private set; }

    public double? LastSeekSeconds { get; private set; }

    public bool FadeOutAndStopSupported { get; set; } = true;

    public bool CrossfadeSupported { get; set; } = true;

    public string? LastCrossfadeNextPath { get; private set; }

    public int LastCrossfadeFadeOutMs { get; private set; }

    public int LastCrossfadeFadeInMs { get; private set; }

    public FadeCurveShape LastCrossfadeCurveShape { get; private set; }

    public int LastFadeOutAndStopMs { get; private set; }

    public FadeCurveShape LastFadeOutAndStopCurveShape { get; private set; }

    public OutputMode GetCurrentOutputMode() => OutputMode.DirectSound;

    public Device GetCurrentOutputDevice() => default!;

    public void SetOutputMode(OutputMode selectedOutputMode, Device? device)
    {
    }

    public void InitializeAudioDevice()
    {
    }

    public void Play()
    {
        IsPlaying = true;
    }

    public void Play(string pathToMusic, double position = 0)
    {
        LastPlayPath = pathToMusic;
        LastPlayStartSeconds = position;

        _loadedTrackPath = pathToMusic;
        PathToMusic = pathToMusic;
        CurrentTrackPosition = position;

        IsPlaying = true;
    }

    public void Stop()
    {
        IsPlaying = false;
        CurrentTrackPosition = 0;
        RaisePlaybackStopped();
    }

    public void Pause()
    {
        IsPlaying = false;
    }

    public void ResumePlay()
    {
        IsPlaying = true;
    }

    public void SeekAudioFile(double position)
    {
        LastSeekSeconds = position;
        CurrentTrackPosition = position;
    }

    public void StopAndPlayFromPosition(double position)
    {
        CurrentTrackPosition = position;
    }

    public bool TryBeginCrossfade(string nextTrackPath, double nextTrackStartSeconds, int fadeOutMs, int fadeInMs, FadeCurveShape curveShape)
    {
        if (!CrossfadeSupported)
        {
            return false;
        }

        LastCrossfadeNextPath = nextTrackPath;
        LastCrossfadeFadeOutMs = fadeOutMs;
        LastCrossfadeFadeInMs = fadeInMs;
        LastCrossfadeCurveShape = curveShape;

        _loadedTrackPath = nextTrackPath;
        PathToMusic = nextTrackPath;
        CurrentTrackPosition = nextTrackStartSeconds;
        IsPlaying = true;

        return true;
    }

    public bool TryFadeOutAndStop(int fadeOutMs, FadeCurveShape curveShape)
    {
        if (!FadeOutAndStopSupported)
        {
            return false;
        }

        LastFadeOutAndStopMs = fadeOutMs;
        LastFadeOutAndStopCurveShape = curveShape;
        return true;
    }

    public void NextTrackPreStopVisuals()
    {
    }

    public double GetDecibelLevel() => 0;

    public (double LeftDb, double RightDb) GetStereoDecibelLevels() => (0, 0);

    public List<EqualizerBandSettings> GetBandsList() => new List<EqualizerBandSettings>();

    public void SetBandsList(List<EqualizerBandSettings> bands)
    {
    }

    public float GetBandGain(int index) => 0;

    public void SetBandGain(float frequency, float gain)
    {
    }

    public void SetBandGainByIndex(int index, float gain)
    {
    }

    public bool GetFftData(float[] fftDataBuffer) => false;

    public int GetFftFrequencyIndex(int frequency) => 0;

    public void RaisePlaybackStopped() => OnPlaybackStopped?.Invoke();

    public void RaiseTrackEnded() => OnTrackEnded?.Invoke();

    public void RaiseCrossfadeCommitted(string committedPath) => OnCrossfadeCommitted?.Invoke(committedPath);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    public int ChannelCount { get; set; } = 2;

    public bool TryGetChannelDecibelLevels(out double[] levels)
    {
        // For testing, simulate all channels at -10 dB except the first two, which oscillate for visual feedback
        levels = new double[ChannelCount];
        double t = (DateTime.Now.Millisecond % 1000) / 1000.0;
        for (int i = 0; i < ChannelCount; i++)
        {
            if (i == 0)
                levels[i] = -10 + 10 * Math.Sin(2 * Math.PI * t);
            else if (i == 1)
                levels[i] = -10 + 10 * Math.Cos(2 * Math.PI * t);
            else
                levels[i] = -10;
        }
        return true;
    }
}
