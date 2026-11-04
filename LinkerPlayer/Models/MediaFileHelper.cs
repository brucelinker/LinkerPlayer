using ATL;
using Microsoft.Extensions.Logging;

namespace LinkerPlayer.Models;

public interface IMediaFileHelper
{
    string GetBestArtistField(Track track);
    string GetBestAlbumArtistField(Track track);
}

public class MediaFileHelper : IMediaFileHelper
{
    private readonly ILogger<MediaFileHelper> _logger;

    public MediaFileHelper(ILogger<MediaFileHelper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// The rating scale for ATL's Popularity property. ATL normalizes Popularity to 0–1 for every
    /// format and encodes it into the format's native convention (Vorbis RATING, ID3v2 POPM).
    /// Writing 0–255 values is clamped by ATL and the rating is lost on re-read.
    /// </summary>
    public static double RatingScaleForPath(string? path) => 1.0;

    /// <summary>Converts a 0–5 star rating to the file's native Popularity value.</summary>
    public static float StarsToPopularity(double stars, string? path)
    {
        double value = Math.Clamp(stars, 0.0, 5.0) * RatingScaleForPath(path) / 5.0;
        // Two decimals keep fractional stars intact (e.g. 4.2★ → 0.84, not 0.8).
        return (float)Math.Round(value, 2);
    }

    /// <summary>Converts a native Popularity value to a 0–5 star rating.</summary>
    public static double PopularityToStars(float popularity, string? path)
    {
        double scale = RatingScaleForPath(path);
        return Math.Round(Math.Clamp(popularity / scale * 5.0, 0.0, 5.0), 1);
    }

    public static string ParseArtist(Track track)
    {
        string artistField = track.Artist;
        string[] artists = string.IsNullOrEmpty(artistField)
            ? Array.Empty<string>()
            : artistField.Split(new[] { ';', '/' }, StringSplitOptions.TrimEntries);
        return artists.Length > 0 ? artists[0] : string.Empty;
    }

    public string GetBestArtistField(Track track) => ParseArtist(track);

    public static string ParseAlbumArtist(Track track)
    {
        string albumArtist = track.AlbumArtist;
        string[] albumArtists = string.IsNullOrEmpty(albumArtist)
            ? Array.Empty<string>()
            : albumArtist.Split(new[] { ';', '/' }, StringSplitOptions.TrimEntries);
        return albumArtists.Length > 0 ? albumArtists[0] : string.Empty;
    }

    public string GetBestAlbumArtistField(Track track) => ParseAlbumArtist(track);
}
