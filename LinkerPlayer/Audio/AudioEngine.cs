using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
using NAudio.Extras;
using NAudio.Wave;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LinkerPlayer.Audio;

public class AudioEngine : IDisposable
{
    private static readonly DispatcherTimer _positionTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
    //private readonly BackgroundWorker _waveformGenerateWorker = new BackgroundWorker();
    private static AudioFileReader? _audioFile;
    private static string? _pathToMusic;
    //private TagLib.File _fileTag;

    private static Equalizer? _equalizer;
    private static EqualizerBand[]? _bands;
    private static readonly SampleAggregator? _sampleAggregator;
    //private static readonly int _fftDataSize = (int)FFTDataSize.FFT2048;
    private static bool _canPlay;
    private static bool _canPause;
    private static bool _canStop;
    private static bool _isPlaying;
    //private float[]? _waveformData;
    //private static WaveOutEvent? _outputDevice;
    private static IWavePlayer? _outputDevice;
    public static event EventHandler<EventArgs>? StoppedEvent;
    //private TimeSpan _repeatStart;
    //private TimeSpan _repeatStop;
    //private bool _inRepeatSet;
    private static bool _inChannelSet;
    private static bool _inChannelTimerUpdate;
    private static double _channelLength;
    private static double _channelPosition;
    private static string _mainOutputDevice;
    //private const int waveformCompressedPointCount = 2000;
    //private const int repeatThreshold = 200;

    public static event EventHandler<FftEventArgs> FftCalculated;
    public static event EventHandler<MaxSampleEventArgs> MaximumCalculated;

    static AudioEngine()
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
            //_sampleAggregator = new SampleAggregator(_fftDataSize);

            _musicVolume = (float)Properties.Settings.Default.VolumeSliderValue;

            if (Properties.Settings.Default.EqualizerOnStartEnabled)
            {
                if (!String.IsNullOrEmpty(Properties.Settings.Default.EqualizerProfileName))
                {
                    //SelectedEqualizerProfile = new BandsSettings() { Name = Properties.Settings.Default.EqualizerProfileName ?? "Flat" };

                    InitializeEqualizer();
                }
            }

            //StoppedEvent += Playback_StoppedEvent;

            IsPlaying = false;
            CanStop = false;
            CanPlay = true;
            CanPause = false;

