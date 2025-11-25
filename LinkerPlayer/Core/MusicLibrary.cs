using LinkerPlayer.Database;
using LinkerPlayer.Models;
using ManagedBass;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;

namespace LinkerPlayer.Core;

public interface IMusicLibrary
{
    ObservableCollection<MediaFile> MainLibrary { get; }
    ObservableCollection<Playlist> Playlists { get; }

    Task<MediaFile?> AddTrackToLibraryAsync(MediaFile mediaFile, bool saveImmediately = true);
    Task RemoveTrackFromPlaylistAsync(string playlistName, string trackId);
    Task<Playlist> AddNewPlaylistAsync(string playlistName);
    Task<bool> AddPlaylistAsync(Playlist newPlaylist);
    Task RemovePlaylistAsync(string playlistName);
    Task AddTracksToPlaylistAsync(IList<string> trackIds, string playlistName, bool saveImmediately = true);
    Task AddTrackToPlaylistAsync(string trackId, string playlistName, bool saveImmediately = true, int position = -1);
    MediaFile? IsTrackInLibrary(MediaFile mediaFile);
    List<Playlist> GetPlaylists();
    List<MediaFile> GetTracksFromPlaylist(string? playlistName);
    Task SaveTracksBatchAsync(IEnumerable<MediaFile> tracks);
    Task SaveToDatabaseAsync();
    void SaveToDatabase();
    Task LoadFromDatabaseAsync();
    Task CleanOrphanedTracksAsync();
}

public class MusicLibrary : IMusicLibrary
{
    private readonly ILogger<MusicLibrary> _logger;

    private readonly string _dbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LinkerPlayer", "music_library.db");

    private readonly IDbContextFactory<MusicLibraryDbContext> _dbContextFactory;
    public ObservableCollection<MediaFile> MainLibrary { get; } = new();
    public ObservableCollection<Playlist> Playlists { get; } = new();
    public static string[] _supportedAudioExtensions = [".mp3", ".flac", ".ape", ".ac3", ".dts", ".m4a", ".mka", ".mp4", ".mpc", ".ofr", ".ogg", ".opus", ".wav", ".wma", ".wv"];

