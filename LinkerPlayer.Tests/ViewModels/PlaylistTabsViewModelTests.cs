using FluentAssertions;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.Tests.Mocks;
using Moq;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace LinkerPlayer.Tests.ViewModels;

public class PlaylistTabsViewModelTests
{
    [Fact]
    public void BasicTest_ShouldPass()
    {
        bool result = true;
        result.Should().BeTrue();
    }

    [Fact]
    public void Startup_LoadPlaylistTabs_ShouldRestoreSelectedTrack()
    {
        // Arrange
        Mock<IMusicLibrary> musicLibrary = new Mock<IMusicLibrary>();
        ObservableCollection<Playlist> playlists = new ObservableCollection<Playlist>
        {
            new Playlist
            {
                Name = "TestPlaylist",
                TrackIds = new ObservableCollection<string> { "trk1", "trk2" },
                SelectedTrackId = "trk2"
            }
        };
        ObservableCollection<MediaFile> mainLibrary = new ObservableCollection<MediaFile>
        {
            new MediaFile { Id = "trk1", FileName = "A", Path = "A.mp3" },
            new MediaFile { Id = "trk2", FileName = "B", Path = "B.mp3" }
        };
        musicLibrary.SetupGet(m => m.Playlists).Returns(playlists);
        musicLibrary.Setup(m => m.GetPlaylists()).Returns(playlists.ToList());
        musicLibrary.SetupGet(m => m.MainLibrary).Returns(mainLibrary);

        Mock<ISettingsManager> settingsManager = new Mock<ISettingsManager>();
        AppSettings appSettings = new AppSettings { SelectedTabIndex = 0 };
        settingsManager.SetupGet(s => s.Settings).Returns(appSettings);

        Mock<IFileImportService> fileImport = new Mock<IFileImportService>();
        Mock<IPlaylistManagerService> playlistManager = new Mock<IPlaylistManagerService>();
        playlistManager.Setup(p => p.LoadPlaylistTracks("TestPlaylist")).Returns(new List<MediaFile>
        {
            new MediaFile { Id = "trk1", FileName = "A", Path = "A.mp3" },
            new MediaFile { Id = "trk2", FileName = "B", Path = "B.mp3" }
        });
        Mock<ITrackNavigationService> nav = new Mock<ITrackNavigationService>();
        IUiDispatcher ui = new MockUIDispatcher();
        Mock<IDatabaseSaveService> saveSvc = new Mock<IDatabaseSaveService>();
        Mock<ILogger<PlaylistTabsViewModel>> logger = new Mock<ILogger<PlaylistTabsViewModel>>();

        SharedDataModel shared = new SharedDataModel();
        ISelectionService selection = new TestSelectionService();

        PlaylistTabsViewModel vm = new PlaylistTabsViewModel(
            musicLibrary.Object,
            shared,
            settingsManager.Object,
            fileImport.Object,
            playlistManager.Object,
            nav.Object,
            ui,
            saveSvc.Object,
            selection,
            logger.Object);

        // Act
        vm.LoadPlaylistTabs();

        // Assert
        vm.SelectedTrack.Should().NotBeNull();
        vm.SelectedTrack!.Id.Should().Be("trk2");
        vm.SelectedTrackIndex.Should().Be(1);
    }

