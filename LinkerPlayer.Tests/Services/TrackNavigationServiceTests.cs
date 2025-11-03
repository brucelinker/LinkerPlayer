using FluentAssertions;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace LinkerPlayer.Tests.Services;

public class TrackNavigationServiceTests
{
    private readonly Mock<ILogger<TrackNavigationService>> _mockLogger;
    private readonly TrackNavigationService _trackNavigationService;

    public TrackNavigationServiceTests()
    {
        _mockLogger = new Mock<ILogger<TrackNavigationService>>();
        _trackNavigationService = new TrackNavigationService(_mockLogger.Object);
    }

    //[Theory]
    //[InlineData(0, 1)] // First track -> Second track
    //[InlineData(2, 3)] // Middle track -> Next track
    //[InlineData(4, 0)] // Last track -> First track (wrap around)
    //public void GetNextTrackIndex_SequentialMode_ShouldReturnCorrectIndex(int currentIndex, int expectedIndex)
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();

    //    // Act
    //    var result = _trackNavigationService.GetNextTrackIndex(testTracks, currentIndex, shuffleMode: false);

    //    // Assert
    //    result.Should().Be(expectedIndex);
    //}

    //[Theory]
    //[InlineData(1, 0)] // Second track -> First track
    //[InlineData(3, 2)] // Middle track -> Previous track
    //[InlineData(0, 4)] // First track -> Last track (wrap around)
    //public void GetPreviousTrackIndex_SequentialMode_ShouldReturnCorrectIndex(int currentIndex, int expectedIndex)
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();

    //    // Act
    //    var result = _trackNavigationService.GetPreviousTrackIndex(testTracks, currentIndex, shuffleMode: false);

    //    // Assert
    //    result.Should().Be(expectedIndex);
    //}

    [Fact]
    public void GetNextTrackIndex_WithEmptyTrackList_ShouldReturnMinusOne()
    {
        // Arrange
        List<MediaFile> emptyTracks = new List<MediaFile>();

        // Act
        int result = _trackNavigationService.GetNextTrackIndex(emptyTracks, 0, shuffleMode: false);

        // Assert
        result.Should().Be(-1);
    }

    [Fact]
    public void GetPreviousTrackIndex_WithEmptyTrackList_ShouldReturnMinusOne()
    {
        // Arrange
        List<MediaFile> emptyTracks = new List<MediaFile>();

        // Act
        int result = _trackNavigationService.GetPreviousTrackIndex(emptyTracks, 0, shuffleMode: false);

        // Assert
        result.Should().Be(-1);
    }

    //[Theory]
    //[InlineData(-1, 0)] // Invalid index -> First track
    //[InlineData(10, 0)] // Out of bounds index -> First track
    //public void GetNextTrackIndex_WithInvalidCurrentIndex_ShouldReturnFirstTrack(int invalidIndex, int expectedIndex)
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();

    //    // Act
    //    var result = _trackNavigationService.GetNextTrackIndex(testTracks, invalidIndex, shuffleMode: false);

    //    // Assert
    //    result.Should().Be(expectedIndex);
    //}

    //[Theory]
    //[InlineData(-1, 4)] // Invalid index -> Last track
    //[InlineData(10, 4)] // Out of bounds index -> Last track
    //public void GetPreviousTrackIndex_WithInvalidCurrentIndex_ShouldReturnLastTrack(int invalidIndex, int expectedIndex)
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();

    //    // Act
    //    var result = _trackNavigationService.GetPreviousTrackIndex(testTracks, invalidIndex, shuffleMode: false);

    //    // Assert
    //    result.Should().Be(expectedIndex);
    //}

    //[Fact]
    //public void InitializeShuffle_WithValidTracks_ShouldCreateShuffleList()
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();

    //    // Act
    //    _trackNavigationService.InitializeShuffle(testTracks);

    //    // Assert
    //    _trackNavigationService.GetShufflePosition().Should().Be(0);
    //}

    //[Fact]
    //public void InitializeShuffle_WithCurrentTrackId_ShouldSetCorrectPosition()
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();
    //    var currentTrackId = "3";

    //    // Act
    //    _trackNavigationService.InitializeShuffle(testTracks, currentTrackId);

    //    // Assert
    //    var position = _trackNavigationService.GetShufflePosition();
    //    position.Should().BeGreaterOrEqualTo(0);
    //    position.Should().BeLessThan(testTracks.Count);
    //}

    [Fact]
    public void InitializeShuffle_WithEmptyTracks_ShouldClearShuffle()
    {
        // Arrange
        List<MediaFile> emptyTracks = new List<MediaFile>();

        // Act
        _trackNavigationService.InitializeShuffle(emptyTracks);

        // Assert
        _trackNavigationService.GetShufflePosition().Should().Be(-1);
    }

