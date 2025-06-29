using LinkerPlayer.Database;
using LinkerPlayer.Models;
using ManagedBass;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Serilog;
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
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LinkerPlayer", "music_library.db");

    private static readonly IDbContextFactory<MusicLibraryDbContext> _dbContextFactory;
    public static ObservableCollection<MediaFile> MainLibrary { get; } = new();
    public static ObservableCollection<Playlist> Playlists { get; } = new();
    private static readonly string[] SupportedAudioExtensions = [".mp3", ".flac", ".wav"];
    private static readonly Dictionary<string, (DateTime LastModified, MediaFile Metadata)> MetadataCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool _isInitialized;

    static MusicLibrary()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            DbContextOptions<MusicLibraryDbContext> options = new DbContextOptionsBuilder<MusicLibraryDbContext>()
                .UseSqlite($"Data Source={DbPath};Pooling=True;")
                .Options;
            _dbContextFactory = new PooledDbContextFactory<MusicLibraryDbContext>(options);

            using (var context = _dbContextFactory.CreateDbContext())
            {
                try
                {
                    Log.Information("Ensuring database is created");
                    context.Database.EnsureCreated();
                }
                catch (SqliteException ex)
                {
                    Log.Information($"Database not found. Creating a new one: {ex.Message}");
                }

                Log.Information("Setting WAL mode");
                context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                Log.Information("Creating index idx_tracks_path");
                context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_tracks_path ON Tracks(Path);");
                Log.Information("Creating index idx_playlisttracks_playlistid");
                context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_playlisttracks_playlistid ON PlaylistTracks(PlaylistId);");
                Log.Information("Creating MetadataCache table");
                context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS MetadataCache (Path TEXT PRIMARY KEY, LastModified INTEGER, Metadata TEXT NOT NULL);");
            }
            LoadFromDatabaseAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize MusicLibrary");
            throw;
        }
    }

    public static async Task SaveMetadataCacheAsync()
    {
        try
        {
            using var context = _dbContextFactory.CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync();
            foreach (var entry in MetadataCache)
            {
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT OR REPLACE INTO MetadataCache (Path, LastModified, Metadata) VALUES (@p0, @p1, @p2)",
                    entry.Key, entry.Value.LastModified.Ticks, JsonSerializer.Serialize(entry.Value.Metadata));
            }
            await transaction.CommitAsync();
            Log.Information("Saved MetadataCache with {Count} entries", MetadataCache.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save metadata cache");
        }
    }

    public static async Task LoadMetadataCacheAsync()
    {
        try
        {
            using var context = _dbContextFactory.CreateDbContext();

            await context.Database.EnsureCreatedAsync(); // Creates database if it doesn’t exist
            await context.MetadataCache.ToListAsync();


            var entries = await context.MetadataCache.ToListAsync(); // Query MetadataCache entity
            MetadataCache.Clear();
            foreach (var entry in entries)
            {
                try
                {
                    MetadataCache[entry.Path] = (new DateTime(entry.LastModified),
                        JsonSerializer.Deserialize<MediaFile>(entry.Metadata)!);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Failed to deserialize metadata for {entry.Path}");
                }
            }
            Log.Information("Loaded MetadataCache with {Count} entries", MetadataCache.Count);
        }
        catch (SqliteException ex)
        {
            Log.Error(ex, "SQLite error during metadata cache load");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load metadata cache");
        }
    }

    public static async Task LoadFromDatabaseAsync()
    {
        using var context = _dbContextFactory.CreateDbContext();
        try
        {
            Playlists.Clear();
            MainLibrary.Clear();

            var tracks = await context.Tracks.AsNoTracking().ToListAsync();
            foreach (var track in tracks)
            {
                MainLibrary.Add(track);
            }

            var playlists = await context.Playlists
                .Include(p => p.PlaylistTracks)
                .ThenInclude(pt => pt!.Track)
                .Include(p => p.SelectedTrackNavigation)
                .AsNoTracking()
                .ToListAsync();
            foreach (var playlist in playlists)
            {
                var validTrackIds = playlist.PlaylistTracks!
                    .Where(pt => pt!.TrackId != null && MainLibrary.Any(t => t.Id == pt!.TrackId))
                    .OrderBy(pt => pt!.Position)
                    .Select(pt => pt!.TrackId!)
                    .ToList();
                playlist.TrackIds = new ObservableCollection<string>(validTrackIds);
                Log.Information(
                    $"Loaded playlist {playlist.Name} with TrackIds: {string.Join(", ", playlist.TrackIds)}");

                if (playlist.SelectedTrack != null && MainLibrary.All(t => t.Id != playlist.SelectedTrack))
                {
                    Log.Warning(
                        $"Invalid SelectedTrack {playlist.SelectedTrack} in playlist {playlist.Name}, clearing");
                    playlist.SelectedTrack = null;
                }
                else if (playlist.SelectedTrack == null && playlist.TrackIds.Any())
                {
                    playlist.SelectedTrack = playlist.TrackIds.First();
                    Log.Information(
                        $"Set SelectedTrack to {playlist.SelectedTrack} for playlist {playlist.Name} during load");
                }

                Playlists.Add(playlist);
            }

            ClearPlayState();

            if (!Playlists.Any())
            {
                var newPlaylist = new Playlist
                {
                    Name = "New Playlist",
                    TrackIds = new ObservableCollection<string>(),
                    SelectedTrack = null
                };
                Playlists.Add(newPlaylist);
                await SaveToDatabaseAsync();
            }

            Log.Information("Data loaded from database");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load data from database");
            throw;
        }
    }

    public static async Task SaveTracksBatchAsync(IEnumerable<MediaFile> tracks)
    {
        using var context = _dbContextFactory.CreateDbContext();
        try
        {
            context.ChangeTracker.AutoDetectChangesEnabled = false;

            foreach (var track in tracks)
            {
                if (track.Composers == null)
                {
                    Log.Warning($"Skipping invalid track {track.Path}: Composers is null");
                    continue;
                }

                var existingTrack = await context.Tracks.FirstOrDefaultAsync(t =>
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
            Log.Information($"Batch saved {tracks.Count()} tracks to database");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to batch save tracks to database");
            throw;
        }
    }
    public static async Task SaveToDatabaseAsync()
    {
        using var context = _dbContextFactory.CreateDbContext();
        try
        {
            Log.Information($"Using database file: {Path.GetFullPath(DbPath)}");
            context.ChangeTracker.AutoDetectChangesEnabled = false;

            ClearPlayState();

            foreach (var track in MainLibrary)
            {
                if (track.Composers == null)
                {
                    Log.Warning($"Skipping invalid track {track.Path}: Composers is null");
                    continue;
                }

                var existingTrack = await context.Tracks.FirstOrDefaultAsync(t =>
                    t.Path == track.Path && t.Album == track.Album && t.Duration == track.Duration);
                if (existingTrack != null)
                {
                    track.Id = existingTrack.Id;
                }
            }

            foreach (var playlist in Playlists)
            {
                Log.Information($"Saving playlist {playlist.Name}"); // with TrackIds: {string.Join(", ", playlist.TrackIds)}, SelectedTrack: {playlist.SelectedTrack}");
                var existingPlaylist = await context.Playlists
                    .Include(p => p.PlaylistTracks)
                    .FirstOrDefaultAsync(p => p.Id == playlist.Id || p.Name == playlist.Name);
                int playlistId;
                if (existingPlaylist == null)
                {
                    var newPlaylist = new Playlist
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
                    Log.Information(
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
                    Log.Information($"Updated playlist {existingPlaylist.Name}");
                }

                var validTrackIds = playlist.TrackIds.Where(id => context.Tracks.Any(t => t.Id == id)).ToList();
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
                    Log.Information(
                        $"Saved {validTrackIds.Count} PlaylistTrack entries for playlist {playlist.Name}");
                }
                else
                {
                    Log.Information($"Playlist '{playlist.Name}' is empty, no PlaylistTracks to save");
                }
            }

            await context.SaveChangesAsync();
            context.ChangeTracker.AutoDetectChangesEnabled = true;
            Log.Information("Data saved to database");

            var savedPlaylists = await context.Playlists.ToListAsync();
            foreach (var p in savedPlaylists)
            {
                var trackIds = await context.PlaylistTracks
                    .Where(pt => pt.PlaylistId == p.Id)
                    .OrderBy(pt => pt.Position)
                    .Select(pt => pt.TrackId)
                    .ToListAsync();
                Log.Information(
                    $"Database state for playlist {p.Name}"); // (Id {p.Id}): SelectedTrack={p.SelectedTrack}, TrackIds={string.Join(", ", trackIds)}");
            }
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 5)
        {
            Log.Error(ex, "Database is locked while saving to database");
            throw new InvalidOperationException("Database is locked. Please try again later.", ex);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save data to database");
            throw;
        }
    }

    public static async Task CleanOrphanedTracksAsync()
    {
        using var context = _dbContextFactory.CreateDbContext();
        try
        {
            var referencedTrackIds =
                await context.PlaylistTracks.Select(pt => pt!.TrackId!).Distinct().ToListAsync();
            var orphanedTracks = await context.Tracks
                .Where(t => !referencedTrackIds.Contains(t.Id))
                .ToListAsync();
            if (orphanedTracks.Any())
            {
                context.Tracks.RemoveRange(orphanedTracks);
                await context.SaveChangesAsync();
                Log.Information($"Removed {orphanedTracks.Count} orphaned tracks from database");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clean orphaned tracks from database");
            throw;
        }
    }

    public static MediaFile? IsTrackInLibrary(MediaFile mediaFile)
    {
        return MainLibrary.FirstOrDefault(s =>
            s.Path.Equals(mediaFile.Path, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<MediaFile?> AddTrackToLibraryAsync(MediaFile mediaFile, bool saveImmediately = true,
        bool skipMetadata = false)
    {
        Log.Debug($"Entering AddTrackToLibraryAsync for {mediaFile.Path}");
        try
        {
            var existingTrack = IsTrackInLibrary(mediaFile);
            if (existingTrack != null)
            {
                Log.Debug($"Track already in library: {mediaFile.Path}");
                return existingTrack;
            }

            if (!string.IsNullOrEmpty(mediaFile.Path) &&
                SupportedAudioExtensions.Any(s =>
                    s.Equals(Path.GetExtension(mediaFile.Path), StringComparison.OrdinalIgnoreCase)))
            {
                if (!skipMetadata)
                {
                    var fileInfo = new FileInfo(mediaFile.Path);
                    if (MetadataCache.TryGetValue(mediaFile.Path, out var cached) &&
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
                        Log.Debug($"Metadata cache hit for {mediaFile.Path}");
                    }
                    else
                    {
                        try
                        {
                            mediaFile.UpdateFromFileMetadata(false, minimal: false);
                            Log.Debug($"Extracted metadata for {mediaFile.Path}: Artist={mediaFile.Artist}, Title={mediaFile.Title}");
                            MetadataCache[mediaFile.Path] = (fileInfo.LastWriteTime, mediaFile.Clone());
                            Log.Debug($"Added {mediaFile.Path} to MetadataCache. Cache size: {MetadataCache.Count}");
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"Failed to extract metadata for {mediaFile.Path}");
                            return null;
                        }
                    }
                }
                var clonedTrack = mediaFile.Clone();
                MainLibrary.Add(clonedTrack);
                if (saveImmediately)
                {
                    await SaveTracksBatchAsync(new[] { clonedTrack });
                }
                return clonedTrack;
            }

            Log.Warning($"Unsupported file format: {mediaFile.Path}");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to add track to library: {mediaFile.Path}");
            return null;
        }
    }

    public static async Task RemoveTrackFromPlaylistAsync(string playlistName, string trackId)
    {
        var playlist = Playlists.FirstOrDefault(p => p.Name == playlistName);
        if (playlist != null && playlist!.TrackIds!.Contains(trackId))
        {
            playlist!.TrackIds!.Remove(trackId);
            if (playlist.SelectedTrack == trackId)
            {
                playlist.SelectedTrack = null;
            }
            await SaveToDatabaseAsync();
            await CleanOrphanedTracksAsync();
            Log.Information($"Track {trackId} removed from playlist {playlistName}");
        }
    }

    public static async Task<Playlist> AddNewPlaylistAsync(string playlistName)
    {
        var existingPlaylist = Playlists.FirstOrDefault(p => p.Name == playlistName);
        if (existingPlaylist != null)
        {
            Log.Information($"Returning existing playlist {playlistName} with Id {existingPlaylist.Id}");
            return existingPlaylist;
        }

        var playlist = new Playlist
        {
            Name = playlistName,
            TrackIds = new ObservableCollection<string>(),
            SelectedTrack = null
        };
        await AddPlaylistAsync(playlist);
        Log.Information($"Created new playlist {playlistName} with Id {playlist.Id}");
        return playlist;
    }

    public static async Task<bool> AddPlaylistAsync(Playlist newPlaylist)
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
            Log.Information($"Cleared invalid SelectedTrack for playlist {newPlaylist.Name}");
        }

        Playlists.Add(newPlaylist);
        await SaveToDatabaseAsync();
        Log.Information($"New playlist '{newPlaylist.Name}' added");
        return true;
    }

    public static async Task RemovePlaylistAsync(string playlistName)
    {
        using var context = _dbContextFactory.CreateDbContext();
        try
        {
            var playlist = Playlists.FirstOrDefault(p => p.Name == playlistName);
            if (playlist != null)
            {
                Playlists.Remove(playlist);
                var dbPlaylist = await context.Playlists
                    .Include(p => p.PlaylistTracks)
                    .FirstOrDefaultAsync(p => p.Name == playlistName);
                if (dbPlaylist != null)
                {
                    context.PlaylistTracks!.RemoveRange(dbPlaylist!.PlaylistTracks!);
                    context.Playlists!.Remove(dbPlaylist);
                    await context.SaveChangesAsync();
                    Log.Information(
                        $"Removed playlist '{playlistName}' and {dbPlaylist!.PlaylistTracks!.Count} tracks from database");
                }
                await CleanOrphanedTracksAsync();
                Log.Information($"Playlist '{playlistName}' removed");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to remove playlist '{playlistName}'");
            throw;
        }
    }

    public static async Task AddTracksToPlaylistAsync(IList<string> trackIds, string playlistName,
        bool saveImmediately = true)
    {
        var playlist = Playlists.FirstOrDefault(p => p.Name == playlistName);
        if (playlist == null)
        {
            Log.Warning($"Playlist {playlistName} not found");
            return;
        }

        var validTrackIds = trackIds
            .Where(id => !playlist.TrackIds.Contains(id) && MainLibrary.Any(t => t.Id == id)).ToList();
        if (!validTrackIds.Any())
        {
            Log.Information($"No new valid tracks to add to playlist {playlistName}");
            return;
        }

        foreach (var trackId in validTrackIds)
        {
            playlist.TrackIds.Add(trackId);
        }

        if (playlist.SelectedTrack == null && playlist.TrackIds.Any())
        {
            playlist.SelectedTrack = playlist.TrackIds.First();
            Log.Information($"Set SelectedTrack to {playlist.SelectedTrack} for playlist {playlistName}");
        }

        Log.Information($"Added {validTrackIds.Count} tracks to playlist {playlistName}");
        if (saveImmediately)
        {
            await SaveToDatabaseAsync();
        }
    }

    public static async Task AddTrackToPlaylistAsync(string trackId, string playlistName,
        bool saveImmediately = true, int position = -1)
    {
        var playlist = Playlists.FirstOrDefault(p => p.Name == playlistName);
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
                Log.Information($"Set SelectedTrack to {playlist.SelectedTrack} for playlist {playlistName}");
            }
            Log.Information($"Track {trackId} added to playlist {playlistName} at position {position}");
            if (saveImmediately)
            {
                await SaveToDatabaseAsync();
            }
        }
        else
        {
            Log.Warning($"Failed to add track {trackId} to playlist {playlistName}: Playlist or track not found");
        }
    }

    public static List<Playlist> GetPlaylists()
    {
        return Playlists.ToList();
    }

    public static List<MediaFile> GetTracksFromPlaylist(string? playlistName)
    {
        Log.Information("MusicLibrary - GetTracksFromPlaylist");
        var playlist = Playlists.FirstOrDefault(p => p.Name == playlistName);
        if (playlist == null)
        {
            return new List<MediaFile>();
        }

        return playlist!.TrackIds!
            .Select(trackId => MainLibrary.FirstOrDefault(p => p.Id == trackId))
            .Where(t => t != null)
            .Select(t => t!)
            .ToList();
    }

    public static void ClearPlayState()
    {
        foreach (var file in MainLibrary)
        {
            file.State = PlaybackState.Stopped;
        }
    }
}