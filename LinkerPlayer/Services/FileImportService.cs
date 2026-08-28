using LinkerPlayer.BassLibs;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.IO;
using static ATL.ChannelsArrangements;

namespace LinkerPlayer.Services;

public interface IFileImportService
{
    /// <summary>
    /// Imports files or folders and returns the imported tracks
    /// </summary>
    /// <param name="filePaths">Array of file or folder paths to import</param>
    /// <param name="progress">Progress reporting interface</param>
    /// <returns>List of successfully imported MediaFiles</returns>
    Task<List<MediaFile>> ImportFilesAsync(string[] filePaths, IProgress<ProgressData>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports all audio files from a folder recursively
    /// </summary>
    /// <param name="folderPath">Path to the folder to import</param>
    /// <param name="progress">Progress reporting interface</param>
    /// <returns>List of successfully imported MediaFiles</returns>
    Task<List<MediaFile>> ImportFolderAsync(string folderPath, IProgress<ProgressData>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a single audio file
    /// </summary>
    /// <param name="filePath">Path to the audio file</param>
    /// <returns>The imported MediaFile or null if import failed</returns>
    Task<MediaFile?> ImportFileAsync(string filePath);

    /// <summary>
    /// Checks if a file is a supported audio format
    /// </summary>
    /// <param name="path">File path to check</param>
    /// <returns>True if the file is a supported audio format</returns>
    bool IsAudioFile(string path);

    /// <summary>
    /// Gets the count of audio files in a folder recursively
    /// </summary>
    /// <param name="folderPath">Path to the folder</param>
    /// <returns>Number of audio files found</returns>
    int GetAudioFileCount(string folderPath);

    /// <summary>
    /// Returns the paths of all audio files in a folder recursively
    /// </summary>
    /// <param name="folderPath">Path to the folder</param>
    /// <returns>List of audio file paths</returns>
    List<string> GetAudioFilesFromFolder(string folderPath);
}

public class FileImportService : IFileImportService
{
    private readonly IMusicLibrary _musicLibrary;
    private readonly ILogger<FileImportService> _logger;
    private readonly IImportErrorLogger _importErrorLogger;
    private readonly IImportCancellationService _importCancellationService;


    public FileImportService(IMusicLibrary musicLibrary, ILogger<FileImportService> logger, IImportErrorLogger importErrorLogger, IImportCancellationService importCancellationService)
    {
        _musicLibrary = musicLibrary ?? throw new ArgumentNullException(nameof(musicLibrary));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _importErrorLogger = importErrorLogger ?? throw new ArgumentNullException(nameof(importErrorLogger));
        _importCancellationService = importCancellationService ?? throw new ArgumentNullException(nameof(importCancellationService));
    }

    public async Task<List<MediaFile>> ImportFilesAsync(string[] filePaths, IProgress<ProgressData>? progress = null, CancellationToken cancellationToken = default)
    {
        List<MediaFile> importedFiles = new List<MediaFile>();

        if (filePaths == null || filePaths.Length == 0)
        {
            _logger.LogWarning("ImportFilesAsync called with null or empty file paths");
            return importedFiles;
        }

        _logger.LogInformation("Starting import of {Count} items", filePaths.Length);

        // Separate files from folders for better progress tracking
        List<string> files = filePaths.Where(path => File.Exists(path) && IsAudioFile(path)).ToList();
        List<string> folders = filePaths.Where(Directory.Exists).ToList();
        int totalItems = files.Count + folders.Count;
        int processedItems = 0;

        // Report initial progress
        progress?.Report(new ProgressData
        {
            IsProcessing = true,
            TotalTracks = totalItems,
            ProcessedTracks = 0,
            Status = "Starting file import...",
            Phase = "Importing"
        });

        // Process individual files in parallel with batching to speed up network imports
        const int degreeOfParallelism = 4;
        const int batchSize = 100;
        const int progressInterval = 25;

        System.Threading.SemaphoreSlim sem = new System.Threading.SemaphoreSlim(degreeOfParallelism);
        List<Task> tasks = new List<Task>();
        List<MediaFile> pendingSaves = new List<MediaFile>();
        object pendingLock = new object();

        int processed = 0;

        foreach (string filePath in files)
        {
            _logger.LogDebug("Attempting to import: {FilePath}", filePath);

            if (cancellationToken.IsCancellationRequested)
                break;

            if (!await _importCancellationService.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false))
                break;

            try
            {
                await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            Task t = Task.Run(async () =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    string fileName = Path.GetFileName(filePath);

                    // Throttle UI updates slightly
                    if (processed % progressInterval == 0)
                    {
                        progress?.Report(new ProgressData
                        {
                            IsProcessing = true,
                            TotalTracks = totalItems,
                            ProcessedTracks = processed,
                            Status = $"Importing: {fileName}",
                            Phase = "Importing"
                        });
                        await Task.Delay(25).ConfigureAwait(false);
                    }

                    MediaFile? importedFile = await ImportFileAsync(filePath).ConfigureAwait(false);
                    if (importedFile != null)
                    {
                        lock (pendingLock)
                        {
                            pendingSaves.Add(importedFile);
                        }
                        importedFiles.Add(importedFile);
                    }

                    int current = System.Threading.Interlocked.Increment(ref processed);
                    if (current % progressInterval == 0)
                    {
                        progress?.Report(new ProgressData
                        {
                            IsProcessing = true,
                            TotalTracks = totalItems,
                            ProcessedTracks = current,
                            Status = $"Imported: {fileName}",
                            Phase = "Importing"
                        });
                    }

                    // Flush batch when threshold reached
                    List<MediaFile>? toSave = null;
                    lock (pendingLock)
                    {
                        if (pendingSaves.Count >= batchSize)
                        {
                            toSave = pendingSaves.ToList();
                            pendingSaves.Clear();
                        }
                    }

                    if (toSave != null && toSave.Count > 0)
                    {
                        try
                        {
                            // Add to MainLibrary
                            await _musicLibrary.AddTracksToLibraryBatchAsync(toSave).ConfigureAwait(false);
                            // Save to DB
                            await _musicLibrary.SaveTracksBatchAsync(toSave).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to add/save batch");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to import file: {Path}", filePath);
                }
                finally
                {
                    sem.Release();
                }
            });
            tasks.Add(t);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Final flush of any remaining pending saves
        List<MediaFile> finalBatch;
        lock (pendingLock)
        {
            finalBatch = pendingSaves.ToList();
            pendingSaves.Clear();
        }
        if (finalBatch.Count > 0)
        {
            try
            {
                // Add to MainLibrary
                await _musicLibrary.AddTracksToLibraryBatchAsync(finalBatch).ConfigureAwait(false);
                // Save to DB
                await _musicLibrary.SaveTracksBatchAsync(finalBatch).ConfigureAwait(false);
                // Enqueue background metadata refresh for the final batch
                try
                {
                    LinkerPlayer.Services.Metadata.BackgroundMetadataRefresher.Enqueue(finalBatch);
                }
                catch { }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add/save final batch");
            }
        }

        // Process folders
        foreach (string folderPath in folders)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                string folderName = Path.GetFileName(folderPath);
                progress?.Report(new ProgressData
                {
                    IsProcessing = true,
                    TotalTracks = totalItems,
                    ProcessedTracks = processedItems,
                    Status = $"Processing folder: {folderName}",
                    Phase = "Importing"
                });

                List<MediaFile> folderFiles = await ImportFolderAsync(folderPath, progress, cancellationToken).ConfigureAwait(false);
                importedFiles.AddRange(folderFiles);
                processedItems++;

                //_logger.LogDebug("Successfully imported {Count} files from folder: {Path}", folderFiles.Count, folderPath);

                progress?.Report(new ProgressData
                {
                    IsProcessing = true,
                    TotalTracks = totalItems,
                    ProcessedTracks = processedItems,
                    Status = $"Completed folder: {folderName} ({folderFiles.Count} files)",
                    Phase = "Importing"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import folder: {Path}", folderPath);
                processedItems++;

                string folderName = Path.GetFileName(folderPath);
                progress?.Report(new ProgressData
                {
                    IsProcessing = true,
                    TotalTracks = totalItems,
                    ProcessedTracks = processedItems,
                    Status = $"Failed to process folder: {folderName}",
                    Phase = "Importing"
                });
            }
        }

        // Handle any invalid paths
        List<string> invalidPaths = filePaths.Where(path => !File.Exists(path) && !Directory.Exists(path)).ToList();
        foreach (string invalidPath in invalidPaths)
        {
            _logger.LogWarning("Invalid path (not a file or directory): {Path}", invalidPath);
        }

        // Final progress report
        progress?.Report(new ProgressData
        {
            IsProcessing = false,
            TotalTracks = totalItems,
            ProcessedTracks = processedItems,
            Status = $"Import completed. {importedFiles.Count} files imported successfully.",
            Phase = ""
        });

        // Clear progress after a short delay (run in background to avoid blocking)
        if (progress != null)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000).ConfigureAwait(false);
                progress.Report(new ProgressData
                {
                    IsProcessing = false,
                    TotalTracks = 0,
                    ProcessedTracks = 0,
                    Status = string.Empty,
                    Phase = string.Empty
                });
            });
        }

        _logger.LogInformation("Import completed. Successfully imported {Count} files", importedFiles.Count);
        return importedFiles;
    }

    public async Task<List<MediaFile>> ImportFolderAsync(string folderPath, IProgress<ProgressData>? progress = null, CancellationToken cancellationToken = default)
    {
        List<MediaFile> importedFiles = new List<MediaFile>();

        if (!Directory.Exists(folderPath))
        {
            _logger.LogError("Folder does not exist: {FolderPath}", folderPath);
            return importedFiles;
        }

        List<string> audioFiles = GetAudioFilesFromFolder(folderPath);
        int totalFiles = audioFiles.Count;

        if (totalFiles == 0)
        {
            _logger.LogInformation("No audio files found in folder: {FolderPath}", folderPath);
            return importedFiles;
        }

        _logger.LogInformation("Importing {TotalFiles} audio files from folder: {FolderPath}", totalFiles, folderPath);

        int processedCount = 0;
        const int degreeOfParallelism = 8;

        object importLock = new object();
        List<MediaFile> pendingDbSaves = new List<MediaFile>();

        progress?.Report(new ProgressData { IsProcessing = true, TotalTracks = totalFiles, ProcessedTracks = 0, Status = $"Scanning: {Path.GetFileName(folderPath)}", Phase = "Importing" });

        SemaphoreSlim sem = new SemaphoreSlim(degreeOfParallelism);
        List<Task> tasks = new List<Task>();

        foreach (string filePath in audioFiles)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (!await _importCancellationService.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false))
                break;

            try
            {
                await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            Task task = Task.Run(async () =>
            {
                try
                {
                    MediaFile? importedFile = await ImportFileAsync(filePath).ConfigureAwait(false);
                    if (importedFile != null)
                    {
                        // Minimal duplicate check - only skip if already in library
                        MediaFile? existing = _musicLibrary.IsTrackInLibrary(importedFile);
                        if (existing == null)
                        {
                            lock (importLock)
                            {
                                importedFiles.Add(importedFile);
                                pendingDbSaves.Add(importedFile);
                            }
                            //_logger.LogInformation("Queued for library: {FilePath}", filePath);
                        }
                        else
                        {
                            _logger.LogDebug("Skipped duplicate (already in library): {FilePath}", filePath);
                            // Still add to playlist later if this is "add to playlist" flow
                            lock (importLock)
                            {
                                importedFiles.Add(importedFile);  // Keep for playlist
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed in worker for {FilePath}", filePath);
                }
                finally
                {
                    sem.Release();
                }
            }, cancellationToken);

            tasks.Add(task);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Final DB flush
        List<MediaFile> finalDbBatch;
        lock (importLock)
        {
            finalDbBatch = pendingDbSaves.ToList();
            pendingDbSaves.Clear();
        }
        if (finalDbBatch.Count > 0)
        {
            await _musicLibrary.SaveTracksBatchAsync(finalDbBatch).ConfigureAwait(false);
        }

        // Final UI batch add
        if (importedFiles.Count > 0)
        {
            progress?.Report(new ProgressData { IsProcessing = true, TotalTracks = totalFiles, ProcessedTracks = totalFiles, Status = $"Adding {importedFiles.Count:N0} tracks to library…", Phase = "Importing" });
            await _musicLibrary.AddTracksToLibraryBatchAsync(importedFiles).ConfigureAwait(false);
        }

        // Background metadata
        if (importedFiles.Count > 0)
        {
            try
            { LinkerPlayer.Services.Metadata.BackgroundMetadataRefresher.Enqueue(importedFiles); }
            catch { }
        }

        progress?.Report(new ProgressData { IsProcessing = false, TotalTracks = totalFiles, ProcessedTracks = processedCount, Status = $"Done — {importedFiles.Count:N0} new track{(importedFiles.Count == 1 ? "" : "s")} added.", Phase = "" });

        _logger.LogInformation("Folder import completed. {ImportedCount}/{TotalFiles} files imported successfully", importedFiles.Count, totalFiles);

        return importedFiles;
    }

    public async Task<MediaFile?> ImportFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _logger.LogWarning("ImportFileAsync: Invalid or missing file: {FilePath}", filePath);
            return null;
        }

        if (!IsAudioFile(filePath))
        {
            _logger.LogWarning("ImportFileAsync: Not supported audio: {FilePath}", filePath);
            return null;
        }

        _logger.LogDebug("ImportFileAsync starting: {FilePath}", filePath);

        try
        {
            MediaFile mediaFile = new MediaFile { Path = filePath };
            mediaFile.FileName = Path.GetFileName(filePath);

            try
            {
                mediaFile.UpdateFromFileMetadata(raisePropertyChanged: true);
                mediaFile.NeedsMetadataRefresh = false;
                _logger.LogDebug("Metadata extracted successfully for {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Metadata extraction failed (will background refresh): {FilePath}", filePath);
                mediaFile.NeedsMetadataRefresh = true;
            }

            return mediaFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ImportFileAsync failed: {FilePath}", filePath);
            _importErrorLogger.Log(filePath, ex);
            return null;
        }
    }

    public bool IsAudioFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        return MusicLibrary._supportedAudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public int GetAudioFileCount(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return 0;
        }

        try
        {
            return Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                           .Count(IsAudioFile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to count audio files in folder: {FolderPath}", folderPath);
            return 0;
        }
    }

    public List<string> GetAudioFilesFromFolder(string folderPath)
    {
        try
        {
            List<string> files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                               .Where(IsAudioFile)
                               .ToList();

            _logger.LogInformation("GetAudioFilesFromFolder: Found {Count} audio files in {Path}", files.Count, folderPath);
            return files;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get audio files from folder: {FolderPath}", folderPath);
            return new List<string>();
        }
    }
}
