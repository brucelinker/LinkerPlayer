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
            // Clean up duplicate playlists in database
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

            // Load tracks
            var tracks = await _dbContext.Tracks.AsNoTracking().ToListAsync();
            foreach (var track in tracks)
            {
                MainLibrary.Add(track);
            }

            // Load playlists
            var playlists = await _dbContext.Playlists
                .Include(p => p.PlaylistTracks)
                .ThenInclude(pt => pt!.Track)
                .Include(p => p.SelectedTrackNavigation)
                .AsNoTracking()
                .ToListAsync();
            foreach (var playlist in playlists)
            {
                // Populate TrackIds
                var validTrackIds = playlist.PlaylistTracks!
                    .Where(pt => pt!.TrackId != null && MainLibrary.Any(t => t.Id == pt!.TrackId))
                    .OrderBy(pt => pt!.Position)
                    .Select(pt => pt!.TrackId!)
                    .ToList();
                playlist.TrackIds = new ObservableCollection<string>(validTrackIds);

                // Validate SelectedTrack
                if (playlist.SelectedTrack != null && MainLibrary.All(t => t.Id != playlist.SelectedTrack))
                {
                    Log.Warning($"Invalid SelectedTrack {playlist.SelectedTrack} in playlist {playlist.Name}, clearing");
                    playlist.SelectedTrack = null;
                }

                Playlists.Add(playlist);
            }

            ClearPlayState();

            // Create default empty playlist if none exist
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

    public static async Task SaveToDatabaseAsync()
    {
        try
        {
            ClearPlayState();

            // Upsert tracks
            foreach (var track in MainLibrary)
            {
                // Validate track
                if (track.Composers == null)
                {
                    Log.Warning($"Skipping invalid track {track.Path}: Composers is null");
                    continue;
                }

                var existingTrack = await _dbContext.Tracks.FirstOrDefaultAsync(t => t.Path == track.Path && t.Album == track.Album && t.Duration == track.Duration);
                if (existingTrack == null)
                {
                    _dbContext.Tracks.Add(track);
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
                }
            }

            // Upsert playlists
            foreach (var playlist in Playlists)
            {
                var existingPlaylist = await _dbContext.Playlists
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
                    _dbContext.Playlists.Add(newPlaylist);
                    await _dbContext.SaveChangesAsync();
                    playlistId = newPlaylist.Id;
                    playlist.Id = playlistId;
                }
                else
                {
                    playlistId = existingPlaylist.Id;
                    existingPlaylist.Name = playlist.Name;
                    existingPlaylist.SelectedTrack = playlist.SelectedTrack != null && MainLibrary.Any(t => t.Id == playlist.SelectedTrack)
                        ? playlist.SelectedTrack
                        : null;
                    _dbContext.PlaylistTracks.RemoveRange(existingPlaylist!.PlaylistTracks);
                    playlist.Id = playlistId;
                }

                // Save PlaylistTracks
                var validTrackIds = playlist.TrackIds!.Where(id => MainLibrary.Any(t => t.Id == id)).ToList();
                if (validTrackIds.Any())
                {
                    for (int i = 0; i < validTrackIds.Count; i++)
                    {
                        _dbContext.PlaylistTracks.Add(new PlaylistTrack
                        {
                            PlaylistId = playlistId,
                            TrackId = validTrackIds[i],
                            Position = i
                        });
                    }
                }
                else
                {
                    Log.Information($"Playlist '{playlist.Name}' is empty, no PlaylistTracks to save");
                }
            }

            await _dbContext.SaveChangesAsync();
            Log.Information("Data saved to database");
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
            MainLibrary!.Add(clonedTrack);
            if (saveImmediately)
            {
                await SaveToDatabaseAsync();
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

    public static async Task<bool> AddPlaylistAsync(Playlist newPlaylist)
    {
        if (Playlists.Any(p => p.Name == newPlaylist.Name))
        {
            return false;
        }

        // Validate playlist
        newPlaylist!.TrackIds = new ObservableCollection<string>(newPlaylist!.TrackIds!.Where(id => MainLibrary.Any(t => t.Id == id)));
        if (newPlaylist.SelectedTrack != null && MainLibrary.All(t => t.Id != newPlaylist!.SelectedTrack))
        {
            newPlaylist!.SelectedTrack = null;
        }

        Playlists.Add(newPlaylist);
        await SaveToDatabaseAsync();
        Log.Information($"New playlist '{newPlaylist.Name}' added");
        return true;
    }

    public static async Task<Playlist> AddNewPlaylistAsync(string playlistName)
    {
        var existingPlaylist = Playlists.FirstOrDefault(p => p.Name == playlistName);
        if (existingPlaylist != null)
        {
            return existingPlaylist!;
        }

        var playlist = new Playlist
        {
            Name = playlistName,
            TrackIds = new ObservableCollection<string>(),
            SelectedTrack = null
        };
        await AddPlaylistAsync(playlist);
        return playlist!;
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

    public static async Task AddTrackToPlaylistAsync(string trackId, string? playlistName, int position = -1, bool saveImmediately = true)
    {
        var playlist = Playlists.FirstOrDefault(p => p.Name == playlistName);
        if (playlist != null && !playlist!.TrackIds!.Contains(trackId) && MainLibrary.Any(t => t.Id == trackId))
        {
            if (position == -1 || position >= playlist!.TrackIds!.Count)
            {
                playlist!.TrackIds!.Add(trackId);
            }
            else if (position >= 0)
            {
                playlist!.TrackIds!.Insert(position, trackId);
            }
            if (saveImmediately)
            {
                await SaveToDatabaseAsync();
            }
            Log.Information($"Track {trackId} added to playlist {playlistName} at position {position}");
        }
        else
        {
            Log.Warning($"Cannot add track {trackId} to playlist '{playlistName}': Track not found or already exists");
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