    [Fact]
    public async Task LoadSelectedPlaylistTracksAsync_ShouldPopulateSelectedTab()
    {
        // Arrange
        Mock<IMusicLibrary> musicLibrary = new Mock<IMusicLibrary>();
        ObservableCollection<Playlist> playlists = new ObservableCollection<Playlist>
        {
            new Playlist
            {
                Name = "P1",
                TrackIds = new ObservableCollection<string> { "t1", "t2" },
                SelectedTrackId = "t1"
            }
        };
        musicLibrary.SetupGet(m => m.Playlists).Returns(playlists);
        musicLibrary.Setup(m => m.GetPlaylists()).Returns(playlists.ToList());
        musicLibrary.SetupGet(m => m.MainLibrary).Returns(new ObservableCollection<MediaFile>
        {
            new MediaFile { Id = "t1", FileName = "A", Path = "A.mp3" },
            new MediaFile { Id = "t2", FileName = "B", Path = "B.mp3" }
        });

        Mock<ISettingsManager> settingsManager = new Mock<ISettingsManager>();
        settingsManager.SetupGet(s => s.Settings).Returns(new AppSettings { SelectedTabIndex = 0 });

        Mock<IFileImportService> fileImport = new Mock<IFileImportService>();
        Mock<IPlaylistManagerService> playlistManager = new Mock<IPlaylistManagerService>();
        playlistManager.Setup(p => p.LoadPlaylistTracks("P1")).Returns(new List<MediaFile>
        {
            new MediaFile { Id = "t1", FileName = "A", Path = "A.mp3" },
            new MediaFile { Id = "t2", FileName = "B", Path = "B.mp3" }
        });
        Mock<ITrackNavigationService> nav = new Mock<ITrackNavigationService>();
        IUiDispatcher ui = new MockUIDispatcher();
        Mock<IDatabaseSaveService> saveSvc = new Mock<IDatabaseSaveService>();
        Mock<ILogger<PlaylistTabsViewModel>> logger = new Mock<ILogger<PlaylistTabsViewModel>>();

        SharedDataModel shared = new SharedDataModel();
        ISelectionService selection = new TestSelectionService();

        PlaylistTabsViewModel vm = new PlaylistTabsViewModel(
            musicLibrary.Object,
            shared,
            settingsManager.Object,
            fileImport.Object,
            playlistManager.Object,
            nav.Object,
            ui,
            saveSvc.Object,
            selection,
            logger.Object);

        // Seed tabs
        vm.LoadPlaylistTabs();
        // Make selected tab empty to force load via async method
        vm.TabList[0].Tracks.Clear();

        // Act
        await vm.LoadSelectedPlaylistTracksAsync();

        // Assert
        vm.TabList[0].Tracks.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReorderTabs_ShouldMoveTab_AndPreserveSelection()
    {
        // Arrange
        Mock<IMusicLibrary> musicLibrary = new Mock<IMusicLibrary>();
        ObservableCollection<Playlist> playlists = new ObservableCollection<Playlist>
        {
            new Playlist { Name = "A", TrackIds = new ObservableCollection<string>(), SelectedTrackId = null },
            new Playlist { Name = "B", TrackIds = new ObservableCollection<string>(), SelectedTrackId = null },
            new Playlist { Name = "C", TrackIds = new ObservableCollection<string>(), SelectedTrackId = null }
        };
        musicLibrary.SetupGet(m => m.Playlists).Returns(playlists);
        musicLibrary.Setup(m => m.GetPlaylists()).Returns(playlists.ToList());
        musicLibrary.SetupGet(m => m.MainLibrary).Returns(new ObservableCollection<MediaFile>());

        Mock<ISettingsManager> settingsManager = new Mock<ISettingsManager>();
        settingsManager.SetupGet(s => s.Settings).Returns(new AppSettings { SelectedTabIndex = 1 });

        Mock<IFileImportService> fileImport = new Mock<IFileImportService>();
        Mock<IPlaylistManagerService> playlistManager = new Mock<IPlaylistManagerService>();
        playlistManager.Setup(p => p.ReorderPlaylistsAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(true);
        // Tracks loading isn't relevant here
        playlistManager.Setup(p => p.LoadPlaylistTracks(It.IsAny<string>())).Returns(new List<MediaFile>());
        Mock<ITrackNavigationService> nav = new Mock<ITrackNavigationService>();
        IUiDispatcher ui = new MockUIDispatcher();
        Mock<IDatabaseSaveService> saveSvc = new Mock<IDatabaseSaveService>();
        Mock<ILogger<PlaylistTabsViewModel>> logger = new Mock<ILogger<PlaylistTabsViewModel>>();

        SharedDataModel shared = new SharedDataModel();
        ISelectionService selection = new TestSelectionService();

        PlaylistTabsViewModel vm = new PlaylistTabsViewModel(
            musicLibrary.Object,
            shared,
            settingsManager.Object,
            fileImport.Object,
            playlistManager.Object,
            nav.Object,
            ui,
            saveSvc.Object,
            selection,
            logger.Object);

        vm.LoadPlaylistTabs();
        // Select middle tab "B"
        vm.SelectedTabIndex = 1;
        string selectedName = vm.TabList[vm.SelectedTabIndex].Name;

        // Act: move last tab (index 2) to front (index 0)
        await vm.ReorderTabsCommand.ExecuteAsync((2, 0));

        // Assert order changed
        vm.TabList.Select(t => t.Name).Should().ContainInOrder("C", "A", "B");
        // Selected tab should still be the same logical tab ("B") now at index 2
        vm.TabList[vm.SelectedTabIndex].Name.Should().Be(selectedName);
    }
}
