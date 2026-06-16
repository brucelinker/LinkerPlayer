using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.IO;

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

public class WatchedFolderService : IWatchedFolderService, IDisposable
{
    private readonly ISettingsManager _settingsManager;
    private readonly IFileImportService _fileImportService;
    private readonly IMusicLibrary _musicLibrary;
    private readonly IAudioEngine _audioEngine;
    private readonly ILogger<WatchedFolderService> _logger;
    private readonly IRescanLogger _rescanLogger;
    private readonly SemaphoreSlim _scanSemaphore = new(1, 1);
    private readonly SemaphoreSlim _fswProcessSemaphore = new(1, 1);
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.Timer> _debounceTimers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);
    private bool _disposed;

    public WatchedFolderService(
        ISettingsManager settingsManager,
        IFileImportService fileImportService,
        IMusicLibrary musicLibrary,
        IAudioEngine audioEngine,
        ILogger<WatchedFolderService> logger,
        IRescanLogger rescanLogger)
    {
        _settingsManager = settingsManager;
        _fileImportService = fileImportService;
        _musicLibrary = musicLibrary;
        _audioEngine = audioEngine;
        _logger = logger;
        _rescanLogger = rescanLogger;

        InitializeWatchers();
    }

    private void InitializeWatchers()
    {
        foreach (string folder in WatchedFolders)
        {
            if (System.IO.Directory.Exists(folder))
            {
                FileSystemWatcher watcher = new(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
                    InternalBufferSize = 65536, // 64 KB — reduces dropped events on busy NAS shares
                    EnableRaisingEvents = true
                };
                watcher.Changed += OnFileChanged;
                watcher.Created += OnFileChanged;
                watcher.Deleted += OnFileChanged;
                watcher.Renamed += OnFileRenamed;
                _watchers.Add(watcher);
            }
        }
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

            // Add FileSystemWatcher
            FileSystemWatcher watcher = new(folderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
                InternalBufferSize = 65536, // 64 KB — reduces dropped events on busy NAS shares
                EnableRaisingEvents = true
            };
            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileDeleted;
            watcher.Renamed += OnFileRenamed;
            _watchers.Add(watcher);
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

            // Remove and dispose FileSystemWatcher
            FileSystemWatcher? watcher = _watchers.FirstOrDefault(w => string.Equals(w.Path, folderPath, StringComparison.OrdinalIgnoreCase));
            if (watcher != null)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _watchers.Remove(watcher);
            }
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
                    long diskSize = new FileInfo(t.Path).Length;
                    return Math.Abs((diskTime - t.FileLastWriteTimeUtc.Value).TotalSeconds) > 10 ||
                           t.FileSize != diskSize;
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
                    // Guard against files that vanished between enumeration and the read —
                    // common on UNC/NAS shares. UpdateFromFileMetadata handles this too,
                    // but avoiding the throw entirely is cleaner and quieter in the debugger.
                    if (!File.Exists(track.Path))
                    {
                        track.HealthStatus = TrackHealthStatus.Missing;
                        _logger.LogWarning("File no longer exists during scan, marking missing: {Path}", track.Path);
                        _rescanLogger.LogWarning($"File missing during scan: {track.Path}");
                        continue;
                    }

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

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed || !IsAudioFile(e.FullPath))
            return;

        // Debounce: reset the timer each time an event fires for this path.
        // The callback runs only after DebounceDelay of silence on that file.
        _debounceTimers.AddOrUpdate(
            e.FullPath,
            _ => new System.Threading.Timer(OnDebounceElapsed, e.FullPath, DebounceDelay, System.Threading.Timeout.InfiniteTimeSpan),
            (_, existing) =>
            {
                existing.Change(DebounceDelay, System.Threading.Timeout.InfiniteTimeSpan);
                return existing;
            });
    }

    private void OnDebounceElapsed(object? state)
    {
        if (_disposed || state is not string path)
            return;

        if (_debounceTimers.TryRemove(path, out System.Threading.Timer? t))
            t.Dispose();

        // Serialize FSW-triggered refreshes so they don't pile up and saturate I/O
        _ = Task.Run(async () =>
        {
            if (_disposed)
                return;

            if (!await _fswProcessSemaphore.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false))
                return;

            try
            {
                if (_disposed)
                    return;

                MediaFile? track = _musicLibrary.MainLibrary
                    .FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));

                if (track != null)
                {
                    // Guard against the file being deleted during the debounce window.
                    if (!File.Exists(path))
                    {
                        track.HealthStatus = TrackHealthStatus.Missing;
                        await _musicLibrary.UpdateTracksAsync(new[] { track }).ConfigureAwait(false);
                        _logger.LogInformation("File removed during debounce window, marked missing: {Path}", path);
                        return;
                    }

                    // BASS holds an exclusive file lock for the lifetime of playback.
                    // ATL cannot open the same file — skip the refresh; metadata is current.
                    if (string.Equals(_audioEngine.LoadedTrackPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Metadata refresh skipped — file in use by BASS: {Path}", path);
                        return;
                    }

                    // An external process (tag editor, Windows Search, etc.) may briefly hold
                    // the file after writing it. Retry with the same pattern as TrackMetadataRefresher.
                    const int maxRetries = 5;
                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        try
                        {
                            track.UpdateFromFileMetadata(raisePropertyChanged: true);
                            await _musicLibrary.UpdateTracksAsync(new[] { track }).ConfigureAwait(false);
                            _logger.LogInformation("Auto-updated metadata for modified file: {Path}", path);
                            break;
                        }
                        catch (IOException) when (attempt < maxRetries)
                        {
                            _logger.LogWarning("Metadata read attempt {Attempt}/{Max} failed for {Path} — retrying in 500ms",
                                attempt, maxRetries, path);
                            await Task.Delay(500).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-update metadata for {Path}", path);
            }
            finally
            {
                _fswProcessSemaphore.Release();
            }
        });
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (_disposed || !IsAudioFile(e.FullPath))
            return;

        // Cancel any pending debounce for this path — no metadata refresh needed.
        if (_debounceTimers.TryRemove(e.FullPath, out System.Threading.Timer? t))
            t.Dispose();

        _ = Task.Run(async () =>
        {
            if (_disposed)
                return;

            try
            {
                MediaFile? track = _musicLibrary.MainLibrary
                    .FirstOrDefault(t => string.Equals(t.Path, e.FullPath, StringComparison.OrdinalIgnoreCase));

                if (track != null)
                {
                    track.HealthStatus = TrackHealthStatus.Missing;
                    await _musicLibrary.UpdateTracksAsync(new[] { track }).ConfigureAwait(false);
                    _logger.LogInformation("File deleted, marked missing in library: {Path}", e.FullPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to handle deletion for {Path}", e.FullPath);
            }
        });
    }

    private void OnDirectoryRenamed(string oldPath, string newPath)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Update the Path (and WatchedFolderPath when it matches) of every track
                // whose path sits under the renamed directory. Use a char-after check so
                // "Rock" doesn't accidentally match "Rock and Roll".
                List<MediaFile> affected = _musicLibrary.MainLibrary
                    .Where(t => t.Path.Length > oldPath.Length &&
                                t.Path.StartsWith(oldPath, StringComparison.OrdinalIgnoreCase) &&
                                (t.Path[oldPath.Length] == System.IO.Path.DirectorySeparatorChar ||
                                 t.Path[oldPath.Length] == '/'))
                    .ToList();

                if (affected.Count == 0)
                    return;

                foreach (MediaFile track in affected)
                {
                    track.Path = newPath + track.Path.Substring(oldPath.Length);

                    // If the renamed dir IS the watched root, update that too.
                    if (string.Equals(track.WatchedFolderPath, oldPath, StringComparison.OrdinalIgnoreCase))
                        track.WatchedFolderPath = newPath;
                }

                await _musicLibrary.UpdateTracksAsync(affected).ConfigureAwait(false);
                _logger.LogInformation("Directory renamed: {OldPath} -> {NewPath}, updated {Count} track path(s)",
                    oldPath, newPath, affected.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to handle directory rename {OldPath} -> {NewPath}", oldPath, newPath);
            }
        });
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // A directory rename fires here too (NotifyFilters.DirectoryName).
        // Route it to the dedicated handler so all child track paths stay current.
        if (Directory.Exists(e.FullPath))
        {
            OnDirectoryRenamed(e.OldFullPath, e.FullPath);
            return;
        }

        if (!IsAudioFile(e.FullPath))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                MediaFile? track = _musicLibrary.MainLibrary.FirstOrDefault(t => string.Equals(t.Path, e.OldFullPath, StringComparison.OrdinalIgnoreCase));
                if (track != null)
                {
                    track.Path = e.FullPath;
                    track.FileName = System.IO.Path.GetFileName(e.FullPath);

                    // BASS holds an exclusive file lock for the lifetime of playback.
                    // The path is already updated above — that's all we need for the playing track.
                    if (string.Equals(_audioEngine.LoadedTrackPath, e.FullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        await _musicLibrary.UpdateTracksAsync(new[] { track }).ConfigureAwait(false);
                        _logger.LogInformation("Renamed playing track path updated (metadata refresh skipped — file in use by BASS): {NewPath}", e.FullPath);
                        return;
                    }

                    // The process that triggered the rename (e.g. a tag editor or the OS on a
                    // UNC/NAS share) may still hold the file handle briefly. Retry with a short
                    // delay, matching the same pattern used by TrackMetadataRefresher.
                    const int maxRetries = 5;
                    bool refreshed = false;
                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        try
                        {
                            track.UpdateFromFileMetadata(raisePropertyChanged: true);
                            refreshed = true;
                            break;
                        }
                        catch (IOException) when (attempt < maxRetries)
                        {
                            _logger.LogWarning("Metadata read attempt {Attempt}/{Max} failed for renamed file {Path} — retrying in 500ms",
                                attempt, maxRetries, e.FullPath);
                            await Task.Delay(500).ConfigureAwait(false);
                        }
                    }

                    if (refreshed)
                    {
                        await _musicLibrary.UpdateTracksAsync(new[] { track }).ConfigureAwait(false);
                        _logger.LogInformation("Auto-updated metadata for renamed file: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);
                    }
                    else
                    {
                        _logger.LogWarning("Giving up metadata refresh for renamed file after {Max} attempts: {Path}", maxRetries, e.FullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-update metadata for renamed file {Path}", e.FullPath);
            }
        });
    }

    private static bool IsAudioFile(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp3" or ".flac" or ".ape" or ".ac3" or ".dsd" or ".dsf" or ".dts"
                   or ".m4a" or ".mka" or ".mp4" or ".mpc" or ".ofr" or ".ogg" or ".opus"
                   or ".wav" or ".wma" or ".wv";
    }

    public void Dispose()
    {
        _disposed = true;

        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();

        foreach (System.Threading.Timer t in _debounceTimers.Values)
            t.Dispose();
        _debounceTimers.Clear();

        _fswProcessSemaphore.Dispose();
    }
}
