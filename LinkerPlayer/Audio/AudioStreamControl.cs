using NAudio.Extras;
using System;
using System.Collections.Generic;
using System.Linq;
using LinkerPlayer.Core;
using LinkerPlayer.Core.Log;

namespace LinkerPlayer.Audio;

public class AudioStreamControl
{
    protected ILog Log = LogSettings.SelectedLog;

    public MusicStream? MainMusic;
    public MusicStream? AdditionalMusic;

    public MicrophoneStream? Microphone;

    private bool _delayedEqualizerInitialization;
    private string? _selectedBandName;

    public AudioStreamControl(string mainOutputDevice)
    {
        if (string.IsNullOrWhiteSpace(mainOutputDevice))
        {
            Log.Print("Device name can`t be null", LogInfoType.Error);
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
            Log.Print("Device name can`t be null", LogInfoType.Error);
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
                Log.Print("MainMusic should be initialized", LogInfoType.Error);
            }
        }
    }

    public void ActivateMic(string inputDevice, string outputDevice)
    {
        if (String.IsNullOrWhiteSpace(inputDevice) || String.IsNullOrWhiteSpace(outputDevice))
        {
            Log.Print("Device name can`t be null", LogInfoType.Error);
        }
        else
        {
            Microphone = new MicrophoneStream(inputDevice, outputDevice);

            Microphone.Play();
        }
    }

    public void Stop()
    {
        MainMusic?.Stop();

        if (AdditionalMusic != null)
        {
            AdditionalMusic.Stop();
        }
    }

    public void Play()
    {
        MainMusic?.Play();

        if (AdditionalMusic != null)
        {
            AdditionalMusic.Play();
        }
    }

    public void Pause()
    {
        MainMusic?.Pause();

        if (AdditionalMusic != null)
        {
            AdditionalMusic.Pause();
        }
    }

    public void StopAndPlayFromPosition(double startingPosition)
    {
        MainMusic?.StopAndPlayFromPosition(startingPosition);

        if (AdditionalMusic != null)
        {
            AdditionalMusic.StopAndPlayFromPosition(startingPosition);
        }

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

                    Log.Print("Profile has been selected", LogInfoType.Info);
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

        if (AdditionalMusic != null)
        {
            AdditionalMusic.Seek(offset);
        }
    }

    public void InitializeEqualizer(string? selectedBandName = null)
    {
        MainMusic?.InitializeEqualizer();

        if (AdditionalMusic != null)
        {
            AdditionalMusic.InitializeEqualizer();
        }

        if (MainMusic is { IsEqualizerWorking: false })
        {
            _delayedEqualizerInitialization = true;
            _selectedBandName = selectedBandName;
        }
    }

    public void StopEqualizer()
    {
        MainMusic?.StopEqualizer();

        if (AdditionalMusic != null)
        {
            AdditionalMusic.StopEqualizer();
        }
    }

    public void SetBandGain(int index, float value)
    {
        MainMusic?.SetBandGain(index, value);

        if (AdditionalMusic != null)
        {
            AdditionalMusic.SetBandGain(index, value);
        }
    }

    public void SetBandsList(List<EqualizerBand>? equalizerBandsToAdd)
    {
        MainMusic?.SetBandsList(equalizerBandsToAdd);

        if (AdditionalMusic != null)
        {
            AdditionalMusic.SetBandsList(equalizerBandsToAdd);
        }
    }
}