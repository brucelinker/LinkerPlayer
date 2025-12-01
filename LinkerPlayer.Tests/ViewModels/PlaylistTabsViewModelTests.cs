using FluentAssertions;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.Tests.Mocks;
using LinkerPlayer.UserControls;
using LinkerPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Controls;

namespace LinkerPlayer.Tests.ViewModels;

public class PlaylistTabsViewModelTests : IDisposable
{
    private readonly Mock<IMusicLibrary> _mockLibrary;
    private readonly Mock<ISharedDataModel> _mockShared;
    private readonly Mock<ISettingsManager> _mockSettings;
    private readonly Mock<IFileImportService> _mockFileImport;
    private readonly Mock<IPlaylistManagerService> _mockPlaylist;
    private readonly Mock<ITrackNavigationService> _mockNav;
    private readonly Mock<IUiDispatcher> _mockDispatcher;
    private readonly Mock<IDatabaseSaveService> _mockSave;
    private readonly Mock<ISelectionService> _mockSelection;
    private readonly Mock<ILogger<PlaylistTabsViewModel>> _mockLogger;
    private readonly PlaylistTabsViewModel _vm;

    public PlaylistTabsViewModelTests()
    {
        _mockLibrary = new Mock<IMusicLibrary>();
        _mockShared = new Mock<ISharedDataModel>();
        _mockSettings = new Mock<ISettingsManager>();
        _mockFileImport = new Mock<IFileImportService>();
        _mockPlaylist = new Mock<IPlaylistManagerService>();
        _mockNav = new Mock<ITrackNavigationService>();
        _mockDispatcher = new Mock<IUiDispatcher>();
        _mockSave = new Mock<IDatabaseSaveService>();
        _mockSelection = new Mock<ISelectionService>();
        _mockLogger = new Mock<ILogger<PlaylistTabsViewModel>>();

        _mockSettings.Setup(s => s.Settings).Returns(new AppSettings());

        _vm = new PlaylistTabsViewModel(
            _mockLibrary.Object,
            _mockShared.Object,
            _mockSettings.Object,
            _mockFileImport.Object,
            _mockPlaylist.Object,
            _mockNav.Object,
            _mockDispatcher.Object,
            _mockSave.Object,
            _mockSelection.Object,
            _mockLogger.Object
        );
    }

    [StaFact]
    public void BasicTest_ShouldPass()
    {
        bool result = true;
        result.Should().BeTrue();
    }

    [StaFact]
    public void DoubleClick_SameTrack_DoesNotRestart()
    {
        var track = new MediaFile { Id = "123", Title = "Test" };
        _vm.ActiveTrack = track;

        _vm.OnDoubleClickDataGrid(); // with track selected

        _mockShared.Verify(s => s.UpdateActiveTrack(track), Times.Once); // or twice with the null trick
    }

    [StaFact]
    public void ColumnRegeneration_AlwaysHasPlayPauseColumn()
    {
        // Arrange - force zero tag columns
        var selectedField = typeof(PlaylistTabsViewModel)
            .GetField("_selectedColumnNames", BindingFlags.NonPublic | BindingFlags.Instance);

        selectedField!.SetValue(_vm, new List<string>());

        var dg = new DataGrid();

        // Act - call the real method (now internal = accessible)
        var playlistTabs = new PlaylistTabs { DataContext = _vm };
        playlistTabs.RegenerateColumns(dg);

        // Assert
        Assert.Single(dg.Columns); // only Play/Pause
        var col = Assert.IsType<DataGridTemplateColumn>(dg.Columns[0]);
        Assert.NotNull(col.CellTemplate); // proves Application.Current.TryFindResource worked
    }

    // Add more tests for selection sync, tab reordering, scroll restore, etc.

    [StaFact]
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

    [StaFact]
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
        // Make selected tab empty to force load via async method:
        // Instead of clearing (not possible on read-only), recreate playlist manager mock to return empty then populate.
        playlistManager.Setup(p => p.LoadPlaylistTracks("P1")).Returns(new List<MediaFile>());
        vm.LoadPlaylistTabs(); // reload with empty
        playlistManager.Setup(p => p.LoadPlaylistTracks("P1")).Returns(new List<MediaFile>
        {
            new MediaFile { Id = "t1", FileName = "A", Path = "A.mp3" },
            new MediaFile { Id = "t2", FileName = "B", Path = "B.mp3" }
        });

        // Act
        await vm.LoadSelectedPlaylistTracksAsync();

        // Assert
        vm.TabList[0].Tracks.Should().HaveCount(2);
    }

    [StaFact]
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

    public void Dispose() { }
}
