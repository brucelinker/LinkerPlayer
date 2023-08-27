using LinkerPlayer.Audio.Log;
using NAudio.Extras;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;

namespace LinkerPlayer.Audio;

public class AudioStream
{
    protected WaveOutEvent? outputDevice;
    protected ILog log = LogSettings.SelectedLog;

    public event EventHandler<EventArgs>? StoppedEvent;

    public float OutputDeviceVolume
    {
        get
        {
            if (outputDevice != null) return outputDevice.Volume;
            return 0f;
        }
        set
        {
            if (value < 0f || value > 1f)
            {
                log.Print("Volume < 0.0 or > 1.0", LogInfoType.Error);

                if (value < 0)
                {
                    if (outputDevice != null) outputDevice.Volume = 0f;
                }
                else
                {
                    if (outputDevice != null) outputDevice.Volume = 1f;
                }
            }
            else
            {
                if (outputDevice != null) outputDevice.Volume = value;
            }
        }
    }

    public AudioStream(string deviceName)
    {
        SelectOutputDevice(deviceName);
    }

    protected void SelectOutputDevice(string deviceName)
    {
        if (String.IsNullOrWhiteSpace(deviceName))
        {
            log.Print("Device name can`t be null", LogInfoType.Error);

            throw new ArgumentNullException(nameof(deviceName));
        }
        else
        {
            outputDevice = new WaveOutEvent()
            {
                DeviceNumber = DeviceControl.GetOutputDeviceId(deviceName)
            };

            outputDevice.PlaybackStopped += PlaybackStopped;

            log.Print("Device has been selected", LogInfoType.Info);
        }
    }

    protected virtual void PlaybackStopped(object? sender, EventArgs e)
    {
        if (StoppedEvent != null)
        {
            try
            {
                float unused = OutputDeviceVolume;
            }
            catch (Exception ex)
            {
                if (ex.Message == "NoDriver calling waveOutGetVolume")
                {
                    sender = null;
                    SelectOutputDevice(DeviceControl.GetOutputDeviceNameById(0));
                }
            }

            StoppedEvent(sender, e);
        }
    }

    public virtual void Play()
    {
        if (outputDevice != null)
        {
            log.Print($"Playing to {DeviceControl.GetOutputDeviceNameById(outputDevice.DeviceNumber)}.", LogInfoType.Info);

            outputDevice?.Play();
        }
    }

    public virtual void Stop()
    {
        if (outputDevice != null)
        {
            log.Print($"Stopped {DeviceControl.GetOutputDeviceNameById(outputDevice.DeviceNumber)}.", LogInfoType.Info);

            outputDevice?.Stop();
        }
    }

    public virtual void Pause()
    {
        if (outputDevice != null)
        {
            log.Print($"Paused {DeviceControl.GetOutputDeviceNameById(outputDevice.DeviceNumber)}.", LogInfoType.Info);

            outputDevice?.Pause();
        }
    }

    public virtual void CloseStream()
    {
        log.Print("Stream was closed", LogInfoType.Info);
        outputDevice?.Dispose();
    }

    public bool IsPlaying => outputDevice is { PlaybackState: PlaybackState.Playing };

    public bool IsPaused => outputDevice is { PlaybackState: PlaybackState.Paused };

    public bool IsStopped => outputDevice is { PlaybackState: PlaybackState.Stopped };

    public int GetOutputDeviceId()
    {
        if (outputDevice != null) return outputDevice.DeviceNumber;

        return -1;
    }
}

public class MusicStream : AudioStream
{
    private AudioFileReader? _audioFile;
    private string? _pathToMusic;

    private Equalizer? _equalizer;
    private EqualizerBand[]? _bands;

    private float _musicVolume;

