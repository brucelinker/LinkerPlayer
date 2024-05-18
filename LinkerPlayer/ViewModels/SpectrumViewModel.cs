using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Messages;
using System;

namespace LinkerPlayer.ViewModels;

public class SpectrumViewModel : ObservableObject
{
    public readonly AudioEngine audioEngine;

    private double[] _frequencies = Array.Empty<double>();
    private int[] _frequencyBins = Array.Empty<int>();
    private const double ConstMaxEqGain = 30;
    private const double ConstMinDbValue = -60;

    public SpectrumViewModel()
    {
        audioEngine = AudioEngine.Instance;

        Prepare();

        WeakReferenceMessenger.Default.Register<EnginePropertyChangedMessage>(this, (_, m) =>
        {
            EnginePropertyChanged(m.Value);
        });
    }

    public void Prepare()
    {
        PrepareSpectrumAnalyzer();

        MinEqGain = -ConstMaxEqGain;
        MaxEqGain = ConstMaxEqGain;
        EqMinimumDb = ConstMinDbValue;
    }

    private bool _loading;
    public bool IsLoading
    {
        get => _loading;
        set
        {
            _loading = value;
            OnPropertyChanged();
        }
    }
    
    private double _splLeft;
    public double SoundChannelLeft
    {
        get => _splLeft;
        set
        {
            _splLeft = value;
            OnPropertyChanged();
        }
    }
    private double _splRight;
    public double SoundChannelRight
    {
        get => _splRight;
        set
        {
            _splRight = value;
            OnPropertyChanged();
        }
    }

    private double _minEq;
    public double MinEqGain
    {
        get => _minEq;
        set
        {
            _minEq = value;
            OnPropertyChanged();
        }
    }

    private double _maxEq;
    public double MaxEqGain
    {
        get => _maxEq;
        set
        {
            _maxEq = value;
            OnPropertyChanged();
        }
    }

    private double[] _eqFrequencyMagnitudes = Array.Empty<double>();
    public double[] EqFrequencyMagnitudes
    {
        get => _eqFrequencyMagnitudes;
        set
        {
            _eqFrequencyMagnitudes = value;
            OnPropertyChanged();
        }
    }
    
    private double _eqMinimumDb;
    public double EqMinimumDb
    {
        get => _eqMinimumDb;
        set
        {
            _eqMinimumDb = value;
            OnPropertyChanged();
        }
    }
    
    private void EnginePropertyChanged(string property)
    {
        switch (property)
        {
            case "TrackLoaded":
                {
                    PrepareFrequencyBinIndexes();
                    IsLoading = false;
                    break;
                }
            case "SoundLevel":
                {
                    var vol = audioEngine.SoundLevel;
                    SoundChannelLeft = vol[0];
                    SoundChannelRight = vol[1];
                    break;
                }
            case "FFTUpdate":
                {
                    DisplaySpectrum(audioEngine.FftUpdate);
                    break;
                }
        }
    }
    
    private void DisplaySpectrum(double[] fftArray)
    {
        double[] intensities = new double[_frequencyBins.Length];

        // currently we display 19 frequencies from 25 to 20k
        for (var i = 0; i < _frequencyBins.Length; i++)
        {
            // decibels for the frequency bin
            intensities[i] = 10 * Math.Log10(fftArray[_frequencyBins[i]]);
        }

        EqFrequencyMagnitudes = intensities;

        audioEngine.OnAudioActivity(EqFrequencyMagnitudes);
    }
 
    private void PrepareFrequencyBinIndexes()
    {
        _frequencyBins = new int[_frequencies.Length];
        for (var i = 0; i < _frequencies.Length; i++)
        {
            _frequencyBins[i] = audioEngine.FrequencyBinIndex(_frequencies[i]);
        }
    }

    private void PrepareSpectrumAnalyzer()
    {
        _frequencies = new[] { 25, 37.5, 50, 75, 100, 150, 200, 350, 500, 750, 1000, 1500, 2000, 3500, 5000, 7500, 10000, 15000, 20000 };
    }
}