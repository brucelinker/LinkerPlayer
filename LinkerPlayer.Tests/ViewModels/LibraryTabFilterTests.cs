using LinkerPlayer.Models;
using LinkerPlayer.Tests.Helpers;
using Shouldly;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
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

    [StaFact]
    public void RebuildMetadataLists_ReenteredFromCodecChange_DoesNotDuplicateCodecs()
    {
        MediaFile flac = TestDataHelper.CreateTestMediaFile("1", "Alpha", "Artist A");
        flac.Codec = "FLAC";
        MediaFile mp3 = TestDataHelper.CreateTestMediaFile("2", "Beta", "Artist B");
        mp3.Codec = "MP3 CBR";

        LibraryTab tab = new(new ObservableCollection<MediaFile> { flac, mp3 });
        tab.Codecs.CollectionChanged += (_, e) =>
        {
            if (e.Action is NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Remove)
            {
                tab.NotifyCodecsChanged();
            }
        };

        MethodInfo? rebuild = typeof(LibraryTab).GetMethod("RebuildMetadataLists", BindingFlags.Instance | BindingFlags.NonPublic);
        rebuild.ShouldNotBeNull();
        rebuild.Invoke(tab, null);

        tab.Codecs.ShouldBe(new[] { "(All)", "FLAC", "MP3 CBR" });
    }

    [StaFact]
    public void NotifyAlbumArtistsChanged_FiltersTrackView()
    {
        MediaFile first = TestDataHelper.CreateTestMediaFile("1", "Alpha", "Track Artist");
        first.AlbumArtist = "Simon & Garfunkel";
        MediaFile second = TestDataHelper.CreateTestMediaFile("2", "Beta", "Other Artist");
        second.AlbumArtist = "The Andrew Sisters";

        LibraryTab tab = new(new ObservableCollection<MediaFile> { first, second });

        tab.AlbumArtists.ShouldBe(new[] { "(All)", "Simon & Garfunkel", "The Andrew Sisters" });

        tab.SelectedAlbumArtists.Clear();
        tab.SelectedAlbumArtists.Add("Simon & Garfunkel");
        tab.NotifyAlbumArtistsChanged();

        tab.TracksView.ShouldNotBeNull();
        tab.TracksView.View.Cast<MediaFile>().Select(track => track.Id).ShouldBe(["1"]);
    }
}
