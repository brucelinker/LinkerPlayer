using LinkerPlayer.Models;
using LinkerPlayer.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;

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

    [StaFact]
    public void GetNextTrackIndex_WithEmptyTrackList_ShouldReturnMinusOne()
    {
        // Arrange
        List<MediaFile> emptyTracks = new List<MediaFile>();

        // Act
        int result = _trackNavigationService.GetNextTrackIndex(emptyTracks, 0, shuffleMode: false);

        // Assert
        result.ShouldBe(-1);
    }

    [StaFact]
    public void GetPreviousTrackIndex_WithEmptyTrackList_ShouldReturnMinusOne()
    {
        // Arrange
        List<MediaFile> emptyTracks = new List<MediaFile>();

        // Act
        int result = _trackNavigationService.GetPreviousTrackIndex(emptyTracks, 0, shuffleMode: false);

        // Assert
        result.ShouldBe(-1);
    }

    [StaFact]
    public void InitializeShuffle_WithEmptyTracks_ShouldClearShuffle()
    {
        // Arrange
        List<MediaFile> emptyTracks = new List<MediaFile>();

        // Act
        _trackNavigationService.InitializeShuffle(emptyTracks);

        // Assert
        ((ITrackNavigationService)_trackNavigationService).GetShufflePosition().ShouldBe(-1);
    }

    private static List<MediaFile> CreateTracks(int count)
    {
        List<MediaFile> tracks = new List<MediaFile>();
        for (int i = 0; i < count; i++)
        {
            tracks.Add(new MediaFile
            {
                Id = $"track-{i}",
                Title = $"Track {i}",
                Path = $"C:\\Music\\track-{i}.mp3",
                Duration = TimeSpan.FromSeconds(200 + i)
            });
        }

        return tracks;
    }

    [StaFact]
    public void GetNextTrackIndex_ShuffleMode_ShouldNotRepeatSameTrackConsecutively()
    {
        // Arrange
        List<MediaFile> tracks = CreateTracks(10);

        int currentIndex = 0;
        _trackNavigationService.InitializeShuffle(tracks, tracks[currentIndex].Id);

        // Act
        int result = _trackNavigationService.GetNextTrackIndex(tracks, currentIndex, shuffleMode: true);

        // Assert
        result.ShouldNotBe(currentIndex);
    }

    [StaFact]
    public void GetPreviousTrackIndex_ShuffleMode_ShouldNotRepeatSameTrackConsecutively()
    {
        // Arrange
        List<MediaFile> tracks = CreateTracks(10);

        int currentIndex = 0;
        _trackNavigationService.InitializeShuffle(tracks, tracks[currentIndex].Id);

        // Act
        int result = _trackNavigationService.GetPreviousTrackIndex(tracks, currentIndex, shuffleMode: true);

        // Assert
        result.ShouldNotBe(currentIndex);
    }

    [StaFact]
    public void GetNextTrackIndex_ShuffleMode_ShouldTraverseFullCycleWithoutRepeats()
    {
        // Arrange
        List<MediaFile> tracks = CreateTracks(8);

        int startIndex = 0;
        _trackNavigationService.InitializeShuffle(tracks, tracks[startIndex].Id);

        HashSet<int> visited = new HashSet<int>();
        int currentIndex = startIndex;

        // Act
        for (int i = 0; i < tracks.Count; i++)
        {
            visited.Add(currentIndex);
            currentIndex = _trackNavigationService.GetNextTrackIndex(tracks, currentIndex, shuffleMode: true);
        }

        // Assert
        visited.Count.ShouldBe(tracks.Count);
    }
}
