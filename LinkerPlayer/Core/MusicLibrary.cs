using LinkerPlayer.Database;
using LinkerPlayer.Models;
using ManagedBass;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LinkerPlayer.Core;

public class MusicLibrary
{
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LinkerPlayer", "music_library.db");
    private static readonly MusicLibraryDbContext _dbContext;
    public static ObservableCollection<MediaFile> MainLibrary { get; } = new();
    public static ObservableCollection<Playlist> Playlists { get; } = new();
    private static readonly string[] SupportedAudioExtensions = [".mp3", ".flac", ".wav"];

    static MusicLibrary()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            var options = new DbContextOptionsBuilder<MusicLibraryDbContext>()
                .UseSqlite($"Data Source={DbPath}")
                .Options;
            _dbContext = new MusicLibraryDbContext(options);
            _dbContext.Database.EnsureCreated();
            LoadFromDatabaseAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize MusicLibrary");
            throw;
        }
    }

    public static async Task LoadFromDatabaseAsync()
    {
        try
        {
            Playlists.Clear();
            using (var context = new MusicLibraryDbContext(new DbContextOptionsBuilder<MusicLibraryDbContext>()
                .UseSqlite($"Data Source={DbPath}")
                .Options))
            {
                var duplicateNames = await context.Playlists
                    .GroupBy(p => p.Name)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToListAsync();
                foreach (var name in duplicateNames)
                {
                    var duplicates = await context.Playlists
                        .Where(p => p.Name == name)
                        .OrderBy(p => p.Id)
                        .Skip(1)
                        .ToListAsync();
                    context.Playlists.RemoveRange(duplicates);
                    Log.Information($"Removed {duplicates.Count} duplicate playlists named '{name}'");
                }
                await context.SaveChangesAsync();
            }

            MainLibrary.Clear();

            var tracks = await _dbContext.Tracks.AsNoTracking().ToListAsync();
            foreach (var track in tracks)
            {
                MainLibrary.Add(track);
            }

            var playlists = await _dbContext.Playlists
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
                Log.Information($"Loaded playlist {playlist.Name} with TrackIds: {string.Join(", ", playlist.TrackIds)}");

                if (playlist.SelectedTrack != null && MainLibrary.All(t => t.Id != playlist.SelectedTrack))
                {
                    Log.Warning($"Invalid SelectedTrack {playlist.SelectedTrack} in playlist {playlist.Name}, clearing");
                    playlist.SelectedTrack = null;
                }
                else if (playlist.SelectedTrack == null && playlist.TrackIds.Any())
                {
                    playlist.SelectedTrack = playlist.TrackIds.First();
                    Log.Information($"Set SelectedTrack to {playlist.SelectedTrack} for playlist {playlist.Name} during load");
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
        try
        {
            using var context = new MusicLibraryDbContext(new DbContextOptionsBuilder<MusicLibraryDbContext>()
                .UseSqlite($"Data Source={DbPath}")
                .Options);

            context.ChangeTracker.AutoDetectChangesEnabled = false; // Optimize bulk inserts

            foreach (var track in tracks)
            {
                if (track.Composers == null)
                {
                    Log.Warning($"Skipping invalid track {track.Path}: Composers is null");
                    continue;
                }

                var existingTrack = await context.Tracks.FirstOrDefaultAsync(t => t.Path == track.Path && t.Album == track.Album && t.Duration == track.Duration);
                if (existingTrack == null)
                {
                    context.Tracks.Add(track);
                    MainLibrary.Add(track); // Ensure MainLibrary is updated
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
                    track.Id = existingTrack.Id; // Sync in-memory Id
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
        try
        {
            using var context = new MusicLibraryDbContext(new DbContextOptionsBuilder<MusicLibraryDbContext>()
                .UseSqlite($"Data Source={DbPath}")
                .Options);

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

                var existingTrack = await context.Tracks.FirstOrDefaultAsync(t => t.Path == track.Path && t.Album == track.Album && t.Duration == track.Duration);
                if (existingTrack != null)
                {
                    track.Id = existingTrack.Id;
                }
            }

            foreach (var playlist in Playlists)
            {
                Log.Information($"Saving playlist {playlist.Name} with TrackIds: {string.Join(", ", playlist.TrackIds)}, SelectedTrack: {playlist.SelectedTrack}");
                var existingPlaylist = await context.Playlists
                    .Include(p => p.PlaylistTracks)
                    .FirstOrDefaultAsync(p => p.Id == playlist.Id || p.Name == playlist.Name);
                int playlistId;
                if (existingPlaylist == null)
                {
                    var newPlaylist = new Playlist
                    {
                        Name = playlist.Name,
                        SelectedTrack = playlist.SelectedTrack != null && MainLibrary.Any(t => t.Id == playlist.SelectedTrack)
                            ? playlist.SelectedTrack
                            : null
                    };
                    context.Playlists.Add(newPlaylist);
                    await context.SaveChangesAsync();
                    playlistId = newPlaylist.Id;
                    playlist.Id = playlistId;
                    Log.Information($"Created new playlist {newPlaylist.Name} with Id {playlistId}, SelectedTrack {newPlaylist.SelectedTrack}");
                }
                else
                {
                    playlistId = existingPlaylist.Id;
                    existingPlaylist.Name = playlist.Name;
                    existingPlaylist.SelectedTrack = playlist.SelectedTrack != null && MainLibrary.Any(t => t.Id == playlist.SelectedTrack)
                        ? playlist.SelectedTrack
                        : null;
                    context.Entry(existingPlaylist).Property(p => p.SelectedTrack).IsModified = true; // Force update
                    context.Entry(existingPlaylist).State = EntityState.Modified;
                    context.PlaylistTracks.RemoveRange(existingPlaylist.PlaylistTracks);
                    playlist.Id = playlistId;
                    Log.Information($"Updated playlist {existingPlaylist.Name} with Id {playlistId}, SelectedTrack {existingPlaylist.SelectedTrack}");
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
                    Log.Information($"Saved {validTrackIds.Count} PlaylistTrack entries for playlist {playlist.Name}");
                }
                else
                {
                    Log.Information($"Playlist '{playlist.Name}' is empty, no PlaylistTracks to save");
                }
            }

            await context.SaveChangesAsync();
            context.ChangeTracker.AutoDetectChangesEnabled = true;
            Log.Information("Data saved to database");

            // Verify database state
            var savedPlaylists = await context.Playlists.ToListAsync();
            foreach (var p in savedPlaylists)
            {
                var trackIds = await context.PlaylistTracks
                    .Where(pt => pt.PlaylistId == p.Id)
                    .Select(pt => pt.TrackId)
                    .ToListAsync();
                Log.Information($"Database state for playlist {p.Name} (Id {p.Id}): SelectedTrack={p.SelectedTrack}, TrackIds={string.Join(", ", trackIds)}");
            }
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            Log.Error(ex, "Foreign key constraint violation while saving to database");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save data to database");
            throw;
        }
    }

    public static async Task CleanOrphanedTracksAsync()
    {
        try
        {
            var referencedTrackIds = await _dbContext!.PlaylistTracks.Select(pt => pt!.TrackId!).Distinct().ToListAsync();
            var orphanedTracks = await _dbContext!.Tracks
                .Where(t => !referencedTrackIds.Contains(t.Id))
                .ToListAsync();
            if (orphanedTracks.Any())
            {
                _dbContext!.Tracks.RemoveRange(orphanedTracks);
                await _dbContext.SaveChangesAsync();
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
            s.Path == mediaFile.Path &&
            s.Album == mediaFile.Album &&
            s.Duration == mediaFile.Duration);
    }

    public static async Task<MediaFile?> AddTrackToLibraryAsync(MediaFile mediaFile, bool saveImmediately = true)
    {
        var existingTrack = IsTrackInLibrary(mediaFile);
        if (existingTrack != null)
        {
            return existingTrack;
        }

        if (!string.IsNullOrEmpty(mediaFile.Path) &&
            SupportedAudioExtensions.Any(s => s.Equals(Path.GetExtension(mediaFile.Path), StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                mediaFile.UpdateFromFileMetadata();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to extract metadata for {mediaFile.Path}");
                return null;
            }
            var clonedTrack = mediaFile.Clone();
            MainLibrary.Add(clonedTrack);
            if (saveImmediately)
            {
                await SaveTracksBatchAsync(new[] { clonedTrack }); // Use batch save
            }
            return clonedTrack;
        }

        Log.Warning($"Unsupported file format: {mediaFile.Path}");
        return null;
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
            return existingPlaylist;
        }

        var playlist = new Playlist
        {
            Name = playlistName,
            TrackIds = new ObservableCollection<string>(),
            SelectedTrack = null
        };
        await AddPlaylistAsync(playlist);
        return playlist;
    }

    public static async Task<bool> AddPlaylistAsync(Playlist newPlaylist)
    {
        if (Playlists.Any(p => p.Name == newPlaylist.Name))
        {
            return false;
        }

        // Validate playlist
        newPlaylist.TrackIds = new ObservableCollection<string>(newPlaylist.TrackIds.Where(id => MainLibrary.Any(t => t.Id == id)));
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
        var playlist = Playlists.FirstOrDefault(p => p.Name == playlistName);
        if (playlist != null)
        {
            Playlists.Remove(playlist);
            var dbPlaylist = await _dbContext.Playlists
                .Include(p => p.PlaylistTracks)
                .FirstOrDefaultAsync(p => p.Name == playlistName);
            if (dbPlaylist != null)
            {
                _dbContext.PlaylistTracks!.RemoveRange(dbPlaylist!.PlaylistTracks!);
                _dbContext.Playlists!.Remove(dbPlaylist);
                await _dbContext.SaveChangesAsync();
                Log.Information($"Removed playlist '{playlistName}' and {dbPlaylist!.PlaylistTracks!.Count} tracks from database");
            }
            await CleanOrphanedTracksAsync();
            Log.Information($"Playlist '{playlistName}' removed");
        }
    }

    public static async Task AddTrackToPlaylistAsync(string trackId, string playlistName, bool saveImmediately = true, int position = -1)
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
                Log.Information($"Set SelectedTrack to {playlist.SelectedTrack} for playlist {playlistName} in AddTrackToPlaylistAsync");
            }
            Log.Information($"Track {trackId} added to playlist {playlistName} at position {position}, TrackIds: {string.Join(", ", playlist.TrackIds)}");
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