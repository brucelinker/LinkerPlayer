using LinkerPlayer.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LinkerPlayer.Services;

public interface IPlaylistManagerService
{
    /// <summary>
    /// Creates a new playlist and returns the corresponding PlaylistTab
    /// </summary>
    /// <param name="name">Name of the playlist</param>
    /// <returns>The created PlaylistTab</returns>
    Task<PlaylistTab> CreatePlaylistTabAsync(string name);

    /// <summary>
    /// Renames an existing playlist
    /// </summary>
    /// <param name="oldName">Current playlist name</param>
    /// <param name="newName">New playlist name</param>
    /// <returns>True if rename was successful</returns>
    Task<bool> RenamePlaylistAsync(string oldName, string newName);

    /// <summary>
    /// Removes a playlist completely
    /// </summary>
    /// <param name="name">Name of the playlist to remove</param>
    /// <returns>True if removal was successful</returns>
    Task<bool> RemovePlaylistAsync(string name);

    /// <summary>
    /// Adds tracks to a specific playlist
    /// </summary>
    /// <param name="playlistName">Name of the target playlist</param>
    /// <param name="tracks">Tracks to add</param>
    /// <returns>True if all tracks were added successfully</returns>
    Task<bool> AddTracksToPlaylistAsync(string playlistName, IEnumerable<MediaFile> tracks);

    /// <summary>
    /// Removes a track from a playlist
    /// </summary>
    /// <param name="playlistName">Name of the playlist</param>
    /// <param name="trackId">ID of the track to remove</param>
    /// <returns>True if removal was successful</returns>
    Task<bool> RemoveTrackFromPlaylistAsync(string playlistName, string trackId);

    /// <summary>
    /// Gets a unique playlist name by appending a number if necessary
    /// </summary>
    /// <param name="baseName">Base name for the playlist</param>
    /// <returns>A unique playlist name</returns>
    string GetUniquePlaylistName(string baseName);

    /// <summary>
    /// Loads tracks for a specific playlist
    /// </summary>
    /// <param name="playlistName">Name of the playlist</param>
    /// <returns>Collection of tracks in the playlist</returns>
    IEnumerable<MediaFile> LoadPlaylistTracks(string playlistName);
}