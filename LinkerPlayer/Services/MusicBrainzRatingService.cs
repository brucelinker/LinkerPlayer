using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace LinkerPlayer.Services;

public interface IMusicBrainzRatingService
{
    Task<double?> GetPreferredRatingAsync(string recordingMbid, CancellationToken ct = default);
    Task EnrichSelectedTracksAsync(IEnumerable<MediaFile> tracks, IProgress<ProgressData>? progress = null, CancellationToken ct = default);
    Task<bool> SubmitRatingAsync(string recordingMbid, double rating0to5, CancellationToken ct = default);
}

public class MusicBrainzRatingService : IMusicBrainzRatingService
{
    private readonly HttpClient _client;
    private readonly ILogger<MusicBrainzRatingService> _logger;
    private readonly IMusicLibrary _musicLibrary;

    public MusicBrainzRatingService(ISettingsManager settingsManager, ILogger<MusicBrainzRatingService> logger, IMusicLibrary musicLibrary)
    {
        AppSettings settings = settingsManager.Settings;
        _musicLibrary = musicLibrary;

        HttpClientHandler handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(settings.MusicBrainzUsername, settings.MusicBrainzPassword)
        };

        _client = new HttpClient(handler);
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("LinkerPlayer/1.0 (bruce@linkerhouse.com)");

        _logger = logger;
    }

    /// <summary>
    /// Gets the best available rating: your personal rating first, otherwise community average.
    /// </summary>
    public async Task<double?> GetPreferredRatingAsync(string recordingMbid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recordingMbid))
            return null;

        string url = $"https://musicbrainz.org/ws/2/recording/{recordingMbid}?inc=ratings&fmt=json";

        try
        {
            HttpResponseMessage response = await _client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            string json = await response.Content.ReadAsStringAsync(ct);
            using JsonDocument doc = JsonDocument.Parse(json);

            // 1. Prefer YOUR personal user rating if it exists
            if (doc.RootElement.TryGetProperty("user-rating", out JsonElement userEl) &&
                userEl.TryGetProperty("value", out JsonElement userValue) &&
                userValue.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                int mbUser = userValue.GetInt32();
                return Math.Round(mbUser / 20.0, 1);
            }

            // 2. Fall back to community average rating
            if (doc.RootElement.TryGetProperty("rating", out JsonElement commEl) &&
                commEl.TryGetProperty("value", out JsonElement commValue) &&
                commValue.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                double mbComm = commValue.GetDouble();
                return Math.Round(mbComm, 1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get rating for MBID {Mbid}", recordingMbid);
        }

        return null;
    }

    public async Task EnrichSelectedTracksAsync(IEnumerable<MediaFile> tracks, IProgress<ProgressData>? progress = null, CancellationToken ct = default)
    {
        List<MediaFile> trackList = tracks.ToList();
        if (trackList.Count == 0)
            return;

        _logger.LogInformation("Fetching MusicBrainz ratings for {Count} track(s)", trackList.Count);

        int processed = 0;
        int updated = 0;

        foreach (MediaFile track in trackList)
        {
            if (ct.IsCancellationRequested)
                break;

            processed++;
            progress?.Report(new ProgressData
            {
                IsProcessing = true,
                TotalTracks = trackList.Count,
                ProcessedTracks = processed,
                Status = $"Fetching rating {processed}/{trackList.Count}..."
            });

            string? mbid = track.GetTag("MUSICBRAINZ_TRACKID") ?? track.GetTag("MUSICBRAINZ_TRACK_ID");
            if (string.IsNullOrWhiteSpace(mbid))
                continue;

            // Only enrich tracks that don't have a rating yet
            if (track.Rating > 0.0)
                continue;

            double? mbRating = await GetPreferredRatingAsync(mbid, ct);

            if (mbRating.HasValue && mbRating.Value > 0.0 && track.Rating == 0.0)
            {
                track.Rating = mbRating.Value;
                updated++;
                _logger.LogInformation("MusicBrainz rating {Rating:0.0} enriched for {Path} (Id={Id}, dirtyTracking={Dirty})",
                    mbRating.Value, track.Path, track.Id, track.IsDirtyTrackingEnabled ? "on" : "off");
            }
        }
    }

    public async Task<bool> SubmitRatingAsync(string recordingMbid, double rating0to5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recordingMbid))
            return false;

        int mbRating = (int)Math.Round(rating0to5 * 20); // Convert 0.0-5.0 → 0-100

        string url = $"https://musicbrainz.org/ws/2/recording/{recordingMbid}/rating";

        try
        {
            StringContent content = new StringContent($"<rating>{mbRating}</rating>", System.Text.Encoding.UTF8, "application/xml");
            HttpResponseMessage response = await _client.PutAsync(url, content, ct);

            bool success = response.IsSuccessStatusCode;
            if (success)
                _logger.LogInformation("Submitted rating {Rating:0.0} for MBID {Mbid}", rating0to5, recordingMbid);
            else
                _logger.LogWarning("Failed to submit rating. Status: {Status}", response.StatusCode);

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting rating for MBID {Mbid}", recordingMbid);
            return false;
        }
    }
}
