using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using Shouldly;

namespace LinkerPlayer.Tests.ViewModels;

public sealed class MusicBrainzRatingsWindowViewModelTests
{
    [Fact]
    public void Apply_Update_CopiesTallyAndRunningState()
    {
        MusicBrainzRatingsWindowViewModel viewModel = new();

        viewModel.Apply(new MusicBrainzRatingProgressUpdate
        {
            Total = 10,
            Processed = 4,
            Rated = 1,
            NoRating = 2,
            Errors = 1,
            Skipped = 0,
            NoMusicBrainzId = 0,
            CurrentTrack = "Song",
            Status = "Checking 4 of 10",
            IsRunning = true
        });

        viewModel.ProgressText.ShouldBe("4 of 10");
        viewModel.Rated.ShouldBe(1);
        viewModel.NoRating.ShouldBe(2);
        viewModel.Errors.ShouldBe(1);
        viewModel.CurrentTrack.ShouldBe("Song");
        viewModel.ActionText.ShouldBe("Cancel");
    }

    [Fact]
    public void PrimaryAction_WhileRunning_CancelsAndDoesNotClose()
    {
        MusicBrainzRatingsWindowViewModel viewModel = new();
        using CancellationTokenSource cancellation = new();
        viewModel.BindCancellation(cancellation);
        bool closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        viewModel.PrimaryActionCommand.Execute(null);

        cancellation.IsCancellationRequested.ShouldBeTrue();
        viewModel.ActionText.ShouldBe("Cancelling…");
        viewModel.PrimaryActionCommand.CanExecute(null).ShouldBeFalse();
        closed.ShouldBeFalse();
    }

    [Fact]
    public void PrimaryAction_WhenFinished_RaisesClose()
    {
        MusicBrainzRatingsWindowViewModel viewModel = new();
        viewModel.Apply(new MusicBrainzRatingProgressUpdate
        {
            Status = "Complete",
            IsRunning = false
        });
        bool closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        viewModel.ActionText.ShouldBe("Close");
        viewModel.PrimaryActionCommand.Execute(null);

        closed.ShouldBeTrue();
    }
}
