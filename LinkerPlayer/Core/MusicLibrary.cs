using LinkerPlayer.Database;
using LinkerPlayer.Models;
using ManagedBass;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Data;

namespace LinkerPlayer.Core;

public interface IMusicLibrary
{
    RangeObservableCollection<MediaFile> MainLibrary { get; }
    ObservableCollection<Playlist> Playlists { get; }

    Task<MediaFile?> AddTrackToLibraryAsync(MediaFile mediaFile, bool saveImmediately = true);
    Task AddTracksToLibraryBatchAsync(IEnumerable<MediaFile> mediaFiles);
    Task RemoveTrackFromLibraryAsync(string trackId);
    Task RemoveTracksAsync(IEnumerable<string> trackIds);
    Task<int> RemoveTracksFromFolderAsync(string folderPath);
    Task RemoveTrackFromPlaylistAsync(string playlistName, string trackId);
    Task<Playlist> AddNewPlaylistAsync(string playlistName);
    Task<bool> AddPlaylistAsync(Playlist newPlaylist);
    Task RemovePlaylistAsync(string playlistName);
    Task AddTracksToPlaylistAsync(IList<string> trackIds, string playlistName, bool saveImmediately = true);
    Task AddTrackToPlaylistAsync(string trackId, string playlistName, bool saveImmediately = true, int position = -1);
    MediaFile? IsTrackInLibrary(MediaFile mediaFile);
    List<Playlist> GetPlaylists();
    List<string> GetPlaylistsContainingTrack(string trackId);
    List<MediaFile> GetTracksFromPlaylist(string? playlistName);
    Task SaveTracksBatchAsync(IEnumerable<MediaFile> tracks);
    Task SaveToDatabaseAsync();
    void SaveToDatabase();
    event EventHandler LibraryLoaded;
    Task LoadFromDatabaseAsync();
    Task CleanOrphanedTracksAsync();
    Task UpdateTracksAsync(IEnumerable<MediaFile> tracks, bool updateMetadata = true, bool updateAnalysis = true);
    Task BackfillEmbeddedCoverAsync(CancellationToken cancellationToken = default);
    Task BackfillMp3VbrAsync(CancellationToken cancellationToken = default);
}

public class MusicLibrary : IMusicLibrary
{
    private readonly ILogger<MusicLibrary> _logger;

    private readonly string _dbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LinkerPlayer", "music_library.db");

    private readonly IDbContextFactory<MusicLibraryDbContext> _dbContextFactory;
    public event EventHandler? LibraryLoaded;

    public RangeObservableCollection<MediaFile> MainLibrary { get; } = new();
    public ObservableCollection<Playlist> Playlists { get; } = new();
    public static string[] _supportedAudioExtensions = [".mp3", ".flac", ".ape", ".ac3", ".dsd", ".dsf", ".dts", ".m4a", ".mka", ".mp4", ".mpc", ".ofr", ".ogg", ".opus", ".wav", ".wma", ".wv"];

    private readonly object _mainLibraryLock = new();
    private readonly object _playlistsLock = new();
    private readonly object _batchAddLock = new();  // Serialize batch UI adds to prevent overlapping CollectionChanged events

    public MusicLibrary(ILogger<MusicLibrary> logger)
    {
        _logger = logger;

        // Enable cross-thread collection synchronization for WPF binding
        BindingOperations.EnableCollectionSynchronization(MainLibrary, _mainLibraryLock);
        BindingOperations.EnableCollectionSynchronization(Playlists, _playlistsLock);

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

                // Add LeadingSilenceMs/TrailingSilenceMs columns to Tracks table if they don't exist (for existing databases)
                try
                {
                    List<int> leadingResult = context.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) FROM pragma_table_info('Tracks') WHERE name='LeadingSilenceMs'").ToList();
                    if (leadingResult.FirstOrDefault() == 0)
                    {
                        context.Database.ExecuteSqlRaw("ALTER TABLE Tracks ADD COLUMN \"LeadingSilenceMs\" INTEGER NULL;");
                        _logger.LogInformation("Added LeadingSilenceMs column to Tracks table");
                    }

                    List<int> trailingResult = context.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) FROM pragma_table_info('Tracks') WHERE name='TrailingSilenceMs'").ToList();
                    if (trailingResult.FirstOrDefault() == 0)
                    {
                        context.Database.ExecuteSqlRaw("ALTER TABLE Tracks ADD COLUMN \"TrailingSilenceMs\" INTEGER NULL;");
                        _logger.LogInformation("Added TrailingSilenceMs column to Tracks table");
                    }

