using ATL;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using TagLib.Id3v2;
using TagLib.Mpeg4;
using TagLib.Ogg;
using File = TagLib.File;

namespace LinkerPlayer.ViewModels.Properties.Loaders;

/// <summary>
/// Loads comment and lyrics fields
/// </summary>
public class LyricsCommentLoader
{
    private readonly ILogger<LyricsCommentLoader> _logger;

    public LyricsCommentLoader(ILogger<LyricsCommentLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Load comment field from audio file
    /// </summary>
    public TagItem LoadComment(File audioFile)
    {
        if (audioFile?.Tag == null)
        {
            _logger.LogWarning("No tag data found for comment information");
            return CreatePlaceholderComment();
        }

        TagLib.Tag tag = audioFile.Tag;
        string commentValue = tag.Comment ?? "[ No comment available. ]";

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
                    tag.Comment = null;
                }
                else
                {
                    tag.Comment = string.IsNullOrEmpty(v) ? null : v;
                }
            }
        };
    }

    /// <summary>
    /// Load comment field for multiple files
    /// </summary>
    public TagItem LoadCommentMultiple(IReadOnlyList<File> audioFiles)
    {
        if (audioFiles == null || audioFiles.Count == 0)
        {
            _logger.LogWarning("No audio files provided for comment loading");
            return CreatePlaceholderComment();
        }

        // Aggregate comment values
        Dictionary<string, int> commentValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (File audioFile in audioFiles)
        {
            if (audioFile?.Tag == null)
            {
                continue;
            }

            string comment = audioFile.Tag.Comment ?? "";

            if (!commentValues.ContainsKey(comment))
            {
                commentValues[comment] = 0;
            }
            commentValues[comment]++;
        }

        string displayValue;
        if (commentValues.Count == 0)
        {
            // No files had tags
            displayValue = "[ No comment available. ]";
        }
        else if (commentValues.Count == 1 && commentValues.Keys.First() == "")
        {
            // All files have empty/null comments
            displayValue = "[ No comment available. ]";
        }
        else if (commentValues.Count == 1)
        {
            // All files have the same comment
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
    /// Load lyrics field from audio file
    /// </summary>
    public TagItem LoadLyrics(File audioFile)
    {
        if (audioFile?.Tag == null)
        {
            _logger.LogWarning("No tag data found for lyrics information");
            return CreatePlaceholderLyrics();
        }

        string lyricsValue = GetLyricsValue(audioFile) ?? "[ No lyrics available. ]";

        return new TagItem
        {
            Name = "Lyrics",
            Value = lyricsValue,
            IsEditable = true,
            UpdateAction = v =>
            {
                SetLyricsValue(audioFile, v == "[ No lyrics available. ]" ? null : (string.IsNullOrEmpty(v) ? null : v));
            }
        };
    }

    /// <summary>
    /// Load lyrics field for multiple files
    /// </summary>
    public TagItem LoadLyricsMultiple(IReadOnlyList<File> audioFiles)
    {
        if (audioFiles == null || audioFiles.Count == 0)
        {
            _logger.LogWarning("No audio files provided for lyrics loading");
            return CreatePlaceholderLyrics();
        }

        Dictionary<string, int> lyricsValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (File audioFile in audioFiles)
        {
            if (audioFile?.Tag == null)
            {
                continue;
            }

            string lyrics = GetLyricsValue(audioFile) ?? "";

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
            // All files have empty/null lyrics
            displayValue = "[ No lyrics available. ]";
        }
        else if (lyricsValues.Count == 1)
        {
            // All files have the same lyrics
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

    /// <summary>
    /// Load comment field for multiple ATL tracks
    /// </summary>
    public TagItem LoadCommentMultiple(IReadOnlyList<Track> atlTracks)
    {
        if (atlTracks == null || atlTracks.Count == 0)
        {
            _logger.LogWarning("No ATL tracks provided for comment loading");
            return CreatePlaceholderComment();
        }

        List<string> distinctValues = atlTracks
            .Select(t => t.Comment ?? "")
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string displayValue = distinctValues.Count switch
        {
            0 => "[ No comment available. ]",
            1 => distinctValues[0],
            _ => $"<various> {string.Join("; ", distinctValues)}"
        };

        return new TagItem
        {
            Name = "Comment",
            Value = displayValue,
            OriginalValue = displayValue,
            HasMultipleValues = distinctValues.Count > 1,
            IsEditable = false
        };
    }

    /// <summary>
    /// Load lyrics field for multiple ATL tracks
    /// </summary>
    public TagItem LoadLyricsMultiple(IReadOnlyList<Track> atlTracks)
    {
        if (atlTracks == null || atlTracks.Count == 0)
        {
            _logger.LogWarning("No ATL tracks provided for lyrics loading");
            return CreatePlaceholderLyrics();
        }

        List<string> distinctValues = atlTracks
            .Select(t => t.Lyrics?.FirstOrDefault()?.UnsynchronizedLyrics ?? "")
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string displayValue = distinctValues.Count switch
        {
            0 => "[ No lyrics available. ]",
            1 => distinctValues[0],
            _ => $"<various> {string.Join("; ", distinctValues)}"
        };

        return new TagItem
        {
            Name = "Lyrics",
            Value = displayValue,
            OriginalValue = displayValue,
            HasMultipleValues = distinctValues.Count > 1,
            IsEditable = false
        };
    }

    private static string? GetLyricsValue(File audioFile)
    {
        try
        {
            // ID3v2 (MP3): prefer USLT, then try SYLT and TXXX fallbacks
            if (audioFile.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3Tag)
            {
                try
                {
                    UnsynchronisedLyricsFrame? uslt = id3Tag.GetFrames<UnsynchronisedLyricsFrame>().FirstOrDefault();
                    if (uslt != null && !string.IsNullOrWhiteSpace(uslt.Text))
                    {
                        return uslt.Text;
                    }
                }
                catch { }

                try
                {
                    SynchronisedLyricsFrame? sylt = id3Tag.GetFrames<SynchronisedLyricsFrame>().FirstOrDefault();
                    if (sylt != null)
                    {
                        string syltText = sylt.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(syltText))
                        {
                            return syltText;
                        }
                    }
                }
                catch { }

                try
                {
                    // Some tools store lyrics in TXXX custom frames with description like "LYRICS"
                    foreach (UserTextInformationFrame txxx in id3Tag.GetFrames<UserTextInformationFrame>())
                    {
                        string description = txxx.Description ?? string.Empty;
                        if (description.Contains("LYRICS", StringComparison.OrdinalIgnoreCase))
                        {
                            string? text = txxx.Text?.FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                return text;
                            }
                        }
                    }
                }
                catch { }

                string? generic = id3Tag.Lyrics;
                if (!string.IsNullOrWhiteSpace(generic))
                {
                    return generic;
                }
            }

            // Vorbis comments (FLAC/OGG/Opus)
            if (audioFile.GetTag(TagLib.TagTypes.Xiph, false) is XiphComment xiph)
            {
                foreach (string key in new[] { "LYRICS", "UNSYNCEDLYRICS", "UNSYNCED LYRICS" })
                {
                    string? value = xiph.GetFirstField(key);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                string? generic = audioFile.Tag.Lyrics;
                if (!string.IsNullOrWhiteSpace(generic))
                {
                    return generic;
                }
            }

            // MP4/M4A iTunes-style
            if (audioFile.GetTag(TagLib.TagTypes.Apple, false) is AppleTag mp4)
            {
                string? itunesLyrics = mp4.GetText("----:com.apple.iTunes:LYRICS")?.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(itunesLyrics))
                {
                    return itunesLyrics;
                }

                string? generic = audioFile.Tag.Lyrics;
                if (!string.IsNullOrWhiteSpace(generic))
                {
                    return generic;
                }
            }

            return audioFile.Tag.Lyrics;
        }
        catch
        {
            return audioFile.Tag.Lyrics;
        }
    }

    private static void SetLyricsValue(File audioFile, string? value)
    {
        try
        {
            // ID3v2 (MP3) - set USLT
            if (audioFile.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3Tag)
            {
                foreach (UnsynchronisedLyricsFrame f in id3Tag.GetFrames<UnsynchronisedLyricsFrame>().ToList())
                {
                    id3Tag.RemoveFrame(f);
                }

                if (!string.IsNullOrEmpty(value))
                {
                    UnsynchronisedLyricsFrame frame = new UnsynchronisedLyricsFrame("eng", string.Empty)
                    {
                        Text = value
                    };
                    id3Tag.AddFrame(frame);
                }
                else
                {
                    id3Tag.Lyrics = null;
                }
                return;
            }

            // Vorbis comments (FLAC/OGG/Opus)
            if (audioFile.GetTag(TagLib.TagTypes.Xiph, true) is XiphComment xiph)
            {
                xiph.SetField("LYRICS", string.IsNullOrEmpty(value) ? null : value);
                return;
            }

            // MP4/M4A iTunes-style
            if (audioFile.GetTag(TagLib.TagTypes.Apple, true) is AppleTag mp4)
            {
                mp4.SetText("----:com.apple.iTunes:LYRICS", string.IsNullOrEmpty(value) ? null : new[] { value });
                return;
            }

            audioFile.Tag.Lyrics = value;
        }
        catch
        {
            audioFile.Tag.Lyrics = value;
        }
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
