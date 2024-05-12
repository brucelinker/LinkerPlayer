using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
using NAudio.Extras;
using NAudio.Wave;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LinkerPlayer.Audio;

public class AudioEngine : ObservableObject, IDisposable
{
    public static AudioEngine Instance { get; } = new();

    public event EventHandler<StoppedEventArgs>? StoppedEvent;

    private readonly DispatcherTimer _positionTimer = new(DispatcherPriority.ApplicationIdle);
    private AudioFileReader? _audioFile;
    private string? _pathToMusic;
    private Equalizer? _equalizer;
    private EqualizerBand[]? _bands;
    private SampleAggregator? _aggregator;
    private double[] _volume = { 0, 0 };
    private bool _canPlay;
    private bool _canPause;
    private bool _canStop;
    private bool _isPlaying;
    private IWavePlayer? _outputDevice;
    private bool _inChannelSet;
    private bool _inChannelTimerUpdate;
    private double _channelLength;
    private double _channelPosition;
    private readonly string _mainOutputDevice;

    private AudioEngine()
    {
        _mainOutputDevice = Properties.Settings.Default.MainOutputDevice;

        if (string.IsNullOrWhiteSpace(_mainOutputDevice))
        {
            Log.Error("Device name can`t be null");
        }
        else
        {
            _positionTimer.Interval = TimeSpan.FromMilliseconds(50);
            _positionTimer.Tick += positionTimer_Tick!;

            SelectOutputDevice(_mainOutputDevice);

            _musicVolume = (float)Properties.Settings.Default.VolumeSliderValue;

            if (Properties.Settings.Default.EqualizerOnStartEnabled)
            {
                if (!String.IsNullOrEmpty(Properties.Settings.Default.EqualizerProfileName))
                {
                    InitializeEqualizer();
                }
            }

            StoppedEvent += PlaybackStopped;

            IsPlaying = false;
            CanStop = false;
            CanPlay = true;
            CanPause = false;
        }
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

    public double[] SoundLevel
    {
        get => _volume;
        set
        {
            _volume = value;
            WeakReferenceMessenger.Default.Send(new EnginePropertyChangedMessage("SoundLevel"));
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
    
    public bool IsPaused => _outputDevice is { PlaybackState: PlaybackState.Paused };
    public bool IsStopped => _outputDevice is { PlaybackState: PlaybackState.Stopped };

    public void SelectOutputDevice(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            throw new ArgumentNullException(nameof(deviceName), "OutputDevice cannot be null.");
        }

        _outputDevice = new WaveOut
        {
            DeviceNumber = OutputDevice.GetOutputDeviceId(deviceName),
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
            if (!magnitudes.Any() || (magnitudes.Max() < -40) || ((CurrentTrackPosition + 1.0) >= CurrentTrackLength))
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
                    sender = null;
                    SelectOutputDevice(Audio.OutputDevice.GetOutputDeviceNameById(0));
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
                OnPropertyChanged("CanPlay");
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
                OnPropertyChanged("CanPause");
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
                OnPropertyChanged("CanStop");
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

    public void LoadAudioFile(string pathToMusic, double startPosition = 0.0)
    {
        try
        {
            if (_audioFile != null && pathToMusic.Equals(_audioFile.FileName))
            {
                _audioFile.CurrentTime = TimeSpan.FromSeconds(startPosition);
                ResumePlay();

                return;
            }

            Stop();
            CloseFile();
            ReselectOutputDevice(OutputDevice.GetCurrentDeviceName());

            _audioFile = new(pathToMusic);
            _audioFile.CurrentTime = TimeSpan.FromSeconds(startPosition);
            _aggregator = new(_audioFile)
            {
                NotificationCount = _audioFile.WaveFormat.SampleRate / 100,
                PerformFFT = true
            };

            _aggregator.FftCalculated += (s, a) => OnFftCalculated(a);
            _aggregator.MaximumCalculated += (s, a) => OnMaximumCalculated(a);

            _outputDevice.Init(_aggregator);

            if (_equalizer != null)
            {
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

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            bool oldValue = _isPlaying;
            _isPlaying = value;
            if (oldValue != _isPlaying)
                OnPropertyChanged("IsPlaying");
            _positionTimer.IsEnabled = value;
        }
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

                // Log.Warning(fileDoesNotExist);
                throw new FileNotFoundException(fileDoesNotExist);
            }
            else
            {
                _pathToMusic = value;
            }
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

    #region Equalizer

    private bool _isEqualizerInitialized;

    public bool IsEqualizerInitialized
    {
        get => _isEqualizerInitialized;
        set
        {
            if (value == _isEqualizerInitialized) { return; }

            _isEqualizerInitialized = value;
        }
    }

    public float MinimumGain => -12;
    public float MaximumGain => 12;

    public void InitializeEqualizer()
    {
        if (_audioFile != null)
        {
            _bands = new[]
            {
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 32f, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 64f, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 125f, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 250f, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 500f, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 1000f, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 2000f, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 4000f, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 8000f, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 16000f, Gain = 0 },
            };

            _equalizer = new Equalizer(_audioFile, _bands);
        }
    }

    public void StopEqualizer()
    {
        _bands = null;
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
        _equalizer?.Update();
    }

    public List<EqualizerBand> GetBandsList()
    {
        List<EqualizerBand> equalizerBands = new();

        if (_bands != null)
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

    public int FrequencyBinIndex(double frequency)
    {
        var bin = (int)Math.Floor(frequency * _aggregator.FFTLength / _audioFile.WaveFormat.SampleRate);
        return bin;
    }

    void positionTimer_Tick(object sender, EventArgs e)
    {
        _inChannelTimerUpdate = true;
        ChannelPosition = (_audioFile!.Position / (double)_audioFile.Length) * _audioFile.TotalTime.TotalSeconds;
        _inChannelTimerUpdate = false;
    }

    public double ChannelLength
    {
        get => _channelLength;
        private set
        {
            double oldValue = _channelLength;
            _channelLength = value;

            if (Math.Abs(oldValue - _channelLength) > 0)
                OnPropertyChanged("ChannelLength");
        }
    }

    public double ChannelPosition
    {
        get => _channelPosition;
        set
        {
            if (!_inChannelSet)
            {
                _inChannelSet = true; // Avoid recursion
                double oldValue = _channelPosition;
                double position = Math.Max(0, Math.Min(value, ChannelLength));

                if (!_inChannelTimerUpdate && _audioFile != null)
                    _audioFile.Position = (long)((position / _audioFile.TotalTime.TotalSeconds) * _audioFile.Length);

                _channelPosition = position;

                if (Math.Abs(oldValue - _channelPosition) > 0)
                    OnPropertyChanged("ChannelPosition");

                _inChannelSet = false;
            }
        }
    }

    private double[] _fftResult = { };
    public double[] FFTUpdate
    {
        get => _fftResult;
        set
        {
            _fftResult = value;
            WeakReferenceMessenger.Default.Send(new EnginePropertyChangedMessage("FFTUpdate"));
        }
    }

    private void OnFftCalculated(FftEventArgs e)
    {
        var complexNumbers = e.Result;
        double[] fftResult = new double[complexNumbers.Length];

        for (int i = 0; i < complexNumbers.Length / 2; i++)
        {
            fftResult[i] = Math.Sqrt(complexNumbers[i].X * complexNumbers[i].X + complexNumbers[i].Y * complexNumbers[i].Y);
        }

        FFTUpdate = fftResult;
    }

    private void OnMaximumCalculated(MaxSampleEventArgs e)
    {
        //double dbValue = 20 * Math.Log10((double)e.MaxSample);
        //MaxFrequency = e.MaxSample;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(String info)
    {
        PropertyChanged?.Invoke(null, new PropertyChangedEventArgs(info));
    }
}