                    List<int> lastWriteResult = context.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) FROM pragma_table_info('Tracks') WHERE name='FileLastWriteTimeUtc'").ToList();
                    if (lastWriteResult.FirstOrDefault() == 0)
                    {
                        context.Database.ExecuteSqlRaw("ALTER TABLE Tracks ADD COLUMN \"FileLastWriteTimeUtc\" TEXT NULL;");
                        _logger.LogInformation("Added FileLastWriteTimeUtc column to Tracks table");
                    }

                    List<int> lastRefreshResult = context.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) FROM pragma_table_info('Tracks') WHERE name='LastMetadataRefreshUtc'").ToList();
                    if (lastRefreshResult.FirstOrDefault() == 0)
                    {
                        context.Database.ExecuteSqlRaw("ALTER TABLE Tracks ADD COLUMN \"LastMetadataRefreshUtc\" TEXT NULL;");
                        _logger.LogInformation("Added LastMetadataRefreshUtc column to Tracks table");
                    }

                    List<int> needsMetadataResult = context.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) FROM pragma_table_info('Tracks') WHERE name='NeedsMetadataRefresh'").ToList();
                    if (needsMetadataResult.FirstOrDefault() == 0)
                    {
                        // SQLite does not have a boolean type; use INTEGER 0/1 with default 0
                        context.Database.ExecuteSqlRaw("ALTER TABLE Tracks ADD COLUMN \"NeedsMetadataRefresh\" INTEGER NOT NULL DEFAULT 0;");
                        _logger.LogInformation("Added NeedsMetadataRefresh column to Tracks table");
                    }

                    List<int> hasEmbeddedCoverResult = context.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) FROM pragma_table_info('Tracks') WHERE name='HasEmbeddedCover'").ToList();
                    if (hasEmbeddedCoverResult.FirstOrDefault() == 0)
                    {
                        context.Database.ExecuteSqlRaw("ALTER TABLE Tracks ADD COLUMN \"HasEmbeddedCover\" INTEGER NOT NULL DEFAULT 0;");
                        _logger.LogInformation("Added HasEmbeddedCover column to Tracks table");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking/adding analysis/metadata columns to Tracks table");
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
            // Load data from database on background thread
            List<MediaFile> tracks = await context.Tracks.AsNoTracking().ToListAsync();

            List<Playlist> playlists = await context.Playlists
                .Include(p => p.PlaylistTracks)
                .ThenInclude(pt => pt.Track)
                .Include(p => p.SelectedTrackNavigation)
                .AsNoTracking()
                .OrderBy(p => p.Order)
                .ToListAsync();

            // Collection changes are now thread-safe via BindingOperations.EnableCollectionSynchronization
            Playlists.Clear();
            MainLibrary.Clear();

            // Add all tracks to MainLibrary using AddRange for better performance
            foreach (MediaFile track in tracks)
            {
                track.EnableDirtyTracking();
            }
            MainLibrary.AddRange(tracks);

            // Notify subscribers (e.g. PlaylistTabsViewModel) that the library is populated
            Application.Current?.Dispatcher.BeginInvoke(() => LibraryLoaded?.Invoke(this, EventArgs.Empty));

            // Process playlists
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

    /// <summary>
    /// One-time background pass that sets <see cref="MediaFile.HasEmbeddedCover"/> for any
    /// tracks that were loaded from the DB before the column existed (i.e. value is false).
    /// Only reads the ATL picture-list count — no image bytes are decoded.
    /// </summary>
    public async Task BackfillEmbeddedCoverAsync(CancellationToken cancellationToken = default)
    {
        List<MediaFile> needsBackfill = MainLibrary.Where(t => !t.HasEmbeddedCover).ToList();
        if (needsBackfill.Count == 0)
            return;

        _logger.LogInformation("Backfilling HasEmbeddedCover for {Count} tracks…", needsBackfill.Count);

        // Parallelise the ATL file probes — NAS I/O is the bottleneck, not CPU.
        // 64 concurrent readers works well on a 2.5 GbE+ NAS; lower if you see
        // SMB errors or the NAS becomes unresponsive.
        const int dbBatchSize = 500;
        SemaphoreSlim semaphore = new(64, 64);
        System.Collections.Concurrent.ConcurrentBag<string> updatedIds = new();

        IEnumerable<Task> probeTasks = needsBackfill.Select(async track =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (CoverProber.HasEmbeddedCover(track.Path))
                {
                    using (track.SuspendDirtyTracking())
                        track.HasEmbeddedCover = true;   // update in-memory immediately → icon appears live
                    updatedIds.Add(track.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "BackfillEmbeddedCover: skipping {Path}", track.Path);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(probeTasks).ConfigureAwait(false);

        if (updatedIds.IsEmpty)
        {
            _logger.LogInformation("BackfillEmbeddedCover: no tracks with embedded covers found");
            return;
        }

        // Persist to DB in batches — one transaction per batch keeps SQLite happy
        // and avoids a 43 000-row single UPDATE.
        List<string> ids = updatedIds.ToList();
        _logger.LogInformation("BackfillEmbeddedCover: persisting {Count} tracks to DB in batches of {Batch}",
            ids.Count, dbBatchSize);
        try
        {
            await using MusicLibraryDbContext context = await _dbContextFactory.CreateDbContextAsync();
            for (int i = 0; i < ids.Count; i += dbBatchSize)
            {
                if (cancellationToken.IsCancellationRequested) break;

                List<string> batch = ids.GetRange(i, Math.Min(dbBatchSize, ids.Count - i));
                string idList = string.Join(",", batch.Select(id => $"'{id}'"));
                context.Database.ExecuteSqlRaw(
                    $"UPDATE Tracks SET HasEmbeddedCover = 1 WHERE Id IN ({idList})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BackfillEmbeddedCover: failed to persist to DB");
        }

        _logger.LogInformation("BackfillEmbeddedCover complete");
    }

    public async Task BackfillMp3VbrAsync(CancellationToken cancellationToken = default)
    {
        // Only re-probe tracks stored as plain "MP3" — i.e. loaded before this feature.
        List<MediaFile> needsBackfill = MainLibrary
            .Where(t => string.Equals(t.Codec, "MP3", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (needsBackfill.Count == 0)
            return;

        _logger.LogInformation("BackfillMp3Vbr: probing {Count} MP3 tracks…", needsBackfill.Count);

        const int dbBatchSize = 500;
        SemaphoreSlim semaphore = new(64, 64);
        System.Collections.Concurrent.ConcurrentBag<(string Id, string Codec)> updates = new();

        IEnumerable<Task> probeTasks = needsBackfill.Select(async track =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ATL.Track atlTrack = new(track.Path);
                string codec = $"MP3 {(atlTrack.IsVBR ? "VBR" : "CBR")}";
                using (track.SuspendDirtyTracking())
                    track.Codec = codec;   // update in-memory immediately
                updates.Add((track.Id, codec));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "BackfillMp3Vbr: skipping {Path}", track.Path);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(probeTasks).ConfigureAwait(false);

        if (updates.IsEmpty)
            return;

        List<(string Id, string Codec)> updateList = updates.ToList();
        _logger.LogInformation("BackfillMp3Vbr: persisting {Count} tracks in batches of {Batch}",
            updateList.Count, dbBatchSize);
        try
        {
            await using MusicLibraryDbContext context = await _dbContextFactory.CreateDbContextAsync();
            for (int i = 0; i < updateList.Count; i += dbBatchSize)
            {
                if (cancellationToken.IsCancellationRequested) break;

                List<(string Id, string Codec)> batch = updateList.GetRange(i, Math.Min(dbBatchSize, updateList.Count - i));
                foreach ((string id, string codec) in batch)
                    context.Database.ExecuteSqlRaw(
                        $"UPDATE Tracks SET Codec = '{codec}' WHERE Id = '{id}'");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BackfillMp3Vbr: failed to persist to DB");
        }

        _logger.LogInformation("BackfillMp3Vbr complete");
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
            // When called from batch import (saveImmediately=false), the caller already
            // handles duplicate checking via HashSet, so skip the O(n) enumeration that
            // is unsafe during concurrent access.
            if (saveImmediately)
            {
                MediaFile? existingTrack = IsTrackInLibrary(mediaFile);
                if (existingTrack != null)
                {
                    return existingTrack;
                }
            }

            if (!string.IsNullOrEmpty(mediaFile.Path) &&
                _supportedAudioExtensions.Any(s =>
                    s.Equals(Path.GetExtension(mediaFile.Path), StringComparison.OrdinalIgnoreCase)))
            {
                // Metadata is already extracted and set on the mediaFile object
                MediaFile clonedTrack = mediaFile.Clone();

                // Must add on UI thread to prevent "Cannot change ObservableCollection during CollectionChanged" errors
                // Use InvokeAsync to avoid blocking worker threads
                if (System.Windows.Application.Current?.Dispatcher is System.Windows.Threading.Dispatcher dispatcher)
                {
                    await dispatcher.InvokeAsync(() => MainLibrary.Add(clonedTrack));
                }
                else
                {
                    MainLibrary.Add(clonedTrack);
                }

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

    /// <summary>
    /// Adds multiple tracks to the library in a single UI-thread dispatch for performance.
    /// Does NOT save to database — caller must call SaveTracksBatchAsync separately.
    /// </summary>
    public Task AddTracksToLibraryBatchAsync(IEnumerable<MediaFile> mediaFiles)
    {
        List<MediaFile> tracksToAdd = new List<MediaFile>();

        foreach (MediaFile mediaFile in mediaFiles)
        {
            if (!string.IsNullOrEmpty(mediaFile.Path) &&
                _supportedAudioExtensions.Any(s =>
                    s.Equals(Path.GetExtension(mediaFile.Path), StringComparison.OrdinalIgnoreCase)))
            {
                tracksToAdd.Add(mediaFile.Clone());
            }
        }

        if (tracksToAdd.Count > 0)
        {
            // Use AddRange to add all items with a single CollectionChanged (Reset) notification.
            // BeginInvoke at Background priority so the worker thread never blocks the UI thread,
            // keeping input events (scroll, click, playback) responsive during large imports.
            if (System.Windows.Application.Current?.Dispatcher is System.Windows.Threading.Dispatcher dispatcher)
            {
                if (dispatcher.CheckAccess())
                {
                    MainLibrary.AddRange(tracksToAdd);
                }
                else
                {
                    dispatcher.BeginInvoke(
                        new Action(() => MainLibrary.AddRange(tracksToAdd)),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            else
            {
                MainLibrary.AddRange(tracksToAdd);
            }
        }

        return Task.CompletedTask;
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
        }
    }

    public async Task RemoveTrackFromLibraryAsync(string trackId)
    {
        MediaFile? track = MainLibrary.FirstOrDefault(t => t.Id == trackId);
        if (track != null)
        {
            MainLibrary.Remove(track);

            // Remove from database manually to ensure the track entity is deleted
            await using MusicLibraryDbContext context = await _dbContextFactory.CreateDbContextAsync();
            MediaFile? dbTrack = await context.Tracks.FirstOrDefaultAsync(t => t.Id == trackId);
            if (dbTrack != null)
            {
                context.Tracks.Remove(dbTrack);
                await context.SaveChangesAsync();
            }

            // Remove from all playlists that contain it
            foreach (Playlist playlist in Playlists)
            {
                if (playlist.TrackIds.Contains(trackId))
                {
                    playlist.TrackIds.Remove(trackId);
                    if (playlist.SelectedTrackId == trackId)
                    {
                        playlist.SelectedTrackId = null;
                    }
                }
            }
            await SaveToDatabaseAsync();
        }
    }

    public async Task RemoveTracksAsync(IEnumerable<string> trackIds)
    {
        HashSet<string> removeIds = trackIds.ToHashSet();
        if (removeIds.Count == 0) return;

        List<MediaFile> toRemove = MainLibrary.Where(t => removeIds.Contains(t.Id)).ToList();
        foreach (MediaFile track in toRemove)
            MainLibrary.Remove(track);

        foreach (Playlist playlist in Playlists)
        {
            List<string> affected = playlist.TrackIds.Where(id => removeIds.Contains(id)).ToList();
            foreach (string id in affected)
                playlist.TrackIds.Remove(id);
            if (playlist.SelectedTrackId != null && removeIds.Contains(playlist.SelectedTrackId))
                playlist.SelectedTrackId = null;
        }

        await using MusicLibraryDbContext context = await _dbContextFactory.CreateDbContextAsync();
        List<MediaFile> dbTracks = await context.Tracks
            .Where(t => removeIds.Contains(t.Id))
            .ToListAsync();
        context.Tracks.RemoveRange(dbTracks);
        await context.SaveChangesAsync();

        await SaveToDatabaseAsync();
    }

    public async Task<int> RemoveTracksFromFolderAsync(string folderPath)
    {
        // Find all tracks whose path starts with the watched folder (case-insensitive)
        string normalizedFolder = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        List<MediaFile> toRemove = MainLibrary
            .Where(t => t.Path.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (toRemove.Count == 0)
            return 0;

        HashSet<string> removeIds = toRemove.Select(t => t.Id).ToHashSet();

        // Remove from in-memory collection
        foreach (MediaFile track in toRemove)
            MainLibrary.Remove(track);

        // Remove from all playlists
        foreach (Playlist playlist in Playlists)
        {
            List<string> affected = playlist.TrackIds.Where(id => removeIds.Contains(id)).ToList();
            foreach (string id in affected)
                playlist.TrackIds.Remove(id);

            if (playlist.SelectedTrackId != null && removeIds.Contains(playlist.SelectedTrackId))
                playlist.SelectedTrackId = null;
        }

        // Batch delete from DB in one round-trip
        await using MusicLibraryDbContext context = await _dbContextFactory.CreateDbContextAsync();
        List<MediaFile> dbTracks = await context.Tracks
            .Where(t => removeIds.Contains(t.Id))
            .ToListAsync();
        context.Tracks.RemoveRange(dbTracks);
        await context.SaveChangesAsync();

        // Persist updated playlists
        await SaveToDatabaseAsync();

        _logger.LogInformation("Removed {Count} tracks from folder: {Folder}", toRemove.Count, folderPath);
        return toRemove.Count;
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

    public List<string> GetPlaylistsContainingTrack(string trackId)
    {
        return Playlists
            .Where(p => p.TrackIds.Contains(trackId))
            .Select(p => p.Name)
            .ToList();
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

    public async Task UpdateTracksAsync(IEnumerable<MediaFile> tracks, bool updateMetadata = true, bool updateAnalysis = true)
    {
        if (tracks == null)
        {
            throw new ArgumentNullException(nameof(tracks));
        }

        // Snapshot MainLibrary to prevent concurrent modification exceptions during enumeration
        List<MediaFile> mainLibrarySnapshot = MainLibrary.ToList();

        await using MusicLibraryDbContext context = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            foreach (MediaFile incoming in tracks)
            {
                if (incoming == null)
                {
                    continue;
                }

                MediaFile? existing = null;
                if (!string.IsNullOrWhiteSpace(incoming.Id))
                {
                    existing = await context.Tracks.FirstOrDefaultAsync(t => t.Id == incoming.Id);
                }

                if (existing == null && !string.IsNullOrWhiteSpace(incoming.Path))
                {
                    existing = await context.Tracks.FirstOrDefaultAsync(t => t.Path == incoming.Path);
                }

                if (existing == null)
                {
                    continue;
                }

                if (updateMetadata)
                {
                    existing.FileName = incoming.FileName;
                    existing.Title = incoming.Title;
                    existing.Artist = incoming.Artist;
                    existing.Album = incoming.Album;
                    existing.AlbumArtist = incoming.AlbumArtist;
                    existing.Performers = incoming.Performers;
                    existing.Composers = incoming.Composers;
                    existing.Genres = incoming.Genres;
                    existing.Copyright = incoming.Copyright;
                    existing.Comment = incoming.Comment;
                    existing.Track = incoming.Track;
                    existing.TrackCount = incoming.TrackCount;
                    existing.Disc = incoming.Disc;
                    existing.DiscCount = incoming.DiscCount;
                    existing.Year = incoming.Year;
                    existing.Duration = incoming.Duration;
                    existing.Bitrate = incoming.Bitrate;
                    existing.SampleRate = incoming.SampleRate;
                    existing.Channels = incoming.Channels;
                    existing.Codec = incoming.Codec;

                    existing.FileLastWriteTimeUtc = incoming.FileLastWriteTimeUtc;
                    existing.LastMetadataRefreshUtc = incoming.LastMetadataRefreshUtc;
                    existing.HasEmbeddedCover = incoming.HasEmbeddedCover;
                }

                if (updateAnalysis)
                {
                    existing.LeadingSilenceMs = incoming.LeadingSilenceMs;
                    existing.TrailingSilenceMs = incoming.TrailingSilenceMs;
                }

                // Keep in-memory library in sync with DB updates, using the snapshot
                MediaFile? inMemory = null;
                if (!string.IsNullOrWhiteSpace(incoming.Id))
                {
                    inMemory = mainLibrarySnapshot.FirstOrDefault(t => t.Id == incoming.Id);
                }

                if (inMemory == null && !string.IsNullOrWhiteSpace(incoming.Path))
                {
                    inMemory = mainLibrarySnapshot.FirstOrDefault(t => t.Path == incoming.Path);
                }

                if (inMemory != null)
                {
                    using IDisposable _suspend = inMemory.SuspendDirtyTracking();

                    if (updateMetadata)
                    {
                        inMemory.FileName = incoming.FileName;
                        inMemory.Title = incoming.Title;
                        inMemory.Artist = incoming.Artist;
                        inMemory.Album = incoming.Album;
                        inMemory.AlbumArtist = incoming.AlbumArtist;
                        inMemory.Performers = incoming.Performers;
                        inMemory.Composers = incoming.Composers;
                        inMemory.Genres = incoming.Genres;
                        inMemory.Copyright = incoming.Copyright;
                        inMemory.Comment = incoming.Comment;
                        inMemory.Track = incoming.Track;
                        inMemory.TrackCount = incoming.TrackCount;
                        inMemory.Disc = incoming.Disc;
                        inMemory.DiscCount = incoming.DiscCount;
                        inMemory.Year = incoming.Year;
                        inMemory.Duration = incoming.Duration;
                        inMemory.Bitrate = incoming.Bitrate;
                        inMemory.SampleRate = incoming.SampleRate;
                        inMemory.Channels = incoming.Channels;
                        inMemory.Codec = incoming.Codec;

                        inMemory.FileLastWriteTimeUtc = incoming.FileLastWriteTimeUtc;
                        inMemory.LastMetadataRefreshUtc = incoming.LastMetadataRefreshUtc;
                        inMemory.HasEmbeddedCover = incoming.HasEmbeddedCover;
                    }

                    if (updateAnalysis)
                    {
                        inMemory.LeadingSilenceMs = incoming.LeadingSilenceMs;
                        inMemory.TrailingSilenceMs = incoming.TrailingSilenceMs;
                    }
                }
            }

            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update tracks in database");
            throw;
        }
    }
}
