using LinkerPlayer.Models;
using LinkerPlayer.Tests.Fakes;
using LinkerPlayer.Tests.Mocks;
using LinkerPlayer.ViewModels;
using Xunit;

namespace LinkerPlayer.Tests.UserControls;

public sealed class TrackInfoDisplayPolicyTests
{
    private sealed class TrackInfoPolicy
    {
        private readonly FakeAudioEngine _audioEngine;
        private readonly TestSelectionService _selectionService;
        private readonly SharedDataModel _sharedDataModel;

        public MediaFile? LastDisplayed { get; private set; }

        public TrackInfoPolicy(FakeAudioEngine audioEngine, TestSelectionService selectionService, SharedDataModel sharedDataModel)
        {
            _audioEngine = audioEngine;
            _selectionService = selectionService;
            _sharedDataModel = sharedDataModel;
        }

        public void UpdateDisplayedTrack()
        {
            MediaFile? activeTrack = _sharedDataModel.ActiveTrack;
            if (activeTrack != null)
            {
                LastDisplayed = activeTrack;
                return;
            }

            // If stopped, keep displaying the last active track.
            if (!_audioEngine.IsPlaying)
            {
                return;
            }

            LastDisplayed = _selectionService.CurrentTrack;
        }

        public void OnSelectionChangedWhenStopped(MediaFile? track)
        {
            if (!_audioEngine.IsPlaying)
            {
                LastDisplayed = track;
            }
        }
    }

    private static MediaFile CreateTrack(string id, string title)
    {
        MediaFile file = new MediaFile
        {
            Id = id,
            Title = title,
            Path = $"D:\\Music\\{title}.mp3"
        };
        return file;
    }

    [StaFact]
    public void UpdateDisplayedTrack_WhenActiveTrackPresent_DisplaysActiveTrack()
    {
        FakeAudioEngine audio = new FakeAudioEngine();
        TestSelectionService selection = new TestSelectionService();
        SharedDataModel shared = new SharedDataModel();

        TrackInfoPolicy policy = new TrackInfoPolicy(audio, selection, shared);

        MediaFile active = CreateTrack("a", "Active");
        shared.UpdateActiveTrack(active);

        MediaFile selected = CreateTrack("s", "Selected");
        selection.SetTrack(selected, 0);

        policy.UpdateDisplayedTrack();

        Assert.Same(active, policy.LastDisplayed);
    }

    [StaFact]
    public void UpdateDisplayedTrack_WhenStoppedAndNoActiveTrack_KeepsLastDisplayed()
    {
        FakeAudioEngine audio = new FakeAudioEngine();
        TestSelectionService selection = new TestSelectionService();
        SharedDataModel shared = new SharedDataModel();

        TrackInfoPolicy policy = new TrackInfoPolicy(audio, selection, shared);

        MediaFile previousActive = CreateTrack("p", "Prev");
        shared.UpdateActiveTrack(previousActive);
        policy.UpdateDisplayedTrack();
        Assert.Same(previousActive, policy.LastDisplayed);

        // Stop + clear active track
        audio.Stop();
        shared.UpdateActiveTrack(null);

        MediaFile newSelection = CreateTrack("n", "NewSel");
        selection.SetTrack(newSelection, 1);

        policy.UpdateDisplayedTrack();

        // Should still show previous active track while stopped
        Assert.Same(previousActive, policy.LastDisplayed);
    }

    [StaFact]
    public void OnSelectionChangedWhenStopped_UpdatesDisplayedTrack()
    {
        FakeAudioEngine audio = new FakeAudioEngine();
        TestSelectionService selection = new TestSelectionService();
        SharedDataModel shared = new SharedDataModel();

        TrackInfoPolicy policy = new TrackInfoPolicy(audio, selection, shared);

        // stopped
        audio.Stop();

        MediaFile newSelection = CreateTrack("n", "NewSel");
        selection.SetTrack(newSelection, 1);

        policy.OnSelectionChangedWhenStopped(newSelection);

        Assert.Same(newSelection, policy.LastDisplayed);
    }
}
