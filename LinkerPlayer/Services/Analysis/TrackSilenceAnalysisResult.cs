namespace LinkerPlayer.Services.Analysis;

public sealed class TrackSilenceAnalysisResult
{
    public int LeadingSilenceMs { get; init; }
    public int TrailingSilenceMs { get; init; }
}
