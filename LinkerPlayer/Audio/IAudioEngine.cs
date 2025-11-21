using System.Collections.Generic;
using LinkerPlayer.Models;

namespace LinkerPlayer.Audio;

// Fully qualify model types to avoid missing using resolution
public interface IAudioEngine : ISpectrumPlayer, System.IDisposable
{
    // Additional core playback info beyond ISpectrumPlayer
    string PathToMusic { get; }
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

    // Extra events (FFT event comes from ISpectrumPlayer)
    event System.Action? OnPlaybackStopped;

    // Visualization helpers (ExpectedFftSize inherited; keep FftUpdate convenience)
    float[] FftUpdate { get; }
    double GetDecibelLevel();
    (double LeftDb, double RightDb) GetStereoDecibelLevels();
    void NextTrackPreStopVisuals();
}
