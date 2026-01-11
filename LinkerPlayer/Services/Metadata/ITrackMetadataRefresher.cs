using LinkerPlayer.Models;

namespace LinkerPlayer.Services.Metadata;

public interface ITrackMetadataRefresher
{
    Task<TrackMetadataRefreshResult> RefreshIfChangedAsync(MediaFile track, CancellationToken cancellationToken = default);
}
