﻿using LinkerPlayer.Database;
using LinkerPlayer.Models;
using ManagedBass;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LinkerPlayer.Core;

public class MusicLibrary
{
    private readonly ILogger<MusicLibrary> _logger;

    private readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LinkerPlayer", "music_library.db");

    private readonly IDbContextFactory<MusicLibraryDbContext> DbContextFactory;
    public ObservableCollection<MediaFile> MainLibrary { get; } = new();
    public ObservableCollection<Playlist> Playlists { get; } = new();
    private readonly string[] SupportedAudioExtensions = [".mp3", ".flac", ".wav"];
    private readonly Dictionary<string, (DateTime LastModified, MediaFile Metadata)> MetadataCache =
        new(StringComparer.OrdinalIgnoreCase);

    public MusicLibrary(ILogger<MusicLibrary> logger)
    {
        _logger = logger;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            DbContextOptions<MusicLibraryDbContext> options = new DbContextOptionsBuilder<MusicLibraryDbContext>()
                .UseSqlite($"Data Source={DbPath};Pooling=True;")
                .Options;
            DbContextFactory = new PooledDbContextFactory<MusicLibraryDbContext>(options);

            using (MusicLibraryDbContext context = DbContextFactory.CreateDbContext())
            {
                try
                {
                    _logger.LogInformation("Ensuring database is created");
                    context.Database.EnsureCreated();
                }
                catch (SqliteException ex)
                {
                    _logger.LogInformation($"Database not found. Creating a new one: {ex.Message}");
                }

                _logger.LogInformation("Setting WAL mode");
                context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                _logger.LogInformation("Creating index idx_tracks_path");
                context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_tracks_path ON Tracks(Path);");
                _logger.LogInformation("Creating index idx_playlisttracks_playlistid");
                context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_playlisttracks_playlistid ON PlaylistTracks(PlaylistId);");
                _logger.LogInformation("Creating MetadataCache table");
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS MetadataCache (Path TEXT PRIMARY KEY, LastModified INTEGER, Metadata TEXT NOT NULL);");
            }
            LoadFromDatabaseAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize MusicLibrary");
            throw;
        }
    }

    public async Task SaveMetadataCacheAsync()
    {
        try
        {
            await using MusicLibraryDbContext context = await DbContextFactory.CreateDbContextAsync();
            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
            foreach (KeyValuePair<string, (DateTime LastModified, MediaFile Metadata)> entry in MetadataCache)
            {
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT OR REPLACE INTO MetadataCache (Path, LastModified, Metadata) VALUES (@p0, @p1, @p2)",
                    entry.Key, entry.Value.LastModified.Ticks, JsonSerializer.Serialize(entry.Value.Metadata));
            }
            await transaction.CommitAsync();
            _logger.LogInformation("Saved MetadataCache with {Count} entries", MetadataCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save metadata cache");
        }
    }

    public async Task LoadMetadataCacheAsync()
    {
        try
        {
            await using MusicLibraryDbContext context = await DbContextFactory.CreateDbContextAsync();

            await context.Database.EnsureCreatedAsync(); // Creates database if it does not exist
            await context.MetadataCache.ToListAsync();

            List<MetadataCache> entries = await context.MetadataCache.ToListAsync(); // Query MetadataCache entity
            MetadataCache.Clear();
            foreach (MetadataCache entry in entries)
            {
                try
                {
                    MetadataCache[entry.Path] = (new DateTime(entry.LastModified),
                        JsonSerializer.Deserialize<MediaFile>(entry.Metadata)!);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to deserialize metadata for {entry.Path}");
                }
            }
            _logger.LogInformation("Loaded MetadataCache with {Count} entries", MetadataCache.Count);
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "SQLite error during metadata cache load");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load metadata cache");
        }
    }

    public async Task LoadFromDatabaseAsync()
    {
        await using MusicLibraryDbContext context = await DbContextFactory.CreateDbContextAsync();
        try
        {
            Playlists.Clear();
            MainLibrary.Clear();

            List<MediaFile> tracks = await context.Tracks.AsNoTracking().ToListAsync();
            foreach (MediaFile track in tracks)
            {
                MainLibrary.Add(track);
            }

            List<Playlist> playlists = await context.Playlists
                .Include(p => p.PlaylistTracks)
                .ThenInclude(pt => pt.Track)
                .Include(p => p.SelectedTrackNavigation)
                .AsNoTracking()
                .ToListAsync();
            foreach (Playlist playlist in playlists)
            {
                List<string> validTrackIds = playlist.PlaylistTracks
                    .Where(pt => pt.TrackId != null && MainLibrary.Any(t => t.Id == pt.TrackId))
                    .OrderBy(pt => pt.Position)
                    .Select(pt => pt.TrackId!)
                    .ToList();
                playlist.TrackIds = new ObservableCollection<string>(validTrackIds);
                _logger.LogInformation(
                    $"Loaded playlist {playlist.Name}");

                if (playlist.SelectedTrack != null && MainLibrary.All(t => t.Id != playlist.SelectedTrack))
                {
                    _logger.LogWarning(
                        $"Invalid SelectedTrack {playlist.SelectedTrack} in playlist {playlist.Name}, clearing");
                    playlist.SelectedTrack = null;
                }
                else if (playlist.SelectedTrack == null && playlist.TrackIds.Any())
                {
                    playlist.SelectedTrack = playlist.TrackIds.First();
                    _logger.LogInformation(
                        $"Set SelectedTrack to {playlist.SelectedTrack} for playlist {playlist.Name} during load");
                }

                Playlists.Add(playlist);
            }

            ClearPlayState();

            if (!Playlists.Any())
            {
                Playlist newPlaylist = new()
                {
                    Name = "New Playlist",
                    TrackIds = new ObservableCollection<string>(),
                    SelectedTrack = null
                };
                Playlists.Add(newPlaylist);
                await SaveToDatabaseAsync();
            }

            _logger.LogInformation("Data loaded from database");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load data from database");
            throw;
        }
    }

    public async Task SaveTracksBatchAsync(IEnumerable<MediaFile> tracks)
    {
        await using MusicLibraryDbContext context = await DbContextFactory.CreateDbContextAsync();
        try
        {
            context.ChangeTracker.AutoDetectChangesEnabled = false;

            IEnumerable<MediaFile> mediaFiles = tracks.ToList();
            foreach (MediaFile track in mediaFiles)
            {
                //if (track.Composers == null)
                //{
                //    _logger.LogWarning($"Skipping invalid track {track.Path}: Composers is null");
                //    continue;
                //}

                MediaFile? existingTrack = await context.Tracks.FirstOrDefaultAsync(t =>
                    t.Path == track.Path && t.Album == track.Album && t.Duration == track.Duration);
                if (existingTrack == null)
                {
                    context.Tracks.Add(track);
                    MainLibrary.Add(track);
                }
                else
                {
                    existingTrack.Title = track.Title;
                    existingTrack.Artist = track.Artist;
                    existingTrack.FileName = track.FileName;
                    existingTrack.Track = track.Track;
                    existingTrack.TrackCount = track.TrackCount;
                    existingTrack.Performers = track.Performers;
                    existingTrack.Genres = track.Genres;
                    existingTrack.Composers = track.Composers;
                    existingTrack.Bitrate = track.Bitrate;
                    existingTrack.SampleRate = track.SampleRate;
                    existingTrack.Channels = track.Channels;
                    existingTrack.DiscCount = track.DiscCount;
                    existingTrack.Disc = track.Disc;
                    existingTrack.Year = track.Year;
                    existingTrack.Copyright = track.Copyright;
                    existingTrack.Comment = track.Comment;
                    existingTrack.State = track.State;
                    track.Id = existingTrack.Id;
                }
            }

            await context.SaveChangesAsync();
            _logger.LogInformation($"Batch saved {mediaFiles.Count()} tracks to database");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch save tracks to database");
            throw;
        }
    }
    public async Task SaveToDatabaseAsync()
    {
        await using MusicLibraryDbContext context = await DbContextFactory.CreateDbContextAsync();
        try
        {
            _logger.LogInformation($"Using database file: {Path.GetFullPath(DbPath)}");
            context.ChangeTracker.AutoDetectChangesEnabled = false;

            ClearPlayState();

            foreach (MediaFile track in MainLibrary)
            {
                //if (track.Composers == null)
                //{
                //    _logger.LogWarning($"Skipping invalid track {track.Path}: Composers is null");
                //    continue;
                //}

                MediaFile? existingTrack = await context.Tracks.FirstOrDefaultAsync(t =>
                    t.Path == track.Path && t.Album == track.Album && t.Duration == track.Duration);
                if (existingTrack != null)
                {
                    track.Id = existingTrack.Id;
                }
            }

            foreach (Playlist playlist in Playlists)
            {
                _logger.LogInformation($"Saving playlist {playlist.Name}"); // with TrackIds: {string.Join(", ", playlist.TrackIds)}, SelectedTrack: {playlist.SelectedTrack}");
                Playlist? existingPlaylist = await context.Playlists
                    .Include(p => p.PlaylistTracks)
                    .FirstOrDefaultAsync(p => p.Id == playlist.Id || p.Name == playlist.Name);
                int playlistId;
                if (existingPlaylist == null)
                {
                    Playlist newPlaylist = new()
                    {
                        Name = playlist.Name,
                        SelectedTrack = playlist.SelectedTrack != null &&
                                        MainLibrary.Any(t => t.Id == playlist.SelectedTrack)
                            ? playlist.SelectedTrack
                            : null
                    };
                    context.Playlists.Add(newPlaylist);
                    await context.SaveChangesAsync();
                    playlistId = newPlaylist.Id;
                    playlist.Id = playlistId;
                    _logger.LogInformation(
                        $"Created new playlist {newPlaylist.Name} with Id {playlistId}, SelectedTrack {newPlaylist.SelectedTrack}");
                }
                else
                {
                    playlistId = existingPlaylist.Id;
                    existingPlaylist.Name = playlist.Name;
                    existingPlaylist.SelectedTrack = playlist.SelectedTrack != null &&
                                                     MainLibrary.Any(t => t.Id == playlist.SelectedTrack)
                        ? playlist.SelectedTrack
                        : null;
                    context.Entry(existingPlaylist).Property(p => p.SelectedTrack).IsModified = true;
                    context.Entry(existingPlaylist).State = EntityState.Modified;
                    context.PlaylistTracks.RemoveRange(existingPlaylist.PlaylistTracks);
                    playlist.Id = playlistId;
                    _logger.LogInformation($"Updated playlist {existingPlaylist.Name}");
                }

                List<string> validTrackIds = playlist.TrackIds.Where(id => context.Tracks.Any(t => t.Id == id)).ToList();
                if (validTrackIds.Any())
                {
                    for (int i = 0; i < validTrackIds.Count; i++)
                    {
                        context.PlaylistTracks.Add(new PlaylistTrack
                        {
                            PlaylistId = playlistId,
                            TrackId = validTrackIds[i],
                            Position = i
                        });
                    }
                    _logger.LogInformation(
                        $"Saved {validTrackIds.Count} PlaylistTrack entries for playlist {playlist.Name}");
                }
                else
                {
                    _logger.LogInformation($"Playlist '{playlist.Name}' is empty, no PlaylistTracks to save");
                }
            }

            await context.SaveChangesAsync();
            context.ChangeTracker.AutoDetectChangesEnabled = true;
            _logger.LogInformation("Data saved to database");

            List<Playlist> savedPlaylists = await context.Playlists.ToListAsync();
            foreach (Playlist p in savedPlaylists)
            {
                await context.PlaylistTracks
                    .Where(pt => pt.PlaylistId == p.Id)
                    .OrderBy(pt => pt.Position)
                    .Select(pt => pt.TrackId)
                    .ToListAsync();
                _logger.LogInformation(
                    $"Database state for playlist {p.Name}"); // (Id {p.Id}): SelectedTrack={p.SelectedTrack}, TrackIds={string.Join(", ", trackIds)}");
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
        {
            _logger.LogError(ex, "Database is locked while saving to database");
            throw new InvalidOperationException("Database is locked. Please try again later.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save data to database");
            throw;
        }
    }

    public async Task CleanOrphanedTracksAsync()
    {
        await using MusicLibraryDbContext context = await DbContextFactory.CreateDbContextAsync();
        try
        {
            List<string> referencedTrackIds =
                await context.PlaylistTracks.Select(pt => pt.TrackId!).Distinct().ToListAsync();
            List<MediaFile> orphanedTracks = await context.Tracks
                .Where(t => !referencedTrackIds.Contains(t.Id))
                .ToListAsync();
            if (orphanedTracks.Any())
            {
                context.Tracks.RemoveRange(orphanedTracks);
                await context.SaveChangesAsync();
                _logger.LogInformation($"Removed {orphanedTracks.Count} orphaned tracks from database");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean orphaned tracks from database");
            throw;
        }
    }

    public MediaFile? IsTrackInLibrary(MediaFile mediaFile)
    {
        return MainLibrary.FirstOrDefault(s =>
            s.Path.Equals(mediaFile.Path, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MediaFile?> AddTrackToLibraryAsync(MediaFile mediaFile, bool saveImmediately = true,
        bool skipMetadata = false)
    {
        _logger.LogDebug($"Entering AddTrackToLibraryAsync for {mediaFile.Path}");
        try
        {
            MediaFile? existingTrack = IsTrackInLibrary(mediaFile);
            if (existingTrack != null)
            {
                _logger.LogDebug($"Track already in library: {mediaFile.Path}");
                return existingTrack;
            }

            if (!string.IsNullOrEmpty(mediaFile.Path) &&
                SupportedAudioExtensions.Any(s =>
                    s.Equals(Path.GetExtension(mediaFile.Path), StringComparison.OrdinalIgnoreCase)))
            {
                if (!skipMetadata)
                {
                    FileInfo fileInfo = new(mediaFile.Path);
                    if (MetadataCache.TryGetValue(mediaFile.Path, out (DateTime LastModified, MediaFile Metadata) cached) &&
                        cached.LastModified == fileInfo.LastWriteTime)
                    {
                        mediaFile.Duration = cached.Metadata.Duration;
                        mediaFile.Title = cached.Metadata.Title;
                        mediaFile.Album = cached.Metadata.Album;
                        mediaFile.Artist = cached.Metadata.Artist;
                        mediaFile.Bitrate = cached.Metadata.Bitrate;
                        mediaFile.SampleRate = cached.Metadata.SampleRate;
                        mediaFile.Channels = cached.Metadata.Channels;
                        mediaFile.Performers = cached.Metadata.Performers;
                        mediaFile.Composers = cached.Metadata.Composers;
                        mediaFile.Genres = cached.Metadata.Genres;
                        mediaFile.Track = cached.Metadata.Track;
                        mediaFile.TrackCount = cached.Metadata.TrackCount;
                        mediaFile.Disc = cached.Metadata.Disc;
                        mediaFile.DiscCount = cached.Metadata.DiscCount;
                        mediaFile.Year = cached.Metadata.Year;
                        mediaFile.Copyright = cached.Metadata.Copyright;
                        mediaFile.Comment = cached.Metadata.Comment;
                        _logger.LogDebug($"Metadata cache hit for {mediaFile.Path}");
                    }
                    else
                    {
                        try
                        {
                            mediaFile.UpdateFromFileMetadata(false, minimal: false);
                            _logger.LogDebug($"Extracted metadata for {mediaFile.Path}: Artist={mediaFile.Artist}, Title={mediaFile.Title}");
                            MetadataCache[mediaFile.Path] = (fileInfo.LastWriteTime, mediaFile.Clone());
                            _logger.LogDebug($"Added {mediaFile.Path} to MetadataCache. Cache size: {MetadataCache.Count}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to extract metadata for {mediaFile.Path}");
                            return null;
                        }
                    }
                }
                MediaFile clonedTrack = mediaFile.Clone();
                MainLibrary.Add(clonedTrack);
                if (saveImmediately)
                {
                    await SaveTracksBatchAsync([clonedTrack]);
                }
                return clonedTrack;
            }

            _logger.LogWarning($"Unsupported file format: {mediaFile.Path}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to add track to library: {mediaFile.Path}");
            return null;
        }
    }

    public async Task RemoveTrackFromPlaylistAsync(string playlistName, string trackId)
    {
        Playlist? playlist = Playlists.FirstOrDefault(p => p.Name == playlistName);
        if (playlist != null && playlist.TrackIds.Contains(trackId))
        {
            playlist.TrackIds.Remove(trackId);
            if (playlist.SelectedTrack == trackId)
            {
                playlist.SelectedTrack = null;
            }
            await SaveToDatabaseAsync();
            await CleanOrphanedTracksAsync();
            _logger.LogInformation($"Track {trackId} removed from playlist {playlistName}");
        }
    }

    public async Task<Playlist> AddNewPlaylistAsync(string playlistName)
    {
        Playlist? existingPlaylist = Playlists.FirstOrDefault(p => p.Name == playlistName);
        if (existingPlaylist != null)
        {
            _logger.LogInformation($"Returning existing playlist {playlistName} with Id {existingPlaylist.Id}");
            return existingPlaylist;
        }

        Playlist playlist = new() 
        {
            Name = playlistName,
            TrackIds = new ObservableCollection<string>(),
            SelectedTrack = null
        };
        await AddPlaylistAsync(playlist);
        _logger.LogInformation($"Created new playlist {playlistName} with Id {playlist.Id}");
        return playlist;
    }

    public async Task<bool> AddPlaylistAsync(Playlist newPlaylist)
    {
        if (Playlists.Any(p => p.Name == newPlaylist.Name))
        {
            return false;
        }

        newPlaylist.TrackIds =
            new ObservableCollection<string>(newPlaylist.TrackIds.Where(id => MainLibrary.Any(t => t.Id == id)));
        if (newPlaylist.SelectedTrack != null && MainLibrary.All(t => t.Id != newPlaylist.SelectedTrack))
        {
            newPlaylist.SelectedTrack = null;
            _logger.LogInformation($"Cleared invalid SelectedTrack for playlist {newPlaylist.Name}");
        }

        Playlists.Add(newPlaylist);
        await SaveToDatabaseAsync();
        _logger.LogInformation($"New playlist '{newPlaylist.Name}' added");
        return true;
    }

    public async Task RemovePlaylistAsync(string playlistName)
    {
        await using MusicLibraryDbContext context = await DbContextFactory.CreateDbContextAsync();
        try
        {
            Playlist? playlist = Playlists.FirstOrDefault(p => p.Name == playlistName);
            if (playlist != null)
            {
                Playlists.Remove(playlist);
                Playlist? dbPlaylist = await context.Playlists
                    .Include(p => p.PlaylistTracks)
                    .FirstOrDefaultAsync(p => p.Name == playlistName);
                if (dbPlaylist != null)
                {
                    context.PlaylistTracks.RemoveRange(dbPlaylist.PlaylistTracks);
                    context.Playlists.Remove(dbPlaylist);
                    await context.SaveChangesAsync();
                    _logger.LogInformation(
                        $"Removed playlist '{playlistName}' and {dbPlaylist.PlaylistTracks.Count} tracks from database");
                }
                await CleanOrphanedTracksAsync();
                _logger.LogInformation($"Playlist '{playlistName}' removed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to remove playlist '{playlistName}'");
            throw;
        }
    }

    public async Task AddTracksToPlaylistAsync(IList<string> trackIds, string playlistName,
        bool saveImmediately = true)
    {
        Playlist? playlist = Playlists.FirstOrDefault(p => p.Name == playlistName);
        if (playlist == null)
        {
            _logger.LogWarning($"Playlist {playlistName} not found");
            return;
        }

        List<string> validTrackIds = trackIds
            .Where(id => !playlist.TrackIds.Contains(id) && MainLibrary.Any(t => t.Id == id)).ToList();
        if (!validTrackIds.Any())
        {
            _logger.LogInformation($"No new valid tracks to add to playlist {playlistName}");
            return;
        }

        foreach (string trackId in validTrackIds)
        {
            playlist.TrackIds.Add(trackId);
        }

        if (playlist.SelectedTrack == null && playlist.TrackIds.Any())
        {
            playlist.SelectedTrack = playlist.TrackIds.First();
            _logger.LogInformation($"Set SelectedTrack to {playlist.SelectedTrack} for playlist {playlistName}");
        }

        _logger.LogInformation($"Added {validTrackIds.Count} tracks to playlist {playlistName}");
        if (saveImmediately)
        {
            await SaveToDatabaseAsync();
        }
    }

    public async Task AddTrackToPlaylistAsync(string trackId, string playlistName,
        bool saveImmediately = true, int position = -1)
    {
        Playlist? playlist = Playlists.FirstOrDefault(p => p.Name == playlistName);
        if (playlist != null && !playlist.TrackIds.Contains(trackId) && MainLibrary.Any(t => t.Id == trackId))
        {
            if (position == -1 || position >= playlist.TrackIds.Count)
            {
                playlist.TrackIds.Add(trackId);
            }
            else if (position >= 0)
            {
                playlist.TrackIds.Insert(position, trackId);
            }
            if (playlist.SelectedTrack == null && playlist.TrackIds.Any())
            {
                playlist.SelectedTrack = playlist.TrackIds.First();
                _logger.LogInformation($"Set SelectedTrack to {playlist.SelectedTrack} for playlist {playlistName}");
            }
            _logger.LogInformation($"Track {trackId} added to playlist {playlistName} at position {position}");
            if (saveImmediately)
            {
                await SaveToDatabaseAsync();
            }
        }
        else
        {
            _logger.LogWarning($"Failed to add track {trackId} to playlist {playlistName}: Playlist or track not found");
        }
    }

    public List<Playlist> GetPlaylists()
    {
        return Playlists.ToList();
    }

    public List<MediaFile> GetTracksFromPlaylist(string? playlistName)
    {
        //_logger.LogInformation("MusicLibrary - GetTracksFromPlaylist");
        Playlist? playlist = Playlists.FirstOrDefault(p => p.Name == playlistName);
        if (playlist == null)
        {
            return new List<MediaFile>();
        }

        return playlist.TrackIds
            .Select(trackId => MainLibrary.FirstOrDefault(p => p.Id == trackId))
            .Where(t => t != null)
            .Select(t => t!)
            .ToList();
    }

    public void ClearPlayState()
    {
        foreach (MediaFile file in MainLibrary)
        {
            file.State = PlaybackState.Stopped;
        }
    }
}