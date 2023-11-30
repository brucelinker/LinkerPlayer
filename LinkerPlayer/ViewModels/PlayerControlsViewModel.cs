using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Models;
using NAudio.Gui;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows;
using System;

namespace LinkerPlayer.ViewModels;

public partial class PlayerControlsViewModel : ObservableObject
{
    [ObservableProperty]
    private PlayerState _state = PlayerState.Stopped;
    [ObservableProperty]
    private static bool _shuffleMode = false;
    [ObservableProperty]
    private bool _isMute = false;

    public PlayerControlsViewModel()
    {
        
    }


    //this.PlayOrPauseCommand = new DelegateCommand(this.PlayOrPause, this.CanPlayOrPause);

    //public ICommand PlayOrPauseCommand { get; }

    //private bool CanPlayOrPause()
    //{
    //    if (!this.PlayerEngine.Initializied)
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
