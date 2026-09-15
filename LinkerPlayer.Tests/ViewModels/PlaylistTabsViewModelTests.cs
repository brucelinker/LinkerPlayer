using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.Tests.Fakes;
using LinkerPlayer.Tests.Mocks;
using LinkerPlayer.UserControls;
using LinkerPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using System.Windows.Controls;

namespace LinkerPlayer.Tests.ViewModels;

public sealed class PlaylistTabsViewModelTests
{
    [StaFact]
    public void ColumnRegeneration_AddsPlayPauseAndVisibleColumns()
    {
        FakeSettingsManager settings = new()
        {
            Settings = new AppSettings
            {
                VisibleColumns = new List<string> { "Title", "Artist" }
            }
        };

        App.AppHost = null!;
        MediaTabViewModel viewModel = CreateViewModel(new FakeMusicLibrary(), settings, out _);
        PlaylistTabs control = new() { DataContext = viewModel };
        DataGrid dataGrid = new();

        control.RegenerateColumns(dataGrid);

        dataGrid.Columns.Count.ShouldBe(3);
        dataGrid.Columns[0].ShouldBeOfType<DataGridTemplateColumn>();
        dataGrid.Columns[1].Header.ShouldBe("Title");
        dataGrid.Columns[2].Header.ShouldBe("Artist");
    }

    [StaFact]
    public void ColumnRegeneration_PreservesLeadingStaticColumns()
    {
        FakeSettingsManager settings = new()
        {
            Settings = new AppSettings
            {
                VisibleColumns = new List<string> { "Title", "AlbumArtist", "Duration" }
            }
        };

        App.AppHost = null!;
        MediaTabViewModel viewModel = CreateViewModel(new FakeMusicLibrary(), settings, out _);
        PlaylistTabs control = new() { DataContext = viewModel };
        DataGrid dataGrid = new();
        // The play/pause template column is the leading static column; row numbers are
        // rendered via DataGrid row headers, not a '#' text column.
        dataGrid.Columns.Add(new DataGridTemplateColumn());

        control.RegenerateColumns(dataGrid);

        dataGrid.Columns.Count.ShouldBe(4);
        dataGrid.Columns[0].ShouldBeOfType<DataGridTemplateColumn>();
        dataGrid.Columns[1].Header.ShouldBe("Title");
        dataGrid.Columns[2].Header.ShouldBe("Album Artist");
        dataGrid.Columns[3].Header.ShouldBe("Duration");
    }

    [StaFact]
    public void LoadPlaylistTabs_PopulatesTabsAndRespectsSavedSelectionIndex()
    {
        FakeMusicLibrary musicLibrary = new();
        musicLibrary.Playlists.Add(new Playlist { Name = "A" });
        musicLibrary.Playlists.Add(new Playlist { Name = "B" });

        FakeSettingsManager settings = new()
        {
            Settings = new AppSettings
            {
                SelectedTabIndex = 1,
                VisibleColumns = new List<string> { "Title", "Artist" }
            }
        };

        App.AppHost = null!;
        MediaTabViewModel viewModel = CreateViewModel(musicLibrary, settings, out Mock<IPlaylistManagerService> playlistManager);
        playlistManager.Setup(p => p.LoadPlaylistTracks(It.IsAny<string>())).Returns(Array.Empty<MediaFile>());

        viewModel.LoadPlaylistTabs();

        viewModel.TabList.Count.ShouldBe(3);
        viewModel.TabList[1].Name.ShouldBe("A");
        viewModel.TabList[2].Name.ShouldBe("B");
        viewModel.SelectedTabIndex.ShouldBe(1);
        viewModel.SelectedTab.ShouldNotBeNull();
        viewModel.SelectedTab!.Name.ShouldBe("A");
    }

    [StaFact]
    public async Task LoadSelectedPlaylistTracksAsync_LoadsTracksForSelectedPlaylist()
    {
        FakeMusicLibrary musicLibrary = new();
        musicLibrary.Playlists.Add(new Playlist { Name = "P1" });

        FakeSettingsManager settings = new()
        {
            Settings = new AppSettings
            {
                SelectedTabIndex = 1,
                VisibleColumns = new List<string> { "Title", "Artist" }
            }
        };

        App.AppHost = null!;
        MediaTabViewModel viewModel = CreateViewModel(musicLibrary, settings, out Mock<IPlaylistManagerService> playlistManager);
        List<MediaFile> tracks = new()
        {
            new MediaFile { Id = "t1", Title = "Track 1", Path = "c:/music/t1.mp3" },
            new MediaFile { Id = "t2", Title = "Track 2", Path = "c:/music/t2.mp3" }
        };
        playlistManager.Setup(p => p.LoadPlaylistTracks("P1")).Returns(tracks);

        viewModel.LoadPlaylistTabs();
        await viewModel.LoadSelectedPlaylistTracksAsync();

        viewModel.TabList[1].Tracks.Count.ShouldBe(2);
        viewModel.TabList[1].Tracks[0].Id.ShouldBe("t1");
        viewModel.TabList[1].Tracks[1].Id.ShouldBe("t2");
    }

    private static MediaTabViewModel CreateViewModel(FakeMusicLibrary musicLibrary, FakeSettingsManager settings, out Mock<IPlaylistManagerService> playlistManager)
    {
        SharedDataModel sharedDataModel = new();
        SelectionService selectionService = new(sharedDataModel, Mock.Of<ILogger<SelectionService>>());
        TestPlaybackCoordinator playbackCoordinator = new();

        Mock<IUiDispatcher> uiDispatcher = new();
        uiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Action>()))
            .Returns<Action>(action => { action(); return Task.CompletedTask; });
        uiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<Task>>()))
            .Returns<Func<Task>>(async asyncAction => await asyncAction());
        uiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<object>>()))
            .Returns<Func<object>>(func => Task.FromResult(func()));
        uiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<Task<object>>>()))
            .Returns<Func<Task<object>>>(async asyncFunc => await asyncFunc());
        uiDispatcher.Setup(d => d.CheckAccess()).Returns(true);

        playlistManager = new Mock<IPlaylistManagerService>();
        LibraryTabViewModel libraryTabViewModel = new(
            musicLibrary,
            settings,
            selectionService,
            playbackCoordinator,
            Mock.Of<ILogger<LibraryTabViewModel>>());

        return new MediaTabViewModel(
            musicLibrary,
            sharedDataModel,
            settings,
            Mock.Of<IFileImportService>(),
            playlistManager.Object,
            Mock.Of<ITrackNavigationService>(),
            uiDispatcher.Object,
            Mock.Of<IDatabaseSaveService>(),
            selectionService,
            playbackCoordinator,
            Mock.Of<IPlaylistFileService>(),
            Mock.Of<IImportCancellationService>(),
            Mock.Of<IMusicBrainzRatingService>(),
            libraryTabViewModel,
            Mock.Of<ILogger<MediaTabViewModel>>());
    }
}
