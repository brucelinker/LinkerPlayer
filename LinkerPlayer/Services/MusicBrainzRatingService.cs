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
    Task<int> EnrichSelectedTracksAsync(IEnumerable<MediaFile> tracks, IProgress<MusicBrainzRatingProgressUpdate>? progress = null, CancellationToken ct = default);
    Task<bool> SubmitRatingAsync(string recordingMbid, double rating0to5, CancellationToken ct = default);
}

public class MusicBrainzRatingService : IMusicBrainzRatingService
{
    // MusicBrainz allows about one web-service request per second per IP.
    // 1.5s leaves margin so a slightly slow response does not trip that average.
    // Static so every service instance shares the same pace.
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(1500);
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTime _nextRequestUtc = DateTime.MinValue;

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

        try
        {
            (RatingLookupResult result, double rating) = await LookupPreferredRatingAsync(recordingMbid, ct);
            return result == RatingLookupResult.Found ? rating : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    public async Task<int> EnrichSelectedTracksAsync(IEnumerable<MediaFile> tracks, IProgress<MusicBrainzRatingProgressUpdate>? progress = null, CancellationToken ct = default)
    {
        List<MediaFile> trackList = tracks.ToList();
        int total = trackList.Count;
        int processed = 0;
        int rated = 0;
        int noRating = 0;
        int errors = 0;
        int skipped = 0;
        int noMusicBrainzId = 0;
        bool cancelled = false;

        void Report(string currentTrack, string status, bool isRunning)
        {
            progress?.Report(new MusicBrainzRatingProgressUpdate
            {
                Total = total,
                Processed = processed,
                Rated = rated,
                NoRating = noRating,
                Errors = errors,
                Skipped = skipped,
                NoMusicBrainzId = noMusicBrainzId,
                CurrentTrack = currentTrack,
                Status = status,
                IsRunning = isRunning,
                IsCancelled = cancelled
            });
        }

        if (total == 0)
        {
            Report(string.Empty, "No tracks selected.", isRunning: false);
            return 0;
        }

        _logger.LogInformation("Fetching MusicBrainz ratings for {Count} track(s)", total);
        Report(string.Empty, $"Fetching ratings for {total} track(s)…", isRunning: true);

        try
        {
            foreach (MediaFile track in trackList)
            {
                ct.ThrowIfCancellationRequested();

                string name = DisplayName(track);
                int index = processed + 1;
                Report(name, $"Checking {index} of {total}", isRunning: true);

                try
                {
                    if (track.Rating > 0.0)
                    {
                        skipped++;
                        processed++;
                        Report(name, $"Skipped {processed} of {total}", isRunning: true);
                        continue;
                    }

                    // Off the dispatcher: GetFirstTag waits out sharing violations (up to ~2.5 s).
                    string? mbid = await Task.Run(
                        () => track.GetFirstTag("MUSICBRAINZ_TRACKID", "MUSICBRAINZ_TRACK_ID"),
                        ct);
                    if (string.IsNullOrWhiteSpace(mbid))
                    {
                        noMusicBrainzId++;
                        processed++;
                        Report(name, $"No MusicBrainz ID {processed} of {total}", isRunning: true);
                        continue;
                    }

                    (RatingLookupResult result, double rating) = await LookupPreferredRatingAsync(mbid, ct);
                    processed++;
                    if (result == RatingLookupResult.Found && rating > 0.0 && track.Rating == 0.0)
                    {
                        track.Rating = rating;
                        rated++;
                        _logger.LogInformation("MusicBrainz rating {Rating:0.0} enriched for {Path} (Id={Id}, dirtyTracking={Dirty})",
                            rating, track.Path, track.Id, track.IsDirtyTrackingEnabled ? "on" : "off");
                    }
                    else if (result == RatingLookupResult.Failed)
                    {
                        errors++;
                    }
                    else
                    {
                        noRating++;
                    }

                    Report(name, $"Checked {processed} of {total}", isRunning: true);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors++;
                    processed++;
                    _logger.LogWarning(ex, "Failed to fetch MusicBrainz rating for {Path}", track.Path);
                    Report(name, $"Error {processed} of {total}", isRunning: true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Report(string.Empty, cancelled ? "Cancelled. Ratings already fetched were kept." : "Complete", isRunning: false);
        return rated;
    }

    private async Task<(RatingLookupResult Result, double Rating)> LookupPreferredRatingAsync(string recordingMbid, CancellationToken ct)
    {
        string url = $"https://musicbrainz.org/ws/2/recording/{recordingMbid}?inc=ratings&fmt=json";

        try
        {
            using HttpResponseMessage response = await SendThrottledAsync(() => _client.GetAsync(url, ct), ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MusicBrainz rating lookup failed for {Mbid}. Status: {Status}", recordingMbid, response.StatusCode);
                return (RatingLookupResult.Failed, 0);
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            using JsonDocument doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("user-rating", out JsonElement userEl) &&
                userEl.TryGetProperty("value", out JsonElement userValue) &&
                userValue.ValueKind == JsonValueKind.Number)
            {
                double rating = Math.Round(userValue.GetInt32() / 20.0, 1);
                return rating > 0 ? (RatingLookupResult.Found, rating) : (RatingLookupResult.None, 0);
            }

            if (doc.RootElement.TryGetProperty("rating", out JsonElement commEl) &&
                commEl.TryGetProperty("value", out JsonElement commValue) &&
                commValue.ValueKind == JsonValueKind.Number)
            {
                double rating = Math.Round(commValue.GetDouble(), 1);
                return rating > 0 ? (RatingLookupResult.Found, rating) : (RatingLookupResult.None, 0);
            }

            return (RatingLookupResult.None, 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get rating for MBID {Mbid}", recordingMbid);
            return (RatingLookupResult.Failed, 0);
        }
    }

    private static string DisplayName(MediaFile track)
    {
        if (!string.IsNullOrWhiteSpace(track.Title))
            return track.Title;
        if (!string.IsNullOrWhiteSpace(track.FileName))
            return track.FileName;
        return track.Path;
    }

    private enum RatingLookupResult
    {
        Found,
        None,
        Failed
    }

    public async Task<bool> SubmitRatingAsync(string recordingMbid, double rating0to5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recordingMbid))
            return false;

        int mbRating = (int)Math.Round(rating0to5 * 20); // Convert 0.0-5.0 → 0-100

        string url = $"https://musicbrainz.org/ws/2/recording/{recordingMbid}/rating";

        try
        {
            using HttpResponseMessage response = await SendThrottledAsync(
                () => _client.PutAsync(url, new StringContent($"<rating>{mbRating}</rating>", System.Text.Encoding.UTF8, "application/xml"), ct),
                ct);

            bool success = response.IsSuccessStatusCode;
            if (success)
                _logger.LogInformation("Submitted rating {Rating:0.0} for MBID {Mbid}", rating0to5, recordingMbid);
            else
                _logger.LogWarning("Failed to submit rating. Status: {Status}", response.StatusCode);

            return success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting rating for MBID {Mbid}", recordingMbid);
            return false;
        }
    }

    /// <summary>
    /// Serializes MusicBrainz calls and keeps at least one second between request starts.
    /// A 503 is retried once after Retry-After, which is how the service asks clients to back off.
    /// </summary>
    private async Task<HttpResponseMessage> SendThrottledAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        await RequestGate.WaitAsync(ct);
        try
        {
            await WaitForNextRequestSlotAsync(ct);
            HttpResponseMessage response = await send();

            if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                return response;

            TimeSpan retryAfter = GetRetryAfter(response);
            response.Dispose();
            _logger.LogWarning("MusicBrainz returned 503. Waiting {Seconds:0.0}s before one retry.", retryAfter.TotalSeconds);
            _nextRequestUtc = DateTime.UtcNow + retryAfter;
            await WaitForNextRequestSlotAsync(ct);
            return await send();
        }
        finally
        {
            RequestGate.Release();
        }
    }

    private static async Task WaitForNextRequestSlotAsync(CancellationToken ct)
    {
        TimeSpan wait = _nextRequestUtc - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, ct);

        _nextRequestUtc = DateTime.UtcNow + MinRequestInterval;
    }

    private static TimeSpan GetRetryAfter(HttpResponseMessage response)
    {
        TimeSpan retryAfter = TimeSpan.FromSeconds(1);
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            retryAfter = delta;
        else if (response.Headers.RetryAfter?.Date is DateTimeOffset when)
            retryAfter = when - DateTimeOffset.UtcNow;

        if (retryAfter < TimeSpan.FromSeconds(1))
            retryAfter = TimeSpan.FromSeconds(1);
        if (retryAfter > TimeSpan.FromMinutes(2))
            retryAfter = TimeSpan.FromMinutes(2);

        return retryAfter;
    }
}
