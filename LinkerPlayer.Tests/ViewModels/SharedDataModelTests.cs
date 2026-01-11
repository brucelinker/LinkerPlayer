using LinkerPlayer.Models;
using LinkerPlayer.Tests.Helpers;
using LinkerPlayer.ViewModels;
using Shouldly;

namespace LinkerPlayer.Tests.ViewModels;

public class SharedDataModelTests
{
    [StaFact]
    public void UpdateSelectedTrackIndex_ShouldRaiseAndStoreValue()
    {
        SharedDataModel model = new SharedDataModel();
        int observed = -2;
        model.PropertyChanged += (s,e) => { if (e.PropertyName == nameof(SharedDataModel.SelectedTrackIndex)) { observed = model.SelectedTrackIndex; } };
        model.UpdateSelectedTrackIndex(5);
        model.SelectedTrackIndex.ShouldBe(5);
        observed.ShouldBe(5);
    }

    [StaFact]
    public void UpdateSelectedTrack_ShouldSetTrackAndRaise()
    {
        SharedDataModel model = new SharedDataModel();
        MediaFile track = TestDataHelper.CreateTestMediaFile("id-1","Song 1","Artist 1");
        MediaFile? observed = null;
        model.PropertyChanged += (s,e) => { if (e.PropertyName == nameof(SharedDataModel.SelectedTrack)) { observed = model.SelectedTrack; } };
        model.UpdateSelectedTrack(track);
        model.SelectedTrack.ShouldBe(track);
        observed.ShouldBe(track);
    }

    [StaFact]
    public void UpdateActiveTrack_ShouldSetTrackAndRaise()
    {
        SharedDataModel model = new SharedDataModel();
        MediaFile track = TestDataHelper.CreateTestMediaFile("id-2","Song 2","Artist 2");
        MediaFile? observed = null;
        model.PropertyChanged += (s,e) => { if (e.PropertyName == nameof(SharedDataModel.ActiveTrack)) { observed = model.ActiveTrack; } };
        model.UpdateActiveTrack(track);
        model.ActiveTrack.ShouldBe(track);
        observed.ShouldBe(track);
    }

    [StaFact]
    public void UpdateSelectedTracks_ShouldReplaceContents()
    {
        SharedDataModel model = new SharedDataModel();
        List<MediaFile> list = TestDataHelper.CreateTestMediaFiles(3);
        int changeCount = 0;
        model.SelectedTracksChanged += (s,e) => changeCount++;
        model.UpdateSelectedTracks(list);
        model.SelectedTracks.Count.ShouldBe(3);
        model.SelectedTracks[0].Id.ShouldBe(list[0].Id);
        changeCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [StaFact]
    public void MultiSelection_SwitchToSingleSelection_ShouldReflectCounts()
    {
        SharedDataModel model = new SharedDataModel();
        List<MediaFile> multi = TestDataHelper.CreateTestMediaFiles(4);
        model.UpdateSelectedTracks(multi);
        model.SelectedTracks.Count.ShouldBe(4);
        model.UpdateSelectedTracks(new [] { multi[2] });
        model.SelectedTracks.Count.ShouldBe(1);
        model.SelectedTracks[0].Id.ShouldBe(multi[2].Id);
    }
}
