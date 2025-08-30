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

    public FileImportService(MusicLibrary musicLibrary, ILogger<FileImportService> logger)
    {
        _musicLibrary = musicLibrary ?? throw new ArgumentNullException(nameof(musicLibrary));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<MediaFile>> ImportFilesAsync(string[] filePaths, IProgress<ProgressData>? progress = null)
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

        // Process individual files first
        foreach (string filePath in files)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);
                progress?.Report(new ProgressData
                {
                    IsProcessing = true,
                    TotalTracks = totalItems,
                    ProcessedTracks = processedItems,
                    Status = $"Importing: {fileName}",
                    Phase = "Importing"
                });

                MediaFile? importedFile = await ImportFileAsync(filePath);
                if (importedFile != null)
                {
                    importedFiles.Add(importedFile);
                    //_logger.LogDebug("Successfully imported file: {Path}", filePath);
                }

                processedItems++;
                
                progress?.Report(new ProgressData
                {
                    IsProcessing = true,
                    TotalTracks = totalItems,
                    ProcessedTracks = processedItems,
                    Status = $"Imported: {fileName}",
                    Phase = "Importing"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import file: {Path}", filePath);
                processedItems++;
                
                string fileName = Path.GetFileName(filePath);
                progress?.Report(new ProgressData
                {
                    IsProcessing = true,
                    TotalTracks = totalItems,
                    ProcessedTracks = processedItems,
                    Status = $"Failed to import: {fileName}",
                    Phase = "Importing"
                });
            }
        }

        // Process folders
        foreach (string folderPath in folders)
        {
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

                List<MediaFile> folderFiles = await ImportFolderAsync(folderPath, progress);
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

        _logger.LogInformation("Import completed. Successfully imported {Count} files", importedFiles.Count);
        return importedFiles;
    }

    public async Task<List<MediaFile>> ImportFolderAsync(string folderPath, IProgress<ProgressData>? progress = null)
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

        int processedCount = 0;

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

        // Process files one by one for detailed progress reporting
        foreach (string filePath in audioFiles)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);
                
                // Report progress before processing each file
                progress?.Report(new ProgressData
                {
                    IsProcessing = true,
                    TotalTracks = totalFiles,
                    ProcessedTracks = processedCount,
                    Status = $"Importing: {fileName}",
                    Phase = "Importing"
                });

                // Add a small delay to allow UI to show the "Importing" message
                await Task.Delay(25);

                MediaFile? importedFile = await ImportFileAsync(filePath);
                if (importedFile != null)
                {
                    importedFiles.Add(importedFile);
                }

                processedCount++;

                // Report progress after processing each file
                //progress?.Report(new ProgressData
                //{
                //    IsProcessing = true,
                //    TotalTracks = totalFiles,
                //    ProcessedTracks = processedCount,
                //    Status = $"Imported: {fileName} ({processedCount}/{totalFiles})",
                //    Phase = "Importing"
                //});

                //// Add a small delay to allow UI to update
                //await Task.Delay(50);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import file: {FilePath}", filePath);
                processedCount++;

                string fileName = Path.GetFileName(filePath);
                progress?.Report(new ProgressData
                {
                    IsProcessing = true,
                    TotalTracks = totalFiles,
                    ProcessedTracks = processedCount,
                    Status = $"Failed to import: {fileName} ({processedCount}/{totalFiles})",
                    Phase = "Importing"
                });
            }
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
            MediaFile mediaFile = new MediaFile { Path = filePath };
            mediaFile.UpdateFromFileMetadata();

            // Check if track already exists in library
            MediaFile? existingTrack = _musicLibrary.IsTrackInLibrary(mediaFile);
            if (existingTrack != null)
            {
                //_logger.LogDebug("Track already exists in library: {FilePath}", filePath);
                return existingTrack.Clone();
            }

            // Add new track to library
            MediaFile? addedTrack = await _musicLibrary.AddTrackToLibraryAsync(mediaFile, saveImmediately: false);
            if (addedTrack != null)
            {
                //_logger.LogDebug("Successfully added new track to library: {FilePath}", filePath);
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

        string extension = Path.GetExtension(path);
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
}