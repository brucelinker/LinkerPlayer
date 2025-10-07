﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using ManagedBass;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace LinkerPlayer.ViewModels;

public partial class PlayerControlsViewModel : ObservableObject
{
    private readonly AudioEngine _audioEngine;
    private readonly PlaylistTabsViewModel _playlistTabsViewModel;
    private readonly ISettingsManager _settingsManager;
    private readonly SharedDataModel _sharedDataModel;
    private readonly ILogger<PlayerControlsViewModel> _logger;

    private double _volumeBeforeMute;

    public PlayerControlsViewModel(
        AudioEngine audioEngine,
        PlaylistTabsViewModel playlistTabsViewModel,
        ISettingsManager settingsManager,
        SharedDataModel sharedDataModel,
        ILogger<PlayerControlsViewModel> logger)
    {
        _audioEngine = audioEngine;
        _playlistTabsViewModel = playlistTabsViewModel;
        _settingsManager = settingsManager;
        _sharedDataModel = sharedDataModel;
        _logger = logger;

        try
        {
            VolumeSliderValue = _settingsManager.Settings.VolumeSliderValue;
            _volumeBeforeMute = VolumeSliderValue;

            _settingsManager.SettingsChanged += OnSettingsChanged;
            WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (_, m) =>
            {
                OnPlaybackStateChanged(m.Value);
            });

            _logger.LogInformation("PlayerControlsViewModel initialized successfully");
        }
        catch (IOException ex)
        {
            _logger.LogError("IO error in PlayerControlsViewModel constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error in PlayerControlsViewModel constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
    }

    [ObservableProperty] private PlaybackState _state;
    [ObservableProperty] private bool _shuffleMode;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private double _volumeSliderValue;

    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private int _processedTracks;
    [ObservableProperty] private int _totalTracks;
    [ObservableProperty] private string _status = string.Empty;

    public event Action? UpdateSelectedTrack;

    public MediaFile? SelectedTrack
    {
        get => _sharedDataModel.SelectedTrack;
        set
        {
            _sharedDataModel.UpdateSelectedTrack(value!);
            UpdateSelectedTrack?.Invoke();
        }
    }

    public MediaFile? ActiveTrack
    {
        get => _sharedDataModel.ActiveTrack;
        set => _sharedDataModel.UpdateActiveTrack(value!);
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        PlayPauseTrack();
    }

    partial void OnShuffleModeChanged(bool value)
    {
        _settingsManager.Settings.ShuffleMode = value;
        _settingsManager.SaveSettings(nameof(AppSettings.ShuffleMode));
        //_logger.LogInformation("ShuffleMode changed to {Value}", value);
        WeakReferenceMessenger.Default.Send(new ShuffleModeMessage(value));
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (value)
        {
            _volumeBeforeMute = VolumeSliderValue > 0 ? VolumeSliderValue : _volumeBeforeMute;
        }
        WeakReferenceMessenger.Default.Send(new IsMutedMessage(value));
    }

    partial void OnVolumeSliderValueChanged(double value)
    {
        if (value > 0 && IsMuted)
        {
            IsMuted = false; // Unmute if slider is moved up
        }
        else if (value == 0 && !IsMuted)
        {
            IsMuted = true; // Mute if slider is set to 0
        }

        if (!IsMuted)
        {
            _audioEngine.MusicVolume = (float)value / 100;
            _volumeBeforeMute = value;
        }
        else
        {
            _audioEngine.MusicVolume = 0;
        }

        _settingsManager.Settings.VolumeSliderValue = value;
        _settingsManager.SaveSettings(nameof(AppSettings.VolumeSliderValue));
    }

    private void OnSettingsChanged(string propertyName)
    {
        if (propertyName == nameof(AppSettings.ShuffleMode))
            ShuffleMode = _settingsManager.Settings.ShuffleMode;
        if (propertyName == nameof(AppSettings.VolumeSliderValue))
        {
            VolumeSliderValue = _settingsManager.Settings.VolumeSliderValue;
            if (!IsMuted)
            {
                _volumeBeforeMute = VolumeSliderValue;
            }
        }
    }

    private void OnPlaybackStateChanged(PlaybackState playbackState)
    {
        State = playbackState;
    }

    public double GetVolumeBeforeMute()
    {
        return _volumeBeforeMute > 0 ? _volumeBeforeMute : 50; // Fallback to 50 if 0
    }

    public void UpdateVolumeAfterAnimation(double value, bool isMuted)
    {
        VolumeSliderValue = value;
        if (!isMuted)
        {
            _audioEngine.MusicVolume = (float)value / 100;
            _volumeBeforeMute = value;
        }
        else
        {
            _audioEngine.MusicVolume = 0;
        }
    }

    public void SaveSettingsOnShutdown(double volumeValue, double seekBarValue)
    {
        _settingsManager.Settings.VolumeSliderValue = volumeValue;
        _settingsManager.SaveSettings(nameof(AppSettings.VolumeSliderValue));
        //_logger.LogInformation("Saved shutdown settings: Volume={Volume}, SeekBar={SeekBar}", volumeValue, seekBarValue);
    }

    public void PlayPauseTrack()
    {
        // Ensure SelectedTrack is set
        SelectedTrack = _playlistTabsViewModel.SelectedTrack ?? _playlistTabsViewModel.SelectFirstTrack();

        if (_audioEngine.IsPlaying)
        {
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
                ResumeTrack();
            }
            else
            {
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
            _audioEngine.PathToMusic = SelectedTrack.Path;
            _audioEngine.Play();

            if (ActiveTrack == null)
            {
                ActiveTrack = SelectedTrack;
                ActiveTrack.State = PlaybackState.Playing;
                State = PlaybackState.Playing;
                
                WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(ActiveTrack));
            }
        }
        else
        {
            _logger.LogWarning("Cannot play: Both ActiveTrack and SelectedTrack are null");
        }

        WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(State));
        WeakReferenceMessenger.Default.Send(new ActiveTrackChangedMessage(ActiveTrack));
    }

    public void ResumeTrack()
    {
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
        _audioEngine.Stop();
        State = PlaybackState.Stopped;

        if (ActiveTrack != null)
        {
            ActiveTrack.State = PlaybackState.Stopped;
            ActiveTrack = null;
        }

        WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(State));
        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));
    }

    [RelayCommand]
    private void Prev()
    {
        PreviousTrack();
    }

    public void PreviousTrack()
    {
        StopTrack();

        MediaFile? prevMediaFile = _playlistTabsViewModel.PreviousMediaFile();

        if (prevMediaFile == null || !File.Exists(prevMediaFile.Path))
        {
            _logger.LogError("MediaFile not found for previous track.");
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
        StopTrack();

        MediaFile? nextMediaFile = _playlistTabsViewModel.NextMediaFile();

        if (nextMediaFile == null || !File.Exists(nextMediaFile.Path))
        {
            _logger.LogError("MediaFile not found for next track.");
            return;
        }

        ActiveTrack = nextMediaFile;
        PlayTrack();
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