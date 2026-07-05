using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Core;
using LinkerPlayer.Interop;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Windows;
using Microsoft.Extensions.Logging;
using PlaylistsNET.Content;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LinkerPlayer.Services;

public interface IPlaylistFileService
{
    Task LoadPlaylistFileAsync(string fileName, MediaTabViewModel tabManager);
}

public class PlaylistFileService : IPlaylistFileService
{
    private readonly ILogger<PlaylistFileService> _logger;
    private readonly IFileImportService _fileImportService;
    private readonly IImportCancellationService _importCancellationService;

    private readonly IUiDispatcher _uiDispatcher;
    private readonly IMusicLibrary _musicLibrary;
    private readonly IPlaylistManagerService _playlistManagerService;

    public PlaylistFileService(
        IFileImportService fileImportService,
        IImportCancellationService importCancellationService,
        IUiDispatcher uiDispatcher,
        IMusicLibrary musicLibrary,
        IPlaylistManagerService playlistManagerService,
        ILogger<PlaylistFileService> logger
)
    {
        _fileImportService = fileImportService;
        _importCancellationService = importCancellationService;
        _uiDispatcher = uiDispatcher;
        _musicLibrary = musicLibrary;
        _playlistManagerService = playlistManagerService;
        _logger = logger;
    }

