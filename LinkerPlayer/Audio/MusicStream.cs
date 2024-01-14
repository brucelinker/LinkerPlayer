using NAudio.Extras;
using NAudio.Wave;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;

namespace LinkerPlayer.Audio;

public class MusicStream : AudioStream
{
    private AudioFileReader? _audioFile;
    private string? _pathToMusic;

    private Equalizer? _equalizer;
    private EqualizerBand[]? _bands;


    public MusicStream(string deviceName) : base(deviceName)
    {
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
            if (value < 0f || value > 1f)
            {
                Log.Error("Volume < 0.0 or > 1.0");
                
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
                    OutputDevice?.Init(_equalizer);
                }
                else
                {
                    OutputDevice?.Init(_audioFile);
                }
            }

            base.Play();
        }
        Log.Information("MusicStream - Play");
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
                OutputDevice?.Init(_equalizer);
            }
            else
            {
                OutputDevice?.Init(_audioFile);
            }

            MusicVolume = oldVol;

            base.Play();
        }
        Log.Information("MusicStream - StopAndPlayFromPosition");

    }
    public void StopAndResetPosition()
    {
        if (_pathToMusic != null)
        {
            Stop();

            _audioFile = new AudioFileReader(_pathToMusic);
            _audioFile.CurrentTime = TimeSpan.FromSeconds(0);
        }

        Log.Information("MusicStream - StopAndResetPosition");
    }

    public override void Stop()
    {
        base.Stop();

        Dispose();
        Log.Information("MusicStream - Stop");
    }

    private void Dispose()
    {
        if (_audioFile != null)
        {
            _audioFile?.Dispose();
            _audioFile = null;
        }
        Log.Information("MusicStream - Dispose");
    }

    public override void CloseStream()
    {
        Stop();
        StopEqualizer();
        base.CloseStream();
        Log.Information("MusicStream - CloseStream");
    }

    public string? PathToMusic
    {
        get => _pathToMusic;
        set
        {
            if (!File.Exists(value))
            {
                string fileDoesNotExist = "File does not exist";

                Log.Warning(fileDoesNotExist);
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

            Log.Information("MusicStream - Initialize equalizer");
        }
    }

    public void StopEqualizer()
    {
        _bands = null;

        _equalizer = null;

        Log.Information("MusicStream - Stop equalizer");
    }

    public bool IsEqualizerWorking => _equalizer != null;

    public float MinimumGain => -30;

    public float MaximumGain => 30;

    public float GetBandGain(int index)
    {
        // Log.Information("MusicStream - GetBandGain");

        if (_bands != null && index is >= 0 and <= 7)
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
        if (_bands != null && index is >= 0 and <= 7)
        {
            if (Math.Abs(_bands[index].Gain - value) > 0)
            {
                _bands[index].Gain = value;
                _equalizer?.Update();
            }
        }
        Log.Information("MusicStream - SetBandGain");
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

        Log.Information("MusicStream - GetBandsList");
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
        Log.Information("MusicStream - SetBandsList");
    }

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
        Log.Information("MusicStream - ReselectOutputDevice");
    }
}