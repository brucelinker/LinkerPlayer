using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;

namespace LinkerPlayer.Services;

public interface ITrackNavigationService
{
    int GetNextTrackIndex(IList<MediaFile> currentTracks, int currentIndex, bool shuffleMode);
    int GetPreviousTrackIndex(IList<MediaFile> currentTracks, int currentIndex, bool shuffleMode);
    void InitializeShuffle(IEnumerable<MediaFile> tracks, string? currentTrackId = null);
    void ClearShuffle();
    int GetShufflePosition();
    bool SetShufflePosition(string trackId);
}

public class TrackNavigationService : ITrackNavigationService
{
    private readonly ILogger<TrackNavigationService> _logger;
    private readonly List<MediaFile> _shuffleList = new();
    private int _shuffledIndex = 0;

    public TrackNavigationService(ILogger<TrackNavigationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int GetNextTrackIndex(IList<MediaFile> currentTracks, int currentIndex, bool shuffleMode)
    {
        if (currentTracks == null || !currentTracks.Any())
        {
            _logger.LogWarning("GetNextTrackIndex called with empty track list");
            return -1;
        }

        if (currentIndex < 0 || currentIndex >= currentTracks.Count)
        {
            _logger.LogWarning("GetNextTrackIndex called with invalid current index: {CurrentIndex} (Track count: {TrackCount})",
                currentIndex, currentTracks.Count);
            return 0; // Return first track if index is invalid
        }

        if (shuffleMode)
        {
            return GetNextShuffledIndex(currentTracks);
        }

        // Normal sequential navigation
        int nextIndex = currentIndex == currentTracks.Count - 1 ? 0 : currentIndex + 1;
        //_logger.LogDebug("Next track index (sequential): {NextIndex}", nextIndex);
        return nextIndex;
    }

    public int GetPreviousTrackIndex(IList<MediaFile> currentTracks, int currentIndex, bool shuffleMode)
    {
        if (currentTracks == null || !currentTracks.Any())
        {
            _logger.LogWarning("GetPreviousTrackIndex called with empty track list");
            return -1;
        }

        if (currentIndex < 0 || currentIndex >= currentTracks.Count)
        {
            _logger.LogWarning("GetPreviousTrackIndex called with invalid current index: {CurrentIndex} (Track count: {TrackCount})",
                currentIndex, currentTracks.Count);
            return currentTracks.Count - 1; // Return last track if index is invalid
        }

        if (shuffleMode)
        {
            return GetPreviousShuffledIndex(currentTracks);
        }

        // Normal sequential navigation
        int previousIndex = currentIndex == 0 ? currentTracks.Count - 1 : currentIndex - 1;
        //_logger.LogDebug("Previous track index (sequential): {PreviousIndex}", previousIndex);
        return previousIndex;
    }

    public void InitializeShuffle(IEnumerable<MediaFile> tracks, string? currentTrackId = null)
    {
        List<MediaFile>? trackList = tracks?.ToList();
        if (trackList == null || !trackList.Any())
        {
            _logger.LogWarning("InitializeShuffle called with empty track list");
            ClearShuffle();
            return;
        }

        _shuffleList.Clear();

        // Create a copy of the track list for shuffling
        List<MediaFile> tempList = new List<MediaFile>(trackList);
        Random random = new Random();

        // Fisher-Yates shuffle algorithm
        for (int i = tempList.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (tempList[i], tempList[j]) = (tempList[j], tempList[i]);
        }

        _shuffleList.AddRange(tempList);

        // Set shuffle position based on current track
        if (!string.IsNullOrWhiteSpace(currentTrackId))
        {
            SetShufflePosition(currentTrackId);
        }
        else
        {
            _shuffledIndex = 0;
        }

        //_logger.LogInformation("Initialized shuffle with {Count} tracks, current position: {Position}", 
        //    _shuffleList.Count, _shuffledIndex);
    }

    public void ClearShuffle()
    {
        _shuffleList.Clear();
        _shuffledIndex = 0;
        _logger.LogDebug("Shuffle list cleared");
    }

    public int GetShufflePosition()
    {
        return _shuffleList.Any() ? _shuffledIndex : -1;
    }

    public bool SetShufflePosition(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId) || !_shuffleList.Any())
        {
            return false;
        }

        int index = _shuffleList.FindIndex(track => track.Id == trackId);
        if (index >= 0)
        {
            _shuffledIndex = index;
            //_logger.LogDebug("Set shuffle position to {Position} for track {TrackId}", index, trackId);
            return true;
        }

        _logger.LogWarning("Track {TrackId} not found in shuffle list", trackId);
        return false;
    }

    private static int FindTrackIndexById(IList<MediaFile> tracks, string id)
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }
    private int GetNextShuffledIndex(IList<MediaFile> currentTracks)
    {
        if (!_shuffleList.Any())
        {
            InitializeShuffle(currentTracks);
        }

        if (!_shuffleList.Any())
        {
            _logger.LogError("Failed to initialize shuffle list");
            return 0;
        }

        _shuffledIndex = (_shuffledIndex == _shuffleList.Count - 1) ? 0 : _shuffledIndex + 1;

        MediaFile shuffledTrack = _shuffleList[_shuffledIndex];
        int actualIndex = FindTrackIndexById(currentTracks, shuffledTrack.Id);

        if (actualIndex < 0)
        {
            InitializeShuffle(currentTracks);
            actualIndex = 0;
        }

        return actualIndex;
    }

    private int GetPreviousShuffledIndex(IList<MediaFile> currentTracks)
    {
        if (!_shuffleList.Any())
        {
            InitializeShuffle(currentTracks);
        }

        if (!_shuffleList.Any())
        {
            _logger.LogError("Failed to initialize shuffle list");
            return currentTracks.Count - 1;
        }

        _shuffledIndex = (_shuffledIndex == 0) ? _shuffleList.Count - 1 : _shuffledIndex - 1;

        MediaFile shuffledTrack = _shuffleList[_shuffledIndex];
        int actualIndex = FindTrackIndexById(currentTracks, shuffledTrack.Id);

        if (actualIndex < 0)
        {
            InitializeShuffle(currentTracks);
            actualIndex = currentTracks.Count - 1;
        }

        return actualIndex;
    }
}
