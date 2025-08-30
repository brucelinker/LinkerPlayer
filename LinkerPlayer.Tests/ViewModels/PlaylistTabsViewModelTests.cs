using FluentAssertions;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.ObjectModel;
using Xunit;

namespace LinkerPlayer.Tests.ViewModels;

public class PlaylistTabsViewModelTests
{
    private readonly Mock<MusicLibrary> _mockMusicLibrary;
    private readonly Mock<SharedDataModel> _mockSharedDataModel;
    private readonly Mock<SettingsManager> _mockSettingsManager;
    private readonly Mock<IFileImportService> _mockFileImportService;
    private readonly Mock<IPlaylistManagerService> _mockPlaylistManagerService;
    private readonly Mock<ITrackNavigationService> _mockTrackNavigationService;
    private readonly Mock<IUIDispatcher> _mockUIDispatcher;
    private readonly Mock<ILogger<PlaylistTabsViewModel>> _mockLogger;
    private readonly PlaylistTabsViewModel _viewModel;

    public PlaylistTabsViewModelTests()
    {
        _mockMusicLibrary = new Mock<MusicLibrary>(Mock.Of<ILogger<MusicLibrary>>());
        _mockSharedDataModel = new Mock<SharedDataModel>();
        _mockSettingsManager = new Mock<SettingsManager>(Mock.Of<ILogger<SettingsManager>>());
        _mockFileImportService = new Mock<IFileImportService>();
        _mockPlaylistManagerService = new Mock<IPlaylistManagerService>();
        _mockTrackNavigationService = new Mock<ITrackNavigationService>();
        _mockUIDispatcher = new Mock<IUIDispatcher>();
        _mockLogger = new Mock<ILogger<PlaylistTabsViewModel>>();

        // Setup basic mocks
        _mockSettingsManager.Setup(x => x.Settings).Returns(new AppSettings());
        
        // Setup UI dispatcher to execute actions immediately for testing
        _mockUIDispatcher.Setup(x => x.InvokeAsync(It.IsAny<Action>()))
                        .Callback<Action>(action => action())
                        .Returns(Task.CompletedTask);

        _mockUIDispatcher.Setup(x => x.InvokeAsync(It.IsAny<Func<Task>>()))
                        .Returns<Func<Task>>(func => func());

        _viewModel = new PlaylistTabsViewModel(
            _mockMusicLibrary.Object,
            _mockSharedDataModel.Object,
            _mockSettingsManager.Object,
            _mockFileImportService.Object,
            _mockPlaylistManagerService.Object,
            _mockTrackNavigationService.Object,
            _mockUIDispatcher.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithValidDependencies_ShouldInitializeCorrectly()
    {
        // Assert
        _viewModel.Should().NotBeNull();
        _viewModel.TabList.Should().NotBeNull();
        _viewModel.TabList.Should().BeEmpty();
        _viewModel.AllowDrop.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithNullDependency_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        FluentActions.Invoking(() => new PlaylistTabsViewModel(
            null!, // Null MusicLibrary
            _mockSharedDataModel.Object,
            _mockSettingsManager.Object,
            _mockFileImportService.Object,
            _mockPlaylistManagerService.Object,
            _mockTrackNavigationService.Object,
            _mockUIDispatcher.Object,
            _mockLogger.Object))
        .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void LoadPlaylistTabs_WithValidPlaylists_ShouldPopulateTabList()
    {
        // Arrange
        var playlists = new List<Playlist>
        {
            new() { Name = "Playlist 1" },
            new() { Name = "Playlist 2" },
            new() { Name = "Playlist 3" }
        };

        _mockMusicLibrary.Setup(x => x.GetPlaylists()).Returns(playlists);
        
        _mockPlaylistManagerService.SetupSequence(x => x.LoadPlaylistTracksAsync(It.IsAny<string>()))
                                  .ReturnsAsync(new List<MediaFile>())
                                  .ReturnsAsync(new List<MediaFile>())
                                  .ReturnsAsync(new List<MediaFile>());

        // Act
        _viewModel.LoadPlaylistTabs();

        // Assert
        _viewModel.TabList.Should().HaveCount(3);
        _viewModel.TabList.Should().Contain(t => t.Name == "Playlist 1");
        _viewModel.TabList.Should().Contain(t => t.Name == "Playlist 2");
        _viewModel.TabList.Should().Contain(t => t.Name == "Playlist 3");
    }

    [Fact]
    public void LoadPlaylistTabs_WithEmptyPlaylistNames_ShouldSkipEmptyNames()
    {
        // Arrange
        var playlists = new List<Playlist>
        {
            new() { Name = "Valid Playlist" },
            new() { Name = "" },
            new() { Name = "   " },
            new() { Name = "Another Valid Playlist" }
        };

        _mockMusicLibrary.Setup(x => x.GetPlaylists()).Returns(playlists);
        
        _mockPlaylistManagerService.Setup(x => x.LoadPlaylistTracksAsync(It.IsAny<string>()))
                                  .ReturnsAsync(new List<MediaFile>());

        // Act
        _viewModel.LoadPlaylistTabs();

        // Assert
        _viewModel.TabList.Should().HaveCount(2);
        _viewModel.TabList.Should().Contain(t => t.Name == "Valid Playlist");
        _viewModel.TabList.Should().Contain(t => t.Name == "Another Valid Playlist");
    }

    [Fact]
    public async Task NewPlaylistCommand_ShouldCreateNewPlaylistAndAddToTabList()
    {
        // Arrange
        var expectedTab = new PlaylistTab 
        { 
            Name = "New Playlist (1)", 
            Tracks = new ObservableCollection<MediaFile>() 
        };

        _mockPlaylistManagerService.Setup(x => x.CreatePlaylistTabAsync("New Playlist"))
                                  .ReturnsAsync(expectedTab);

        // Act
        await _viewModel.NewPlaylistCommand.ExecuteAsync(null);

        // Assert
        _viewModel.TabList.Should().HaveCount(1);
        _viewModel.TabList.First().Name.Should().Be("New Playlist (1)");
        
        _mockPlaylistManagerService.Verify(x => x.CreatePlaylistTabAsync("New Playlist"), Times.Once);
    }

    [Fact]
    public async Task RemovePlaylistCommand_WithValidPlaylistTab_ShouldRemoveFromTabList()
    {
        // Arrange
        var playlistTab = new PlaylistTab { Name = "Test Playlist" };
        _viewModel.TabList.Add(playlistTab);
        _viewModel.TabList.Add(new PlaylistTab { Name = "Another Playlist" });

        _mockPlaylistManagerService.Setup(x => x.RemovePlaylistAsync("Test Playlist"))
                                  .ReturnsAsync(true);

        // Setup GetSelectedPlaylist dependencies
        _mockMusicLibrary.Setup(x => x.Playlists)
                        .Returns(new ObservableCollection<Playlist>());

        // Act
        await _viewModel.RemovePlaylistCommand.ExecuteAsync(playlistTab);

        // Assert
        _viewModel.TabList.Should().HaveCount(1);
        _viewModel.TabList.Should().NotContain(t => t.Name == "Test Playlist");
        
        _mockPlaylistManagerService.Verify(x => x.RemovePlaylistAsync("Test Playlist"), Times.Once);
    }

    [Fact]
    public async Task RemovePlaylistCommand_WithFailedRemoval_ShouldNotRemoveFromTabList()
    {
        // Arrange
        var playlistTab = new PlaylistTab { Name = "Test Playlist" };
        _viewModel.TabList.Add(playlistTab);

        _mockPlaylistManagerService.Setup(x => x.RemovePlaylistAsync("Test Playlist"))
                                  .ReturnsAsync(false);

        // Act
        await _viewModel.RemovePlaylistCommand.ExecuteAsync(playlistTab);

        // Assert
        _viewModel.TabList.Should().HaveCount(1);
        _viewModel.TabList.Should().Contain(t => t.Name == "Test Playlist");
    }

    [Fact]
    public async Task AddFilesCommand_WithValidFiles_ShouldAddTracksToCurrentPlaylist()
    {
        // Arrange
        var existingTab = new PlaylistTab 
        { 
            Name = "Existing Playlist", 
            Tracks = new ObservableCollection<MediaFile>() 
        };
        
        _viewModel.TabList.Add(existingTab);
        _viewModel.SelectedTab = existingTab;

        var importedTracks = new List<MediaFile>
        {
            new() { Id = "1", Title = "Song 1" },
            new() { Id = "2", Title = "Song 2" }
        };

        _mockFileImportService.Setup(x => x.ImportFilesAsync(It.IsAny<string[]>(), null))
                             .ReturnsAsync(importedTracks);

        _mockPlaylistManagerService.Setup(x => x.AddTracksToPlaylistAsync("Existing Playlist", importedTracks))
                                  .ReturnsAsync(true);

        // Mock file dialog behavior (this would normally require UI interaction)
        // In a real test, you'd abstract the file dialog into a service

        // Act - We can't easily test the full command due to OpenFileDialog dependency
        // But we can test the core logic through the private method if we expose it or create a separate service

        // Assert
        // This test demonstrates the limitation - UI dependencies make testing difficult
        // The solution is to extract the file selection logic into a separate service
    }

    [Fact]
    public void CanRemovePlaylist_WithSinglePlaylist_ShouldReturnFalse()
    {
        // Arrange
        var singleTab = new PlaylistTab { Name = "Only Playlist" };
        _viewModel.TabList.Add(singleTab);

        // Act
        var canRemove = _viewModel.RemovePlaylistCommand.CanExecute(singleTab);

        // Assert
        canRemove.Should().BeFalse();
    }

    [Fact]
    public void CanRemovePlaylist_WithMultiplePlaylists_ShouldReturnTrue()
    {
        // Arrange
        var tab1 = new PlaylistTab { Name = "Playlist 1" };
        var tab2 = new PlaylistTab { Name = "Playlist 2" };
        _viewModel.TabList.Add(tab1);
        _viewModel.TabList.Add(tab2);

        // Act
        var canRemove = _viewModel.RemovePlaylistCommand.CanExecute(tab1);

        // Assert
        canRemove.Should().BeTrue();
    }

    [Fact]
    public async Task RenamePlaylistCommand_WithValidNames_ShouldUpdatePlaylistName()
    {
        // Arrange
        var playlistTab = new PlaylistTab { Name = "New Name" };
        var oldName = "Old Name";

        _mockPlaylistManagerService.Setup(x => x.RenamePlaylistAsync(oldName, "New Name"))
                                  .ReturnsAsync(true);

        _mockMusicLibrary.Setup(x => x.Playlists)
                        .Returns(new ObservableCollection<Playlist> 
                        { 
                            new() { Name = "New Name" } 
                        });

        // Act
        await _viewModel.RenamePlaylistCommand.ExecuteAsync((playlistTab, oldName));

        // Assert
        _mockPlaylistManagerService.Verify(x => x.RenamePlaylistAsync(oldName, "New Name"), Times.Once);
    }

    [Fact]
    public async Task RenamePlaylistCommand_WithFailedRename_ShouldRevertName()
    {
        // Arrange
        var playlistTab = new PlaylistTab { Name = "New Name" };
        var oldName = "Old Name";

        _mockPlaylistManagerService.Setup(x => x.RenamePlaylistAsync(oldName, "New Name"))
                                  .ReturnsAsync(false);

        // Act
        await _viewModel.RenamePlaylistCommand.ExecuteAsync((playlistTab, oldName));

        // Assert
        playlistTab.Name.Should().Be(oldName); // Should be reverted
        _mockUIDispatcher.Verify(x => x.InvokeAsync(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void ProgressInfo_InitialState_ShouldBeCorrect()
    {
        // Assert
        _viewModel.ProgressInfo.Should().NotBeNull();
        _viewModel.ProgressInfo.IsProcessing.Should().BeFalse();
        _viewModel.ProgressInfo.ProcessedTracks.Should().Be(0);
        _viewModel.ProgressInfo.TotalTracks.Should().Be(1);
        _viewModel.ProgressInfo.Status.Should().BeEmpty();
    }
}