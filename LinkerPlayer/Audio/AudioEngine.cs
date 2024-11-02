using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
using NAudio.Dsp;
using NAudio.Extras;
using NAudio.Wave;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace LinkerPlayer.Audio;

public class AudioEngine : ObservableObject, ISpectrumPlayer, IDisposable
{
    public static AudioEngine Instance { get; } = new();
    public event EventHandler<StoppedEventArgs>? StoppedEvent;

    private readonly DispatcherTimer _positionTimer = new(DispatcherPriority.ApplicationIdle);
    private AudioFileReader? _audioFile;
    private const int FftDataSize = (int)SpectrumAnalyzer.FftDataSize.Fft2048;
    private string? _pathToMusic;
    private Equalizer? _equalizer;
    private EqualizerBand[]? _bands;
    private SampleAggregator? _aggregator;
    private bool _canPlay;
    private bool _canPause;
    private bool _canStop;
    //private bool _isPlaying;
    private WaveOut? _outputDevice;

    private AudioEngine()
    {
        string mainOutputDevice = Properties.Settings.Default.MainOutputDevice;

        if (string.IsNullOrWhiteSpace(mainOutputDevice))
        {
            Log.Error("Device name can`t be null");
        }
        else
        {
            _positionTimer.Interval = TimeSpan.FromMilliseconds(50);

            SelectOutputDevice(mainOutputDevice);

            _musicVolume = (float)Properties.Settings.Default.VolumeSliderValue;

            CreateEqBands();

            this.PropertyChanged += OnPropertyChanged;
            StoppedEvent += PlaybackStopped;

            IsPlaying = false;
            CanStop = false;
            CanPlay = true;
            CanPause = false;
        }

        WeakReferenceMessenger.Default.Register<EqualizerIsOnMessage>(this, (_, m) =>
        {
            OnEqualizerIsOnMessage(m.Value);
        });

    }

    public float OutputDeviceVolume
    {
        get => _outputDevice?.Volume ?? 0f;
        set
        {
            if (value is < 0f or > 1f)
            {
                if (value < 0)
                {
                    if (_outputDevice != null) _outputDevice.Volume = 0f;
                }
                else
                {
                    if (_outputDevice != null) _outputDevice.Volume = 1f;
                }
            }
            else
            {
                if (_outputDevice != null) _outputDevice.Volume = value;
            }
        }
    }

    private float _musicVolume;
    public float MusicVolume
    {
        get
        {
            if (_audioFile != null)
            {
                return _audioFile.Volume;
            }

            return _musicVolume;
        }
        set
        {
            if (value is < 0f or > 1f)
            {
                _musicVolume = value < 0 ? 0f : 1f;
            }
            else
            {
                _musicVolume = value;
            }

            if (_audioFile != null)
            {
                _audioFile.Volume = _musicVolume;
            }
        }
    }

    public bool EqIsOn { get; set; }
    public bool EqSwitched { get; set; }

    public bool IsPaused => _outputDevice is { PlaybackState: PlaybackState.Paused };
    public bool IsStopped => _outputDevice is { PlaybackState: PlaybackState.Stopped };

    public void SelectOutputDevice(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            throw new ArgumentNullException(nameof(deviceName), "OutputDeviceManager cannot be null.");
        }

        _outputDevice = new WaveOut
        {
            DeviceNumber = OutputDeviceManager.GetOutputDeviceId(deviceName),
            DesiredLatency = 200
        };

