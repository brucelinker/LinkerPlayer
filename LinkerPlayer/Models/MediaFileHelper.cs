using Microsoft.Extensions.Logging;
using System.Reflection;
using Tag = TagLib.Tag;

namespace LinkerPlayer.Models;

public interface IMediaFileHelper
{
    string GetBestArtistField(Tag tag);
    string GetBestAlbumArtistField(Tag tag);
}

public class MediaFileHelper : IMediaFileHelper
{
    private readonly ILogger<MediaFileHelper> _logger;

    public MediaFileHelper(ILogger<MediaFileHelper> logger)
    {
        _logger = logger;
    }

    public string GetBestArtistField(Tag tag)
    {
        // Try multiple artist fields in order of preference

        // 1. Try Performers array (most common)
        if (tag.Performers is { Length: > 0 } && !string.IsNullOrWhiteSpace(tag.Performers[0]))
        {
            return string.Join(", ", tag.Performers);
        }

        // 2. Try FirstPerformer
        if (!string.IsNullOrWhiteSpace(tag.FirstPerformer))
        {
            return tag.FirstPerformer;
        }

        // 3. Search for any other artist-related fields via reflection
        try
        {
            PropertyInfo[] tagProps = tag.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo prop in tagProps)
            {
                if ((prop.Name.Contains("Artist", StringComparison.OrdinalIgnoreCase) ||
                     prop.Name.Contains("Performer", StringComparison.OrdinalIgnoreCase)) &&
                    !prop.Name.StartsWith("First") && !prop.Name.StartsWith("Joined"))
                {
                    try
                    {
                        object? value = prop.GetValue(tag);
                        string stringValue = value switch
                        {
                            string s when !string.IsNullOrWhiteSpace(s) => s,
                            string[] { Length: > 0 } arr when !string.IsNullOrWhiteSpace(arr[0]) => string.Join(", ", arr),
                            _ => ""
                        };

                        if (!string.IsNullOrWhiteSpace(stringValue))
                        {
                            return stringValue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error reading artist property {PropertyName}: {Message}", prop.Name, ex.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error searching for artist fields: {Message}", ex.Message);
        }

        return "";
    }

    public string GetBestAlbumArtistField(Tag tag)
    {
        // Try multiple album artist fields in order of preference

        // 1. Try AlbumArtists array
        if (tag.AlbumArtists is { Length: > 0 } && !string.IsNullOrWhiteSpace(tag.AlbumArtists[0]))
        {
            return string.Join(", ", tag.AlbumArtists);
        }

        // 2. Try FirstAlbumArtist
        if (!string.IsNullOrWhiteSpace(tag.FirstAlbumArtist))
        {
            return tag.FirstAlbumArtist;
        }

        // 3. Fall back to regular artist if no album artist is specified
        string artistField = GetBestArtistField(tag);
        if (!string.IsNullOrWhiteSpace(artistField))
        {
            return artistField;
        }

        return "";
    }
}
