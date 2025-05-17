using System;
using System.ComponentModel;

namespace LinkerPlayer.Audio;

public interface ISpectrumPlayer : INotifyPropertyChanged
{
    bool IsPlaying { get; }
    double CurrentTrackPosition { get; }
    double CurrentTrackLength { get; }
    event Action<float[]> OnFftCalculated;
    int ExpectedFftSize { get; }
    bool GetFftData(float[] fftDataBuffer);
    int GetFftFrequencyIndex(int frequency);
}