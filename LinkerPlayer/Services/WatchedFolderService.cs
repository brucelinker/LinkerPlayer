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

    public WatchedFolderService(
        ISettingsManager settingsManager,
        IFileImportService fileImportService,
        IMusicLibrary musicLibrary,
        ILogger<WatchedFolderService> logger)
    {
        _settingsManager = settingsManager;
        _fileImportService = fileImportService;
        _musicLibrary = musicLibrary;
        _logger = logger;
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
}
