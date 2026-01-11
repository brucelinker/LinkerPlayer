using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.IO;

namespace LinkerPlayer.Services.Metadata;

public sealed class TrackMetadataRefresher : ITrackMetadataRefresher
{
    private readonly ILogger<TrackMetadataRefresher> _logger;

    public TrackMetadataRefresher(ILogger<TrackMetadataRefresher> logger)
    {
        _logger = logger;
    }

    public Task<TrackMetadataRefreshResult> RefreshIfChangedAsync(MediaFile track, CancellationToken cancellationToken = default)
    {
        if (track == null)
        {
            throw new ArgumentNullException(nameof(track));
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path = track.Path;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _logger.LogWarning("Metadata refresh skipped (missing file): TrackId={TrackId} Path='{Path}' Exists={Exists}", track.Id, path, !string.IsNullOrWhiteSpace(path) && File.Exists(path));
                return new TrackMetadataRefreshResult { Track = track, WasRefreshed = false };
            }

            DateTime utcWriteTime = File.GetLastWriteTimeUtc(path);
            if (track.FileLastWriteTimeUtc.HasValue && track.FileLastWriteTimeUtc.Value == utcWriteTime)
            {
                _logger.LogDebug("Metadata refresh skipped (unchanged write-time): TrackId={TrackId} Path='{Path}' UtcWriteTime={UtcWriteTime}", track.Id, path, utcWriteTime);
                return new TrackMetadataRefreshResult { Track = track, WasRefreshed = false };
            }

            track.UpdateFromFileMetadata();
            track.FileLastWriteTimeUtc = utcWriteTime;
            track.LastMetadataRefreshUtc = DateTime.UtcNow;

            _logger.LogDebug("Refreshed metadata for {Path} at {UtcWriteTime}", path, utcWriteTime);

            return new TrackMetadataRefreshResult { Track = track, WasRefreshed = true };
        }, cancellationToken);
    }
}
