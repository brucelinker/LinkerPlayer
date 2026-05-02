using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;

namespace LinkerPlayer.Services;

public interface IWatchedFolderService
{
    /// <summary>
    /// Scans all watched folders and imports any new audio files found.
    /// </summary>
    Task ScanAllAsync(IProgress<ProgressData>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans a single folder and imports any new audio files found.
    /// </summary>
    Task ScanFolderAsync(string folderPath, IProgress<ProgressData>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full diff-scan of a single folder: removes missing tracks, updates modified tracks, imports new tracks.
    /// </summary>
    Task FullScanFolderAsync(string folderPath, IProgress<ProgressData>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full diff-scan of all watched folders.
    /// </summary>
    Task FullScanAllAsync(IProgress<ProgressData>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a folder to the watched list, persists it, and triggers an immediate scan.
    /// </summary>
    void AddFolder(string folderPath);

    /// <summary>
    /// Removes a folder from the watched list and persists it.
    /// </summary>
    void RemoveFolder(string folderPath);

    /// <summary>
    /// Removes a folder from the watched list and deletes all library tracks imported from it.
    /// Returns the number of tracks removed.
    /// </summary>
    Task<int> RemoveFolderAndTracksAsync(string folderPath);

    /// <summary>
    /// Returns the current list of watched folder paths.
    /// </summary>
    IReadOnlyList<string> WatchedFolders { get; }
}

public class WatchedFolderService : IWatchedFolderService
{
    private readonly ISettingsManager _settingsManager;
    private readonly IFileImportService _fileImportService;
    private readonly IMusicLibrary _musicLibrary;
    private readonly ILogger<WatchedFolderService> _logger;
    private readonly IRescanLogger _rescanLogger;
    private readonly SemaphoreSlim _scanSemaphore = new(1, 1);

    public WatchedFolderService(
        ISettingsManager settingsManager,
        IFileImportService fileImportService,
        IMusicLibrary musicLibrary,
        ILogger<WatchedFolderService> logger,
        IRescanLogger rescanLogger)
    {
        _settingsManager = settingsManager;
        _fileImportService = fileImportService;
        _musicLibrary = musicLibrary;
        _logger = logger;
        _rescanLogger = rescanLogger;
    }

    public IReadOnlyList<string> WatchedFolders => _settingsManager.Settings.WatchedFolders.AsReadOnly();

    public void AddFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        if (!_settingsManager.Settings.WatchedFolders.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
        {
            _settingsManager.Settings.WatchedFolders.Add(folderPath);
            _settingsManager.SaveSettings(nameof(AppSettings.WatchedFolders));
            _logger.LogInformation("Added watched folder: {Path}", folderPath);
        }
    }

    public void RemoveFolder(string folderPath)
    {
        string? existing = _settingsManager.Settings.WatchedFolders
            .FirstOrDefault(f => string.Equals(f, folderPath, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            _settingsManager.Settings.WatchedFolders.Remove(existing);
            _settingsManager.SaveSettings(nameof(AppSettings.WatchedFolders));
            _logger.LogInformation("Removed watched folder: {Path}", folderPath);
        }
    }

    public async Task<int> RemoveFolderAndTracksAsync(string folderPath)
    {
        RemoveFolder(folderPath);
        return await _musicLibrary.RemoveTracksFromFolderAsync(folderPath).ConfigureAwait(false);
    }

    public async Task ScanFolderAsync(string folderPath, IProgress<ProgressData>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!System.IO.Directory.Exists(folderPath))
        {
            _logger.LogWarning("Watched folder not found, skipping: {Path}", folderPath);
            return;
        }

        _logger.LogInformation("Scanning watched folder: {Path}", folderPath);

        List<MediaFile> imported = await _fileImportService.ImportFolderAsync(folderPath, progress, cancellationToken)
            .ConfigureAwait(false);

        foreach (MediaFile file in imported)
        {
            file.Source = TrackSource.WatchedFolder;
            file.WatchedFolderPath = folderPath;
            _rescanLogger.LogAdded(file.Path);
        }

        _logger.LogInformation("Watched folder scan complete: {Path} — {Count} new file(s) imported", folderPath, imported.Count);
    }

    public async Task ScanAllAsync(IProgress<ProgressData>? progress = null, CancellationToken cancellationToken = default)
    {
        List<string> folders = _settingsManager.Settings.WatchedFolders.ToList();

        if (folders.Count == 0)
        {
            _logger.LogInformation("No watched folders configured — skipping scan");
            return;
        }

        _logger.LogInformation("Scanning {Count} watched folder(s)", folders.Count);

        foreach (string folder in folders)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ScanFolderAsync(folder, progress, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("All watched folders scanned");
    }

    public async Task FullScanFolderAsync(string folderPath, IProgress<ProgressData>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!System.IO.Directory.Exists(folderPath))
        {
            _logger.LogWarning("Watched folder not found, skipping full scan: {Path}", folderPath);
            return;
        }

        _logger.LogInformation("Full diff-scan started: {Path}", folderPath);
        _rescanLogger.LogInfo($"Scanning: {folderPath}");

        // Snapshot to avoid "collection was modified" if library changes during scan
        List<MediaFile> snapshot = _musicLibrary.MainLibrary
            .Where(t => string.Equals(t.WatchedFolderPath, folderPath, StringComparison.OrdinalIgnoreCase)
                        || t.Path.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // --- 1. DELETES: tracks in snapshot whose file no longer exists ---
        List<MediaFile> missing = snapshot
            .Where(t => !System.IO.File.Exists(t.Path))
            .ToList();

        if (missing.Count > 0)
        {
            _logger.LogInformation("Removing {Count} missing track(s) from library", missing.Count);
            progress?.Report(new ProgressData
            {
                IsProcessing = true,
                TotalTracks = missing.Count,
                ProcessedTracks = 0,
                Status = $"Removing {missing.Count} missing track(s)…",
                Phase = "Cleanup"
            });

            foreach (MediaFile track in missing)
                _rescanLogger.LogRemoved(track.Path);

            await _musicLibrary.RemoveTracksAsync(missing.Select(t => t.Id)).ConfigureAwait(false);

            progress?.Report(new ProgressData
            {
                IsProcessing = true,
                TotalTracks = missing.Count,
                ProcessedTracks = missing.Count,
                Status = $"Removed {missing.Count} missing track(s)",
                Phase = "Cleanup"
            });
        }

        // --- 2. MODIFICATIONS: existing tracks whose file write-time has changed ---
        List<MediaFile> existing = snapshot
            .Where(t => System.IO.File.Exists(t.Path))
            .ToList();

        List<MediaFile> modified = existing
            .Where(t =>
            {
                // Skip tracks that have never had their write-time recorded —
                // FileLastWriteTimeUtc will be populated the next time the file is imported/updated.
                if (!t.FileLastWriteTimeUtc.HasValue)
                    return false;

                try
                {
                    DateTime diskTime = System.IO.File.GetLastWriteTimeUtc(t.Path);
                    return Math.Abs((diskTime - t.FileLastWriteTimeUtc.Value).TotalSeconds) > 2;
                }
                catch { return false; }
            })
            .ToList();

        if (modified.Count > 0)
        {
            _logger.LogInformation("Refreshing metadata for {Count} modified track(s)", modified.Count);
            progress?.Report(new ProgressData
            {
                IsProcessing = true,
                TotalTracks = modified.Count,
                ProcessedTracks = 0,
                Status = $"Refreshing {modified.Count} modified track(s)…",
                Phase = "Updating"
            });

            int updated = 0;
            foreach (MediaFile track in modified)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    track.UpdateFromFileMetadata(raisePropertyChanged: true);
                    _rescanLogger.LogUpdated(track.Path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh metadata for {Path}", track.Path);
                    _rescanLogger.LogWarning($"Metadata refresh failed: {track.Path} — {ex.Message}");
                }
                updated++;
                progress?.Report(new ProgressData
                {
                    IsProcessing = true,
                    TotalTracks = modified.Count,
                    ProcessedTracks = updated,
                    Status = $"Updated {updated} / {modified.Count} tracks",
                    Phase = "Updating"
                });
            }

            if (modified.Count > 0)
                await _musicLibrary.UpdateTracksAsync(modified).ConfigureAwait(false);
        }

        // --- 3. ADDS: new files on disk not yet in the library ---
        HashSet<string> knownPaths = new HashSet<string>(
            _musicLibrary.MainLibrary
                .Where(t => t.Path.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Path),
            StringComparer.OrdinalIgnoreCase);

        List<string> newFiles = _fileImportService.GetAudioFilesFromFolder(folderPath)
            .Where(f => !knownPaths.Contains(f))
            .ToList();

        int added = 0;
        if (newFiles.Count > 0)
        {
            _logger.LogInformation("Adding {Count} new file(s) found in {Path}", newFiles.Count, folderPath);
            progress?.Report(new ProgressData
            {
                IsProcessing = true,
                TotalTracks = newFiles.Count,
                ProcessedTracks = 0,
                Status = $"Adding {newFiles.Count} new file(s)…",
                Phase = "Adding"
            });

            List<MediaFile> imported = await _fileImportService.ImportFilesAsync(
                newFiles.ToArray(), progress: null, cancellationToken).ConfigureAwait(false);

            foreach (MediaFile file in imported)
            {
                file.Source = TrackSource.WatchedFolder;
                file.WatchedFolderPath = folderPath;
                _rescanLogger.LogAdded(file.Path);
            }

            added = imported.Count;

            progress?.Report(new ProgressData
            {
                IsProcessing = true,
                TotalTracks = newFiles.Count,
                ProcessedTracks = added,
                Status = $"Added {added} new file(s)",
                Phase = "Adding"
            });
        }

        _rescanLogger.LogInfo($"{System.IO.Path.GetFileName(folderPath.TrimEnd('\\', '/'))}: done  —  added={added}  removed={missing.Count}  updated={modified.Count}");
        _logger.LogInformation("Full diff-scan complete: {Path}  added={Add}  deleted={Del}  modified={Mod}",
            folderPath, added, missing.Count, modified.Count);
    }

    public async Task FullScanAllAsync(IProgress<ProgressData>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!await _scanSemaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Full diff-scan already in progress — skipping concurrent request");
            return;
        }

        try
        {
            List<string> folders = _settingsManager.Settings.WatchedFolders.ToList();

            if (folders.Count == 0)
            {
                _logger.LogInformation("No watched folders configured — skipping full scan");
                return;
            }

            _logger.LogInformation("Full diff-scan of {Count} watched folder(s)", folders.Count);
            _rescanLogger.BeginSession();

            foreach (string folder in folders)
            {
                if (cancellationToken.IsCancellationRequested) break;
                await FullScanFolderAsync(folder, progress, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Full diff-scan of all watched folders complete");

            _rescanLogger.EndSession();

            progress?.Report(new ProgressData
            {
                IsProcessing = false,
                TotalTracks = 0,
                ProcessedTracks = 0,
                Status = "Rescan complete",
                Phase = "Done"
            });
        }
        finally
        {
            _scanSemaphore.Release();
        }
    }
}
