using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using ManagedBass;
using Serilog;
using System;
using System.IO;

namespace LinkerPlayer.ViewModels;

public partial class PlayerControlsViewModel : BaseViewModel
{
    private readonly SettingsManager _settingsManager;
    private readonly AudioEngine _audioEngine;
    private readonly PlaylistTabsViewModel _playlistTabsViewModel;
    private readonly EqualizerViewModel _equalizerViewModel;
    private double _volumeBeforeMute;

    public PlayerControlsViewModel(
        SettingsManager settingsManager,
        AudioEngine audioEngine,
        PlaylistTabsViewModel playlistTabsViewModel,
        EqualizerViewModel equalizerViewModel)
    {
        _settingsManager = settingsManager;
        _audioEngine = audioEngine;
        _playlistTabsViewModel = playlistTabsViewModel;
        _equalizerViewModel = equalizerViewModel;

        ShuffleMode = _settingsManager.Settings.ShuffleMode;
        VolumeSliderValue = _settingsManager.Settings.VolumeSliderValue;

        _settingsManager.SettingsChanged += OnSettingsChanged;
        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (_, m) =>
        {
            OnPlaybackStateChanged(m.Value);
        });

        WeakReferenceMessenger.Default.Register<PlaybackStoppedMessage>(this, (_, m) =>
        {
            OnAudioStopped(m.Value);
        });
    }

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private PlaybackState _state;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private bool _shuffleMode;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private bool _isMuted;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private double _volumeSliderValue;

    // Direct access to other ViewModels
    public PlaylistTabsViewModel PlaylistTabs => _playlistTabsViewModel;
    public EqualizerViewModel Equalizer => _equalizerViewModel;

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        PlayPauseTrack();
    }

    partial void OnShuffleModeChanged(bool value)
    {
        _settingsManager.Settings.ShuffleMode = value;
        _settingsManager.SaveSettings(nameof(AppSettings.ShuffleMode));
        Log.Information("ShuffleMode changed to {Value}", value);
        WeakReferenceMessenger.Default.Send(new ShuffleModeMessage(value));
    }

    partial void OnVolumeSliderValueChanged(double value)
    {
        _audioEngine.MusicVolume = (float)value / 100;
        _settingsManager.Settings.VolumeSliderValue = value;
        _settingsManager.SaveSettings(nameof(AppSettings.VolumeSliderValue));
        Log.Information("VolumeSliderValue changed to {Value}", value);
    }

    private void OnSettingsChanged(string propertyName)
    {
        if (propertyName == nameof(AppSettings.ShuffleMode))
            ShuffleMode = _settingsManager.Settings.ShuffleMode;
        if (propertyName == nameof(AppSettings.VolumeSliderValue))
            VolumeSliderValue = _settingsManager.Settings.VolumeSliderValue;
    }

    public void SaveSettingsOnShutdown(double volumeValue, double seekBarValue)
    {
        _settingsManager.Settings.VolumeSliderValue = volumeValue;
        _settingsManager.SaveSettings(nameof(AppSettings.VolumeSliderValue));
        Log.Information("Saved shutdown settings: Volume={Volume}, SeekBar={SeekBar}", volumeValue, seekBarValue);
    }

    public void PlayPauseTrack()
    {
        Log.Information("PlayPauseTrack called");

        // Ensure SelectedTrack is set
        SelectedTrack = _playlistTabsViewModel.SelectedTrack ?? _playlistTabsViewModel.SelectFirstTrack();
        Log.Information($"SelectedTrack: {(SelectedTrack != null ? SelectedTrack.Path : "null")}");

        if (_audioEngine.IsPlaying)
        {
            Log.Information("Track is playing, pausing");
            _audioEngine.Pause();
            State = PlaybackState.Paused;

            if (ActiveTrack != null)
            {
                ActiveTrack.State = PlaybackState.Paused;
            }
        }
        else
        {
            if (ActiveTrack?.State == PlaybackState.Paused)
            {
                Log.Information("Track is paused, resuming");
                ResumeTrack();
            }
            else
            {
                Log.Information("Track is stopped, playing");
                PlayTrack();
            }
        }
    }

    public void PlayTrack()
    {
        if (ActiveTrack != null)
        {
            _audioEngine.PathToMusic = ActiveTrack.Path;
            _audioEngine.Play();
            ActiveTrack.State = PlaybackState.Playing;
            State = PlaybackState.Playing;
        }
        else if (SelectedTrack != null)
        {
            Log.Information($"Playing SelectedTrack: {SelectedTrack.Path}");
            _audioEngine.PathToMusic = SelectedTrack.Path;
            _audioEngine.Play();

            if (ActiveTrack == null)
            {
                ActiveTrack = SelectedTrack;
                ActiveTrack.State = PlaybackState.Playing;
                State = PlaybackState.Playing;
            }
        }
        else
        {
            Log.Warning("Cannot play: Both ActiveTrack and SelectedTrack are null");
        }

        WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(State));
        WeakReferenceMessenger.Default.Send(new ActiveTrackChangedMessage(ActiveTrack));
    }

    public void ResumeTrack()
    {
        Log.Information("ResumeTrack called");
        _audioEngine.ResumePlay();
        State = PlaybackState.Playing;

        if (ActiveTrack != null)
        {
            ActiveTrack.State = PlaybackState.Playing;
        }

        WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(PlaybackState.Playing));
    }

    [RelayCommand]
    private void Stop()
    {
        StopTrack();
    }

    public void StopTrack()
    {
        Log.Information("StopTrack called");
        _audioEngine.Stop();
        State = PlaybackState.Stopped;

        if (ActiveTrack != null)
        {
            ActiveTrack.State = PlaybackState.Stopped;
            ActiveTrack = null;
        }

        WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(State));
    }

    [RelayCommand]
    private void Prev()
    {
        PreviousTrack();
    }

    public void PreviousTrack()
    {
        Log.Information("PreviousTrack called");
        MediaFile? prevMediaFile = _playlistTabsViewModel.PreviousMediaFile();

        if (prevMediaFile == null || !File.Exists(prevMediaFile.Path))
        {
            Log.Error("MediaFile not found for previous track.");
            return;
        }

        ActiveTrack = prevMediaFile;
        PlayTrack();
    }

    [RelayCommand]
    private void Next()
    {
        NextTrack();
    }

    public void NextTrack()
    {
        Log.Information("NextTrack called");
        MediaFile? nextMediaFile = _playlistTabsViewModel.NextMediaFile();

        if (nextMediaFile == null || !File.Exists(nextMediaFile.Path))
        {
            Log.Error("MediaFile not found for next track.");
            return;
        }

        ActiveTrack = nextMediaFile;
        PlayTrack();
    }

    private void OnPlaybackStateChanged(PlaybackState playbackState)
    {
        State = playbackState;
    }

    private void OnAudioStopped(bool trackEnded)
    {
        Log.Information($"Audio stopped, trackEnded: {trackEnded}");
        if (trackEnded)
        {
            NextTrack();
        }
        else
        {
            _audioEngine.Stop();
        }
    }

    [RelayCommand]
    private void Mute(bool isMuted)
    {
        IsMuted = isMuted;
        WeakReferenceMessenger.Default.Send(new MuteMessage(isMuted));
    }

    private bool CanPlayPause()
    {
        return true;
    }

    public double CurrentSeekbarPosition()
    {
        MonitorNextTrack();

        double length = _audioEngine.CurrentTrackLength;
        double position = _audioEngine.CurrentTrackPosition;

        if (length <= 0 || double.IsNaN(position) || double.IsNaN(length))
        {
            return 0;
        }

        double percentage = (position / length) * 100;
        if (double.IsNaN(percentage) || double.IsInfinity(percentage))
        {
            return 0;
        }

        return percentage;
    }

    private void MonitorNextTrack()
    {
        double length = _audioEngine.CurrentTrackLength;
        double position = _audioEngine.CurrentTrackPosition;

        if (length > 0 && position + 10.0 > length)
        {
            if (_audioEngine.GetDecibelLevel() <= -50 || position + 0.5 > length)
                NextTrack();
        }
    }
}
