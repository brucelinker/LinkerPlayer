using LinkerPlayer.Core;
using NAudio.Extras;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LinkerPlayer.Audio;

public class AudioStreamControl
{
    public MusicStream? MainMusic;
    public MusicStream? AdditionalMusic;

    private bool _delayedEqualizerInitialization;
    private string? _selectedBandName;

    public AudioStreamControl(string mainOutputDevice)
    {
        if (string.IsNullOrWhiteSpace(mainOutputDevice))
        {
            Log.Error("Device name can`t be null");
        }
        else
        {
            MainMusic = new MusicStream(mainOutputDevice);
        }
    }

    public void ActivateAdditionalMusic(string additionalOutputDevice)
    {
        if (string.IsNullOrWhiteSpace(additionalOutputDevice))
        {
            Log.Error("Device name can`t be null");
        }
        else
        {
            if (MainMusic != null)
            {
                if (AdditionalMusic == null)
                {
                    AdditionalMusic = new MusicStream(additionalOutputDevice);

                    if (PathToMusic != null)
                    {
                        AdditionalMusic.PathToMusic = PathToMusic;

                        if (MainMusic.IsPlaying)
                        {
                            StopAndPlayFromPosition(CurrentTrackPosition);
                        }
                    }
                }
                else
                {
                    AdditionalMusic.ReselectOutputDevice(additionalOutputDevice);
                }
            }
            else
            {
                Log.Error("MainMusic should be initialized");
            }
        }
    }

    public void Stop()
    {
        MainMusic?.Stop();
        AdditionalMusic?.Stop();
    }

    public void Play()
    {
        MainMusic?.Play();
        AdditionalMusic?.Play();
    }

    public void Pause()
    {
        MainMusic?.Pause();
        AdditionalMusic?.Pause();
    }

    public void StopAndPlayFromPosition(double startingPosition)
    {
        MainMusic?.StopAndPlayFromPosition(startingPosition);

        AdditionalMusic?.StopAndPlayFromPosition(startingPosition);

        if (_delayedEqualizerInitialization && !String.IsNullOrEmpty(_selectedBandName))
        {
            InitializeEqualizer();

            if (MainMusic is { IsEqualizerWorking: true })
            {
                EqualizerLibrary.LoadFromJson();

                var band = EqualizerLibrary.BandsSettings!.FirstOrDefault(n => n.Name == _selectedBandName);

                if (band != null)
                {
                    SetBandsList(band.EqualizerBands);

                    Log.Information("Profile has been selected");
                }
            }
        }

        _delayedEqualizerInitialization = false;
    }

    public string? PathToMusic
    {
        get => MainMusic?.PathToMusic;
        set
        {
            if (MainMusic != null) MainMusic.PathToMusic = value;

            if (AdditionalMusic != null)
            {
                AdditionalMusic.PathToMusic = value;
            }
        }
    }

    public double CurrentTrackLength
    {
        get
        {
            if (MainMusic != null) return MainMusic.CurrentTrackLength;
            return 0;
        }
    }

    public double CurrentTrackPosition
    {
        get
        {
            if (MainMusic != null) return MainMusic.CurrentTrackPosition;
            return 0;
        }
        set
        {
            if (MainMusic != null) MainMusic.CurrentTrackPosition = value;

            if (AdditionalMusic != null)
            {
                AdditionalMusic.CurrentTrackPosition = value;
            }
        }
    }

    public void Seek(double offset)
    {
        MainMusic?.Seek(offset);
        AdditionalMusic?.Seek(offset);
    }

    public void InitializeEqualizer(string? selectedBandName = null)
    {
        MainMusic?.InitializeEqualizer();

        AdditionalMusic?.InitializeEqualizer();

        if (MainMusic is { IsEqualizerWorking: false })
        {
            _delayedEqualizerInitialization = true;
            _selectedBandName = selectedBandName;
        }
    }

    public void StopEqualizer()
    {
        MainMusic?.StopEqualizer();
        AdditionalMusic?.StopEqualizer();
    }

    public void SetBandGain(int index, float value)
    {
        MainMusic?.SetBandGain(index, value);
        AdditionalMusic?.SetBandGain(index, value);
    }

    public void SetBandsList(List<EqualizerBand>? equalizerBandsToAdd)
    {
        MainMusic?.SetBandsList(equalizerBandsToAdd);
        AdditionalMusic?.SetBandsList(equalizerBandsToAdd);
    }
}