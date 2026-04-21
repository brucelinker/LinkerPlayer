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

        // Build a HashSet of existing paths for O(1) duplicate checking
        HashSet<string> existingPaths = new HashSet<string>(
            _musicLibrary.MainLibrary.Select(t => t.Path),
            StringComparer.OrdinalIgnoreCase);

        int processedCount = 0;
        const int progressInterval = 10;
        const int degreeOfParallelism = 8;
        const int saveBatchSize = 50;

        object importLock = new object();
        List<MediaFile> pendingSaves = new List<MediaFile>();

        progress?.Report(new ProgressData
        {
            IsProcessing = true,
            TotalTracks = totalFiles,
            ProcessedTracks = 0,
            Status = $"Importing folder: {Path.GetFileName(folderPath)}",
            Phase = "Importing"
        });

        SemaphoreSlim sem = new SemaphoreSlim(degreeOfParallelism);
        List<Task> tasks = new List<Task>();

        foreach (string filePath in audioFiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Import cancelled after {Processed}/{Total} files", processedCount, totalFiles);
                break;
            }

            if (!await _importCancellationService.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Import cancelled while paused after {Processed}/{Total} files", processedCount, totalFiles);
                break;
            }

            // Skip duplicates immediately (O(1) check)
            if (existingPaths.Contains(filePath))
            {
                System.Threading.Interlocked.Increment(ref processedCount);
                continue;
            }

            try
            {
                await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Import cancelled during semaphore wait after {Processed}/{Total} files", processedCount, totalFiles);
                break;
            }

            Task task = Task.Run(async () =>
            {
                try
                {
                    MediaFile? importedFile = await ImportFileAsync(filePath).ConfigureAwait(false);
                    if (importedFile != null)
                    {
                        lock (importLock)
                        {
                            importedFiles.Add(importedFile);
                            pendingSaves.Add(importedFile);
                            existingPaths.Add(filePath);
                        }
                    }

                    int current = System.Threading.Interlocked.Increment(ref processedCount);
                    if (current % progressInterval == 0)
                    {
                        progress?.Report(new ProgressData
                        {
                            IsProcessing = true,
                            TotalTracks = totalFiles,
                            ProcessedTracks = current,
                            Status = $"Importing: {Path.GetFileName(filePath)} ({current}/{totalFiles})",
                            Phase = "Importing"
                        });
                    }

                    // Flush to MainLibrary and DB in batches
                    List<MediaFile>? toSave = null;
                    lock (importLock)
                    {
                        if (pendingSaves.Count >= saveBatchSize)
                        {
                            toSave = pendingSaves.ToList();
                            pendingSaves.Clear();
                        }
                    }

                    if (toSave != null && toSave.Count > 0)
                    {
                        try
                        {
                            // Add to MainLibrary in a single UI dispatch
                            await _musicLibrary.AddTracksToLibraryBatchAsync(toSave).ConfigureAwait(false);
                            // Then save to DB
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
                    _logger.LogError(ex, "Failed to import file: {FilePath}", filePath);
                    System.Threading.Interlocked.Increment(ref processedCount);
                }
                finally
                {
                    sem.Release();
                }
            }, cancellationToken);

            tasks.Add(task);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Final flush of remaining pending saves
        List<MediaFile> finalBatch;
        lock (importLock)
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add/save final batch");
            }
        }

        // Enqueue background metadata refresh for all imported files
        if (importedFiles.Count > 0)
        {
            try
            {
                LinkerPlayer.Services.Metadata.BackgroundMetadataRefresher.Enqueue(importedFiles);
            }
            catch { }
        }

        progress?.Report(new ProgressData
        {
            IsProcessing = false,
            TotalTracks = totalFiles,
            ProcessedTracks = processedCount,
            Status = $"Import completed. {importedFiles.Count} files imported successfully.",
            Phase = string.Empty
        });

        _logger.LogInformation("Folder import completed. {ImportedCount}/{TotalFiles} files imported successfully",
            importedFiles.Count, totalFiles);

        return importedFiles;
    }

    public async Task<MediaFile?> ImportFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _logger.LogWarning("ImportFileAsync called with invalid file path: {FilePath}", filePath);
            return null;
        }

        if (!IsAudioFile(filePath))
        {
            _logger.LogDebug("File is not a supported audio format: {FilePath}", filePath);
            return null;
        }

        try
        {
            // Construct MediaFile with full metadata extraction
            MediaFile mediaFile = new MediaFile { Path = filePath };
            mediaFile.FileName = Path.GetFileName(filePath);

            // Extract metadata synchronously for immediate display
            try
            {
                mediaFile.UpdateFromFileMetadata(raisePropertyChanged: true);
                mediaFile.NeedsMetadataRefresh = false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract metadata for {FilePath}, will retry in background", filePath);
                mediaFile.NeedsMetadataRefresh = true;
            }

            // NOTE: ImportFolderAsync will batch-add these to MainLibrary.
            // For single-file imports via drag-drop, the caller should use AddTrackToLibraryAsync separately.
            return mediaFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import file: {FilePath}", filePath);
            _importErrorLogger.Log(filePath, ex);
        }

        return null;
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

    private List<string> GetAudioFilesFromFolder(string folderPath)
    {
        try
        {
            return Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                           .Where(IsAudioFile)
                           .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get audio files from folder: {FolderPath}", folderPath);
            return new List<string>();
        }
    }
}
