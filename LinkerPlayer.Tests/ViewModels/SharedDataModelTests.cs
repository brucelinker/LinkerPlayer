using LinkerPlayer.Models;
using LinkerPlayer.Tests.Helpers;
using LinkerPlayer.ViewModels;

namespace LinkerPlayer.Tests.ViewModels;

public class SharedDataModelTests
{
    [Fact]
    public void UpdateSelectedTrackIndex_ShouldRaiseAndStoreValue()
    {
        SharedDataModel model = new SharedDataModel();
        int observed = -2;
        model.PropertyChanged += (s,e) => { if (e.PropertyName == nameof(SharedDataModel.SelectedTrackIndex)) { observed = model.SelectedTrackIndex; } };
        model.UpdateSelectedTrackIndex(5);
        Assert.Equal(5, model.SelectedTrackIndex);
        Assert.Equal(5, observed);
    }

    [Fact]
    public void UpdateSelectedTrack_ShouldSetTrackAndRaise()
    {
        SharedDataModel model = new SharedDataModel();
        MediaFile track = TestDataHelper.CreateTestMediaFile("id-1","Song 1","Artist 1");
        MediaFile? observed = null;
        model.PropertyChanged += (s,e) => { if (e.PropertyName == nameof(SharedDataModel.SelectedTrack)) { observed = model.SelectedTrack; } };
        model.UpdateSelectedTrack(track);
        Assert.Equal(track, model.SelectedTrack);
        Assert.Equal(track, observed);
    }

    [Fact]
    public void UpdateActiveTrack_ShouldSetTrackAndRaise()
    {
        SharedDataModel model = new SharedDataModel();
        MediaFile track = TestDataHelper.CreateTestMediaFile("id-2","Song 2","Artist 2");
        MediaFile? observed = null;
        model.PropertyChanged += (s,e) => { if (e.PropertyName == nameof(SharedDataModel.ActiveTrack)) { observed = model.ActiveTrack; } };
        model.UpdateActiveTrack(track);
        Assert.Equal(track, model.ActiveTrack);
        Assert.Equal(track, observed);
    }

    [Fact]
    public void UpdateSelectedTracks_ShouldReplaceContents()
    {
        SharedDataModel model = new SharedDataModel();
        List<MediaFile> list = TestDataHelper.CreateTestMediaFiles(3);
        int changeCount = 0;
        model.SelectedTracksChanged += (s,e) => changeCount++;
        model.UpdateSelectedTracks(list);
        Assert.Equal(3, model.SelectedTracks.Count);
        Assert.Equal(list[0].Id, model.SelectedTracks[0].Id);
        Assert.True(changeCount >= 1);
    }

    [Fact]
    public void MultiSelection_SwitchToSingleSelection_ShouldReflectCounts()
    {
        SharedDataModel model = new SharedDataModel();
        List<MediaFile> multi = TestDataHelper.CreateTestMediaFiles(4);
        model.UpdateSelectedTracks(multi);
        Assert.Equal(4, model.SelectedTracks.Count);
        model.UpdateSelectedTracks(new [] { multi[2] });
        Assert.Single(model.SelectedTracks);
        Assert.Equal(multi[2].Id, model.SelectedTracks[0].Id);
    }
}
