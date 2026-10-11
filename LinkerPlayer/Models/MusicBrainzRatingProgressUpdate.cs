namespace LinkerPlayer.Models;

/// <summary>
/// Snapshot reported by <see cref="Services.IMusicBrainzRatingService"/> while a ratings fetch is running.
/// </summary>
public sealed record MusicBrainzRatingProgressUpdate
{
    public int Total { get; init; }
    public int Processed { get; init; }
    public int Rated { get; init; }
    public int NoRating { get; init; }
    public int Errors { get; init; }
    public int Skipped { get; init; }
    public int NoMusicBrainzId { get; init; }
    public string CurrentTrack { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ProcessedLabel { get; init; } = "Checked";
    public string RatedLabel { get; init; } = "Rated";
    public bool IsRunning { get; init; }
    public bool IsCancelled { get; init; }
}
