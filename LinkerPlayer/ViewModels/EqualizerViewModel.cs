using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Audio;

namespace LinkerPlayer.ViewModels;

public partial class EqualizerViewModel : ObservableObject
{
    private readonly AudioEngine _audioEngine;

    public EqualizerViewModel()
    {
        _audioEngine = AudioEngine.Instance;
    }

    public readonly float MinimumGain = -12;
    public readonly float MaximumGain = 12;

    [ObservableProperty] private float _band0;
    [ObservableProperty] private float _band1;
    [ObservableProperty] private float _band2;
    [ObservableProperty] private float _band3;
    [ObservableProperty] private float _band4;
    [ObservableProperty] private float _band5;
    [ObservableProperty] private float _band6;
    [ObservableProperty] private float _band7;
    [ObservableProperty] private float _band8;
    [ObservableProperty] private float _band9;

    partial void OnBand0Changed(float value) { _audioEngine.SetBandGain(0, value); }
    partial void OnBand1Changed(float value) { _audioEngine.SetBandGain(1, value); }
    partial void OnBand2Changed(float value) { _audioEngine.SetBandGain(2, value); }
    partial void OnBand3Changed(float value) { _audioEngine.SetBandGain(3, value); }
    partial void OnBand4Changed(float value) { _audioEngine.SetBandGain(4, value); }
    partial void OnBand5Changed(float value) { _audioEngine.SetBandGain(5, value); }
    partial void OnBand6Changed(float value) { _audioEngine.SetBandGain(6, value); }
    partial void OnBand7Changed(float value) { _audioEngine.SetBandGain(7, value); }
    partial void OnBand8Changed(float value) { _audioEngine.SetBandGain(8, value); }
    partial void OnBand9Changed(float value) { _audioEngine.SetBandGain(9, value); }
}