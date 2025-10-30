using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        return new TagItem
        {
 Name = "Comment",
            Value = "[ Multiple files selected ]",
   IsEditable = false
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
        return new TagItem
 {
            Name = "Lyrics",
Value = "[ Multiple files selected ]",
 IsEditable = false
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
