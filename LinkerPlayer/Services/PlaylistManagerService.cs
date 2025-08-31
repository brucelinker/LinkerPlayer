using LinkerPlayer.Core;
using LinkerPlayer.Database;
using LinkerPlayer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

public class PlaylistManagerService : IPlaylistManagerService
{
    private readonly IMusicLibrary _musicLibrary;
    private readonly ILogger<PlaylistManagerService> _logger;

    public PlaylistManagerService(IMusicLibrary musicLibrary, ILogger<PlaylistManagerService> logger)
    {
        _musicLibrary = musicLibrary ?? throw new ArgumentNullException(nameof(musicLibrary));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PlaylistTab> CreatePlaylistTabAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Playlist name cannot be null or empty", nameof(name));
        }

        string uniqueName = GetUniquePlaylistName(name);
        
        try
        {
            Playlist playlist = await _musicLibrary.AddNewPlaylistAsync(uniqueName);
            IEnumerable<MediaFile> tracks = LoadPlaylistTracks(uniqueName);
            
            PlaylistTab tab = new PlaylistTab
            {
                Name = uniqueName,
                Tracks = new ObservableCollection<MediaFile>(tracks)
            };

            _logger.LogInformation("Created new playlist tab: {PlaylistName}", uniqueName);
            return tab;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create playlist tab: {PlaylistName}", uniqueName);
            throw;
        }
    }

    public async Task<bool> RenamePlaylistAsync(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            _logger.LogWarning("RenamePlaylistAsync called with invalid names: '{OldName}' -> '{NewName}'", oldName, newName);
            return false;
        }

        if (oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Playlist rename skipped - names are identical: {Name}", oldName);
            return true;
        }

        try
        {
            // Check if new name already exists
            if (_musicLibrary.Playlists.Any(p => p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase) && 
                                                 !p.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Cannot rename playlist - name already exists: {NewName}", newName);
                return false;
            }

            // Update database directly for better control
            await using MusicLibraryDbContext context = new MusicLibraryDbContext(new DbContextOptionsBuilder<MusicLibraryDbContext>()
                .UseSqlite($"Data Source={GetDatabasePath()}")
                .Options);

            Playlist? dbPlaylist = await context.Playlists.FirstOrDefaultAsync(p => p.Name == oldName);
            if (dbPlaylist == null)
            {
                _logger.LogWarning("Playlist not found in database: {OldName}", oldName);
                return false;
            }

            dbPlaylist.Name = newName;
            await context.SaveChangesAsync();

            // Update in-memory playlists
            Playlist? playlist = _musicLibrary.Playlists.FirstOrDefault(p => p.Name == oldName);
            if (playlist != null)
            {
                playlist.Name = newName;
            }
            else
            {
                // Reload from database if not found in memory
                await _musicLibrary.LoadFromDatabaseAsync();
            }

            await _musicLibrary.SaveToDatabaseAsync();
            _logger.LogInformation("Successfully renamed playlist from '{OldName}' to '{NewName}'", oldName, newName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename playlist from '{OldName}' to '{NewName}'", oldName, newName);
            return false;
        }
    }

    public async Task<bool> RemovePlaylistAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogWarning("RemovePlaylistAsync called with invalid name: {Name}", name);
            return false;
        }

        const int maxRetries = 3;
        for (int retryCount = 0; retryCount < maxRetries; retryCount++)
        {
            try
            {
                await _musicLibrary.RemovePlaylistAsync(name);
                _logger.LogInformation("Successfully removed playlist: {Name}", name);
                return true;
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 5) // Database locked
            {
                if (retryCount == maxRetries - 1)
                {
                    _logger.LogError(ex, "Failed to remove playlist '{Name}' after {MaxRetries} retries - database locked", name, maxRetries);
                    return false;
                }
                
                _logger.LogWarning("Database locked during playlist removal, retrying ({RetryCount}/{MaxRetries})", retryCount + 1, maxRetries);
                await Task.Delay(1000 * (retryCount + 1)); // Exponential backoff
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove playlist: {Name}", name);
                return false;
            }
        }

        return false;
    }

    public async Task<bool> AddTracksToPlaylistAsync(string playlistName, IEnumerable<MediaFile> tracks)
    {
        if (string.IsNullOrWhiteSpace(playlistName))
        {
            _logger.LogWarning("AddTracksToPlaylistAsync called with invalid playlist name");
            return false;
        }

        List<MediaFile>? trackList = tracks?.ToList();
        if (trackList == null || !trackList.Any())
        {
            _logger.LogWarning("AddTracksToPlaylistAsync called with no tracks");
            return false;
        }

        try
        {
            // Save tracks to library first
            await _musicLibrary.SaveTracksBatchAsync(trackList);

            // Add track IDs to playlist
            List<string> trackIds = trackList.Select(t => t.Id).ToList();
            await _musicLibrary.AddTracksToPlaylistAsync(trackIds, playlistName, saveImmediately: false);

            await _musicLibrary.SaveToDatabaseAsync();
            
            _logger.LogInformation("Successfully added {Count} tracks to playlist: {PlaylistName}", trackList.Count, playlistName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add {Count} tracks to playlist: {PlaylistName}", trackList?.Count ?? 0, playlistName);
            return false;
        }
    }

    public async Task<bool> RemoveTrackFromPlaylistAsync(string playlistName, string trackId)
    {
        if (string.IsNullOrWhiteSpace(playlistName) || string.IsNullOrWhiteSpace(trackId))
        {
            _logger.LogWarning("RemoveTrackFromPlaylistAsync called with invalid parameters");
            return false;
        }

        try
        {
            await _musicLibrary.RemoveTrackFromPlaylistAsync(playlistName, trackId);
            _logger.LogInformation("Successfully removed track {TrackId} from playlist: {PlaylistName}", trackId, playlistName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove track {TrackId} from playlist: {PlaylistName}", trackId, playlistName);
            return false;
        }
    }

    public string GetUniquePlaylistName(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "New Playlist";
        }

        string uniqueName = baseName;
        int index = 1;

        while (_musicLibrary.Playlists.Any(p => p.Name.Equals(uniqueName, StringComparison.OrdinalIgnoreCase)))
        {
            uniqueName = $"{baseName} ({index++})";
        }

        return uniqueName;
    }

    public IEnumerable<MediaFile> LoadPlaylistTracks(string playlistName)
    {
        if (string.IsNullOrWhiteSpace(playlistName))
        {
            _logger.LogWarning("LoadPlaylistTracks called with invalid playlist name");
            return Enumerable.Empty<MediaFile>();
        }

        try
        {
            List<MediaFile> tracks = _musicLibrary.GetTracksFromPlaylist(playlistName);
            
            // Update metadata for all tracks
            foreach (MediaFile track in tracks)
            {
                track.UpdateFromFileMetadata();
            }

            //_logger.LogDebug("Loaded {Count} tracks for playlist: {PlaylistName}", tracks.Count, playlistName);
            return tracks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tracks for playlist: {PlaylistName}", playlistName);
            return Enumerable.Empty<MediaFile>();
        }
    }

    private static string GetDatabasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LinkerPlayer",
            "music_library.db");
    }
}