    //[Fact]
    //public void ClearShuffle_ShouldResetShuffleState()
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();
    //    _trackNavigationService.InitializeShuffle(testTracks);
    //    _trackNavigationService.GetShufflePosition().Should().Be(0); // Verify it was set

    //    // Act
    //    _trackNavigationService.ClearShuffle();

    //    // Assert
    //    _trackNavigationService.GetShufflePosition().Should().Be(-1);
    //}

    //[Fact]
    //public void SetShufflePosition_WithValidTrackId_ShouldReturnTrueAndSetPosition()
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();
    //    _trackNavigationService.InitializeShuffle(testTracks);
    //    var targetTrackId = "2";

    //    // Act
    //    var result = _trackNavigationService.SetShufflePosition(targetTrackId);

    //    // Assert
    //    result.Should().BeTrue();
    //    _trackNavigationService.GetShufflePosition().Should().BeGreaterOrEqualTo(0);
    //}

    //[Fact]
    //public void SetShufflePosition_WithInvalidTrackId_ShouldReturnFalse()
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();
    //    _trackNavigationService.InitializeShuffle(testTracks);
    //    var invalidTrackId = "999";

    //    // Act
    //    var result = _trackNavigationService.SetShufflePosition(invalidTrackId);

    //    // Assert
    //    result.Should().BeFalse();
    //}

    //[Theory]
    //[InlineData("")]
    //[InlineData(null)]
    //public void SetShufflePosition_WithNullOrEmptyTrackId_ShouldReturnFalse(string trackId)
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();
    //    _trackNavigationService.InitializeShuffle(testTracks);

    //    // Act
    //    var result = _trackNavigationService.SetShufflePosition(trackId);

    //    // Assert
    //    result.Should().BeFalse();
    //}

    //[Fact]
    //public void GetNextTrackIndex_ShuffleMode_ShouldReturnValidIndex()
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();
    //    _trackNavigationService.InitializeShuffle(testTracks);

    //    // Act
    //    var result = _trackNavigationService.GetNextTrackIndex(testTracks, 0, shuffleMode: true);

    //    // Assert
    //    result.Should().BeInRange(0, testTracks.Count - 1);
    //}

    //[Fact]
    //public void GetPreviousTrackIndex_ShuffleMode_ShouldReturnValidIndex()
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();
    //    _trackNavigationService.InitializeShuffle(testTracks);

    //    // Act
    //    var result = _trackNavigationService.GetPreviousTrackIndex(testTracks, 0, shuffleMode: true);

    //    // Assert
    //    result.Should().BeInRange(0, testTracks.Count - 1);
    //}

    //[Fact]
    //public void GetNextTrackIndex_ShuffleMode_WithoutInitializedShuffle_ShouldInitializeAndReturnValidIndex()
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();

    //    // Act
    //    var result = _trackNavigationService.GetNextTrackIndex(testTracks, 0, shuffleMode: true);

    //    // Assert
    //    result.Should().BeInRange(0, testTracks.Count - 1);
    //    _trackNavigationService.GetShufflePosition().Should().BeGreaterOrEqualTo(0);
    //}

    //[Fact]
    //public void ShuffleNavigation_ShouldEventuallyVisitAllTracks()
    //{
    //    // Arrange
    //    var testTracks = CreateTestTracks();
    //    _trackNavigationService.InitializeShuffle(testTracks);
    //    var visitedIndices = new HashSet<int>();
    //    var currentIndex = 0;

    //    // Act - Navigate through all tracks
    //    for (int i = 0; i < testTracks.Count; i++)
    //    {
    //        var nextIndex = _trackNavigationService.GetNextTrackIndex(testTracks, currentIndex, shuffleMode: true);
    //        visitedIndices.Add(nextIndex);
    //        currentIndex = nextIndex;
    //    }

    //    // Assert - Should have visited each track exactly once
    //    visitedIndices.Should().HaveCount(testTracks.Count);
    //    visitedIndices.Should().BeEquivalentTo(Enumerable.Range(0, testTracks.Count));
    //}

    //private static List<MediaFile> CreateTestTracks()
    //{
    //    // Create test tracks manually without triggering constructor dependencies
    //    return new List<MediaFile>
    //    {
    //        new() { Id = "1", Title = "Track 1", Path = "path1.mp3" },
    //        new() { Id = "2", Title = "Track 2", Path = "path2.mp3" },
    //        new() { Id = "3", Title = "Track 3", Path = "path3.mp3" },
    //        new() { Id = "4", Title = "Track 4", Path = "path4.mp3" },
    //        new() { Id = "5", Title = "Track 5", Path = "path5.mp3" }
    //    };
    //}
}
