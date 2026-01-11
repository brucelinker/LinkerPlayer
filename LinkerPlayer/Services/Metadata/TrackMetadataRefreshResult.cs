using LinkerPlayer.Models;

namespace LinkerPlayer.Services.Metadata;

public sealed class TrackMetadataRefreshResult
{
    public required MediaFile Track { get; init; }
    public bool WasRefreshed { get; init; }
}
