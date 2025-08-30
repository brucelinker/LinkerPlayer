using LinkerPlayer.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LinkerPlayer.Services;

public interface IFileImportService
{
    /// <summary>
    /// Imports files or folders and returns the imported tracks
    /// </summary>
    /// <param name="filePaths">Array of file or folder paths to import</param>
    /// <param name="progress">Progress reporting interface</param>
    /// <returns>List of successfully imported MediaFiles</returns>
    Task<List<MediaFile>> ImportFilesAsync(string[] filePaths, IProgress<ProgressData>? progress = null);

    /// <summary>
    /// Imports all audio files from a folder recursively
    /// </summary>
    /// <param name="folderPath">Path to the folder to import</param>
    /// <param name="progress">Progress reporting interface</param>
    /// <returns>List of successfully imported MediaFiles</returns>
    Task<List<MediaFile>> ImportFolderAsync(string folderPath, IProgress<ProgressData>? progress = null);

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