        _outputDevice.PlaybackStopped += PlaybackStopped;
    }

    public void ReselectOutputDevice(string deviceName)
    {
        _outputDevice?.Dispose();
        SelectOutputDevice(deviceName);
    }

    public void OnAudioActivity(double[] magnitudes)
    {
        if (CurrentTrackPosition >= CurrentTrackLength - 5)
        {
            Log.Information($"OverallLoudness: {magnitudes.Max()}");
            if (magnitudes.Length == 0 || (magnitudes.Max() < -40) || ((CurrentTrackPosition + 1.0) >= CurrentTrackLength))
            {
                PlaybackStopped(null, null!);
            }
        }
    }

    private void PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (StoppedEvent != null)
        {
            try
            {
                float unused = OutputDeviceVolume;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
                if (ex.Message == "NoDriver calling waveOutGetVolume")
                {
                    SelectOutputDevice(OutputDeviceManager.GetOutputDeviceNameById(0));
                }
            }

            if (_audioFile != null)
            {
                WeakReferenceMessenger.Default.Send(new PlaybackStoppedMessage(true));
            }
        }
    }

    public bool CanPlay
    {
        get => _canPlay;
        private set
        {
            bool oldValue = _canPlay;
            _canPlay = value;
            if (oldValue != _canPlay)
                OnPropertyChanged();
        }
    }

    public bool CanPause
    {
        get => _canPause;
        private set
        {
            bool oldValue = _canPause;
            _canPause = value;
            if (oldValue != _canPause)
                OnPropertyChanged();
        }
    }

    public bool CanStop
    {
        get => _canStop;
        private set
        {
            bool oldValue = _canStop;
            _canStop = value;
            if (oldValue != _canStop)
                OnPropertyChanged();
        }
    }

    public void Stop()
    {
        if (_outputDevice != null)
        {
            _outputDevice.Stop();

            if (_audioFile != null)
            {
                CloseFile();
            }
        }

        IsPlaying = false;
        CanStop = false;
        CanPlay = true;
        CanPause = false;
    }

    public void Pause()
    {
        if (IsPlaying && CanPause)
        {
            _outputDevice!.Pause();

            IsPlaying = false;
            CanPlay = true;
            CanPause = false;
        }
    }

    public void UpdateEqualizer()
    {
        _equalizer?.Update();
    }

    public void LoadAudioFile(string pathToMusic, double startPosition = 0.0)
    {
        try
        {
            if (_audioFile != null && IsPaused)
            {
                _audioFile.CurrentTime = TimeSpan.FromSeconds(startPosition);
                ResumePlay();

                return;
            }

            Stop();
            CloseFile();
            ReselectOutputDevice(OutputDeviceManager.GetCurrentDeviceName());

            _audioFile = new AudioFileReader(pathToMusic);
            _audioFile.CurrentTime = TimeSpan.FromSeconds(startPosition);
            _aggregator = new(_audioFile)
            {
                NotificationCount = _audioFile.WaveFormat.SampleRate / 100,
                PerformFft = true
            };

            _aggregator.FftCalculated += (_, a) => OnFftCalculated(a);

            //_outputDevice.Init(_aggregator);
            EqSwitched = false;

            if (EqIsOn)
            {
                InitializeEqualizer();
                _equalizer = new Equalizer(_aggregator, _bands);
                _outputDevice.Init(_equalizer);
            }
            else
            {
                _outputDevice.Init(_aggregator);
            }

            WeakReferenceMessenger.Default.Send(new EnginePropertyChangedMessage("TrackLoaded"));
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Problem opening file");
            CloseFile();
        }
    }

    public void Play()
    {
        if (_pathToMusic == null || _outputDevice == null) return;

        if (CanPlay)
        {
            LoadAudioFile(_pathToMusic);

            IsPlaying = true;
            CanPause = true;
            CanPlay = false;
            CanStop = true;

            _outputDevice.Play();
        }
    }

    public void ResumePlay()
    {
        _outputDevice!.Play();

        IsPlaying = true;
        CanPause = true;
        CanPlay = false;
        CanStop = true;
    }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            bool oldValue = _isPlaying;
            _isPlaying = value;
            if (oldValue != _isPlaying)
                OnPropertyChanged();
            _positionTimer.IsEnabled = value;
        }
    }

    public float[] GetFftData()
    {
        return FftUpdate;
    }

    private void OnFftCalculated(FftEventArgs e)
    {
        Complex[] complexNumbers = e.Result;
        float[] fftResult = new float[complexNumbers.Length];

        //int m = (int)Math.Log(FftUpdate.Length, 2);
        //FastFourierTransform.FFT(true, m, complexNumbers);

        for (int i = 0; i < complexNumbers.Length / 2; i++)
        {
            fftResult[i] = (float)Math.Sqrt(complexNumbers[i].X * complexNumbers[i].X + complexNumbers[i].Y * complexNumbers[i].Y);
        }

        FftUpdate = fftResult;
    }

    public int GetFftFrequencyIndex(int frequency)
    {
        double maxFrequency;
        if (_audioFile != null)
            maxFrequency = _audioFile.WaveFormat.SampleRate / 2.0d;
        else
            maxFrequency = 22050; // Assume a default 44.1 kHz sample rate.
        return (int)((frequency / maxFrequency) * (FftDataSize / 2.0d));
    }

    public static int FftFrequency2Index(int frequency, int length, int sampleRate)
    {
        int num = (int)Math.Round(length * (double)frequency / sampleRate);
        if (num > length / 2 - 1)
            num = length / 2 - 1;
        return num;
    }

    public void StopAndPlayFromPosition(double startingPosition)
    {
        if (_pathToMusic == null || _outputDevice == null) return;

        float oldVol = MusicVolume;

        LoadAudioFile(_pathToMusic, startingPosition);

        MusicVolume = oldVol;

        _outputDevice.Play();

        IsPlaying = true;
        CanPause = true;
        CanPlay = false;
        CanStop = true;
    }

    public void Dispose()
    {
        CloseDevice();
    }

    public void CloseFile()
    {
        if (_audioFile != null)
        {
            _audioFile.Dispose();
            _audioFile = null;
        }
    }

    public void CloseDevice()
    {
        Stop();
        StopEqualizer();
        CloseFile();
        _outputDevice?.Dispose();
    }

    public string? PathToMusic
    {
        get => _pathToMusic;
        set
        {
            if (!File.Exists(value))
            {
                string fileDoesNotExist = "File does not exist";

                throw new FileNotFoundException(fileDoesNotExist);
            }

            _pathToMusic = value;
        }
    }

    public double CurrentTrackLength => _audioFile != null ? _audioFile.TotalTime.TotalSeconds : 0;

    public double CurrentTrackPosition
    {
        get => _audioFile != null ? _audioFile.CurrentTime.TotalSeconds : 0;
        set
        {
            if (_audioFile != null)
            {
                _audioFile.CurrentTime = TimeSpan.FromSeconds(value);
            }
        }
    }

    private float[] _fftResult = [];
    public float[] FftUpdate
    {
        get => _fftResult;
        set
        {
            _fftResult = value;
            WeakReferenceMessenger.Default.Send(new EnginePropertyChangedMessage("FFTUpdate"));
        }
    }

    #region Equalizer

    //private bool _isEqualizerInitialized;

    //public bool IsEqualizerInitialized
    //{
    //    get => _isEqualizerInitialized;
    //    set
    //    {
    //        if (value == IsEqualizerInitialized) { return; }

    //        _isEqualizerInitialized = value;
    //    }
    //}

    private void CreateEqBands()
    {
        _bands = new EqualizerBand[]
        {
            new() { Bandwidth = 0.8f, Frequency = 32f, Gain = 0 },
            new() { Bandwidth = 0.8f, Frequency = 64f, Gain = 0 },
            new() { Bandwidth = 0.8f, Frequency = 125f, Gain = 0 },
            new() { Bandwidth = 0.8f, Frequency = 250f, Gain = 0 },
            new() { Bandwidth = 0.8f, Frequency = 500f, Gain = 0 },
            new() { Bandwidth = 0.8f, Frequency = 1000f, Gain = 0 },
            new() { Bandwidth = 0.8f, Frequency = 2000f, Gain = 0 },
            new() { Bandwidth = 0.8f, Frequency = 4000f, Gain = 0 },
            new() { Bandwidth = 0.8f, Frequency = 8000f, Gain = 0 },
            new() { Bandwidth = 0.8f, Frequency = 16000f, Gain = 0 }
        };
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs propertyChangedEventArgs)
    {
        _equalizer?.Update();
    }

    private void OnEqualizerIsOnMessage(bool isOn)
    {
        EqIsOn = isOn;
        EqSwitched = true;
    }

    public void InitializeEqualizer()
    {

        if (_aggregator != null && _bands?.Length != 0)
        {
            _equalizer = new Equalizer(_aggregator, _bands);
        }
    }

    public void StopEqualizer()
    {
//        _bands = null;
        _equalizer = null;
    }

    public float GetBandGain(int index)
    {
        if (_bands != null && index is >= 0 and <= 9)
        {
            return _bands[index].Gain;
        }

        return 0;
    }

    public void SetBandGain(int index, float value)
    {
        if (_bands == null || index is < 0 or > 9) return;
        if (!(Math.Abs(_bands[index].Gain - value) > 0)) return;

        _bands[index].Gain = value;

        //Log.Information($"Band{index} - {value}");
        _equalizer?.Update();
    }

    public List<EqualizerBand> GetBandsList()
    {
        List<EqualizerBand> equalizerBands = new();

        if (_bands == null) return equalizerBands;

        foreach (EqualizerBand band in _bands)
        {
            equalizerBands.Add(new EqualizerBand
            {
                Bandwidth = band.Bandwidth,
                Frequency = band.Frequency,
                Gain = band.Gain
            });
        }

        return equalizerBands;
    }

    public void SetBandsList(List<EqualizerBand> equalizerBandsToAdd)
    {
        for (int i = 0; i < equalizerBandsToAdd.Count; i++)
        {
            SetBandGain(i, equalizerBandsToAdd[i].Gain);
        }
    }

    #endregion Equalizer

}