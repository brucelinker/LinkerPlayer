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
using System.Windows.Media;
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
    private static SampleAggregator? _aggregator;
    private static double[] frequencies;
    private int[] frequencyBins;

    static int xPos = 2;
    static int yScale = 50;

    static List<Point> topPoints = new List<Point>();
    static List<Point> bottomPoints = new List<Point>();
    private static double[] _volume = { 0, 0 };


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
            //PrepareSpectrumAnalyzer();

            //MinEQGain = -MAX_EQ_GAIN;
            //MaxEQGain = MAX_EQ_GAIN;
            //EQMinimumDb = MIN_DB_VALUE;

            IsPlaying = false;
            CanStop = false;
            CanPlay = true;
            CanPause = false;

            //waveformGenerateWorker.DoWork += waveformGenerateWorker_DoWork;
            //waveformGenerateWorker.RunWorkerCompleted += waveformGenerateWorker_RunWorkerCompleted;
            //waveformGenerateWorker.WorkerSupportsCancellation = true;
        }
    }

    //public void Prepare(AudioEngine Model)
    //{
    //    //_model = Model;
    //    //_model.PropertyChanged += _engine_PropertyChanged;

    //    PrepareSpectrumAnalyzer();

    //    //timer = new DispatcherTimer();
    //    //timer.Interval = TimeSpan.FromMilliseconds(500);
    //    //timer.Tick += Timer_Tick;

    //    //_isLocationChanging = false;
    //    //PopulateDevicesCombo();
    //    //SetupWasapi(); method called when combo is created

    //    //EnableControls(false);
    //    //PlayButton.IsEnabled = false;

    //    MinEQGain = -MAX_EQ_GAIN;
    //    MaxEQGain = MAX_EQ_GAIN;
    //    EQMinimumDb = MIN_DB_VALUE;
    //}
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

    public static PointCollection WaveformPoints
    {
        get
        {
            PointCollection points = new PointCollection();

            points.Add(new Point(0, yScale));

            foreach (var p in topPoints)
            {
                points.Add(p);
            }

            points.Add(new Point(xPos, yScale));
            bottomPoints.Reverse();

            foreach (var p in bottomPoints)
            {
                points.Add(p);
            }
            points.Add(new Point(0, yScale));

            return points;
        }
    }

    public static double[] SoundLevel
    {
        get => _volume;
        set
        {
            _volume = value;
            WeakReferenceMessenger.Default.Send(new EnginePropertyChangedMessage("SoundLevel"));
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

    private static double _minEQ = 0;
    public static double MinEQGain
    {
        get => _minEQ;
        set
        {
            _minEQ = value;
            OnPropertyChanged("MinEQGain");
        }
    }

    private static double _maxEQ = 0;
    public static double MaxEQGain
    {
        get => _maxEQ;
        set
        {
            _maxEQ = value;
            OnPropertyChanged("MaxEQGain");
        }
    }

    private static double _eqMinimumDb;
    public static double EQMinimumDb
    {
        get => _eqMinimumDb;
        set
        {
            _eqMinimumDb = value;
            OnPropertyChanged("EQMinimumDb");
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
                OnPropertyChanged("CanPlay");
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
                OnPropertyChanged("CanPause");
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
                OnPropertyChanged("CanStop");
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
        try
        {
            Log.Information($"Loading Track: {pathToMusic}");

            if(_audioFile != null && pathToMusic.Equals(_audioFile.FileName))
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

            Log.Information($"Sending TrackLoaded message: {pathToMusic}");

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
                OnPropertyChanged("IsPlaying");
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

    public static int FrequencyBinIndex(double frequency)
    {
        var bin = (int)Math.Floor(frequency * _aggregator.FFTLength / _audioFile.WaveFormat.SampleRate);
        return bin;
    }

    static void positionTimer_Tick(object sender, EventArgs e)
    {
        _inChannelTimerUpdate = true;
        ChannelPosition = (_audioFile!.Position / (double)_audioFile.Length) * _audioFile.TotalTime.TotalSeconds;
        _inChannelTimerUpdate = false;
    }

    public static double ChannelLength
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
                    OnPropertyChanged("ChannelPosition");

                _inChannelSet = false;
            }
        }
    }

    private static double[] _fftResult = { };
    public static double[] FFTUpdate
    {
        get => _fftResult;
        set
        {
            _fftResult = value;
            WeakReferenceMessenger.Default.Send(new EnginePropertyChangedMessage("FFTUpdate"));
        }
    }

    //private bool _markersDrawn = true;
    //private void CreateWaveformMarkers()
    //{
    //    if (!_markersDrawn)
    //    {
    //        double seconds = _model.Duration;
    //        double points = Math.Floor(seconds / 10);
    //        double marginLeft = _waveformWidth / points;

    //        var markers = new List<Marker>();

    //        // note we skip 00:00
    //        for (var i = 1; i <= points; i++)
    //        {
    //            double time = i * 10;
    //            var mins = Math.Floor(time / 60);
    //            var secs = time - (mins * 60);
    //            var display = $"{mins:00}:{secs:00}";
    //            var marker = new Marker(display, marginLeft);
    //            markers.Add(marker);
    //        }

    //        _markersDrawn = true;
    //        WaveformMarkers = markers;
    //    }
    //}

    private static void OnFftCalculated(FftEventArgs e)
    {
        var complexNumbers = e.Result;
        double[] fftResult = new double[complexNumbers.Length];

        for (int i = 0; i < complexNumbers.Length / 2; i++)
        {
            fftResult[i] = Math.Sqrt(complexNumbers[i].X * complexNumbers[i].X + complexNumbers[i].Y * complexNumbers[i].Y);
        }

        FFTUpdate = fftResult;
    }

    private static void OnMaximumCalculated(MaxSampleEventArgs e)
    {
        //double dbValue = 20 * Math.Log10((double)e.MaxSample);
        //MaxFrequency = e.MaxSample;
    }

    public static event PropertyChangedEventHandler? PropertyChanged;
    private static void OnPropertyChanged(String info)
    {
        PropertyChanged?.Invoke(null, new PropertyChangedEventArgs(info));
    }
}