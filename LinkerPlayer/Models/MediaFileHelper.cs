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

    public string GetBestArtistField(Track track)
    {
        string artistField = track.Artist;                    // e.g. "Artist1; Artist2; Artist3"
        string[] artists = string.IsNullOrEmpty(artistField)
            ? Array.Empty<string>()
            : artistField.Split(new[] { ';', ',', '/' }, StringSplitOptions.TrimEntries);

        if (artists.Length > 0)
        {
            return artists[0];
        }

        return string.Empty;

        //// 2. Try FirstPerformer
        //if (!string.IsNullOrWhiteSpace(track.FirstPerformer))
        //{
        //    return track.FirstPerformer;
        //}

        //// 3. Search for any other artist-related fields via reflection
        //try
        //{
        //    PropertyInfo[] trackProps = track.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        //    foreach (PropertyInfo prop in trackProps)
        //    {
        //        if ((prop.Name.Contains("Artist", StringComparison.OrdinalIgnoreCase) ||
        //             prop.Name.Contains("Performer", StringComparison.OrdinalIgnoreCase)) &&
        //            !prop.Name.StartsWith("First") && !prop.Name.StartsWith("Joined"))
        //        {
        //            try
        //            {
        //                object? value = prop.GetValue(track);
        //                string stringValue = value switch
        //                {
        //                    string s when !string.IsNullOrWhiteSpace(s) => s,
        //                    string[] { Length: > 0 } arr when !string.IsNullOrWhiteSpace(arr[0]) => string.Join(", ", arr),
        //                    _ => ""
        //                };

        //                if (!string.IsNullOrWhiteSpace(stringValue))
        //                {
        //                    return stringValue;
        //                }
        //            }
        //            catch (Exception ex)
        //            {
        //                _logger.LogDebug(ex, "Error reading artist property {PropertyName}: {Message}", prop.Name, ex.Message);
        //            }
        //        }
        //    }
        //}
        //catch (Exception ex)
        //{
        //    _logger.LogDebug(ex, "Error searching for artist fields: {Message}", ex.Message);
        //}

        //return "";
    }

    public string GetBestAlbumArtistField(Track track)
    {
        string albumArtist = track.AlbumArtist;                    // e.g. "Artist1; Artist2; Artist3"
        string[] albumArtists = string.IsNullOrEmpty(albumArtist)
            ? Array.Empty<string>()
            : albumArtist.Split(new[] { ';', ',', '/' }, StringSplitOptions.TrimEntries);

        if (albumArtists.Length > 0)
        {
            return albumArtists[0];
        }

        return string.Empty;

        //    // Try multiple album artist fields in order of preference

        //    // 1. Try AlbumArtists array
        //    if (track.AlbumArtists is { Length: > 0 } && !string.IsNullOrWhiteSpace(track.AlbumArtist)
        //    {
        //        return string.Join(", ", track.AlbumArtists);
        //    }

        //    // 2. Try FirstAlbumArtist
        //    if (!string.IsNullOrWhiteSpace(track.FirstAlbumArtist))
        //    {
        //        return track.FirstAlbumArtist;
        //    }

        //    // 3. Fall back to regular artist if no album artist is specified
        //    string artistField = GetBestArtistField(track);
        //    if (!string.IsNullOrWhiteSpace(artistField))
        //    {
        //        return artistField;
        //    }

        //    return "";
    }
}
