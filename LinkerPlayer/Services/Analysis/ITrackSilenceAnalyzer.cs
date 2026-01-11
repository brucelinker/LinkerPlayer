using LinkerPlayer.Models;

namespace LinkerPlayer.Services.Analysis;

public interface ITrackSilenceAnalyzer
{
    Task<TrackSilenceAnalysisResult> AnalyzeAsync(MediaFile track, CancellationToken cancellationToken = default);
}
