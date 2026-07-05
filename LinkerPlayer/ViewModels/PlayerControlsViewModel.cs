using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Services.Playback;
using ManagedBass;
using Microsoft.Extensions.Logging;
using System.IO;

namespace LinkerPlayer.ViewModels;

public interface IPlayerControlsViewModel
{
    PlaybackState State { get; }
    bool ShuffleMode { get; set; }
    bool IsMuted { get; set; }
    double VolumeSliderValue { get; set; }
    MediaFile? SelectedTrack { get; set; }
    MediaFile? ActiveTrack { get; set; }
    event Action? UpdateSelectedTrack;
    void PlayPauseTrack();
    void PlayTrack();
    void StopTrack();
    void PreviousTrack();
    void NextTrack();
    double CurrentSeekbarPosition();
    double GetVolumeBeforeMute();
    void UpdateVolumeAfterAnimation(double value, bool isMuted);
    void SaveSettingsOnShutdown(double volumeValue, double seekBarValue);
}

public partial class PlayerControlsViewModel : ObservableObject, IPlayerControlsViewModel
{
    private readonly IAudioEngine _audioEngine;
    private readonly MediaTabViewModel _playlistTabsViewModel; // TODO: change to interface once selection APIs are exposed
    private readonly IPlaybackCoordinator _playbackCoordinator;
    private readonly ISettingsManager _settingsManager;
    private readonly ISharedDataModel _sharedDataModel; // switched to interface
    private readonly ILogger<PlayerControlsViewModel> _logger;

    private double _volumeBeforeMute;

    // Guard to prevent re-entrant Next/Prev during transitions
    private bool _isNavigatingTrack = false;

