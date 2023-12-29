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

    public void Stop()
    {
        MainMusic?.StopAndResetPosition();
    }

    public void Play()
    {
        MainMusic?.Play();
    }

    public void Pause()
    {
        MainMusic?.Pause();
    }

    public void StopAndPlayFromPosition(double startingPosition)
    {
        MainMusic?.StopAndPlayFromPosition(startingPosition);

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
        }
    }

    public void Seek(double offset)
    {
        MainMusic?.Seek(offset);
    }

    public void InitializeEqualizer(string? selectedBandName = null)
    {
        MainMusic?.InitializeEqualizer();

        if (MainMusic is { IsEqualizerWorking: false })
        {
            _delayedEqualizerInitialization = true;
            _selectedBandName = selectedBandName;
        }
    }

    public void StopEqualizer()
    {
        MainMusic?.StopEqualizer();
    }

    public void SetBandGain(int index, float value)
    {
        MainMusic?.SetBandGain(index, value);
    }

    public void SetBandsList(List<EqualizerBand>? equalizerBandsToAdd)
    {
        MainMusic?.SetBandsList(equalizerBandsToAdd);
    }
}