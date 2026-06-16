using Shouldly;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.Services.Playback;
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
    private readonly Mock<IMusicLibrary> _mockMusicLibrary;
    private readonly Mock<ISharedDataModel> _mockSharedDataModel;
    private readonly Mock<ISettingsManager> _mockSettingsManager;
    private readonly Mock<IFileImportService> _mockFileImportService;
    private readonly Mock<IPlaylistManagerService> _mockPlaylistManagerService;
    private readonly Mock<ITrackNavigationService> _mockTrackNavigationService;
    private readonly Mock<IUiDispatcher> _mockUiDispatcher;
    private readonly Mock<IDatabaseSaveService> _mockDatabaseSaveService;
    private readonly Mock<ISelectionService> _mockSelectionService;
    private readonly IPlaybackCoordinator _playbackCoordinator;
    private readonly Mock<IMusicBrainzRatingService> _mockMusicBrainzRatingService;
    private readonly Mock<ILogger<PlaylistTabsViewModel>> _mockLogger;
    private readonly PlaylistTabsViewModel _vm;

    public PlaylistTabsViewModelTests()
    {
        _mockMusicLibrary = new Mock<IMusicLibrary>();
        _mockSharedDataModel = new Mock<ISharedDataModel>();
        _mockSettingsManager = new Mock<ISettingsManager>();
        _mockFileImportService = new Mock<IFileImportService>();
        _mockPlaylistManagerService = new Mock<IPlaylistManagerService>();
        _mockTrackNavigationService = new Mock<ITrackNavigationService>();
        _mockUiDispatcher = new Mock<IUiDispatcher>();

        // Ensure UI dispatcher executes actions immediately in tests
        _mockUiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Action>())).Returns<Action>(a => { a(); return Task.CompletedTask; });
        _mockUiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<Task>>())).Returns<Func<Task>>(async f => await f());
        _mockUiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<object>>())).Returns<Func<object>>(f => Task.FromResult(f()));
        _mockUiDispatcher.Setup(d => d.InvokeAsync(It.IsAny<Func<Task<object>>>())).Returns<Func<Task<object>>>(async f => await f());
        _mockDatabaseSaveService = new Mock<IDatabaseSaveService>();
        _mockSelectionService = new Mock<ISelectionService>();

        // Provide a simple backing store for CurrentTrack/CurrentTrackIndex and raise events when SetTrack is called
        MediaFile? currentTrack = null;
        int currentIndex = -1;
        _mockSelectionService.SetupGet(s => s.CurrentTrack).Returns(() => currentTrack);
        _mockSelectionService.SetupGet(s => s.CurrentTrackIndex).Returns(() => currentIndex);
        _mockSelectionService.SetupGet(s => s.MultiSelection).Returns(() => Array.Empty<MediaFile>());
        _mockSelectionService.SetupGet(s => s.CurrentTab).Returns(() => null as PlaylistTab);
        _mockSelectionService.Setup(s => s.SetTrack(It.IsAny<MediaFile?>(), It.IsAny<int>()))
            .Callback<MediaFile?, int>((t, idx) =>
            {
                currentTrack = t;
                currentIndex = idx;
                _mockSelectionService.Raise(s => s.TrackChanged += null, t);
                _mockSelectionService.Raise(s => s.PropertyChanged += null, new System.ComponentModel.PropertyChangedEventArgs(nameof(ISelectionService.CurrentTrack)));
                _mockSelectionService.Raise(s => s.PropertyChanged += null, new System.ComponentModel.PropertyChangedEventArgs(nameof(ISelectionService.CurrentTrackIndex)));
            });
        _playbackCoordinator = new TestPlaybackCoordinator();
        _mockMusicBrainzRatingService = new Mock<IMusicBrainzRatingService>();
        _mockLogger = new Mock<ILogger<PlaylistTabsViewModel>>();

        _mockSettingsManager.Setup(s => s.Settings).Returns(new AppSettings());

        // Backing store for shared data model used by the view model
        MediaFile? sharedSelectedTrack = null;
        int sharedSelectedIndex = -1;
        _mockSharedDataModel.SetupGet(s => s.SelectedTrack).Returns(() => sharedSelectedTrack);
        _mockSharedDataModel.SetupGet(s => s.SelectedTrackIndex).Returns(() => sharedSelectedIndex);
        _mockSharedDataModel.Setup(s => s.UpdateSelectedTrack(It.IsAny<MediaFile>())).Callback<MediaFile>(t => sharedSelectedTrack = t);
        _mockSharedDataModel.Setup(s => s.UpdateSelectedTrackIndex(It.IsAny<int>())).Callback<int>(i => sharedSelectedIndex = i);

        _vm = new PlaylistTabsViewModel(
            _mockMusicLibrary.Object,
            _mockSharedDataModel.Object,
            _mockSettingsManager.Object,
            _mockFileImportService.Object,
            _mockPlaylistManagerService.Object,
            _mockTrackNavigationService.Object,
            _mockUiDispatcher.Object,
            _mockDatabaseSaveService.Object,
            _mockSelectionService.Object,
            _playbackCoordinator,
            Mock.Of<IImportCancellationService>(),
            _mockMusicBrainzRatingService.Object,
            _mockLogger.Object
        );
    }

    [StaFact]
    public void BasicTest_ShouldPass()
    {
        bool result = true;
        result.ShouldBeTrue();
    }

    [StaFact]
    public void DoubleClick_SameTrack_DoesNotRestart()
    {
        MediaFile track = new MediaFile { Id = "123", Title = "Test" };
        _vm.ActiveTrack = track;

        _vm.OnDoubleClickDataGrid(); // with track selected

        _mockSharedDataModel.Verify(s => s.UpdateActiveTrack(track), Times.Once); // or twice with the null trick
    }

    [StaFact]
    public void ColumnRegeneration_AlwaysHasPlayPauseColumn()
    {
        // Arrange - force zero tag columns
        FieldInfo? selectedField = typeof(PlaylistTabsViewModel)
            .GetField("_selectedColumnNames", BindingFlags.NonPublic | BindingFlags.Instance);

        selectedField!.SetValue(_vm, new List<string>());

        DataGrid dg = new DataGrid();

        // Act - call the real method (now internal = accessible)
        PlaylistTabs playlistTabs = new PlaylistTabs { DataContext = _vm };
        playlistTabs.RegenerateColumns(dg);

        // Assert
        dg.Columns.Count.ShouldBe(1); // only Play/Pause
        DataGridTemplateColumn col = dg.Columns[0].ShouldBeOfType<DataGridTemplateColumn>();
        col.CellTemplate.ShouldNotBeNull(); // proves Application.Current.TryFindResource worked
    }

    // Add more tests for selection sync, tab reordering, scroll restore, etc.

    [StaFact]
    public void Startup_LoadPlaylistTabs_ShouldRestoreSelectedTrack()
    {
        // Arrange
       ObservableCollection<Playlist> playlists = new ObservableCollection<Playlist>
        {
            new Playlist
            {
                Name = "TestPlaylist",
                TrackIds = new ObservableCollection<string> { "trk1", "trk2" },
                SelectedTrackId = "trk2"
            }
        };
        RangeObservableCollection<MediaFile> mainLibrary = new RangeObservableCollection<MediaFile>
        {
            new MediaFile { Id = "trk1", FileName = "A", Path = "A.mp3" },
            new MediaFile { Id = "trk2", FileName = "B", Path = "B.mp3" }
        };
        _mockMusicLibrary.SetupGet(m => m.Playlists).Returns(playlists);
        _mockMusicLibrary.Setup(m => m.GetPlaylists()).Returns(playlists.ToList());
        _mockMusicLibrary.SetupGet(m => m.MainLibrary).Returns(mainLibrary);

        AppSettings appSettings = new AppSettings { SelectedTabIndex = 1 };
        _mockSettingsManager.SetupGet(s => s.Settings).Returns(appSettings);

        _mockPlaylistManagerService.Setup(p => p.LoadPlaylistTracks("TestPlaylist")).Returns(new List<MediaFile>
        {
            new MediaFile { Id = "trk1", FileName = "A", Path = "A.mp3" },
            new MediaFile { Id = "trk2", FileName = "B", Path = "B.mp3" }
        });

        // Act
        _vm.LoadPlaylistTabs();

        // Assert
        _vm.SelectedTrack.ShouldNotBeNull();
        _vm.SelectedTrack!.Id.ShouldBe("trk2");
        _vm.SelectedTrackIndex.ShouldBe(1);
    }

    [StaFact]
    public async Task LoadSelectedPlaylistTracksAsync_ShouldPopulateSelectedTab()
    {
        // Arrange
        ObservableCollection<Playlist> playlists = new ObservableCollection<Playlist>
        {
            new Playlist
            {
                Name = "P1",
                TrackIds = new ObservableCollection<string> { "t1", "t2" },
                SelectedTrackId = "t1"
            }
        };
        _mockMusicLibrary.SetupGet(m => m.Playlists).Returns(playlists);
        _mockMusicLibrary.Setup(m => m.GetPlaylists()).Returns(playlists.ToList());
        _mockMusicLibrary.SetupGet(m => m.MainLibrary).Returns(new RangeObservableCollection<MediaFile>
        {
            new MediaFile { Id = "t1", FileName = "A", Path = "A.mp3" },
            new MediaFile { Id = "t2", FileName = "B", Path = "B.mp3" }
        });

        //Mock<ISettingsManager> settingsManager = new Mock<ISettingsManager>();
        _mockSettingsManager.SetupGet(s => s.Settings).Returns(new AppSettings { SelectedTabIndex = 0 });

        _mockPlaylistManagerService.Setup(p => p.LoadPlaylistTracks("P1")).Returns(new List<MediaFile>
        {
            new MediaFile { Id = "t1", FileName = "A", Path = "A.mp3" },
            new MediaFile { Id = "t2", FileName = "B", Path = "B.mp3" }
        });

        // Seed tabs
        _vm.LoadPlaylistTabs();
        // Make selected tab empty to force load via async method:
        // Instead of clearing (not possible on read-only), recreate playlist manager mock to return empty then populate.
        _mockPlaylistManagerService.Setup(p => p.LoadPlaylistTracks("P1")).Returns(new List<MediaFile>());
        _vm.LoadPlaylistTabs(); // reload with empty
        _mockPlaylistManagerService.Setup(p => p.LoadPlaylistTracks("P1")).Returns(new List<MediaFile>
        {
            new MediaFile { Id = "t1", FileName = "A", Path = "A.mp3" },
            new MediaFile { Id = "t2", FileName = "B", Path = "B.mp3" }
        });

        // Act
        await _vm.LoadSelectedPlaylistTracksAsync();

        // Assert
        _vm.TabList[0].Tracks.Count.ShouldBe(2);
    }

    [StaFact]
    public async Task ReorderTabs_ShouldMoveTab_AndPreserveSelection()
    {
        // Arrange
        ObservableCollection<Playlist> playlists = new ObservableCollection<Playlist>
        {
            new Playlist { Name = "A", TrackIds = new ObservableCollection<string>(), SelectedTrackId = null },
            new Playlist { Name = "B", TrackIds = new ObservableCollection<string>(), SelectedTrackId = null },
            new Playlist { Name = "C", TrackIds = new ObservableCollection<string>(), SelectedTrackId = null }
        };
        _mockMusicLibrary.SetupGet(m => m.Playlists).Returns(playlists);
        _mockMusicLibrary.Setup(m => m.GetPlaylists()).Returns(playlists.ToList());
        _mockMusicLibrary.SetupGet(m => m.MainLibrary).Returns(new RangeObservableCollection<MediaFile>());

        _mockSettingsManager.SetupGet(s => s.Settings).Returns(new AppSettings { SelectedTabIndex = 1 });
        _mockPlaylistManagerService.Setup(p => p.ReorderPlaylistsAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(true);

        // Tracks loading isn't relevant here
        _mockPlaylistManagerService.Setup(p => p.LoadPlaylistTracks(It.IsAny<string>())).Returns(new List<MediaFile>());

        _vm.LoadPlaylistTabs();
        // TabList is now ["Music Library"(0), "A"(1), "B"(2), "C"(3)]
        // Select "A" (the first playlist, index 1)
        _vm.SelectedTabIndex = 1;
        string selectedName = _vm.TabList[_vm.SelectedTabIndex].Name;

        // Act: move "C" (index 3) to the first playlist slot (index 1), pushing A and B down
        await _vm.ReorderTabsCommand.ExecuteAsync((3, 1));

        // Assert: playlist order is now C, A, B (Music Library stays at index 0)
        _vm.TabList.Skip(1).Select(t => t.Name).ShouldBe(new[] { "C", "A", "B" });
        // Selected tab should still be the same logical tab ("A"), now at index 2
        _vm.TabList[_vm.SelectedTabIndex].Name.ShouldBe(selectedName);
    }

    [StaFact]
    public void ColumnRegeneration_PreservesIconAndRebuildsDynamicColumns()
    {
        // Arrange: set two visible columns
        FieldInfo? selectedField = typeof(PlaylistTabsViewModel)
            .GetField("_selectedColumnNames", BindingFlags.NonPublic | BindingFlags.Instance);
        selectedField!.SetValue(_vm, new List<string> { "Title", "Artist" });

        DataGrid dg = new DataGrid();
        PlaylistTabs playlistTabs = new PlaylistTabs { DataContext = _vm };

        // Act: first regeneration should inject icon column and add dynamic ones
        playlistTabs.RegenerateColumns(dg);

        // Assert: one static (icon) + two dynamic
        dg.Columns.Count.ShouldBe(3);
        dg.Columns[0].ShouldBeOfType<DataGridTemplateColumn>();
        dg.Columns[1].Header.ShouldBe("Title");
        dg.Columns[2].Header.ShouldBe("Artist");

        // Change selection to a single column and regenerate
        selectedField.SetValue(_vm, new List<string> { "Title" });
        playlistTabs.RegenerateColumns(dg);

        // Assert: icon preserved, only one dynamic column remains
        dg.Columns.Count.ShouldBe(2);
        dg.Columns[0].ShouldBeOfType<DataGridTemplateColumn>();
        dg.Columns[1].Header.ShouldBe("Title");
    }

    [StaFact]
    public void ColumnRegeneration_PreservesTwoStaticWhenPresent()
    {
        // Arrange: simulate XAML-defined index and icon columns already present
        FieldInfo? selectedField = typeof(PlaylistTabsViewModel)
            .GetField("_selectedColumnNames", BindingFlags.NonPublic | BindingFlags.Instance);
        selectedField!.SetValue(_vm, new List<string> { "Title", "AlbumArtist", "Duration" });

        DataGrid dg = new DataGrid();
        DataGridTextColumn indexCol = new DataGridTextColumn { Header = "#" };
        DataGridTemplateColumn iconCol = new DataGridTemplateColumn { Header = string.Empty, CellTemplate = new System.Windows.DataTemplate() };
        dg.Columns.Add(indexCol);
        dg.Columns.Add(iconCol);

        PlaylistTabs playlistTabs = new PlaylistTabs { DataContext = _vm };

        // Act
        playlistTabs.RegenerateColumns(dg);

        // Assert: preserves first two, appends 3 dynamic with correct headers (mapped names)
        dg.Columns.Count.ShouldBe(5);
        dg.Columns[0].Header.ShouldBe("#");
        dg.Columns[1].ShouldBeOfType<DataGridTemplateColumn>();
        dg.Columns[2].Header.ShouldBe("Title");
        dg.Columns[3].Header.ShouldBe("Album Artist");
        dg.Columns[4].Header.ShouldBe("Duration");

        // Change selection to a single dynamic column and regenerate
        selectedField.SetValue(_vm, new List<string> { "Artist" });
        playlistTabs.RegenerateColumns(dg);

        // Assert: still preserves first two, now only one dynamic column
        dg.Columns.Count.ShouldBe(3);
        dg.Columns[0].Header.ShouldBe("#");
        dg.Columns[1].ShouldBeOfType<DataGridTemplateColumn>();
        dg.Columns[2].Header.ShouldBe("Artist");
    }

    public void Dispose() { }
}
