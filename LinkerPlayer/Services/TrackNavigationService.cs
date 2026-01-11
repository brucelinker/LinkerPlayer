using System;
using System.Linq;
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
            currentIndex = 0;
        }

        if (shuffleMode)
        {
            if (!_shuffleList.Any())
            {
                InitializeShuffle(currentTracks, currentTracks[currentIndex].Id);
            }
            else
            {
                SetShufflePosition(currentTracks[currentIndex].Id);
            }

            int nextIndex = GetNextShuffledIndex(currentTracks, currentTracks[currentIndex].Id);
            return nextIndex;
        }

        int sequentialNextIndex = currentIndex == currentTracks.Count - 1 ? 0 : currentIndex + 1;
        return sequentialNextIndex;
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
            currentIndex = currentTracks.Count - 1;
        }

        if (shuffleMode)
        {
            if (!_shuffleList.Any())
            {
                InitializeShuffle(currentTracks, currentTracks[currentIndex].Id);
            }
            else
            {
                SetShufflePosition(currentTracks[currentIndex].Id);
            }

            int previousIndex = GetPreviousShuffledIndex(currentTracks, currentTracks[currentIndex].Id);
            return previousIndex;
        }

        int sequentialPreviousIndex = currentIndex == 0 ? currentTracks.Count - 1 : currentIndex - 1;
        return sequentialPreviousIndex;
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

        List<MediaFile> tempList = new List<MediaFile>(trackList);
        Random random = new Random();

        for (int i = tempList.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (tempList[i], tempList[j]) = (tempList[j], tempList[i]);
        }

        _shuffleList.AddRange(tempList);

        // Set shuffle position based on current track
        if (!string.IsNullOrWhiteSpace(currentTrackId))
        {
            if (!SetShufflePosition(currentTrackId))
            {
                _shuffledIndex = 0;
            }
        }
        else
        {
            _shuffledIndex = 0;
        }
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

        int index = _shuffleList.FindIndex(track => string.Equals(track.Id, trackId, StringComparison.Ordinal));
        if (index >= 0)
        {
            _shuffledIndex = index;
            return true;
        }

        _logger.LogWarning("Track {TrackId} not found in shuffle list", trackId);
        return false;
    }

    private int GetNextShuffledIndex(IList<MediaFile> currentTracks, string currentTrackId)
    {
        if (!_shuffleList.Any())
        {
            InitializeShuffle(currentTracks, currentTrackId);
        }

        if (!_shuffleList.Any())
        {
            _logger.LogError("Failed to initialize shuffle list");
            return 0;
        }

        // Align to current track before stepping.
        SetShufflePosition(currentTrackId);

        _shuffledIndex = (_shuffledIndex == _shuffleList.Count - 1) ? 0 : _shuffledIndex + 1;

        MediaFile shuffledTrack = _shuffleList[_shuffledIndex];
        int actualIndex = FindTrackIndexById(currentTracks, shuffledTrack.Id);
        if (actualIndex < 0)
        {
            // Playlist changed while shuffle is enabled. Do not reshuffle; fall back to sequential navigation.
            _logger.LogWarning("Shuffle track missing from current list; falling back to sequential next (TrackId={TrackId})", shuffledTrack.Id);
            return currentTracks.Count == 0 ? -1 : ((FindTrackIndexById(currentTracks, currentTrackId) + 1) % currentTracks.Count);
        }

        return actualIndex;
    }

    private int GetPreviousShuffledIndex(IList<MediaFile> currentTracks, string currentTrackId)
    {
        if (!_shuffleList.Any())
        {
            InitializeShuffle(currentTracks, currentTrackId);
        }

        if (!_shuffleList.Any())
        {
            _logger.LogError("Failed to initialize shuffle list");
            return currentTracks.Count - 1;
        }

        SetShufflePosition(currentTrackId);

        _shuffledIndex = (_shuffledIndex == 0) ? _shuffleList.Count - 1 : _shuffledIndex - 1;

        MediaFile shuffledTrack = _shuffleList[_shuffledIndex];
        int actualIndex = FindTrackIndexById(currentTracks, shuffledTrack.Id);
        if (actualIndex < 0)
        {
            _logger.LogWarning("Shuffle track missing from current list; falling back to sequential previous (TrackId={TrackId})", shuffledTrack.Id);
            int currentActualIndex = FindTrackIndexById(currentTracks, currentTrackId);
            if (currentActualIndex < 0)
            {
                return currentTracks.Count - 1;
            }
            return currentActualIndex == 0 ? currentTracks.Count - 1 : currentActualIndex - 1;
        }

        return actualIndex;
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
}
