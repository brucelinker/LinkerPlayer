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
    /// The rating tag's native value scale for a file, by extension. Vorbis-family formats
    /// (FLAC, Ogg, Opus, Matroska) use ATL's Vorbis RATING convention of 0–1; ID3v2 POPM
    /// (MP3 and most others) uses 0–255. ATL surfaces the raw value, so the divisor must
    /// match the format or a rating written for one scale reads back wrong on the other.
    /// Empirically ATL's Vorbis path returns Popularity=1 for a stored rating, confirming
    /// its 0–1 convention — writing 0–100 would be clamped and the rating lost on re-read.
    /// </summary>
    public static double RatingScaleForPath(string? path)
    {
        string ext = System.IO.Path.GetExtension(path ?? string.Empty);
        return ext.Equals(".flac", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".opus", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mka", StringComparison.OrdinalIgnoreCase)
            ? 1.0
            : 255.0;
    }

    /// <summary>Converts a 0–5 star rating to the file's native Popularity value.</summary>
    public static float StarsToPopularity(double stars, string? path)
    {
        double scale = RatingScaleForPath(path);
        double value = Math.Clamp(stars, 0.0, 5.0) * scale / 5.0;
        // Round to one decimal on the native scale so fractional stars survive on
        // the 0–1 Vorbis scale (e.g. 4.2★ → 0.84, not 0.8) without sub-POPM-unit
        // noise on the 0–255 scale.
        int digits = scale <= 1.0 ? 2 : 1;
        return (float)Math.Round(value, digits);
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
