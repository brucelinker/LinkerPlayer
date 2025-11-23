using System.Collections.Generic;
using LinkerPlayer.Models;

namespace LinkerPlayer.Audio;

public interface IAudioEngine : ISpectrumPlayer, System.IDisposable
{
    // Path to current file
    string PathToMusic { get; set; }
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
}
