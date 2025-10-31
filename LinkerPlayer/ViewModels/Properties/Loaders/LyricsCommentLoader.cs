using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
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

        var tag = audioFile.Tag;
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
                    tag.Comment = null;
                else
                    tag.Comment = string.IsNullOrEmpty(v) ? null : v;
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
        var commentValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var audioFile in audioFiles)
        {
            if (audioFile?.Tag == null)
                continue;

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

        var tag = audioFile.Tag;
        string lyricsValue = tag.Lyrics ?? "[ No lyrics available. ]";

        return new TagItem
        {
            Name = "Lyrics",
            Value = lyricsValue,
            IsEditable = true,
            UpdateAction = v =>
                  {
                      // Don't update if the value is the placeholder text
                      if (v == "[ No lyrics available. ]")
                          tag.Lyrics = null;
                      else
                          tag.Lyrics = string.IsNullOrEmpty(v) ? null : v;
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

        // Aggregate lyrics values
        var lyricsValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var audioFile in audioFiles)
        {
            if (audioFile?.Tag == null)
                continue;

            string lyrics = audioFile.Tag.Lyrics ?? "";

            if (!lyricsValues.ContainsKey(lyrics))
            {
                lyricsValues[lyrics] = 0;
            }
            lyricsValues[lyrics]++;
        }

        string displayValue;
        if (lyricsValues.Count == 0)
        {
            // No files had tags
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
