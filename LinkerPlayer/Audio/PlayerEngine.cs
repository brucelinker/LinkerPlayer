using LinkerPlayer.SpectrumAnalyzer;
using NAudio.Extras;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Serilog;
using System.Windows.Threading;
using LinkerPlayer.Models;

namespace LinkerPlayer.Audio;

public class PlayerEngine : ISpectrumPlayer, IDisposable
{
    private static PlayerEngine? _instance;
    private readonly DispatcherTimer _positionTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
    //private readonly BackgroundWorker _waveformGenerateWorker = new BackgroundWorker();
    private AudioFileReader? _audioFile;
    private string? _pathToMusic;
    //private TagLib.File _fileTag;

    private Equalizer? _equalizer;
    private EqualizerBand[]? _bands;
    private readonly SampleAggregator? _sampleAggregator;
    private readonly int _fftDataSize = (int)FFTDataSize.FFT2048;
    private bool _canPlay;
    private bool _canPause;
    private bool _canStop;
    private bool _isPlaying;
    //private float[]? _waveformData;
    protected WaveOutEvent? OutputDevice;
    public event EventHandler<EventArgs>? StoppedEvent;
    //private TimeSpan _repeatStart;
    //private TimeSpan _repeatStop;
    //private bool _inRepeatSet;
    private bool _inChannelSet;
    private bool _inChannelTimerUpdate;
    private double _channelLength;
    private double _channelPosition;
    
    //private const int waveformCompressedPointCount = 2000;
    //private const int repeatThreshold = 200;

    public PlayerEngine(string outputDeviceName)
    {
        _instance = this;

        if (string.IsNullOrWhiteSpace(outputDeviceName))
        {
            Log.Error("Device name can`t be null");
        }
        else
        {
            _positionTimer.Interval = TimeSpan.FromMilliseconds(50);
            _positionTimer.Tick += positionTimer_Tick!;

            SelectOutputDevice(outputDeviceName);
            _sampleAggregator = new SampleAggregator(_fftDataSize);

            IsPlaying = false;
            CanStop = false;
            CanPlay = true;
            CanPause = false;

            //waveformGenerateWorker.DoWork += waveformGenerateWorker_DoWork;
            //waveformGenerateWorker.RunWorkerCompleted += waveformGenerateWorker_RunWorkerCompleted;
            //waveformGenerateWorker.WorkerSupportsCancellation = true;
        }
    }

    public float OutputDeviceVolume
    {
        get => OutputDevice?.Volume ?? 0f;
        set
        {
            if (value is < 0f or > 1f)
            {
                if (value < 0)
                {
                    if (OutputDevice != null) OutputDevice.Volume = 0f;
                }
                else
                {
                    if (OutputDevice != null) OutputDevice.Volume = 1f;
                }
            }
            else
            {
                if (OutputDevice != null) OutputDevice.Volume = value;
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

    //public TagLib.File FileTag
    //{
    //    get { return _fileTag; }
    //    set
    //    {
    //        TagLib.File oldValue = _fileTag;
    //        _fileTag = value;
    //        if (oldValue != _fileTag)
    //            NotifyPropertyChanged("FileTag");
    //    }
    //}

    //public bool IsPlaying => OutputDevice is { PlaybackState: PlaybackState.Playing };
    public bool IsPaused => OutputDevice is { PlaybackState: PlaybackState.Paused };
    //public bool IsStopped => OutputDevice is { PlaybackState: PlaybackState.Stopped };

    public void SelectOutputDevice(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            throw new ArgumentNullException(nameof(deviceName), "OutputDevice cannot be null.");
        }

        OutputDevice = new WaveOutEvent()
        {
            DeviceNumber = Audio.OutputDevice.GetOutputDeviceId(deviceName)
        };

        OutputDevice.PlaybackStopped += PlaybackStopped;
    }

    public virtual void PlaybackStopped(object? sender, EventArgs e)
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

            StoppedEvent(sender, e);
        }
    }

    public bool CanPlay
    {
        get { return _canPlay; }
        protected set
        {
            bool oldValue = _canPlay;
            _canPlay = value;
            if (oldValue != _canPlay)
                NotifyPropertyChanged("CanPlay");
        }
    }

    public bool CanPause
    {
        get { return _canPause; }
        protected set
        {
            bool oldValue = _canPause;
            _canPause = value;
            if (oldValue != _canPause)
                NotifyPropertyChanged("CanPause");
        }
    }

    public bool CanStop
    {
        get { return _canStop; }
        protected set
        {
            bool oldValue = _canStop;
            _canStop = value;
            if (oldValue != _canStop)
                NotifyPropertyChanged("CanStop");
        }
    }

    public void Stop()
    {
        if (OutputDevice != null)
        {
            OutputDevice.Stop();
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
            OutputDevice!.Pause();
            IsPlaying = false;
            CanPlay = true;
            CanPause = false;
        }
    }

    public void Play()
    {
        if (_pathToMusic == null || OutputDevice == null) return;

        if (CanPlay)
        {

            AudioFileReader newAudioFile = new(_pathToMusic);

            if (_audioFile == null || newAudioFile.FileName != _audioFile.FileName)
            {
                _audioFile = newAudioFile;
                
                if (_equalizer != null)
                {
                    _equalizer = new Equalizer(_audioFile, _bands);

                    OutputDevice.Init(_equalizer);
                }
                else
                {
                    OutputDevice.Init(_audioFile);
                }
            }

            IsPlaying = true;
            CanPause = true;
            CanPlay = false;
            CanStop = true;

            string outputDeviceName = Audio.OutputDevice.GetOutputDeviceNameById(OutputDevice.DeviceNumber);
            Log.Information($"Playing to {outputDeviceName}.");

            OutputDevice.Play();
        }
    }

