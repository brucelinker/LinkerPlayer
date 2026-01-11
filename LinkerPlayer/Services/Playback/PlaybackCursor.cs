namespace LinkerPlayer.Services.Playback;

public sealed class PlaybackCursor
{
    public required string PlaylistName { get; init; }
    public required int TrackIndex { get; init; }
    public required string TrackId { get; init; }
}
