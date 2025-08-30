using FluentAssertions;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.ObjectModel;
using Xunit;

namespace LinkerPlayer.Tests.Services;

public class PlaylistManagerServiceTests
{
    private readonly Mock<MusicLibrary> _mockMusicLibrary;
    private readonly Mock<ILogger<PlaylistManagerService>> _mockLogger;
    private readonly PlaylistManagerService _playlistManagerService;

    public PlaylistManagerServiceTests()
    {
        _mockMusicLibrary = new Mock<MusicLibrary>(Mock.Of<ILogger<MusicLibrary>>());
        _mockLogger = new Mock<ILogger<PlaylistManagerService>>();
        _playlistManagerService = new PlaylistManagerService(_mockMusicLibrary.Object, _mockLogger.Object);
    }

    [Fact]
    public void GetUniquePlaylistName_WithNewName_ShouldReturnSameName()
    {
        // Arrange
        var baseName = "My Playlist";
        _mockMusicLibrary.Setup(x => x.Playlists)
                        .Returns(new ObservableCollection<Playlist>());

        // Act
        var result = _playlistManagerService.GetUniquePlaylistName(baseName);

        // Assert
        result.Should().Be(baseName);
    }

    [Fact]
    public void GetUniquePlaylistName_WithExistingName_ShouldAppendNumber()
    {
        // Arrange
        var baseName = "My Playlist";
        var existingPlaylists = new ObservableCollection<Playlist>
        {
            new() { Name = "My Playlist" },
            new() { Name = "My Playlist (1)" }
        };
        
        _mockMusicLibrary.Setup(x => x.Playlists).Returns(existingPlaylists);

        // Act
        var result = _playlistManagerService.GetUniquePlaylistName(baseName);

        // Assert
        result.Should().Be("My Playlist (2)");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void GetUniquePlaylistName_WithEmptyName_ShouldReturnNewPlaylist(string baseName)
    {
        // Arrange
        _mockMusicLibrary.Setup(x => x.Playlists)
                        .Returns(new ObservableCollection<Playlist>());

        // Act
        var result = _playlistManagerService.GetUniquePlaylistName(baseName);

        // Assert
        result.Should().Be("New Playlist");
    }

    [Fact]
    public async Task CreatePlaylistTabAsync_ShouldCreatePlaylistAndTab()
    {
        // Arrange
        var playlistName = "Test Playlist";
        var expectedPlaylist = new Playlist { Name = playlistName };
        var testTracks = new List<MediaFile>
        {
            new() { Title = "Song 1", Path = "path1.mp3" },
            new() { Title = "Song 2", Path = "path2.mp3" }
        };

        _mockMusicLibrary.Setup(x => x.AddNewPlaylistAsync(playlistName))
                        .ReturnsAsync(expectedPlaylist);
        
        _mockMusicLibrary.Setup(x => x.GetTracksFromPlaylist(playlistName))
                        .Returns(testTracks);

        // Act
        var result = await _playlistManagerService.CreatePlaylistTabAsync(playlistName);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be(playlistName);
        result.Tracks.Should().HaveCount(2);
        result.Tracks.Should().Contain(t => t.Title == "Song 1");
        result.Tracks.Should().Contain(t => t.Title == "Song 2");

        _mockMusicLibrary.Verify(x => x.AddNewPlaylistAsync(playlistName), Times.Once);
    }

    [Fact]
    public async Task CreatePlaylistTabAsync_WithNullOrEmptyName_ShouldThrowArgumentException()
    {
        // Act & Assert
        await FluentActions.Invoking(() => _playlistManagerService.CreatePlaylistTabAsync(""))
                          .Should().ThrowAsync<ArgumentException>();
        
        await FluentActions.Invoking(() => _playlistManagerService.CreatePlaylistTabAsync(null!))
                          .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AddTracksToPlaylistAsync_WithValidData_ShouldReturnTrue()
    {
        // Arrange
        var playlistName = "Test Playlist";
        var tracks = new List<MediaFile>
        {
            new() { Id = "1", Title = "Song 1" },
            new() { Id = "2", Title = "Song 2" }
        };

        _mockMusicLibrary.Setup(x => x.SaveTracksBatchAsync(tracks))
                        .Returns(Task.CompletedTask);
        
        _mockMusicLibrary.Setup(x => x.AddTracksToPlaylistAsync(
                            It.Is<IList<string>>(ids => ids.Count == 2), 
                            playlistName, 
                            false))
                        .Returns(Task.CompletedTask);
        
        _mockMusicLibrary.Setup(x => x.SaveToDatabaseAsync())
                        .Returns(Task.CompletedTask);

        // Act
        var result = await _playlistManagerService.AddTracksToPlaylistAsync(playlistName, tracks);

        // Assert
        result.Should().BeTrue();
        
        _mockMusicLibrary.Verify(x => x.SaveTracksBatchAsync(tracks), Times.Once);
        _mockMusicLibrary.Verify(x => x.AddTracksToPlaylistAsync(
            It.Is<IList<string>>(ids => ids.SequenceEqual(new[] { "1", "2" })), 
            playlistName, 
            false), Times.Once);
        _mockMusicLibrary.Verify(x => x.SaveToDatabaseAsync(), Times.Once);
    }

    [Fact]
    public async Task AddTracksToPlaylistAsync_WithEmptyTracks_ShouldReturnFalse()
    {
        // Arrange
        var playlistName = "Test Playlist";
        var emptyTracks = new List<MediaFile>();

        // Act
        var result = await _playlistManagerService.AddTracksToPlaylistAsync(playlistName, emptyTracks);

        // Assert
        result.Should().BeFalse();
        
        _mockMusicLibrary.Verify(x => x.SaveTracksBatchAsync(It.IsAny<IEnumerable<MediaFile>>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task AddTracksToPlaylistAsync_WithInvalidPlaylistName_ShouldReturnFalse(string playlistName)
    {
        // Arrange
        var tracks = new List<MediaFile> { new() { Id = "1" } };

        // Act
        var result = await _playlistManagerService.AddTracksToPlaylistAsync(playlistName, tracks);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveTrackFromPlaylistAsync_WithValidData_ShouldReturnTrue()
    {
        // Arrange
        var playlistName = "Test Playlist";
        var trackId = "track123";

        _mockMusicLibrary.Setup(x => x.RemoveTrackFromPlaylistAsync(playlistName, trackId))
                        .Returns(Task.CompletedTask);

        // Act
        var result = await _playlistManagerService.RemoveTrackFromPlaylistAsync(playlistName, trackId);

        // Assert
        result.Should().BeTrue();
        _mockMusicLibrary.Verify(x => x.RemoveTrackFromPlaylistAsync(playlistName, trackId), Times.Once);
    }

    [Theory]
    [InlineData("", "trackId")]
    [InlineData("playlist", "")]
    [InlineData(null, "trackId")]
    [InlineData("playlist", null)]
    public async Task RemoveTrackFromPlaylistAsync_WithInvalidParameters_ShouldReturnFalse(string playlistName, string trackId)
    {
        // Act
        var result = await _playlistManagerService.RemoveTrackFromPlaylistAsync(playlistName, trackId);

        // Assert
        result.Should().BeFalse();
        _mockMusicLibrary.Verify(x => x.RemoveTrackFromPlaylistAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoadPlaylistTracksAsync_WithValidPlaylist_ShouldReturnTracksWithUpdatedMetadata()
    {
        // Arrange
        var playlistName = "Test Playlist";
        var tracks = new List<MediaFile>
        {
            new() { Title = "Song 1", Path = "path1.mp3" },
            new() { Title = "Song 2", Path = "path2.mp3" }
        };

        _mockMusicLibrary.Setup(x => x.GetTracksFromPlaylist(playlistName))
                        .Returns(tracks);

        // Act
        var result = await _playlistManagerService.LoadPlaylistTracksAsync(playlistName);

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo(tracks);
        
        // Verify that UpdateFromFileMetadata was called on each track
        // Note: This would require making MediaFile methods virtual or using a wrapper/interface
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task LoadPlaylistTracksAsync_WithInvalidPlaylistName_ShouldReturnEmptyCollection(string playlistName)
    {
        // Act
        var result = await _playlistManagerService.LoadPlaylistTracksAsync(playlistName);

        // Assert
        result.Should().BeEmpty();
    }
}