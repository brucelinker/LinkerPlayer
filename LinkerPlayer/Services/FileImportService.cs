using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LinkerPlayer.Services;

public class FileImportService : IFileImportService
{
    private readonly MusicLibrary _musicLibrary;
    private readonly ILogger<FileImportService> _logger;
    private readonly string[] _supportedAudioExtensions = [".mp3", ".flac", ".wma", ".ape", ".wav"];

    // Constants for progress reporting
    private const int DefaultBatchSize = 10;
    private const int MaxBatchSize = 100;

    public FileImportService(MusicLibrary musicLibrary, ILogger<FileImportService> logger)
    {
        _musicLibrary = musicLibrary ?? throw new ArgumentNullException(nameof(musicLibrary));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<MediaFile>> ImportFilesAsync(string[] filePaths, IProgress<ProgressData>? progress = null)
    {
        var importedFiles = new List<MediaFile>();
        
        if (filePaths == null || filePaths.Length == 0)
        {
            _logger.LogWarning("ImportFilesAsync called with null or empty file paths");
            return importedFiles;
        }

        _logger.LogInformation("Starting import of {Count} items", filePaths.Length);

        foreach (string path in filePaths)
        {
            try
            {
                if (File.Exists(path) && IsAudioFile(path))
                {
                    var importedFile = await ImportFileAsync(path);
                    if (importedFile != null)
                    {
                        importedFiles.Add(importedFile);
                        _logger.LogDebug("Successfully imported file: {Path}", path);
                    }
                }
                else if (Directory.Exists(path))
                {
                    var folderFiles = await ImportFolderAsync(path, progress);
                    importedFiles.AddRange(folderFiles);
                    _logger.LogDebug("Successfully imported {Count} files from folder: {Path}", folderFiles.Count, path);
                }
                else
                {
                    _logger.LogWarning("Invalid path (not a file or directory): {Path}", path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import item: {Path}", path);
            }
        }

        _logger.LogInformation("Import completed. Successfully imported {Count} files", importedFiles.Count);
        return importedFiles;
    }

    public async Task<List<MediaFile>> ImportFolderAsync(string folderPath, IProgress<ProgressData>? progress = null)
    {
        var importedFiles = new List<MediaFile>();
        
        if (!Directory.Exists(folderPath))
        {
            _logger.LogError("Folder does not exist: {FolderPath}", folderPath);
            return importedFiles;
        }

        var audioFiles = GetAudioFilesFromFolder(folderPath);
        var totalFiles = audioFiles.Count;
        
        if (totalFiles == 0)
        {
            _logger.LogInformation("No audio files found in folder: {FolderPath}", folderPath);
            return importedFiles;
        }

        var batchSize = CalculateBatchSize(totalFiles);
        var processedCount = 0;

        // Report initial progress
        progress?.Report(new ProgressData
        {
            IsProcessing = true,
            TotalTracks = totalFiles,
            ProcessedTracks = 0,
            Status = "Starting folder import...",
            Phase = "Importing"
        });

        _logger.LogInformation("Importing {TotalFiles} audio files from folder: {FolderPath}", totalFiles, folderPath);

        // Process files in batches for better performance and progress reporting
        var batches = audioFiles.Chunk(batchSize);
        
        foreach (var batch in batches)
        {
            var batchTasks = batch.Select(async file =>
            {
                try
                {
                    var importedFile = await ImportFileAsync(file);
                    if (importedFile != null)
                    {
                        return importedFile;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to import file in batch: {FilePath}", file);
                }
                return null;
            });

            var batchResults = await Task.WhenAll(batchTasks);
            var successfulImports = batchResults.Where(f => f != null).Cast<MediaFile>().ToList();
            
            importedFiles.AddRange(successfulImports);
            processedCount += batch.Length;

            // Report progress
            progress?.Report(new ProgressData
            {
                IsProcessing = true,
                TotalTracks = totalFiles,
                ProcessedTracks = processedCount,
                Status = $"Imported {successfulImports.Count}/{batch.Length} files from current batch",
                Phase = "Importing"
            });

            _logger.LogDebug("Processed batch: {ProcessedCount}/{TotalFiles}", processedCount, totalFiles);
        }

        // Final progress report
        progress?.Report(new ProgressData
        {
            IsProcessing = false,
            TotalTracks = totalFiles,
            ProcessedTracks = processedCount,
            Status = $"Import completed. {importedFiles.Count} files imported successfully.",
            Phase = ""
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
            var mediaFile = new MediaFile { Path = filePath };
            mediaFile.UpdateFromFileMetadata();

            // Check if track already exists in library
            var existingTrack = _musicLibrary.IsTrackInLibrary(mediaFile);
            if (existingTrack != null)
            {
                _logger.LogDebug("Track already exists in library: {FilePath}", filePath);
                return existingTrack.Clone();
            }

            // Add new track to library
            var addedTrack = await _musicLibrary.AddTrackToLibraryAsync(mediaFile, saveImmediately: false);
            if (addedTrack != null)
            {
                _logger.LogDebug("Successfully added new track to library: {FilePath}", filePath);
                return addedTrack.Clone();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import file: {FilePath}", filePath);
        }

        return null;
    }

    public bool IsAudioFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);
        return _supportedAudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public int GetAudioFileCount(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return 0;

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

    private static int CalculateBatchSize(int totalFiles)
    {
        if (totalFiles <= DefaultBatchSize)
            return totalFiles;

        var batchSize = Math.Max(DefaultBatchSize, totalFiles / 10);
        return Math.Min(batchSize, MaxBatchSize);
    }
}