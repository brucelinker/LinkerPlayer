using LinkerPlayer.Audio;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.IO;

namespace LinkerPlayer.Services.Metadata;

public sealed class TrackMetadataRefresher : ITrackMetadataRefresher
{
    private readonly ILogger<TrackMetadataRefresher> _logger;
    private readonly IAudioEngine _audioEngine;
    private const int MaxRetries = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    // How long after a successful save we trust the in-memory metadata and skip
    // re-reading from disk. Covers SMB write-flush and metadata-propagation delays on NAS.
    private static readonly TimeSpan PostSaveSkipWindow = TimeSpan.FromSeconds(30);

    public TrackMetadataRefresher(ILogger<TrackMetadataRefresher> logger, IAudioEngine audioEngine)
    {
        _logger = logger;
        _audioEngine = audioEngine;
    }

    public Task<TrackMetadataRefreshResult> RefreshIfChangedAsync(MediaFile track, CancellationToken cancellationToken = default)
    {
        if (track == null)
            throw new ArgumentNullException(nameof(track));

        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path = track.Path;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _logger.LogWarning("Metadata refresh skipped (missing file): TrackId={TrackId} Path='{Path}'", track.Id, path);
                return new TrackMetadataRefreshResult { Track = track, WasRefreshed = false };
            }

            DateTime utcWriteTime = File.GetLastWriteTimeUtc(path);
            bool shouldRefresh = !track.FileLastWriteTimeUtc.HasValue ||
                                  track.FileLastWriteTimeUtc.Value != utcWriteTime ||
                                  track.Duration == 0 ||
                                  string.IsNullOrWhiteSpace(track.Artist);

            if (!shouldRefresh)
            {
                _logger.LogDebug("Metadata refresh skipped (unchanged): TrackId={TrackId} Path='{Path}'", track.Id, path);
                return new TrackMetadataRefreshResult { Track = track, WasRefreshed = false };
            }

            // BASS holds a Windows file lock on the decode stream for the entire lifetime
            // of playback. Opening the same file with ATL while BASS has it locked will
            // throw an IOException. Skip the refresh — the metadata is already current
            // because we stamped FileLastWriteTimeUtc right after the ATL save completed.
            if (string.Equals(_audioEngine.LoadedTrackPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Metadata refresh skipped (file in use by BASS): Path='{Path}'", path);
                return new TrackMetadataRefreshResult { Track = track, WasRefreshed = false };
            }

            // We just saved this file ourselves. The SMB/NAS write-flush and filesystem
            // metadata propagation means the on-disk write-time may not yet match what we
            // stamped, so the shouldRefresh check above would pass — but the file handle
            // from the ATL save may still be held by the OS cache layer.
            // Skip the refresh: our in-memory metadata is already authoritative.
            if (track.LastSavedByAppUtc.HasValue &&
                (DateTime.UtcNow - track.LastSavedByAppUtc.Value) < PostSaveSkipWindow)
            {
                _logger.LogDebug("Metadata refresh skipped (recently saved by app): Path='{Path}'", path);
                return new TrackMetadataRefreshResult { Track = track, WasRefreshed = false };
            }

            // Retry on transient file-lock errors (common on UNC/NAS paths where
            // another process — or a just-completed ATL save — may briefly hold the handle).
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    track.UpdateFromFileMetadata();
                    track.FileLastWriteTimeUtc = utcWriteTime;
                    track.LastMetadataRefreshUtc = DateTime.UtcNow;
                    _logger.LogDebug("Refreshed metadata for {Path} (attempt {Attempt})", path, attempt);
                    return new TrackMetadataRefreshResult { Track = track, WasRefreshed = true };
                }
                catch (IOException ex)
                {
                    if (attempt < MaxRetries)
                    {
                        _logger.LogWarning("Metadata refresh attempt {Attempt}/{Max} failed for {Path}: {Message} — retrying in {Delay}ms",
                            attempt, MaxRetries, path, ex.Message, RetryDelay.TotalMilliseconds);
                        await Task.Delay(RetryDelay, cancellationToken);
                    }
                    else
                    {
                        _logger.LogWarning("Metadata refresh skipped after {Max} attempts (file locked): {Path} — {Message}",
                            MaxRetries, path, ex.Message);
                    }
                }
            }

            return new TrackMetadataRefreshResult { Track = track, WasRefreshed = false };

        }, cancellationToken);
    }
}
