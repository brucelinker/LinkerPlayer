using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using TagLib.Id3v2;
using TagLib.Mpeg4;
using TagLib.Ogg;
using File = TagLib.File;

namespace LinkerPlayer.ViewModels.Properties.Loaders;

/// <summary>
/// Loads ReplayGain tags (track/album gain and peak values)
/// </summary>
public class ReplayGainLoader : IMetadataLoader
{
    private readonly ILogger<ReplayGainLoader> _logger;

    public ReplayGainLoader(ILogger<ReplayGainLoader> logger)
    {
        _logger = logger;
    }

    public void Load(File audioFile, ObservableCollection<TagItem> targetCollection)
    {
        if (audioFile?.Tag == null)
        {
            _logger.LogWarning("No tag data found for ReplayGain information");
            return;
        }

        // DON'T clear - ViewModel handles this
        // targetCollection.Clear();

        TagLib.Tag tag = audioFile.Tag;

        // Format-specific ReplayGain handling
        if (audioFile.Tag is Tag)
        {
            // ID3v2 (MP3) - ReplayGain not well supported, show empty editable fields
            AddReplayGainItem(targetCollection, "ReplayGain Track Gain", "", false, null);
            AddReplayGainItem(targetCollection, "ReplayGain Track Peak", "", false, null);
        }
        else if (audioFile.Tag is XiphComment xiphComment)
        {
            // FLAC/OGG/Opus - Full ReplayGain support with editable fields
            AddReplayGainItem(targetCollection, "ReplayGain Track Gain",
     xiphComment.GetFirstField("REPLAYGAIN_TRACK_GAIN") ?? "", true,
       v => xiphComment.SetField("REPLAYGAIN_TRACK_GAIN", string.IsNullOrEmpty(v) ? null : v));

            AddReplayGainItem(targetCollection, "ReplayGain Track Peak",
          xiphComment.GetFirstField("REPLAYGAIN_TRACK_PEAK") ?? "", true,
                        v => xiphComment.SetField("REPLAYGAIN_TRACK_PEAK", string.IsNullOrEmpty(v) ? null : v));

            AddReplayGainItem(targetCollection, "ReplayGain Album Gain",
          xiphComment.GetFirstField("REPLAYGAIN_ALBUM_GAIN") ?? "", true,
     v => xiphComment.SetField("REPLAYGAIN_ALBUM_GAIN", string.IsNullOrEmpty(v) ? null : v));

            AddReplayGainItem(targetCollection, "ReplayGain Album Peak",
                   xiphComment.GetFirstField("REPLAYGAIN_ALBUM_PEAK") ?? "", true,
        v => xiphComment.SetField("REPLAYGAIN_ALBUM_PEAK", string.IsNullOrEmpty(v) ? null : v));
        }
        else if (audioFile.Tag is AppleTag mp4Tag)
        {
            // MP4/M4A - iTunes-style ReplayGain tags
            AddReplayGainItem(targetCollection, "ReplayGain Track Gain",
     mp4Tag.GetText("----:com.apple.iTunes:REPLAYGAIN_TRACK_GAIN")?.FirstOrDefault() ?? "", true,
         v => mp4Tag.SetText("----:com.apple.iTunes:REPLAYGAIN_TRACK_GAIN", string.IsNullOrEmpty(v) ? null : [v]));

            AddReplayGainItem(targetCollection, "ReplayGain Track Peak",
                 mp4Tag.GetText("----:com.apple.iTunes:REPLAYGAIN_TRACK_PEAK")?.FirstOrDefault() ?? "", true,
                    v => mp4Tag.SetText("----:com.apple.iTunes:REPLAYGAIN_TRACK_PEAK", string.IsNullOrEmpty(v) ? null : [v]));

            AddReplayGainItem(targetCollection, "ReplayGain Album Gain",
        mp4Tag.GetText("----:com.apple.iTunes:REPLAYGAIN_ALBUM_GAIN")?.FirstOrDefault() ?? "", true,
     v => mp4Tag.SetText("----:com.apple.iTunes:REPLAYGAIN_ALBUM_GAIN", string.IsNullOrEmpty(v) ? null : [v]));

            AddReplayGainItem(targetCollection, "ReplayGain Album Peak",
                  mp4Tag.GetText("----:com.apple.iTunes:REPLAYGAIN_ALBUM_PEAK")?.FirstOrDefault() ?? "", true,
          v => mp4Tag.SetText("----:com.apple.iTunes:REPLAYGAIN_ALBUM_PEAK", string.IsNullOrEmpty(v) ? null : [v]));
        }
        else
        {
            // Generic fallback - use TagLib's built-in ReplayGain properties (read-only)
            AddReplayGainItem(targetCollection, "Track Gain", FormatGainToString(tag.ReplayGainTrackGain), false, null);
            AddReplayGainItem(targetCollection, "Track Peak", FormatPeakToString(tag.ReplayGainTrackPeak), false, null);
            AddReplayGainItem(targetCollection, "Album Gain", FormatGainToString(tag.ReplayGainAlbumGain), false, null);
            AddReplayGainItem(targetCollection, "Album Peak", FormatPeakToString(tag.ReplayGainAlbumPeak), false, null);
        }
    }

    public void LoadMultiple(IReadOnlyList<File> audioFiles, ObservableCollection<TagItem> targetCollection)
    {
        // For ReplayGain on multiple files, show empty fields
        // Multi-file ReplayGain calculation will be a separate feature (album gain)
        // DON'T clear - ViewModel handles this
        // targetCollection.Clear();

        AddReplayGainItem(targetCollection, "ReplayGain Track Gain", "", false, null);
        AddReplayGainItem(targetCollection, "ReplayGain Track Peak", "", false, null);
        AddReplayGainItem(targetCollection, "ReplayGain Album Gain", "", false, null);
        AddReplayGainItem(targetCollection, "ReplayGain Album Peak", "", false, null);

        _logger.LogDebug("ReplayGain shown as empty for {Count} selected files (use context menu for album gain)", audioFiles.Count);
    }

    private static void AddReplayGainItem(ObservableCollection<TagItem> collection, string name, string value,
      bool isEditable, Action<string>? updateAction)
    {
        collection.Add(new TagItem
        {
            Name = name,
            Value = value,
            IsEditable = isEditable,
            UpdateAction = isEditable ? updateAction : null
        });
    }

    private static string FormatPeakToString(double peak)
    {
        if (double.IsNaN(peak))
            return string.Empty;
        return peak.ToString("F6");
    }

    private static string FormatGainToString(double gain)
    {
        if (double.IsNaN(gain))
            return string.Empty;
        return gain.ToString("F") + " dB";
    }
}