    public bool IsPlaying
    {
        get { return _isPlaying; }
        protected set
        {
            bool oldValue = _isPlaying;
            _isPlaying = value;
            if (oldValue != _isPlaying)
                NotifyPropertyChanged("IsPlaying");
            _positionTimer.IsEnabled = value;
        }
    }

    public void StopAndPlayFromPosition(double startingPosition)
    {
        if (_pathToMusic == null || OutputDevice == null) return;

        float oldVol = MusicVolume;

        Stop();

        _audioFile = new AudioFileReader(_pathToMusic);
        _audioFile.CurrentTime = TimeSpan.FromSeconds(startingPosition);

        if (_equalizer != null)
        {
            _equalizer = new Equalizer(_audioFile, _bands);
            OutputDevice.Init(_equalizer);
        }
        else
        {
            OutputDevice.Init(_audioFile);
        }

        MusicVolume = oldVol;

        OutputDevice.Play();

        IsPlaying = true;
        CanPause = true;
        CanPlay = false;
        CanStop = true;
    }

    public void StopAndResetPosition()
    {
        if (_pathToMusic != null)
        {
            Stop();

            _audioFile = new AudioFileReader(_pathToMusic);
            _audioFile.CurrentTime = TimeSpan.FromSeconds(0);
        }
    }


    private void Dispose()
    {
        if (_audioFile != null)
        {
            _audioFile?.Dispose();
            _audioFile = null;
        }
    }

    public void CloseStream()
    {
        Stop();
        StopEqualizer();
        Dispose();
        OutputDevice?.Dispose();
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

    public double CurrentTrackLength
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

    public double CurrentTrackPosition
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

    public void Seek(double offset)
    {
        CurrentTrackPosition += offset;
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

    public void ReselectOutputDevice(string deviceName)
    {
        if (IsPlaying)
        {
            double tempPosition = CurrentTrackPosition;
            float tempDeviceVolume = OutputDeviceVolume;
            float tempMusicVolume = MusicVolume;

            Stop();

            OutputDevice?.Dispose();

            SelectOutputDevice(deviceName);

            StopAndPlayFromPosition(tempPosition);

            OutputDeviceVolume = tempDeviceVolume;
            MusicVolume = tempMusicVolume;
        }
        else
        {
            OutputDevice?.Dispose();

            SelectOutputDevice(deviceName);
        }
    }

    public bool GetFFTData(float[] fftDataBuffer)
    {
        _sampleAggregator!.GetFFTResults(fftDataBuffer);
        return IsPlaying;
    }

    public int GetFFTFrequencyIndex(int frequency)
    {
        double maxFrequency;
        if (_audioFile != null)
            maxFrequency = _audioFile.WaveFormat.SampleRate / 2.0d;
        else
            maxFrequency = 22050; // Assume a default 44.1 kHz sample rate.

        return (int)((frequency / maxFrequency) * (_fftDataSize / 2.0));
    }

    public int GetOutputDeviceId()
    {
        if (OutputDevice != null) return OutputDevice.DeviceNumber;

        return -1;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void NotifyPropertyChanged(String info)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(info));
    }

    //public TimeSpan SelectionBegin
    //{
    //    get => _repeatStart;
    //    set
    //    {
    //        if (_inRepeatSet) return;

    //        _inRepeatSet = true;
    //        TimeSpan oldValue = _repeatStart;
    //        _repeatStart = value;

    //        if (oldValue != _repeatStart)
    //            NotifyPropertyChanged("SelectionBegin");

    //        _inRepeatSet = false;
    //    }
    //}

    //public TimeSpan SelectionEnd
    //{
    //    get => _repeatStop;
    //    set
    //    {
    //        if (_inChannelSet) return;

    //        _inRepeatSet = true;
    //        TimeSpan oldValue = _repeatStop;
    //        _repeatStop = value;

    //        if (oldValue != _repeatStop)
    //            NotifyPropertyChanged("SelectionEnd");

    //        _inRepeatSet = false;
    //    }
    //}

    //public float[]? WaveformData
    //{
    //    get => _waveformData;
    //    protected set
    //    {
    //        float[]? oldValue = _waveformData;
    //        _waveformData = value;

    //        if (oldValue != _waveformData)
    //            NotifyPropertyChanged("WaveformData");
    //    }
    //}

    public double ChannelLength
    {
        get => _channelLength;
        protected set
        {
            double oldValue = _channelLength;
            _channelLength = value;

            if (Math.Abs(oldValue - _channelLength) > 0)
                NotifyPropertyChanged("ChannelLength");
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
                    NotifyPropertyChanged("ChannelPosition");

                _inChannelSet = false;
            }
        }
    }

    void positionTimer_Tick(object sender, EventArgs e)
    {
        _inChannelTimerUpdate = true;
        ChannelPosition = (_audioFile!.Position / (double)_audioFile.Length) * _audioFile.TotalTime.TotalSeconds;
        _inChannelTimerUpdate = false;
    }

    void IDisposable.Dispose()
    {
        CloseStream();
    }
}