    public async Task LoadPlaylistFileAsync(string fileName, MediaTabViewModel tabManager)
    {
        if (!File.Exists(fileName))
        {
            _logger.LogWarning("Playlist file does not exist: {FileName}", fileName);
            return;
        }

        try
        {
            string directoryName = Path.GetDirectoryName(fileName)!;
            string playlistName = Path.GetFileNameWithoutExtension(fileName);

            PlaylistTab newTab = await _playlistManagerService.CreatePlaylistTabAsync(playlistName);

            await _uiDispatcher.InvokeAsync(() =>
            {
                newTab.IsLoading = true;
                newTab.LoadingStatus = $"Loading \"{playlistName}\"…";
                newTab.LoadingProgress = 0;
                newTab.LoadingTotal = 1;
                tabManager.TabList.Add(newTab);
                tabManager.SelectedTab = newTab;
                tabManager.SelectedTabIndex = tabManager.TabList.Count - 1;

                // Skip direct _tabControl access for now
                // tabManager.SelectTab(newTab); // we'll add this helper later if needed
            });

            // PHASE 1
            List<string> rawPaths = ExtractPathsFromPlaylistFile(fileName);

            Dictionary<string, MediaFile> libraryIndex = _musicLibrary.MainLibrary
                .GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            List<(MediaFile Track, string CandidatePath)> entries = new List<(MediaFile, string)>();

            foreach (string path in rawPaths)
            {
                try
                {
                    string candidatePath = path;

                    if (Uri.TryCreate(candidatePath, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                        candidatePath = uri.LocalPath;

                    candidatePath = Uri.UnescapeDataString(candidatePath);

                    if (!Path.IsPathRooted(candidatePath))
                        candidatePath = Path.GetFullPath(Path.Combine(directoryName, candidatePath));

                    if (libraryIndex.TryGetValue(candidatePath, out MediaFile? known))
                    {
                        entries.Add((known, candidatePath));
                    }
                    else
                    {
                        MediaFile stub = new MediaFile(candidatePath)
                        {
                            HealthStatus = TrackHealthStatus.Missing
                        };
                        entries.Add((stub, candidatePath));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to normalise path from playlist entry: {Entry}", path);
                }
            }

            if (entries.Count == 0)
            {
                await _uiDispatcher.InvokeAsync(() => newTab.IsLoading = false);
                return;
            }

            List<MediaFile> knownTracks = entries
                .Where(e => e.Track.HealthStatus != TrackHealthStatus.Missing)
                .Select(e => e.Track)
                .ToList();

            List<(MediaFile Track, string CandidatePath)> stubs = entries
                .Where(e => e.Track.HealthStatus == TrackHealthStatus.Missing)
                .ToList();

            await _uiDispatcher.InvokeAsync(() =>
            {
                newTab.LoadingTotal = entries.Count;
                newTab.LoadingProgress = knownTracks.Count;
                newTab.LoadingStatus = stubs.Count > 0
                    ? $"Loaded {knownTracks.Count} tracks, resolving {stubs.Count} unmatched…"
                    : $"Loading {knownTracks.Count} tracks…";

                foreach (MediaFile track in knownTracks)
                    newTab.Tracks.Add(track);
            });

            await _playlistManagerService.AddTracksToPlaylistAsync(playlistName, knownTracks);

            if (stubs.Count == 0)
            {
                await _uiDispatcher.InvokeAsync(() => newTab.IsLoading = false);
                return;
            }

            // PHASE 2
            await _uiDispatcher.InvokeAsync(() =>
            {
                newTab.LoadingTotal = stubs.Count;
                newTab.LoadingProgress = 0;
                newTab.LoadingStatus = $"Resolving {stubs.Count} unmatched track(s)…";
            });

            Progress<ProgressData> progress = new Progress<ProgressData>(data =>
            {
                WeakReferenceMessenger.Default.Send(new ProgressValueMessage(data));
            });

            CancellationToken ct = _importCancellationService.Token;
            List<PlaylistImportLogEntry> importLog = new List<PlaylistImportLogEntry>();

            await Task.Run(async () =>
            {
                int resolved = 0;

                for (int i = 0; i < stubs.Count; i++)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    (MediaFile _, string candidatePath) = stubs[i];

                    try
                    {
                        string? resolvedPath = null;
                        string? fuzzyRecoveredPath = null;

                        if (File.Exists(candidatePath))
                        {
                            resolvedPath = candidatePath;
                        }
                        else
                        {
                            string? fileNameOnly = Path.GetFileName(candidatePath);
                            string? immediateDir = null;
                            try
                            { immediateDir = Path.GetDirectoryName(candidatePath); }
                            catch { }

                            if (!string.IsNullOrEmpty(immediateDir) && Directory.Exists(immediateDir))
                            {
                                string? recovered = TryFindClosestFileInDirectory(immediateDir, fileNameOnly);
                                if (!string.IsNullOrEmpty(recovered))
                                {
                                    _logger.LogInformation("Recovered missing file by fuzzy match: '{Orig}' => '{Match}'", candidatePath, recovered);
                                    resolvedPath = recovered;
                                    fuzzyRecoveredPath = recovered;
                                }
                            }
                            else
                            {
                                try
                                {
                                    string? albumDirPath = string.IsNullOrEmpty(immediateDir) ? null : Path.GetDirectoryName(immediateDir);
                                    if (!string.IsNullOrEmpty(albumDirPath))
                                    {
                                        string? artistDir = Path.GetDirectoryName(albumDirPath);
                                        string albumDirName = Path.GetFileName(albumDirPath);

                                        if (!string.IsNullOrEmpty(artistDir) && Directory.Exists(artistDir))
                                        {
                                            string? recoveredAlbum = TryFindClosestDirectoryInParent(artistDir, albumDirName);
                                            if (!string.IsNullOrEmpty(recoveredAlbum) && Directory.Exists(recoveredAlbum))
                                            {
                                                string? recoveredDeep = TryFindClosestFileRecursively(recoveredAlbum, fileNameOnly);
                                                if (!string.IsNullOrEmpty(recoveredDeep))
                                                {
                                                    _logger.LogInformation("Recovered missing file by directory+file fuzzy match: '{Orig}' => '{Match}'", candidatePath, recoveredDeep);
                                                    resolvedPath = recoveredDeep;
                                                    fuzzyRecoveredPath = recoveredDeep;
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogDebug(ex, "Directory fuzzy recovery failed for {Path}", candidatePath);
                                }
                            }
                        }

                        if (resolvedPath != null)
                        {
                            MediaFile? imported;
                            if (libraryIndex.TryGetValue(resolvedPath, out MediaFile? alreadyKnown))
                            {
                                imported = alreadyKnown;
                            }
                            else
                            {
                                imported = await _fileImportService.ImportFileAsync(resolvedPath).ConfigureAwait(false);
                            }

                            if (imported != null && !string.IsNullOrEmpty(imported.FileName))
                            {
                                await _uiDispatcher.InvokeAsync(() => newTab.Tracks.Add(imported));
                                resolved++;
                                if (fuzzyRecoveredPath != null)
                                {
                                    importLog.Add(new PlaylistImportLogEntry { Action = PlaylistImportAction.Recovered, Path = candidatePath, RecoveredPath = fuzzyRecoveredPath });
                                }
                            }
                            else if (imported != null)
                            {
                                _logger.LogWarning("Playlist '{Name}': imported track has no metadata — skipped: {Path}", playlistName, resolvedPath);
                                importLog.Add(new PlaylistImportLogEntry
                                {
                                    Action = PlaylistImportAction.Skipped,
                                    Path = candidatePath
                                });
                            }
                            else
                            {
                                _logger.LogWarning("Playlist '{Name}': could not import resolved path — skipped: {Path}", playlistName, resolvedPath);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Playlist '{Name}': track not found on disk — skipped: {Path}", playlistName, candidatePath);
                            importLog.Add(new PlaylistImportLogEntry
                            {
                                Action = PlaylistImportAction.Missing,
                                Path = candidatePath
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve stub track: {Path}", candidatePath);
                    }

                    ((IProgress<ProgressData>)progress).Report(new ProgressData
                    {
                        IsProcessing = true,
                        TotalTracks = stubs.Count,
                        ProcessedTracks = i + 1,
                        Status = $"Resolving tracks… {i + 1} / {stubs.Count}",
                        Phase = "Resolving"
                    });
                    await _uiDispatcher.InvokeAsync(() => newTab.LoadingProgress = i + 1);
                }

                _logger.LogInformation(
                    "Playlist '{Name}': load complete — {Known} direct, {Resolved} recovered, {Skipped} not found",
                    playlistName, knownTracks.Count, resolved, stubs.Count - resolved);

                ((IProgress<ProgressData>)progress).Report(new ProgressData
                {
                    IsProcessing = false,
                    TotalTracks = stubs.Count,
                    ProcessedTracks = stubs.Count,
                    Status = string.Empty,
                    Phase = string.Empty
                });

                await _uiDispatcher.InvokeAsync(() =>
                {
                    newTab.IsLoading = false;

                    if (importLog.Count > 0)
                    {
                        PlaylistImportLogWindow logWindow = new PlaylistImportLogWindow(
                            playlistName,
                            knownTracks.Count + resolved,
                            resolved,
                            importLog);
                        OwnedWindowHelper.Show(logWindow, System.Windows.Application.Current.MainWindow);
                    }
                });
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load playlist file: {FileName}", fileName);
        }
    }

    // Helpers restored and used across the class
    private static List<string> ExtractPathsFromPlaylistFile(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();

        // Prefer robust manual parsing for M3U/M3U8 with encoding detection
        if (extension is ".m3u" or ".m3u8")
        {
            return ExtractPathsFromM3u(fileName);
        }

        using FileStream stream = File.OpenRead(fileName);

        return extension switch
        {
            ".pls" => new PlsContent().GetFromStream(stream).GetTracksPaths(),
            ".wpl" => new WplContent().GetFromStream(stream).GetTracksPaths(),
            ".zpl" => new ZplContent().GetFromStream(stream).GetTracksPaths(),
            _ => new List<string>()
        };
    }

    private static List<string> ExtractPathsFromM3u(string fileName)
    {
        List<string> results = new List<string>();

        try
        {
            // Try UTF-8 with BOM detection, then Latin1, then UTF-16
            foreach (Encoding? encoding in new[] { new UTF8Encoding(false, false), Encoding.Latin1, Encoding.Unicode })
            {
                try
                {
                    using StreamReader reader = new StreamReader(fileName, encoding, detectEncodingFromByteOrderMarks: true);
                    string? line;
                    results.Clear();

                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }

                        if (line.StartsWith("#"))
                        {
                            continue; // comment or directive
                        }

                        results.Add(line);
                    }

                    // If we successfully read any entries, break
                    if (results.Count > 0)
                    {
                        break;
                    }
                }
                catch
                {
                    // Try next encoding
                    results.Clear();
                }
            }
        }
        catch
        {
            // Fallback to PlaylistsNET if manual parsing fails
            try
            {
                using FileStream stream = File.OpenRead(fileName);
                results = new M3uContent().GetFromStream(stream).GetTracksPaths();
            }
            catch
            {
                // ignore
            }
        }

        return results;
    }

    // --- String normalization and distance helpers ---
    private static string NormalizeForCompare(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // Lowercase
        string s = input.ToLowerInvariant();

        // Replace curly quotes and similar punctuation with ASCII
        s = s.Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u201C', '"')
            .Replace('\u201D', '"');

        // Remove diacritics
        string formD = s.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder(formD.Length);
        foreach (char ch in formD)
        {
            UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        s = sb.ToString().Normalize(NormalizationForm.FormC);

        // Remove punctuation (keep letters, digits, spaces), collapse spaces
        s = Regex.Replace(s, "[^a-z0-9 ]", string.Empty);
        s = Regex.Replace(s, "\\s+", " ").Trim();
        return s;
    }

    // Correct small spacing issues in helpers
    private static int LevenshteinDistance(string a, string b)
    {
        if (a == b)
        {
            return 0;
        }

        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                            Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                            d[i - 1, j - 1] + cost);
            }
        }
        return d[a.Length, b.Length];
    }

    private static string? TryFindClosestFileInDirectory(string directory, string targetFileName)
    {
        try
        {
            string targetNoExtNorm = NormalizeForCompare(Path.GetFileNameWithoutExtension(targetFileName));
            string targetNorm = NormalizeForCompare(Path.GetFileName(targetFileName));
            HashSet<string> allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
              { ".mp3", ".flac", ".ape", ".ac3", ".dsd", ".dsf", ".dts", ".m4k", ".mka", ".mp4", ".mpc", ".ofr", ".ogg", ".opus", ".wav", ".wma", ".wv" };
            string? bestPath = null;
            int bestScore = int.MaxValue;
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                string ext = Path.GetExtension(file);
                if (!allowed.Contains(ext))
                {
                    continue;
                }

                string name = Path.GetFileName(file);
                string nameNorm = NormalizeForCompare(name);
                string nameNoExtNorm = NormalizeForCompare(Path.GetFileNameWithoutExtension(name));
                if (nameNorm == targetNorm || nameNoExtNorm == targetNoExtNorm)
                {
                    return file;
                }

                int dist = LevenshteinDistance(nameNoExtNorm, targetNoExtNorm);
                if (dist < bestScore)
                {
                    bestScore = dist;
                    bestPath = file;
                    if (bestScore <= 2)
                    {
                        return bestPath;
                    }
                }
            }
            return bestScore <= 3 ? bestPath : null;
        }
        catch { return null; }
    }

    private static string? TryFindClosestFileRecursively(string baseDir, string targetFileName)
    {
        try
        {
            string targetNoExtNorm = NormalizeForCompare(Path.GetFileNameWithoutExtension(targetFileName));
            string targetNorm = NormalizeForCompare(Path.GetFileName(targetFileName));
            HashSet<string> allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
              { ".mp3", ".flac", ".ape", ".ac3", ".dsd", ".dsf", ".dts", ".m4k", ".mka", ".mp4", ".mpc", ".ofr", ".ogg", ".opus", ".wav", ".wma", ".wv" };
            string? bestPath = null;
            int bestScore = int.MaxValue;
            foreach (string file in Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file);
                if (!allowed.Contains(ext))
                {
                    continue;
                }

                string name = Path.GetFileName(file);
                string nameNorm = NormalizeForCompare(name);
                string nameNoExtNorm = NormalizeForCompare(Path.GetFileNameWithoutExtension(name));
                if (nameNorm == targetNorm || nameNoExtNorm == targetNoExtNorm)
                {
                    return file;
                }

                int dist = LevenshteinDistance(nameNoExtNorm, targetNoExtNorm);
                if (dist < bestScore)
                {
                    bestScore = dist;
                    bestPath = file;
                    if (bestScore <= 1)
                    {
                        return bestPath;
                    }
                }
            }
            return bestScore <= 2 ? bestPath : null;
        }
        catch { return null; }
    }

    private static string? TryFindClosestDirectoryInParent(string parentDir, string targetDirName)
    {
        try
        {
            string targetNorm = NormalizeForCompare(targetDirName);
            string? bestPath = null;
            int bestScore = int.MaxValue;
            foreach (string dir in Directory.EnumerateDirectories(parentDir))
            {
                string name = Path.GetFileName(dir);
                string nameNorm = NormalizeForCompare(name);
                if (nameNorm == targetNorm)
                {
                    return dir;
                }

                int dist = LevenshteinDistance(nameNorm, targetNorm);
                if (dist < bestScore)
                {
                    bestScore = dist;
                    bestPath = dir;
                    if (bestScore <= 2)
                    {
                        return bestPath;
                    }
                }
            }
            return bestScore <= 3 ? bestPath : null;
        }
        catch { return null; }
    }
}