            //waveformGenerateWorker.DoWork += waveformGenerateWorker_DoWork;
            //waveformGenerateWorker.RunWorkerCompleted += waveformGenerateWorker_RunWorkerCompleted;
            //waveformGenerateWorker.WorkerSupportsCancellation = true;
        }
    }

    public static float OutputDeviceVolume
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

    private static float _musicVolume;
    public static float MusicVolume
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

    public static bool IsPaused => _outputDevice is { PlaybackState: PlaybackState.Paused };
    public bool IsStopped => _outputDevice is { PlaybackState: PlaybackState.Stopped };

    public static void SelectOutputDevice(string deviceName)
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

        _outputDevice.PlaybackStopped += Playback_StoppedEvent;
    }

    public static void ReselectOutputDevice(string deviceName)
    {
        _outputDevice?.Dispose();
        SelectOutputDevice(deviceName);
    }

    public static void Playback_StoppedEvent(object? sender, StoppedEventArgs stoppedEventArgs)
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

            StoppedEvent(sender, stoppedEventArgs);
            WeakReferenceMessenger.Default.Send(new PlaybackStoppedMessage(true));
            WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(PlaybackState.Stopped));
        }
    }

    public static bool CanPlay
    {
        get => _canPlay;
        private set
        {
            bool oldValue = _canPlay;
            _canPlay = value;
            if (oldValue != _canPlay)
                NotifyPropertyChanged("CanPlay");
        }
    }

    public static bool CanPause
    {
        get => _canPause;
        private set
        {
            bool oldValue = _canPause;
            _canPause = value;
            if (oldValue != _canPause)
                NotifyPropertyChanged("CanPause");
        }
    }

    public static bool CanStop
    {
        get => _canStop;
        private set
        {
            bool oldValue = _canStop;
            _canStop = value;
            if (oldValue != _canStop)
                NotifyPropertyChanged("CanStop");
        }
    }

    public static void Stop()
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

    public static void Pause()
    {
        if (IsPlaying && CanPause)
        {
            _outputDevice!.Pause();

            IsPlaying = false;
            CanPlay = true;
            CanPause = false;
        }
    }

    public static void LoadAudioFile(string pathToMusic, double startPosition = 0.0)
    {
        Stop();
        CloseFile();
        ReselectOutputDevice(OutputDevice.GetCurrentDeviceName());

        try
        {
            _audioFile = new(pathToMusic);
            _audioFile.Position = (long)startPosition;
            SampleAggregator aggregator = new(_audioFile)
            {
                NotificationCount = _audioFile.WaveFormat.SampleRate / 100,
                PerformFFT = true
            };
            aggregator.FftCalculated += (s, a) => FftCalculated?.Invoke(null, a);
            aggregator.MaximumCalculated += (s, a) => MaximumCalculated?.Invoke(null, a);
            _outputDevice.Init(aggregator);

            if (_equalizer != null)
            {
                _equalizer = new Equalizer(aggregator, _bands);
                _outputDevice.Init(_equalizer);
            }
            else
            {
                _outputDevice.Init(aggregator);
            }
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Problem opening file");
            CloseFile();
        }
    }

    //private void CloseFile()
    //{
    //    fileStream?.Dispose();
    //    fileStream = null;
    //}

    //private void OpenFile(string fileName)
    //{
    //    try
    //    {
    //        var inputStream = new AudioFileReader(fileName);
    //        fileStream = inputStream;
    //        var aggregator = new SampleAggregator(inputStream);
    //        aggregator.NotificationCount = inputStream.WaveFormat.SampleRate / 100;
    //        aggregator.PerformFFT = true;
    //        aggregator.FftCalculated += (s, a) => FftCalculated?.Invoke(null, a);
    //        aggregator.MaximumCalculated += (s, a) => MaximumCalculated?.Invoke(null, a);
    //        playbackDevice.Init(aggregator);
    //    }
    //    catch (Exception e)
    //    {
    //        MessageBox.Show(e.Message, "Problem opening file");
    //        CloseFile();
    //    }
    //}
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

    public static void ResumePlay()
    {
        _outputDevice!.Play();

        IsPlaying = true;
        CanPause = true;
        CanPlay = false;
        CanStop = true;
    }

    public static bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            bool oldValue = _isPlaying;
            _isPlaying = value;
            if (oldValue != _isPlaying)
                NotifyPropertyChanged("IsPlaying");
            _positionTimer.IsEnabled = value;
        }
    }

    public static void StopAndPlayFromPosition(double startingPosition)
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

    public static void CloseFile()
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

    public static string? PathToMusic
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

    public static double CurrentTrackLength
    {
        get
        {
            if (_audioFile != null)
            {
                return _audioFile.TotalTime.TotalSeconds;
            }
            else
            {
                return 0;
            }
        }
    }

    public static double CurrentTrackPosition
    {
        get
        {
            if (_audioFile != null)
            {
                return _audioFile.CurrentTime.TotalSeconds;
            }
            else
            {
                return 0;
            }
        }
        set
        {
            if (_audioFile != null)
            {
                _audioFile.CurrentTime = TimeSpan.FromSeconds(value);
            }
        }
    }

    #region Equalizer

    private static bool _isEqualizerInitialized;

    public static bool IsEqualizerInitialized
    {
        get => _isEqualizerInitialized;
        set
        {
            if (value == _isEqualizerInitialized) { return; }

            _isEqualizerInitialized = value;
        }
    }


    public static float MinimumGain => -12;
    public static float MaximumGain => 12;

    public static void InitializeEqualizer()
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

    public static void StopEqualizer()
    {
        _bands = null;
        _equalizer = null;
    }

    public static float GetBandGain(int index)
    {
        if (_bands != null && index is >= 0 and <= 9)
        {
            return _bands[index].Gain;
        }

        return 0;
    }

    public static void SetBandGain(int index, float value)
    {
        if (_bands == null || index is < 0 or > 9) return;
        if (!(Math.Abs(_bands[index].Gain - value) > 0)) return;

        _bands[index].Gain = value;
        _equalizer?.Update();
    }

    public static List<EqualizerBand> GetBandsList()
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

    public static void SetBandsList(List<EqualizerBand> equalizerBandsToAdd)
    {
        for (int i = 0; i < equalizerBandsToAdd.Count; i++)
        {
            SetBandGain(i, equalizerBandsToAdd[i].Gain);
        }
    }

    #endregion Equalizer

    public static event PropertyChangedEventHandler? PropertyChanged;
    private static void NotifyPropertyChanged(String info)
    {
        PropertyChanged?.Invoke(null, new PropertyChangedEventArgs(info));
    }

    public static double ChannelLength
    {
        get => _channelLength;
        private set
        {
            double oldValue = _channelLength;
            _channelLength = value;

            if (Math.Abs(oldValue - _channelLength) > 0)
                NotifyPropertyChanged("ChannelLength");
        }
    }

    public static double ChannelPosition
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
                    NotifyPropertyChanged("ChannelPosition");

                _inChannelSet = false;
            }
        }
    }

    static void positionTimer_Tick(object sender, EventArgs e)
    {
        _inChannelTimerUpdate = true;
        ChannelPosition = (_audioFile!.Position / (double)_audioFile.Length) * _audioFile.TotalTime.TotalSeconds;
        _inChannelTimerUpdate = false;
    }
}