using ATL;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Database;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using System.Collections.Generic;

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
    Task LoadFullLibraryAsync();
    void MarkLibraryDirty();
    Task CleanOrphanedTracksAsync();
    Task UpdateTracksAsync(IEnumerable<MediaFile> tracks, bool updateMetadata = true, bool updateAnalysis = true);
    Task UpdateRatingAsync(string trackId, double rating);
    Task BackfillEmbeddedCoverAsync();
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
    public static string[] _supportedAudioExtensions = new[] { ".mp3", ".flac", ".ape", ".ac3", ".dsd", ".dsf", ".dts", ".m4a", ".mka", ".mp4", ".mpc", ".ofr", ".ogg", ".opus", ".wav", ".wma", ".wv" };

    private readonly object _mainLibraryLock = new();
    private readonly object _playlistsLock = new();
    private readonly object _batchAddLock = new();  // Serialize batch UI adds to prevent overlapping CollectionChanged events

    private readonly DispatcherTimer _autoSaveTimer = new()
    {
        Interval = TimeSpan.FromSeconds(8)
    };

    private bool _hasUnsavedLibraryChanges;

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

                    List<int> ratingResult = context.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) FROM pragma_table_info('Tracks') WHERE name='Rating'").ToList();
                    if (ratingResult.FirstOrDefault() == 0)
                    {
                        context.Database.ExecuteSqlRaw("ALTER TABLE Tracks ADD COLUMN \"Rating\" REAL NOT NULL DEFAULT 0.0;");
                        _logger.LogInformation("Added Rating column to Tracks table");
                    }

                    List<int> replayGainResult = context.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) FROM pragma_table_info('Tracks') WHERE name='ReplayGain'").ToList();
                    if (replayGainResult.FirstOrDefault() == 0)
                    {
                        context.Database.ExecuteSqlRaw("ALTER TABLE Tracks ADD COLUMN \"ReplayGain\" TEXT NOT NULL DEFAULT ''; ");
                        _logger.LogInformation("Added ReplayGain column to Tracks table");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking/adding analysis/metadata columns to Tracks table");
                }

                // One-time fix: re-read artist fields for all tracks now that comma is no longer
                // treated as a multi-artist separator (fixes names like "10,000 Maniacs").
                try
                {
                    List<int> artistFixResult = context.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) FROM pragma_table_info('Tracks') WHERE name='ArtistParserV2Applied'").ToList();
                    if (artistFixResult.FirstOrDefault() == 0)
                    {
                        context.Database.ExecuteSqlRaw("ALTER TABLE Tracks ADD COLUMN \"ArtistParserV2Applied\" INTEGER NOT NULL DEFAULT 0;");
                        context.Database.ExecuteSqlRaw("UPDATE Tracks SET NeedsMetadataRefresh = 1;");
                        _logger.LogInformation("Artist parser fix: scheduled full library metadata refresh");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error applying artist parser v2 migration");
                }
            }

            _autoSaveTimer.Tick += async (s, e) => await AutoSaveIfNeededAsync();
            _autoSaveTimer.Start();
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
            // Load tracks
            List<MediaFile> tracks = await context.Tracks.AsNoTracking().ToListAsync();

            // Load playlists
            List<Playlist> playlists = await context.Playlists
                .Include(p => p.PlaylistTracks)
                .Include(p => p.SelectedTrackNavigation)
                .AsNoTracking()
                .OrderBy(p => p.Order)
                .ToListAsync();

            Playlists.Clear();
            MainLibrary.Clear();

            // Add tracks
            foreach (MediaFile track in tracks)
            {
                track.EnableDirtyTracking();
            }
            MainLibrary.AddRange(tracks);

            // Process playlists
            foreach (Playlist playlist in playlists)
            {
                List<string> validTrackIds = playlist.PlaylistTracks
                    .Where(pt => pt.TrackId != null && MainLibrary.Any(t => t.Id == pt.TrackId))
                    .OrderBy(pt => pt.Position)
                    .Select(pt => pt.TrackId!)
                    .ToList();

                playlist.TrackIds = new ObservableCollection<string>(validTrackIds);

                if (playlist.SelectedTrackId != null && !validTrackIds.Contains(playlist.SelectedTrackId))
                {
                    playlist.SelectedTrackId = null;
                }

                if (playlist.SelectedTrackId == null && playlist.TrackIds.Any())
                {
                    playlist.SelectedTrackId = playlist.TrackIds.First();
                }

                Playlists.Add(playlist);
            }

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

            // Notify UI
            Application.Current?.Dispatcher.BeginInvoke(() => LibraryLoaded?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load data from database");
            throw;
        }
    }

    public async Task LoadFullLibraryAsync()
    {
        await PerformBackfillsAsync();
    }

    private async Task PerformBackfillsAsync()
    {
        await BackfillEmbeddedCoverAsync();
        await BackfillMp3VbrAsync();
        await BackfillReplayGainAsync();
    }

    public void MarkLibraryDirty()
    {
        _hasUnsavedLibraryChanges = true;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();        // Reset debounce
    }

    private async Task AutoSaveIfNeededAsync()
    {
        if (!_hasUnsavedLibraryChanges)
            return;

        _hasUnsavedLibraryChanges = false;
        try
        {
            await SaveToDatabaseAsync();
            _logger.LogDebug("Auto-saved library changes");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-save failed");
        }
    }

    /// <summary>
    /// One-time background pass that sets <see cref="MediaFile.HasEmbeddedCover"/> for any
    /// tracks that were loaded from the DB before the column existed (i.e. value is false).
    /// Only reads the ATL picture-list count — no image bytes are decoded.
    /// </summary>
    public async Task BackfillEmbeddedCoverAsync()
    {
        try
        {
            List<MediaFile> tracksToCheck = MainLibrary
                .Where(t => t.HasEmbeddedCover == false)
                .ToList();

            if (tracksToCheck.Count == 0)
            {
                _logger.LogInformation("BackfillEmbeddedCover: no tracks with embedded covers found");
                return;
            }

            _logger.LogInformation("Backfilling HasEmbeddedCover for {Count} tracks…", tracksToCheck.Count);

            int processed = 0;
            const int batchSize = 1000;   // larger batch = fewer context switches

            for (int i = 0; i < tracksToCheck.Count; i += batchSize)
            {
                List<MediaFile> batch = tracksToCheck.Skip(i).Take(batchSize).ToList();

                await Task.Run(() =>
                {
                    foreach (MediaFile track in batch)
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(track.Path) || !File.Exists(track.Path))
                                continue;

                            ATL.Track atlTrack = new ATL.Track(track.Path);
                            track.HasEmbeddedCover = atlTrack.EmbeddedPictures?.Any() == true;
                        }
                        catch
                        {
                            // Ignore
                        }
                    }
                });

                processed += batch.Count;
                _logger.LogInformation("Backfilling embedded covers... {Processed}/{Total}", processed, tracksToCheck.Count);

                await Task.Delay(5);   // very small yield
            }

            await UpdateTracksAsync(tracksToCheck, updateMetadata: false, updateAnalysis: false);

            _logger.LogInformation("Embedded cover backfill complete");
            _logger.LogInformation("BackfillEmbeddedCover completed for {Count} tracks", tracksToCheck.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BackfillEmbeddedCoverAsync failed");
            _logger.LogError("Embedded cover backfill failed");
        }
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
                if (cancellationToken.IsCancellationRequested)
                    break;

                List<(string Id, string Codec)> batch = updateList.GetRange(i, Math.Min(dbBatchSize, updateList.Count - i));
                foreach ((string id, string codec) in batch)
                    context.Database.ExecuteSqlRaw(
                        "UPDATE Tracks SET Codec = @codec WHERE Id = @id", codec, id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BackfillMp3Vbr: failed to persist to DB");
        }

        _logger.LogInformation("BackfillMp3Vbr complete");
    }

    private async Task BackfillReplayGainAsync(CancellationToken cancellationToken = default)
    {
        List<MediaFile> needsBackfill = MainLibrary
            .Where(t => string.IsNullOrWhiteSpace(t.ReplayGain))
            .ToList();

        if (needsBackfill.Count == 0)
            return;

        _logger.LogInformation("BackfillReplayGain: probing {Count} tracks…", needsBackfill.Count);

        const int dbBatchSize = 500;
        SemaphoreSlim semaphore = new(64, 64);
        System.Collections.Concurrent.ConcurrentBag<(string Id, string ReplayGain)> updates = new();

        static string NormalizeReplayGainKey(string key) =>
            new string(key.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        static bool HasReplayGainGainField(ATL.Track atlTrack, string scope)
        {
            string replayGainGain = $"REPLAYGAIN{scope}GAIN";

            return atlTrack.AdditionalFields.Any(kv =>
            {
                if (string.IsNullOrWhiteSpace(kv.Value))
                    return false;

                string normalized = NormalizeReplayGainKey(kv.Key);
                return normalized.Equals(replayGainGain, StringComparison.Ordinal) ||
                       normalized.EndsWith(replayGainGain, StringComparison.Ordinal);
            });
        }

        IEnumerable<Task> probeTasks = needsBackfill.Select(async track =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (string.IsNullOrWhiteSpace(track.Path) || !File.Exists(track.Path))
                    return;

                ATL.Track atlTrack = new(track.Path);

                bool hasTrackReplayGain = HasReplayGainGainField(atlTrack, "TRACK");
                bool hasAlbumReplayGain = HasReplayGainGainField(atlTrack, "ALBUM");

                string replayGain = hasAlbumReplayGain ? "Album" : hasTrackReplayGain ? "Track" : string.Empty;
                if (!string.IsNullOrEmpty(replayGain))
                    updates.Add((track.Id, replayGain));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "BackfillReplayGain: skipping {Path}", track.Path);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(probeTasks).ConfigureAwait(false);

        if (updates.IsEmpty)
            return;

        List<(string Id, string ReplayGain)> updateList = updates.ToList();
        _logger.LogInformation("BackfillReplayGain: persisting {Count} tracks in batches of {Batch}",
            updateList.Count, dbBatchSize);

        try
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach ((string id, string replayGain) in updateList)
                    {
                        MediaFile? inMemory = MainLibrary.FirstOrDefault(t => t.Id == id);
                        if (inMemory != null)
                            inMemory.ReplayGain = replayGain;
                    }
                });
            }
            else
            {
                foreach ((string id, string replayGain) in updateList)
                {
                    MediaFile? inMemory = MainLibrary.FirstOrDefault(t => t.Id == id);
                    if (inMemory != null)
                        inMemory.ReplayGain = replayGain;
                }
            }

            await using MusicLibraryDbContext context = await _dbContextFactory.CreateDbContextAsync();
            for (int i = 0; i < updateList.Count; i += dbBatchSize)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                List<(string Id, string ReplayGain)> batch = updateList.GetRange(i, Math.Min(dbBatchSize, updateList.Count - i));
                foreach ((string id, string replayGain) in batch)
                    context.Database.ExecuteSqlRaw(
                        "UPDATE Tracks SET ReplayGain = @replayGain WHERE Id = @id", replayGain, id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BackfillReplayGain: failed to persist to DB");
        }

        _logger.LogInformation("BackfillReplayGain complete");
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

            // Build a single path→id map from the DB in one round-trip, then fix up
            // any in-memory tracks whose Id hasn't been assigned yet (e.g. freshly cloned
            // stubs that were inserted by SaveTracksBatchAsync just before this call).
            // This replaces the previous O(N) per-track FirstOrDefaultAsync loop.
            List<(string Path, string Id)> dbPathIds = (await context.Tracks
                .AsNoTracking()
                .Select(t => new { t.Path, t.Id })
                .ToListAsync())
                .Select(r => (r.Path, r.Id))
                .ToList();

            Dictionary<string, string> pathToId = dbPathIds
                .GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            foreach (MediaFile track in MainLibrary)
            {
                if (pathToId.TryGetValue(track.Path, out string? dbId))
                    track.Id = dbId;
            }

            // Get all valid track IDs from the database
            HashSet<string> validTrackIdsSet = new HashSet<string>(pathToId.Values);

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

                // Validate TrackIds and remove duplicates (PlaylistTrack PK is PlaylistId+TrackId)
                List<string> validTrackIds = playlist.TrackIds
                    .Where(id => validTrackIdsSet.Contains(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                playlist.TrackIds = new ObservableCollection<string>(validTrackIds);

                Playlist? existingPlaylist = await context.Playlists
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

                    // Remove existing rows without tracking them, so re-adding the same keys in this
                    // context does not trigger EF identity-map conflicts.
                    await context.PlaylistTracks
                        .Where(pt => pt.PlaylistId == playlistId)
                        .ExecuteDeleteAsync();

                    playlist.Id = playlistId;
                }

                // Rebuild PlaylistTracks for this playlist from the current in-memory TrackIds.
                // Existing links were removed above, so we must re-add all desired links.
                for (int i = 0; i < playlist.TrackIds.Count; i++)
                {
                    string trackId = playlist.TrackIds[i];

                    context.PlaylistTracks.Add(new PlaylistTrack
                    {
                        PlaylistId = playlistId,
                        TrackId = trackId,
                        Position = i
                    });
                }
                await context.SaveChangesAsync();
                context.ChangeTracker.AutoDetectChangesEnabled = true;
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
                !string.IsNullOrEmpty(mediaFile.FileName) &&
                File.Exists(mediaFile.Path) &&
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
        List<MediaFile> tracksToAdd = new();

        HashSet<string> existingPaths;
        lock (_mainLibraryLock)
        {
            existingPaths = MainLibrary
                .Where(t => !string.IsNullOrWhiteSpace(t.Path))
                .Select(t => t.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        foreach (MediaFile mediaFile in mediaFiles)
        {
            if (!string.IsNullOrWhiteSpace(mediaFile.Path) &&
                File.Exists(mediaFile.Path) &&
                _supportedAudioExtensions.Any(ext =>
                    ext.Equals(Path.GetExtension(mediaFile.Path), StringComparison.OrdinalIgnoreCase)) &&
                existingPaths.Add(mediaFile.Path))
            {
                tracksToAdd.Add(mediaFile.Clone());
            }
        }

        if (tracksToAdd.Count == 0)
            return Task.CompletedTask;

        // Add without clearing the entire library!
        if (Application.Current?.Dispatcher is Dispatcher dispatcher)
        {
            if (dispatcher.CheckAccess())
            {
                MainLibrary.AddRange(tracksToAdd);
            }
            else
            {
                dispatcher.BeginInvoke(
                    () => MainLibrary.AddRange(tracksToAdd),
                    DispatcherPriority.Background);
            }
        }
        else
        {
            MainLibrary.AddRange(tracksToAdd);
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
        if (removeIds.Count == 0)
            return;

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

        // Take a snapshot to avoid collection modified exceptions during enumeration
        List<MediaFile> librarySnapshot = MainLibrary.ToList();

        return playlist.TrackIds
            .Select(trackId => librarySnapshot.FirstOrDefault(p => p.Id == trackId))
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
                    existing.ReplayGain = incoming.ReplayGain;

                    existing.FileLastWriteTimeUtc = incoming.FileLastWriteTimeUtc;
                    existing.LastMetadataRefreshUtc = incoming.LastMetadataRefreshUtc;
                    existing.HasEmbeddedCover = incoming.HasEmbeddedCover;
                    existing.NeedsMetadataRefresh = false;
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
                    using IDisposable _notify = inMemory.SuspendNotifications();

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
                        inMemory.ReplayGain = incoming.ReplayGain;

                        inMemory.FileLastWriteTimeUtc = incoming.FileLastWriteTimeUtc;
                        inMemory.LastMetadataRefreshUtc = incoming.LastMetadataRefreshUtc;
                        inMemory.HasEmbeddedCover = incoming.HasEmbeddedCover;
                        inMemory.NeedsMetadataRefresh = false;
                    }

                    if (updateAnalysis)
                    {
                        inMemory.LeadingSilenceMs = incoming.LeadingSilenceMs;
                        inMemory.TrailingSilenceMs = incoming.TrailingSilenceMs;
                    }
                }
            }

            MarkLibraryDirty();
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update tracks in database");
            throw;
        }
    }

    public async Task UpdateRatingAsync(string trackId, double rating)
    {
        if (string.IsNullOrWhiteSpace(trackId))
            return;

        double clamped = Math.Round(Math.Clamp(rating, 0.0, 5.0), 1);

        // Update in-memory
        MediaFile? inMemory = MainLibrary.FirstOrDefault(t => t.Id == trackId);
        if (inMemory != null)
        {
            using IDisposable _ = inMemory.SuspendDirtyTracking();
            inMemory.Rating = clamped;
        }

        // Update database
        await using MusicLibraryDbContext context = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            MediaFile? track = await context.Tracks.FirstOrDefaultAsync(t => t.Id == trackId);
            if (track != null)
            {
                track.Rating = clamped;
                await context.SaveChangesAsync();
                //_logger.LogDebug("Updated rating for track {TrackId} to {Rating}", trackId, clamped);
            }
            else
            {
                _logger.LogWarning("Track {TrackId} not found in database during rating update", trackId);
            }

            MarkLibraryDirty();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update rating for track {TrackId}", trackId);
            throw;
        }
    }
}
