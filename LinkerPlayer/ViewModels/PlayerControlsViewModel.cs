using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Windows;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private bool _isMute;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand))]
    private MediaFile? _selectedMediaFile;

    private static MainWindow? _mainWindow;
    private readonly PlaylistTabsViewModel _playlistTabsViewModel = new();

    public PlayerControlsViewModel()
    {
        _mainWindow = (MainWindow?)Application.Current.MainWindow;

        State = PlayerState.Stopped;

        WeakReferenceMessenger.Default.Register<PlaylistSelectionChangedMessage>(this, (r, m) =>
        {
            OnPlaylistSelectionChanged(m.Value!);
        });

    }

    private void OnPlaylistSelectionChanged(MediaFile? selectedTrack)
    {
        //_selectedMediaFile = selectedTrack;
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        PlayPauseTrack();
    }

    public void PlayPauseTrack()
    {
        MediaFile? selectedTrack = _playlistTabsViewModel.SelectedTrack;

        if (selectedTrack != null)
        {
            if (State != PlayerState.Playing)
            {
                PlayerState prevState = State;
                State = PlayerState.Playing;

                if (prevState == PlayerState.Paused)
                {
                    _mainWindow!.ResumeTrack(selectedTrack);
                }
                else
                {
                    _mainWindow!.PlayTrack(selectedTrack);
                }
            }
            else
            {
                State = PlayerState.Paused;
            }
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
        MediaFile? nextMediaFile = _playlistTabsViewModel.NextMediaFile()!;

        if (!File.Exists(nextMediaFile.Path))
        {
            Log.Error("MediaFile not found.");
            return;
        }

        _mainWindow!.PlayTrack(nextMediaFile);
    }

    [RelayCommand]
    private void Shuffle()
    {
        ShuffleTracks();
    }

    private void ShuffleTracks()
    {
        ShuffleMode = !ShuffleMode;
    }

    [RelayCommand]
    private void Mute()
    {
        Log.Information("MuteCommand Hit");
    }

    private bool CanPlayPause()
    {
        return true;
    }

    private void SelectWithSkipping(MediaFile song, Action<object, RoutedEventArgs> nextPrevButton)
    {
        // skips if mediaFile doesn't exist
        Log.Information("MainWindow - SelectWithSkipping");

        if (!File.Exists(song.Path))
        {
            //InfoSnackbar.MessageQueue?.Clear();
            //InfoSnackbar.MessageQueue?.Enqueue($"Song \"{song.Title}\" could not be found", null, null, null, false,
            //    true, TimeSpan.FromSeconds(2));
            //SelectedTrack = song.Clone();
            nextPrevButton(null!, null!);
        }
        else
        {
            _mainWindow!.PlayTrack(song);
        }
    }

    //private void TimerTick(object sender, EventArgs e)
    //{
    //    if (!(SeekBar.IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed))
    //    {
    //        PlayerControls.SeekBar.Value =
    //            (AudioStreamControl.CurrentTrackPosition * 100) / AudioStreamControl.CurrentTrackLength;
    //    }
    //}


    //this.PlayOrPauseCommand = new DelegateCommand(this.PlayOrPause, this.CanPlayOrPause);

    //public ICommand PlayOrPauseCommand { get; }

    //private bool CanPlayOrPause()
    //{
    //    if (!this.PlayerEngine.Initialized)
    //    {
    //        return false;
    //    }

    //    var canPlay = (this.PlayerEngine.CurrentMediaFile != null && this.PlayerEngine.State != PlayerState.Play)
    //                  || (this.playListsViewModel.FirstSimplePlaylistFiles != null && this.playListsViewModel.FirstSimplePlaylistFiles.OfType<IMediaFile>().Any());
    //    var canPause = this.PlayerEngine.CurrentMediaFile != null && this.PlayerEngine.State == PlayerState.Play;
    //    return canPlay || canPause;
    //}

    //private void PlayOrPause()
    //{
    //    if (this.PlayerEngine.State == PlayerState.Pause || this.PlayerEngine.State == PlayerState.Play)
    //    {
    //        this.PlayerEngine.Pause();
    //    }
    //    else
    //    {
    //        var file = this.playListsViewModel.GetCurrentPlayListFile();
    //        if (file != null)
    //        {
    //            this.PlayerEngine.Play(file);
    //        }
    //    }
    //}
}
