using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Windows;
using Serilog;
using System.IO;
using System.Windows;

namespace LinkerPlayer.ViewModels;

public partial class PlayerControlsViewModel : ObservableRecipient
{
    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private PlayerState _state;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private static bool _shuffleMode;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private bool _isMuted;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand))]
    private MediaFile? _selectedMediaFile;

    private static MainWindow? _mainWindow;
    private readonly PlaylistTabsViewModel _playlistTabsViewModel = new();

    public PlayerControlsViewModel()
    {
        _mainWindow = (MainWindow?)Application.Current.MainWindow;

        State = PlayerState.Stopped;
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        PlayPauseTrack();
    }

    public void PlayPauseTrack()
    {
        SelectedMediaFile = _playlistTabsViewModel.SelectedTrack ?? _playlistTabsViewModel.SelectFirstTrack();

        if (State != PlayerState.Playing)
        {
            PlayerState prevState = State;
            State = PlayerState.Playing;

            if (prevState == PlayerState.Paused)
            {
                _mainWindow!.ResumeTrack(SelectedMediaFile);
            }
            else
            {
                _mainWindow!.PlayTrack(SelectedMediaFile);
            }
        }
        else
        {
            State = PlayerState.Paused;
        }

        WeakReferenceMessenger.Default.Send(new PlayerStateMessage(State));
    }

    [RelayCommand]
    private void Stop()
    {
        StopTrack();
    }

    private void StopTrack()
    {
        State = PlayerState.Stopped;
        WeakReferenceMessenger.Default.Send(new PlayerStateMessage(State));
    }

    [RelayCommand]
    private void Prev()
    {
        PreviousTrack();
    }

    public void PreviousTrack()
    {
        MediaFile prevMediaFile = _playlistTabsViewModel.PreviousMediaFile()!;

        if (!File.Exists(prevMediaFile.Path))
        {
            Log.Error("MediaFile not found.");
            return;
        }

        _mainWindow!.PlayTrack(prevMediaFile);
    }

    [RelayCommand]
    private void Next()
    {
        NextTrack();
    }

    public void NextTrack()
    {
        MediaFile nextMediaFile = _playlistTabsViewModel.NextMediaFile()!;

        if (!File.Exists(nextMediaFile.Path))
        {
            Log.Error("MediaFile not found.");
            return;
        }

        _mainWindow!.PlayTrack(nextMediaFile);
    }

    [RelayCommand]
    private void Shuffle(bool isChecked)
    {
        SetShuffleMode(isChecked);
    }

    private void SetShuffleMode(bool shuffleMode)
    {
        ShuffleMode = shuffleMode;
        _playlistTabsViewModel.ShuffleTracks(shuffleMode);
    }

    [RelayCommand]
    private void Mute(bool isMuted)
    {
        IsMuted = isMuted;
    }
    
    private bool CanPlayPause()
    {
        return true;
    }
}
