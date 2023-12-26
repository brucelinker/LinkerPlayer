using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkerPlayer.Models;
using LinkerPlayer.UserControls;
using LinkerPlayer.Windows;
using Serilog;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
using LinkerPlayer.Audio;
using System.Windows.Threading;
using System;
using System.Windows.Input;

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

    private readonly MainWindow? _mainWindow;
    private readonly PlayListsViewModel _playListsViewModel = new();
    //private readonly AudioStreamControl _audioStreamControl = new(Properties.Settings.Default.MainOutputDevice);
    //public readonly DispatcherTimer SeekBarTimer = new();

    public PlayerControlsViewModel()
    {
        _mainWindow = (MainWindow?)Application.Current.MainWindow;

        State = PlayerState.Stopped;

        //SeekBarTimer.Interval = TimeSpan.FromMilliseconds(50);
        //SeekBarTimer.Tick += TimerTick!;

        WeakReferenceMessenger.Default.Register<PlaylistSelectionChangedMessage>(this, (r, m) =>
        {
            OnPlaylistSelectionChanged(m.Value!);
        });

    }

    private void OnPlaylistSelectionChanged(MediaFile? selectedTrack)
    {
        //_selectedMediaFile = selectedTrack;
    }

    //public object PlayPauseCommand { get; }


    [RelayCommand]
    private void Prev()
    {
        Log.Information("PrevCommand Hit");
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        MediaFile? selectedTrack = _playListsViewModel.SelectedTrack;

        if (selectedTrack != null)
        {
            PlayerState prevState = State;

            if (State != PlayerState.Playing)
            {
                State = PlayerState.Playing;
                selectedTrack.State = PlayerState.Playing;
                _playListsViewModel.UpdatePlayerState(PlayerState.Playing);

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
                selectedTrack.State = PlayerState.Paused;
                _playListsViewModel.UpdatePlayerState(PlayerState.Paused);
            }
        }

        WeakReferenceMessenger.Default.Send(new PlayerStateMessage(State));
    }

    [RelayCommand]
    private void Stop()
    {
        State = PlayerState.Stopped;

        WeakReferenceMessenger.Default.Send(new PlayerStateMessage(State));
    }

    [RelayCommand]
    private void Next()
    {
        Log.Information("NextCommand Hit");
    }
    
    [RelayCommand]
    private void Shuffle()
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
        //return _selectedMediaFile != null;
        return true;
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
