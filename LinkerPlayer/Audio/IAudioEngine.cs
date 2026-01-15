using System.Collections.Generic;
using LinkerPlayer.Models;

namespace LinkerPlayer.Audio;

public enum FadeCurveShape
{
    Linear = 0,
    Cosine = 1,
    Sine = 2,
    Logarithmic = 3
}

public interface IAudioEngine : ISpectrumPlayer, System.IDisposable
{
    // Path to current file
    string PathToMusic { get; set; }
    string LoadedTrackPath { get; }
    float MusicVolume { get; set; }

    // Device / mode
    OutputMode GetCurrentOutputMode();
    Device GetCurrentOutputDevice();
    void SetOutputMode(OutputMode selectedOutputMode, Device? device);
    void InitializeAudioDevice();
    IEnumerable<Device> DirectSoundDevices { get; }
    IEnumerable<Device> WasapiDevices { get; }

    // Playback control
    void Play();
    void Play(string pathToMusic, double position = 0);
    void Stop();
    void Pause();
    void ResumePlay();
    void SeekAudioFile(double position);
    void StopAndPlayFromPosition(double position);

    // Events
    event System.Action? OnPlaybackStopped;
    event System.Action? OnTrackEnded;

    event System.Action<string>? OnCrossfadeCommitted;

    // Visualization helpers
    float[] FftUpdate { get; }
    double GetDecibelLevel();
    (double LeftDb, double RightDb) GetStereoDecibelLevels();
    void NextTrackPreStopVisuals();

    // Equalizer API
    bool EqEnabled { get; set; }
    bool IsEqualizerInitialized { get; }
    List<EqualizerBandSettings> GetBandsList();
    void SetBandsList(List<EqualizerBandSettings> bands);
    float GetBandGain(int index);
    void SetBandGain(float frequency, float gain);
    void SetBandGainByIndex(int index, float gain);

    bool TryBeginCrossfade(string nextTrackPath, double nextTrackStartSeconds, int fadeOutMs, int fadeInMs, FadeCurveShape curveShape);
    bool TryFadeOutAndStop(int fadeOutMs, FadeCurveShape curveShape);

    // Number of output channels (e.g., 2=stereo, 6=5.1, 8=7.1)
    int ChannelCount { get; }
}
