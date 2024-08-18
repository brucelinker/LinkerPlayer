using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Properties;
using NAudio.Wave;
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
//        SelectedTrack = playlistTabsViewModel.SelectedTrack ?? playlistTabsViewModel.SelectFirstTrack();

        if (State != PlaybackState.Playing)
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
        else
        {
            audioEngine.Pause();
            State = PlaybackState.Paused;

            if (ActiveTrack != null)
            {
                ActiveTrack.State = PlaybackState.Paused;
            }

        }
    }

    public void PlayTrack()
    {
        if (ActiveTrack != null)
        {
            audioEngine.PathToMusic = ActiveTrack?.Path;
            audioEngine.StopAndPlayFromPosition(0.0);
            ActiveTrack!.State = PlaybackState.Playing;
            State = PlaybackState.Playing;
        }
        else if (SelectedTrack != null)
        {
            audioEngine.PathToMusic = SelectedTrack.Path;
            audioEngine.StopAndPlayFromPosition(0.0);

            if (ActiveTrack == null || ActivePlaylistIndex == SelectedPlaylistIndex)
            {
                ActivePlaylistIndex = SelectedPlaylistIndex;
                ActiveTrackIndex = SelectedTrackIndex;
                ActiveTrack = SelectedTrack;
                ActiveTrack.State = PlaybackState.Playing;
                State = PlaybackState.Playing;
            }
        }

        WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(State));
        WeakReferenceMessenger.Default.Send(new ActiveTrackChangedMessage(ActiveTrack));
    }

    public void ResumeTrack()
    {
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
        audioEngine.Stop();
        State = PlaybackState.Stopped;

        if (ActiveTrack != null)
        {
            ActiveTrack.State = PlaybackState.Stopped;
            ActivePlaylistIndex = null;
            ActiveTrackIndex = null;
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
        MediaFile prevMediaFile = playlistTabsViewModel.PreviousMediaFile()!;

        if (!File.Exists(prevMediaFile.Path))
        {
            Log.Error("MediaFile not found.");
            return;
        }

        PlayTrack();
    }

    [RelayCommand]
    private void Next()
    {
        NextTrack();
    }

    public void NextTrack()
    {
        MediaFile nextMediaFile = playlistTabsViewModel.NextMediaFile()!;

        if (!File.Exists(nextMediaFile.Path))
        {
            Log.Error("MediaFile not found.");
            return;
        }

        PlayTrack();
    }

    private void OnPlaybackStateChanged(PlaybackState playbackState)
    {
        State = playbackState;
    }

    private void OnAudioStopped(bool songEnded)
    {
        if (songEnded)
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
        //ShuffleMode = shuffleMode;
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
        return (audioEngine.CurrentTrackPosition * 100) / audioEngine.CurrentTrackLength;
    }

    private void MonitorNextTrack()
    {
        if (audioEngine.CurrentTrackPosition + 5.0 > audioEngine.CurrentTrackLength)
        {
            NextTrack();
        }
    }
}
