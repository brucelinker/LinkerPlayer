using ATL;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace LinkerPlayer.ViewModels.Properties.Loaders;

/// <summary>
/// Loads comment and lyrics fields using ATL library
/// </summary>
public class LyricsCommentLoaderAtl
{
    private readonly ILogger<LyricsCommentLoaderAtl> _logger;

    public LyricsCommentLoaderAtl(ILogger<LyricsCommentLoaderAtl> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Load comment field from ATL track
    /// </summary>
    public TagItem LoadComment(Track track)
    {
        if (track == null)
        {
            _logger.LogWarning("No ATL track for comment information");
            return CreatePlaceholderComment();
        }

        string commentValue = track.Comment ?? "[ No comment available. ]";

        return new TagItem
        {
            Name = "Comment",
            Value = commentValue,
            IsEditable = true,
            UpdateAction = v =>
            {
                // Don't update if the value is the placeholder text
                if (v == "[ No comment available. ]")
                {
                    track.Comment = null;
                }
                else
                {
                    track.Comment = string.IsNullOrEmpty(v) ? null : v;
                }
            }
        };
    }

    /// <summary>
    /// Load comment field for multiple ATL tracks
    /// </summary>
    public TagItem LoadCommentMultiple(IReadOnlyList<Track> tracks)
    {
        if (tracks == null || tracks.Count == 0)
        {
            _logger.LogWarning("No ATL tracks provided for comment loading");
            return CreatePlaceholderComment();
        }

        // Aggregate comment values
        Dictionary<string, int> commentValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (Track track in tracks)
        {
            if (track == null)
            {
                continue;
            }

            string comment = track.Comment ?? "";

            if (!commentValues.ContainsKey(comment))
            {
                commentValues[comment] = 0;
            }
            commentValues[comment]++;
        }

        string displayValue;
        if (commentValues.Count == 0)
        {
            // No tracks had data
            displayValue = "[ No comment available. ]";
        }
        else if (commentValues.Count == 1 && commentValues.Keys.First() == "")
        {
            // All tracks have empty/null comments
            displayValue = "[ No comment available. ]";
        }
        else if (commentValues.Count == 1)
        {
            // All tracks have the same comment
            displayValue = commentValues.Keys.First();
        }
        else
        {
            // Different comments
            displayValue = "<various>";
        }

        return new TagItem
        {
            Name = "Comment",
            Value = displayValue,
            IsEditable = false // Read-only for multi-selection
        };
    }

    /// <summary>
    /// Load lyrics field from ATL track
    /// </summary>
    public TagItem LoadLyrics(Track track)
    {
        if (track == null)
        {
            _logger.LogWarning("No ATL track for lyrics information");
            return CreatePlaceholderLyrics();
        }

        string lyricsValue = GetLyricsValue(track) ?? "[ No lyrics available. ]";

        return new TagItem
        {
            Name = "Lyrics",
            Value = lyricsValue,
            IsEditable = true,
            UpdateAction = v =>
            {
                SetLyricsValue(track, v == "[ No lyrics available. ]" ? null : (string.IsNullOrEmpty(v) ? null : v));
            }
        };
    }

    /// <summary>
    /// Load lyrics field for multiple ATL tracks
    /// </summary>
    public TagItem LoadLyricsMultiple(IReadOnlyList<Track> tracks)
    {
        if (tracks == null || tracks.Count == 0)
        {
            _logger.LogWarning("No ATL tracks provided for lyrics loading");
            return CreatePlaceholderLyrics();
        }

        Dictionary<string, int> lyricsValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (Track track in tracks)
        {
            if (track == null)
            {
                continue;
            }

            string lyrics = GetLyricsValue(track) ?? "";

            if (!lyricsValues.ContainsKey(lyrics))
            {
                lyricsValues[lyrics] = 0;
            }
            lyricsValues[lyrics]++;
        }

        string displayValue;
        if (lyricsValues.Count == 0)
        {
            displayValue = "[ No lyrics available. ]";
        }
        else if (lyricsValues.Count == 1 && lyricsValues.Keys.First() == "")
        {
            // All tracks have empty/null lyrics
            displayValue = "[ No lyrics available. ]";
        }
        else if (lyricsValues.Count == 1)
        {
            // All tracks have the same lyrics
            displayValue = lyricsValues.Keys.First();
        }
        else
        {
            // Different lyrics
            displayValue = "<various>";
        }

        return new TagItem
        {
            Name = "Lyrics",
            Value = displayValue,
            IsEditable = false // Read-only for multi-selection
        };
    }

    private string? GetLyricsValue(Track track)
    {
        // Check AdditionalFields for lyrics-related fields first
        if (track.AdditionalFields != null)
        {
            // Check common lyrics field names
            foreach (string key in new[] { "LYRICS", "UNSYNCEDLYRICS", "UNSYNCED LYRICS", "----:com.apple.iTunes:LYRICS" })
            {
                if (track.AdditionalFields.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        // Try the Lyrics property - ATL.Lyrics is IList<LyricsInfo>
        // For now, skip this as the API is unclear
        // if (track.Lyrics != null && track.Lyrics.Count > 0)
        // {
        //     var lyricsInfo = track.Lyrics[0];
        //     // Use appropriate property when known
        // }

        return null;
    }

    private void SetLyricsValue(Track track, string? value)
    {
        // For now, just use AdditionalFields
        if (track.AdditionalFields == null)
        {
            track.AdditionalFields = new Dictionary<string, string>();
        }

        if (string.IsNullOrEmpty(value))
        {
            track.AdditionalFields.Remove("LYRICS");
        }
        else
        {
            track.AdditionalFields["LYRICS"] = value;
        }

        // TODO: Also set track.Lyrics when the API is clear
        // track.Lyrics = ...;
    }

    private static TagItem CreatePlaceholderComment()
    {
        return new TagItem
        {
            Name = "Comment",
            Value = "[ No comment available. ]",
            IsEditable = false
        };
    }

    private static TagItem CreatePlaceholderLyrics()
    {
        return new TagItem
        {
            Name = "Lyrics",
            Value = "[ No lyrics available. ]",
            IsEditable = false
        };
    }
}
