using LinkerPlayer.Models;
using System.Collections.Generic;

namespace LinkerPlayer.Services;

public interface ITrackNavigationService
{
    /// <summary>
    /// Gets the next track in the current playlist
    /// </summary>
    /// <param name="currentTracks">Current playlist tracks</param>
    /// <param name="currentIndex">Current track index</param>
    /// <param name="shuffleMode">Whether shuffle mode is enabled</param>
    /// <returns>Index of the next track</returns>
    int GetNextTrackIndex(IList<MediaFile> currentTracks, int currentIndex, bool shuffleMode);

    /// <summary>
    /// Gets the previous track in the current playlist
    /// </summary>
    /// <param name="currentTracks">Current playlist tracks</param>
    /// <param name="currentIndex">Current track index</param>
    /// <param name="shuffleMode">Whether shuffle mode is enabled</param>
    /// <returns>Index of the previous track</returns>
    int GetPreviousTrackIndex(IList<MediaFile> currentTracks, int currentIndex, bool shuffleMode);

    /// <summary>
    /// Initializes or updates the shuffle list for the current tracks
    /// </summary>
    /// <param name="tracks">Tracks to create shuffle list from</param>
    /// <param name="currentTrackId">ID of currently active track (to maintain position)</param>
    void InitializeShuffle(IEnumerable<MediaFile> tracks, string? currentTrackId = null);

    /// <summary>
    /// Clears the current shuffle list
    /// </summary>
    void ClearShuffle();

    /// <summary>
    /// Gets the current shuffle position
    /// </summary>
    /// <returns>Current position in shuffle list, or -1 if not shuffling</returns>
    int GetShufflePosition();

    /// <summary>
    /// Sets the shuffle position based on a track ID
    /// </summary>
    /// <param name="trackId">ID of the track to position to</param>
    /// <returns>True if track was found in shuffle list</returns>
    bool SetShufflePosition(string trackId);
}