    public float MusicVolume
    {
        get
        {
            if (_audioFile != null)
            {
                return _audioFile.Volume;
            }
            else
            {
                return _musicVolume;
            }
        }
        set
        {
            if (value < 0f || value > 1f)
            {
                log.Print("Volume < 0.0 or > 1.0", LogInfoType.Error);

                if (value < 0)
                {
                    _musicVolume = 0f;
                }
                else
                {
                    _musicVolume = 1f;
                }
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

    public MusicStream(string deviceName) : base(deviceName)
    {
    }

    public override void Play()
    {
        if (_pathToMusic != null)
        {
            if (!IsPlaying && !IsPaused)
            {
                _audioFile = new AudioFileReader(_pathToMusic);
                if (_equalizer != null)
                {
                    _equalizer = new Equalizer(_audioFile, _bands);
                    outputDevice?.Init(_equalizer);
                }
                else
                {
                    outputDevice?.Init(_audioFile);
                }
            }

            base.Play();
        }
    }

    public void StopAndPlayFromPosition(double startingPosition)
    {
        if (_pathToMusic != null)
        {
            float oldVol = MusicVolume;

            Stop();

            _audioFile = new AudioFileReader(_pathToMusic);
            _audioFile.CurrentTime = TimeSpan.FromSeconds(startingPosition);

            if (_equalizer != null)
            {
                _equalizer = new Equalizer(_audioFile, _bands);
                outputDevice?.Init(_equalizer);
            }
            else
            {
                outputDevice?.Init(_audioFile);
            }

            MusicVolume = oldVol;

            base.Play();
        }
    }

    public override void Stop()
    {
        base.Stop();

        Dispose();
    }

    private void Dispose()
    {
        if (_audioFile != null)
        {
            _audioFile?.Dispose();
            _audioFile = null;
        }
    }

    public override void CloseStream()
    {
        Stop();
        StopEqualizer();
        base.CloseStream();
    }

    public string? PathToMusic
    {
        get { return _pathToMusic; }
        set
        {
            if (!File.Exists(value))
            {
                log.Print("File does not exist", LogInfoType.Warning);

                throw new FileNotFoundException("File does not exist");
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

    public void InitializeEqualizer()
    {
        if (_audioFile != null)
        {
            _bands = new[]
            {
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 40, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 80, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 320, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 640, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 1280, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 2560, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 5120, Gain = 0 },
                new EqualizerBand { Bandwidth = 0.8f, Frequency = 10240, Gain = 0 },
            };

            _equalizer = new Equalizer(_audioFile, _bands);

            log.Print("Initialize equalizer", LogInfoType.Info);
        }
    }

    public void StopEqualizer()
    {
        _bands = null;

        _equalizer = null;

        log.Print("Stop equalizer", LogInfoType.Info);
    }

    public bool IsEqualizerWorking
    {
        get { return _equalizer != null; }
    }

    public float MinimumGain => -30;

    public float MaximumGain => 30;

    public float GetBandGain(int index)
    {
        if (_bands != null && index >= 0 && index <= 7)
        {
            return _bands[index].Gain;
        }
        else
        {
            return 0;
        }
    }

    public void SetBandGain(int index, float value)
    {
        if (_bands != null && index >= 0 && index <= 7)
        {
            if (Math.Abs(_bands[index].Gain - value) > 0)
            {
                _bands[index].Gain = value;
                _equalizer?.Update();
            }
        }
    }

    public List<EqualizerBand> GetBandsList()
    {
        List<EqualizerBand> equalizerBands = new List<EqualizerBand>();

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

    public void SetBandsList(List<EqualizerBand>? equalizerBandsToAdd)
    {
        if (equalizerBandsToAdd is { Count: 8 })
        {
            for (int i = 0; i < 8; i++)
            {
                SetBandGain(i, equalizerBandsToAdd[i].Gain);
            }
        }
    }

    public void ReselectOutputDevice(string deviceName)
    {
        if (IsPlaying)
        {
            double tempPosition = CurrentTrackPosition;
            float tempDeviceVolume = OutputDeviceVolume;
            float tempMusicVolume = MusicVolume;

            Stop();

            outputDevice?.Dispose();

            SelectOutputDevice(deviceName);

            StopAndPlayFromPosition(tempPosition);

            OutputDeviceVolume = tempDeviceVolume;
            MusicVolume = tempMusicVolume;
        }
        else
        {
            outputDevice?.Dispose();

            SelectOutputDevice(deviceName);
        }
    }
}

public class BandsSettings
{
    public List<EqualizerBand>? EqualizerBands;
    public string? Name;
}

public class MicrophoneStream : AudioStream
{
    private WaveInEvent? _waveSource;
    private WaveInProvider? _waveIn;
    private VolumeWaveProvider16? _waveInVolume;

    public float InputDeviceVolume
    {
        get
        {
            if (_waveInVolume != null) return _waveInVolume.Volume;

            return 0f;
        }
        set
        {
            if (value < 0f || value > 1f)
            {
                log.Print("Volume < 0.0 or > 1.0", LogInfoType.Error);

                if (value < 0)
                {
                    if (_waveInVolume != null) _waveInVolume.Volume = 0f;
                }
                else
                {
                    if (_waveInVolume != null) _waveInVolume.Volume = 1f;
                }
            }
            else
            {
                if (_waveInVolume != null) _waveInVolume.Volume = value;
            }
        }
    }

    public MicrophoneStream(string deviceIn, string deviceOut) : base(deviceOut)
    {
        SelectInputDevice(deviceIn);
    }

    public void SelectInputDevice(string deviceName)
    {
        if (String.IsNullOrWhiteSpace(deviceName))
        {
            log.Print("Name of input device can`t be null", LogInfoType.Error);

            throw new ArgumentNullException(nameof(deviceName));
        }
        else
        {
            _waveSource = new WaveInEvent()
            {
                DeviceNumber = DeviceControl.GetInputDeviceId(deviceName)
            };

            _waveSource.WaveFormat = new WaveFormat(44100,
                WaveIn.GetCapabilities(DeviceControl.GetInputDeviceId(deviceName)).Channels);

            log.Print("Input device has been selected", LogInfoType.Info);
        }
    }

    protected override void PlaybackStopped(object? sender, EventArgs e)
    {
        try
        {
            float unused = OutputDeviceVolume;
        }
        catch (Exception ex)
        {
            if (ex.Message == "NoDriver calling waveOutGetVolume")
            {
                Stop();
                SelectOutputDevice(DeviceControl.GetOutputDeviceNameById(0));
                Play();
            }
        }
    }

    public override void Play()
    {
        if (!IsPlaying && !IsPaused)
        {
            _waveIn = new WaveInProvider(_waveSource);
            _waveInVolume = new VolumeWaveProvider16(_waveIn);

            outputDevice?.Init(_waveInVolume);
        }

        _waveSource?.StartRecording();
        base.Play();
    }

    public override void Stop()
    {
        base.Stop();

        _waveSource?.StopRecording();
    }

    public override void Pause()
    {
        base.Pause();

        _waveSource?.StopRecording();
    }

    public override void CloseStream()
    {
        Stop();
        _waveSource?.Dispose();
        base.CloseStream();
    }

    public int GetInputDeviceId()
    {
        if (_waveSource != null) return _waveSource.DeviceNumber;

        return -1;
    }
}