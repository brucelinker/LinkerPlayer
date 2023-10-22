using CommunityToolkit.Mvvm.ComponentModel;

namespace LinkerPlayer.ViewModels;

public class PlayerControlsViewModel : ObservableObject
{

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