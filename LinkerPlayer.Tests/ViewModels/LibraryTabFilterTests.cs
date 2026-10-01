using LinkerPlayer.Models;
using LinkerPlayer.Tests.Helpers;
using Shouldly;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace LinkerPlayer.Tests.ViewModels;

public sealed class LibraryTabFilterTests
{
    [StaFact]
    public void NotifyArtistsChanged_AfterSortSnapshot_FiltersTrackView()
    {
        ObservableCollection<MediaFile> tracks = new()
        {
            TestDataHelper.CreateTestMediaFile("1", "Alpha", "Artist A"),
            TestDataHelper.CreateTestMediaFile("2", "Beta", "Artist B")
        };

        LibraryTab tab = new(tracks);
        tab.SetSortOrder([new SortDescription(nameof(MediaFile.Title), ListSortDirection.Ascending)]);

        tab.SelectedArtists.Clear();
        tab.SelectedArtists.Add("Artist A");
        tab.NotifyArtistsChanged();

        tab.TracksView.ShouldNotBeNull();
        tab.TracksView.View.Cast<MediaFile>().Select(track => track.Id).ShouldBe(["1"]);
    }
}