    public MusicLibrary(ILogger<MusicLibrary> logger)
    {
        _logger = logger;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

            DbContextOptions<MusicLibraryDbContext> options = new DbContextOptionsBuilder<MusicLibraryDbContext>()
                .UseSqlite($"Data Source={_dbPath};Pooling=True;")
                .Options;
            _dbContextFactory = new PooledDbContextFactory<MusicLibraryDbContext>(options);

            using (MusicLibraryDbContext context = _dbContextFactory.CreateDbContext())
            {
                try
                {
                    context.Database.EnsureCreated();
                }
                catch (SqliteException ex)
                {
                    _logger.LogInformation($"Database not found. Creating a new one: {ex.Message}");
                }

                context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_tracks_path ON Tracks(Path);");
                context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_playlisttracks_playlistid ON PlaylistTracks(PlaylistId);");

                // Add Order column to Playlists table if it doesn't exist (for existing databases)
                try
                {
                    List<int> result = context.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) FROM pragma_table_info('Playlists') WHERE name='Order'").ToList();

                    if (result.FirstOrDefault() == 0)
                    {
                        context.Database.ExecuteSqlRaw("ALTER TABLE Playlists ADD COLUMN \"Order\" INTEGER NOT NULL DEFAULT 0;");
                        _logger.LogInformation("Added Order column to Playlists table");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking/adding Order column to Playlists table");
                }
            }

            // Synchronous load to populate UI immediately
            LoadFromDatabaseAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize MusicLibrary");
            throw;
        }
    }

    public async Task LoadFromDatabaseAsync()
    {
        await using MusicLibraryDbContext context = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            Playlists.Clear();
            MainLibrary.Clear();

            // Load all tracks from database with all their metadata
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
                .OrderBy(p => p.Order)
                .ToListAsync();
            foreach (Playlist playlist in playlists)
            {
                List<string> validTrackIds = playlist.PlaylistTracks
                    .Where(pt => pt.TrackId != null && MainLibrary.Any(t => t.Id == pt.TrackId))
                    .OrderBy(pt => pt.Position)
                    .Select(pt => pt.TrackId!)
                    .ToList();
                playlist.TrackIds = new ObservableCollection<string>(validTrackIds);

                // Validate that SelectedTrackId exists in the TrackIds list (not MainLibrary)
                if (playlist.SelectedTrackId != null && !validTrackIds.Contains(playlist.SelectedTrackId))
                {
                    _logger.LogWarning(
                        $"Invalid SelectedTrack {playlist.SelectedTrackId} in playlist {playlist.Name}, clearing");
                    playlist.SelectedTrackId = null;
                }

                // Only set to first track if SelectedTrackId is actually null
                if (playlist.SelectedTrackId == null && playlist.TrackIds.Any())
                {
                    playlist.SelectedTrackId = playlist.TrackIds.First();
                }

                Playlists.Add(playlist);
            }

            // Do not clear play state here.

            if (!Playlists.Any())
            {
                Playlist newPlaylist = new()
                {
                    Name = "New Playlist",
                    TrackIds = new ObservableCollection<string>(),
                    SelectedTrackId = null,
                    Order = 0
                };
                Playlists.Add(newPlaylist);
                await SaveToDatabaseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load data from database");
            throw;
        }
    }

    public async Task SaveTracksBatchAsync(IEnumerable<MediaFile> tracks)
    {
        await using MusicLibraryDbContext context = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            context.ChangeTracker.AutoDetectChangesEnabled = false;

            // De-duplicate incoming tracks by Path to avoid adding the same file multiple times in one batch
            List<MediaFile> mediaFiles = tracks
                .Where(t => !string.IsNullOrWhiteSpace(t.Path))
                .GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            foreach (MediaFile track in mediaFiles)
            {
                // If another instance with the same Id is already tracked in this DbContext, skip it
                if (context.ChangeTracker.Entries<MediaFile>().Any(e => e.Entity.Id == track.Id))
                {
                    continue;
                }

                // Only check by Path for uniqueness in the database; use AsNoTracking to avoid tracking the query result
                MediaFile? existingTrack = await context.Tracks
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Path == track.Path);
                if (existingTrack == null)
                {
                    context.Tracks.Add(track);
                    // Do not add to MainLibrary here; callers manage MainLibrary separately
                }
                else
                {
                    // Align Id with the persisted entity so downstream references use the canonical Id
                    track.Id = existingTrack.Id;
                }
            }

            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch save tracks to database");
            throw;
        }
    }

    public async Task SaveToDatabaseAsync()
    {
        await using MusicLibraryDbContext context = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            context.ChangeTracker.AutoDetectChangesEnabled = false;

            // Ensure all MainLibrary tracks have their database Id set
            foreach (MediaFile track in MainLibrary)
            {
                MediaFile? existingTrack = await context.Tracks.FirstOrDefaultAsync(t => t.Path == track.Path);
                if (existingTrack != null)
                {
                    track.Id = existingTrack.Id;
                }
            }

            // Get all valid track IDs from the database
            HashSet<string> validTrackIdsSet = new HashSet<string>(
                await context.Tracks.Select(t => t.Id).ToListAsync()
            );

            // Update Order property for all playlists based on their position in the collection
            for (int i = 0; i < Playlists.Count; i++)
            {
                Playlists[i].Order = i;
            }

            foreach (Playlist playlist in Playlists)
            {
                // Validate SelectedTrack
                if (playlist.SelectedTrackId != null && !validTrackIdsSet.Contains(playlist.SelectedTrackId))
                {
                    playlist.SelectedTrackId = null;
                }

                // Validate TrackIds
                List<string> validTrackIds = playlist.TrackIds.Where(id => validTrackIdsSet.Contains(id)).ToList();
                playlist.TrackIds = new ObservableCollection<string>(validTrackIds);

                Playlist? existingPlaylist = await context.Playlists
                    .Include(p => p.PlaylistTracks)
                    .FirstOrDefaultAsync(p => p.Id == playlist.Id || p.Name == playlist.Name);

                int playlistId;
                if (existingPlaylist == null)
                {
                    Playlist newPlaylist = new()
                    {
                        Name = playlist.Name,
                        SelectedTrackId = playlist.SelectedTrackId,
                        Order = playlist.Order
                    };
                    context.Playlists.Add(newPlaylist);
                    await context.SaveChangesAsync();
                    playlistId = newPlaylist.Id;
                    playlist.Id = playlistId;
                }
                else
                {
                    playlistId = existingPlaylist.Id;
                    existingPlaylist.Name = playlist.Name;
                    existingPlaylist.SelectedTrackId = playlist.SelectedTrackId;
                    existingPlaylist.Order = playlist.Order;
                    context.Entry(existingPlaylist).Property(p => p.SelectedTrackId).IsModified = true;
                    context.Entry(existingPlaylist).Property(p => p.Order).IsModified = true;
                    context.Entry(existingPlaylist).State = EntityState.Modified;
                    context.PlaylistTracks.RemoveRange(existingPlaylist.PlaylistTracks);
                    playlist.Id = playlistId;
                }

                // Add PlaylistTracks only for valid tracks
                if (playlist.TrackIds.Any())
                {
                    for (int i = 0; i < playlist.TrackIds.Count; i++)
                    {
                        context.PlaylistTracks.Add(new PlaylistTrack
                        {
                            PlaylistId = playlistId,
                            TrackId = playlist.TrackIds[i],
                            Position = i
                        });
                    }
                }
            }

            await context.SaveChangesAsync();
            context.ChangeTracker.AutoDetectChangesEnabled = true;
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

    public void SaveToDatabase()
    {
        SaveToDatabaseAsync().GetAwaiter().GetResult();
    }

    public async Task CleanOrphanedTracksAsync()
    {
        await using MusicLibraryDbContext context = await _dbContextFactory.CreateDbContextAsync();
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

    public async Task<MediaFile?> AddTrackToLibraryAsync(MediaFile mediaFile, bool saveImmediately = true)
    {
        try
        {
            MediaFile? existingTrack = IsTrackInLibrary(mediaFile);
            if (existingTrack != null)
            {
                return existingTrack;
            }

            if (!string.IsNullOrEmpty(mediaFile.Path) &&
                _supportedAudioExtensions.Any(s =>
                    s.Equals(Path.GetExtension(mediaFile.Path), StringComparison.OrdinalIgnoreCase)))
            {
                // Metadata is already extracted and set on the mediaFile object
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
            if (playlist.SelectedTrackId == trackId)
            {
                playlist.SelectedTrackId = null;
            }
            await SaveToDatabaseAsync();
            await CleanOrphanedTracksAsync();
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
            SelectedTrackId = null,
            Order = Playlists.Count
        };
        await AddPlaylistAsync(playlist);
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
        if (newPlaylist.SelectedTrackId != null && MainLibrary.All(t => t.Id != newPlaylist.SelectedTrackId))
        {
            newPlaylist.SelectedTrackId = null;
        }

        Playlists.Add(newPlaylist);
        await SaveToDatabaseAsync();
        return true;
    }

    public async Task RemovePlaylistAsync(string playlistName)
    {
        await using MusicLibraryDbContext context = await _dbContextFactory.CreateDbContextAsync();
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
                }
                await CleanOrphanedTracksAsync();
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
            return;
        }

        foreach (string trackId in validTrackIds)
        {
            playlist.TrackIds.Add(trackId);
        }

        if (playlist.SelectedTrackId == null && playlist.TrackIds.Any())
        {
            playlist.SelectedTrackId = playlist.TrackIds.First();
        }

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
            if (playlist.SelectedTrackId == null && playlist.TrackIds.Any())
            {
                playlist.SelectedTrackId = playlist.TrackIds.First();
            }
            if (saveImmediately)
            {
                await SaveToDatabaseAsync();
            }
        }
        else
        {
            _logger.LogWarning("Failed to add track {TrackId} to playlist {PlaylistName}: Playlist or track not found.", trackId, playlistName);
        }
    }

    public List<Playlist> GetPlaylists()
    {
        return Playlists.ToList();
    }

    public List<MediaFile> GetTracksFromPlaylist(string? playlistName)
    {
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
}
