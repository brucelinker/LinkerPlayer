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
