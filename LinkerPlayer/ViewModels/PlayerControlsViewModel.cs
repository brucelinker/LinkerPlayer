using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Properties;
using ManagedBass;
using Serilog;
using System.IO;

namespace LinkerPlayer.ViewModels;

public partial class PlayerControlsViewModel : BaseViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private PlaybackState _state;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private static bool _shuffleMode;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private bool _isMuted;

    public static PlayerControlsViewModel Instance { get; } = new();

    private static int _count;

    public readonly AudioEngine audioEngine;
    public readonly PlaylistTabsViewModel playlistTabsViewModel;

    public PlayerControlsViewModel()
    {
        audioEngine = AudioEngine.Instance;
        playlistTabsViewModel = PlaylistTabsViewModel.Instance;

        Log.Information($"PLAYERCONTROLSVIEWMODEL - {++_count}");

        if (_count == 1)
        {
            WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (_, m) =>
            {
                OnPlaybackStateChanged(m.Value);
            });

            WeakReferenceMessenger.Default.Register<PlaybackStoppedMessage>(this, (_, m) =>
            {
                OnAudioStopped(m.Value);
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        PlayPauseTrack();
    }

    public void PlayPauseTrack()
    {
        Log.Information("PlayPauseTrack called");

        // Ensure SelectedTrack is set
        SelectedTrack = playlistTabsViewModel.SelectedTrack ?? playlistTabsViewModel.SelectFirstTrack();
        Log.Information($"SelectedTrack: {(SelectedTrack != null ? SelectedTrack.Path : "null")}");

        if (audioEngine.IsPlaying)
        {
            Log.Information("Track is playing, pausing");
            audioEngine.Pause();
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
        //Log.Information("PlayTrack called");

        if (ActiveTrack != null)
        {
            //Log.Information($"Playing ActiveTrack: {ActiveTrack.Path}");
            audioEngine.PathToMusic = ActiveTrack.Path;
            audioEngine.Play();
            ActiveTrack.State = PlaybackState.Playing;
            State = PlaybackState.Playing;
        }
        else if (SelectedTrack != null)
        {
            Log.Information($"Playing SelectedTrack: {SelectedTrack.Path}");
            audioEngine.PathToMusic = SelectedTrack.Path;
            audioEngine.Play();

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
        audioEngine.ResumePlay();
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
        audioEngine.Stop();
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
        MediaFile? prevMediaFile = playlistTabsViewModel.PreviousMediaFile();

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
        MediaFile? nextMediaFile = playlistTabsViewModel.NextMediaFile();

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
        //Log.Information($"Playback state changed: {playbackState}");
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
            audioEngine.Stop();
        }
    }

    [RelayCommand]
    private void Shuffle(bool isChecked)
    {
        SetShuffleMode(isChecked);
    }

    private void SetShuffleMode(bool shuffleMode)
    {
        ShuffleMode = shuffleMode;
        Settings.Default.ShuffleMode = shuffleMode;
        Settings.Default.Save();

        WeakReferenceMessenger.Default.Send(new ShuffleModeMessage(shuffleMode));
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

        double length = audioEngine.CurrentTrackLength;
        double position = audioEngine.CurrentTrackPosition;

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
        double length = audioEngine.CurrentTrackLength;
        double position = audioEngine.CurrentTrackPosition;

        if (length > 0 && position + 10.0 > length)
        {
            if (audioEngine.GetDecibelLevel() <= -50 || position + 0.5 > length)
                NextTrack();
        }
    }
}