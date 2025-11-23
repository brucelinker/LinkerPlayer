using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
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
    private readonly AudioEngine _audioEngine;
    private readonly PlaylistTabsViewModel _playlistTabsViewModel;
    private readonly ISettingsManager _settingsManager;
    private readonly ISharedDataModel _sharedDataModel; // switched to interface
    private readonly ILogger<PlayerControlsViewModel> _logger;

    private double _volumeBeforeMute;

    // Guard to prevent re-entrant Next/Prev during transitions
    private bool _isNavigatingTrack = false;

    public PlayerControlsViewModel(
        AudioEngine audioEngine,
        PlaylistTabsViewModel playlistTabsViewModel,
        ISettingsManager settingsManager,
        ISharedDataModel sharedDataModel,
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
        _logger.LogInformation("PlayTrack called - ActiveTrack: {ActiveTrack}, SelectedTrack: {SelectedTrack}",
            ActiveTrack?.Title ?? "null", SelectedTrack?.Title ?? "null");

        try
        {
            if (ActiveTrack != null)
            {
                _logger.LogInformation("Playing ActiveTrack: {Path}", ActiveTrack.Path);
                _audioEngine.PathToMusic = ActiveTrack.Path;

                // Offload heavy audio start to background to keep UI responsive
                _ = Task.Run(() => _audioEngine.Play());

                ActiveTrack.State = PlaybackState.Playing;
                State = PlaybackState.Playing;
            }
            else if (SelectedTrack != null)
            {
                _logger.LogInformation("Playing SelectedTrack: {Path}", SelectedTrack.Path);
                _audioEngine.PathToMusic = SelectedTrack.Path;

                // Offload heavy audio start to background to keep UI responsive
                _ = Task.Run(() => _audioEngine.Play());

                if (ActiveTrack == null)
                {
                    ActiveTrack = SelectedTrack;
                    ActiveTrack.State = PlaybackState.Playing;
                    State = PlaybackState.Playing;
                }
            }
            else
            {
                _logger.LogWarning("Cannot play: Both ActiveTrack and SelectedTrack are null");
            }
        }
        finally
        {
            // Always clear navigation guard after attempting to start playback
            _isNavigatingTrack = false;
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
        // Push zeroed FFT first to drop visuals immediately across Next/Prev
        _audioEngine.NextTrackPreStopVisuals();

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
        if (_isNavigatingTrack)
        {
            _logger.LogDebug("PreviousTrack ignored: navigation in progress");
            return;
        }
        _isNavigatingTrack = true;

        StopTrack();

        MediaFile? prevMediaFile = _playlistTabsViewModel.PreviousMediaFile();

        if (prevMediaFile == null || !File.Exists(prevMediaFile.Path))
        {
            _logger.LogError("MediaFile not found for previous track.");
            _isNavigatingTrack = false;
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
        if (_isNavigatingTrack)
        {
            _logger.LogDebug("NextTrack ignored: navigation in progress");
            return;
        }
        _isNavigatingTrack = true;

        StopTrack();

        MediaFile? nextMediaFile = _playlistTabsViewModel.NextMediaFile();

        if (nextMediaFile == null || !File.Exists(nextMediaFile.Path))
        {
            _logger.LogError("MediaFile not found for next track.");
            _isNavigatingTrack = false;
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
        if (_isNavigatingTrack)
        {
            return; // avoid re-entrancy during transitions
        }

        if (!_audioEngine.IsPlaying)
        {
            return; // only auto-advance while actually playing
        }

        double length = _audioEngine.CurrentTrackLength;
        double position = _audioEngine.CurrentTrackPosition;

        if (length > 0 && position + 10.0 > length)
        {
            if (_audioEngine.GetDecibelLevel() <= -50 || position + 0.5 > length)
            {
                NextTrack();
            }
        }
    }
}