    public PlayerControlsViewModel(
        IAudioEngine audioEngine,
        MediaTabViewModel mediaTabViewModel,
        IPlaybackCoordinator playbackCoordinator,
        ISettingsManager settingsManager,
        ISharedDataModel sharedDataModel,
        ILogger<PlayerControlsViewModel> logger)
    {
        _audioEngine = audioEngine;
        _playlistTabsViewModel = mediaTabViewModel;
        _playbackCoordinator = playbackCoordinator;
        _settingsManager = settingsManager;
        _sharedDataModel = sharedDataModel; // will be fixed by compiler if name mismatch
        _logger = logger;

        try
        {
            VolumeSliderValue = _settingsManager.Settings.VolumeSliderValue;
            _volumeBeforeMute = VolumeSliderValue;

            ShuffleMode = _settingsManager.Settings.ShuffleMode;
            IsMuted = _settingsManager.Settings.VolumeSliderValue == 0;

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
        set { if (value != null) { _sharedDataModel.UpdateSelectedTrack(value); } UpdateSelectedTrack?.Invoke(); }
    }

    public MediaFile? ActiveTrack
    {
        get => _sharedDataModel.ActiveTrack;
        set
        {
            if (value != null)
            {
                _sharedDataModel.UpdateActiveTrack(value);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        _logger.LogDebug("PlayPauseCommand executed");
        PlayPauseTrack();
    }

    [RelayCommand]
    private void Stop()
    {
        _logger.LogInformation("StopCommand executed");
        StopTrack();
    }

    [RelayCommand]
    private void Next()
    {
        _logger.LogInformation("NextCommand executed");
        NextTrack();
    }

    [RelayCommand]
    private void Prev()
    {
        _logger.LogInformation("PrevCommand executed");
        PreviousTrack();
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
        {
            ShuffleMode = _settingsManager.Settings.ShuffleMode;
        }

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
        _logger.LogDebug("PlaybackStateChangedMessage received: {State}", playbackState);
        State = playbackState;
    }

    private bool CanPlayPause()
    {
        return true;
    }

    public double GetVolumeBeforeMute()
    {
        return _volumeBeforeMute > 0 ? _volumeBeforeMute : 50;
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
    }

    public void PlayPauseTrack()
    {
        SelectedTrack = _playlistTabsViewModel.SelectedTrack ?? _playlistTabsViewModel.SelectFirstTrack();

        string playlistName = _playlistTabsViewModel.SelectedTab?.Name ?? "";
        int trackIndex = _playlistTabsViewModel.SelectedTrackIndex;

        if (_audioEngine.IsPlaying)
        {
            _playbackCoordinator.Pause();
            State = PlaybackState.Paused;
        }
        else
        {
            if (State == PlaybackState.Paused)
            {
                _playbackCoordinator.Resume();
                State = PlaybackState.Playing;
            }
            else
            {
                if (SelectedTrack != null && !string.IsNullOrWhiteSpace(playlistName))
                {
                    _playbackCoordinator.SetUserSelection(playlistName, trackIndex, SelectedTrack);
                    _playbackCoordinator.PlayTrack(playlistName, trackIndex, SelectedTrack);

                    MediaFile? active = _sharedDataModel.ActiveTrack;
                    if (active != null)
                    {
                        ActiveTrack = active;
                    }

                    State = PlaybackState.Playing;
                }
                else
                {
                    _logger.LogWarning("PlayPauseTrack ignored: missing playlist or selected track (PlaylistName={PlaylistName}, Track={Track})", playlistName, SelectedTrack?.Id ?? "null");
                }
            }
        }
    }

    public void PlayTrack()
    {
        SelectedTrack = _playlistTabsViewModel.SelectedTrack ?? _playlistTabsViewModel.SelectFirstTrack();

        _logger.LogInformation("PlayTrack called - ActiveTrack: {ActiveTrack}, SelectedTrack: {SelectedTrack}",
            ActiveTrack?.Title ?? "null", SelectedTrack?.Title ?? "null");

        try
        {
            if (SelectedTrack != null)
            {
                _logger.LogInformation("Playing SelectedTrack: {Path}", SelectedTrack.Path);

                string playlistName = _playlistTabsViewModel.SelectedTab?.Name ?? "";
                int trackIndex = _playlistTabsViewModel.SelectedTrackIndex;

                if (!string.IsNullOrWhiteSpace(playlistName))
                {
                    _playbackCoordinator.SetUserSelection(playlistName, trackIndex, SelectedTrack);
                }

                _playbackCoordinator.PlayTrack(playlistName, trackIndex, SelectedTrack);

                MediaFile? active = _sharedDataModel.ActiveTrack;
                if (active != null)
                {
                    ActiveTrack = active;
                }

                State = PlaybackState.Playing;

                if (ActiveTrack == null)
                {
                    _logger.LogWarning("PlayTrack: ActiveTrack is null");
                }
            }
            else
            {
                _logger.LogWarning("Cannot play: SelectedTrack is null");
            }
        }
        finally
        {
            _isNavigatingTrack = false;
        }
    }

    public void StopTrack()
    {
        _playbackCoordinator.Stop();
        State = PlaybackState.Stopped;
        ActiveTrack = null;
    }

    public void PreviousTrack()
    {
        if (_isNavigatingTrack)
        {
            _logger.LogDebug("PreviousTrack ignored: navigation in progress");
            return;
        }

        _isNavigatingTrack = true;
        try
        {
            _logger.LogInformation("PreviousTrack invoked (State={State}, ActiveTrack={ActiveTrack}, SelectedPlaylist={SelectedPlaylist})",
                State,
                ActiveTrack?.Id ?? "null",
                _playlistTabsViewModel.SelectedTab?.Name ?? "null");

            string playlistName = _playlistTabsViewModel.SelectedTab?.Name ?? string.Empty;
            MediaFile? seedTrack = _playlistTabsViewModel.SelectedTrack;
            if (!string.IsNullOrWhiteSpace(playlistName) && seedTrack != null)
            {
                int seedIndex = _playlistTabsViewModel.SelectedTrackIndex;
                _playbackCoordinator.SetUserSelection(playlistName, seedIndex, seedTrack);
            }

            _playbackCoordinator.Prev();
        }
        finally
        {
            _isNavigatingTrack = false;
        }
    }

    public void NextTrack()
    {
        if (_isNavigatingTrack)
        {
            _logger.LogDebug("NextTrack ignored: navigation in progress");
            return;
        }

        _isNavigatingTrack = true;
        try
        {
            _logger.LogInformation("NextTrack invoked (State={State}, ActiveTrack={ActiveTrack}, SelectedPlaylist={SelectedPlaylist})",
                State,
                ActiveTrack?.Id ?? "null",
                _playlistTabsViewModel.SelectedTab?.Name ?? "null");

            string playlistName = _playlistTabsViewModel.SelectedTab?.Name ?? string.Empty;
            MediaFile? seedTrack = _playlistTabsViewModel.SelectedTrack;
            if (!string.IsNullOrWhiteSpace(playlistName) && seedTrack != null)
            {
                int seedIndex = _playlistTabsViewModel.SelectedTrackIndex;
                _playbackCoordinator.SetUserSelection(playlistName, seedIndex, seedTrack);
            }

            _playbackCoordinator.Next();
        }
        finally
        {
            _isNavigatingTrack = false;
        }
    }

    public double CurrentSeekbarPosition()
    {
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
}
