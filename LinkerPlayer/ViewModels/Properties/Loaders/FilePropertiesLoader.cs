using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using File = TagLib.File;

namespace LinkerPlayer.ViewModels.Properties.Loaders;

/// <summary>
/// Loads file properties (duration, bitrate, codec, etc.) - read-only technical information
/// </summary>
public class FilePropertiesLoader : IMetadataLoader
{
    private readonly ILogger<FilePropertiesLoader> _logger;

    public FilePropertiesLoader(ILogger<FilePropertiesLoader> logger)
    {
        _logger = logger;
    }

    public void Load(File audioFile, ObservableCollection<TagItem> targetCollection)
    {
        if (audioFile?.Properties == null)
        {
            _logger.LogWarning("No properties found for the current file");
            return;
        }

        // DON'T clear - ViewModel handles this
        // targetCollection.Clear();

        var props = audioFile.Properties;

        // Technical file properties
        AddPropertyItem(targetCollection, "Duration", props.Duration.ToString(@"mm\:ss"));
        AddPropertyItem(targetCollection, "Bitrate", props.AudioBitrate > 0 ? props.AudioBitrate.ToString() + " kbps" : "");
        AddPropertyItem(targetCollection, "Sample Rate", props.AudioSampleRate > 0 ? props.AudioSampleRate.ToString() + " Hz" : "");
        AddPropertyItem(targetCollection, "Channels", props.AudioChannels > 0 ? props.AudioChannels.ToString() : "");
        AddPropertyItem(targetCollection, "Bits Per Sample", props.BitsPerSample > 0 ? props.BitsPerSample.ToString() : "");
        AddPropertyItem(targetCollection, "Media Types", props.MediaTypes.ToString());
        AddPropertyItem(targetCollection, "Codec", props.Description ?? props.Codecs?.FirstOrDefault()?.Description ?? "");

        // Tag format information
        AddPropertyItem(targetCollection, "Tag Types", audioFile.TagTypes.ToString());

        // Format-specific technical info
        try
        {
            if (audioFile.Tag is TagLib.Id3v2.Tag id3v2Tag)
            {
                AddPropertyItem(targetCollection, "ID3v2 Version", $"2.{id3v2Tag.Version}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading ID3v2 version: {Message}", ex.Message);
        }

        // Encoding tool information
        try
        {
            string? encoderInfo = GetEncoderInfo(audioFile.Tag);
            if (!string.IsNullOrWhiteSpace(encoderInfo))
            {
                AddPropertyItem(targetCollection, "Encoder", encoderInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading encoder info: {Message}", ex.Message);
        }
    }

    public void LoadMultiple(IReadOnlyList<File> audioFiles, ObservableCollection<TagItem> targetCollection)
    {
        if (audioFiles.Count == 0)
        {
            _logger.LogWarning("No files provided for multiple file properties loading");
            return;
        }

        // DON'T clear - ViewModel handles this
        // targetCollection.Clear();

        // Show aggregate properties or <various> if different
        AddPropertyItemMultiple(targetCollection, audioFiles, "Duration", f => f.Properties.Duration.ToString(@"mm\:ss"));
        AddPropertyItemMultiple(targetCollection, audioFiles, "Bitrate", f => f.Properties.AudioBitrate > 0 ? f.Properties.AudioBitrate.ToString() + " kbps" : "");
        AddPropertyItemMultiple(targetCollection, audioFiles, "Sample Rate", f => f.Properties.AudioSampleRate > 0 ? f.Properties.AudioSampleRate.ToString() + " Hz" : "");
        AddPropertyItemMultiple(targetCollection, audioFiles, "Channels", f => f.Properties.AudioChannels > 0 ? f.Properties.AudioChannels.ToString() : "");
        AddPropertyItemMultiple(targetCollection, audioFiles, "Codec", f => f.Properties.Description ?? f.Properties.Codecs?.FirstOrDefault()?.Description ?? "");
    }

    private static void AddPropertyItem(ObservableCollection<TagItem> collection, string name, string value)
    {
        // Only add if there's actually a value (avoid empty entries)
        if (!string.IsNullOrWhiteSpace(value))
        {
            collection.Add(new TagItem
            {
                Name = name,
                Value = value,
                IsEditable = false // Properties are read-only
            });
        }
    }

    private static void AddPropertyItemMultiple(ObservableCollection<TagItem> collection, IReadOnlyList<File> files, string name, Func<File, string> getValue)
    {
        var values = files.Select(getValue).ToList();
        var distinctValues = values.Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();

        string displayValue = distinctValues.Count switch
        {
            0 => "",
            1 => distinctValues[0],
            _ => "<various>"
        };

        if (!string.IsNullOrWhiteSpace(displayValue))
        {
            collection.Add(new TagItem
            {
                Name = name,
                Value = displayValue,
                IsEditable = false
            });
        }
    }

    private string? GetEncoderInfo(TagLib.Tag tag)
    {
        string[] possibleKeys =
        [
            "ENCODED_BY", "ENCODEDBY", "ENCODER", "TOOL", "SOFTWARE",
            "WRITING_LIBRARY", "WRITINGLIBRARY", "ENCODING_TOOL"
        ];

        try
        {
            // Vorbis Comments (FLAC, OGG)
            if (tag is TagLib.Ogg.XiphComment xiphTag)
            {
                foreach (string key in possibleKeys)
                {
                    string? value = xiphTag.GetFirstField(key);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            // iTunes tags (MP4, M4A)
            if (tag is TagLib.Mpeg4.AppleTag mp4Tag)
            {
                foreach (string key in possibleKeys)
                {
                    string? value = mp4Tag.GetText("----:com.apple.iTunes:" + key)?.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            // ID3v2 tags (MP3)
            if (tag is TagLib.Id3v2.Tag id3v2Tag)
            {
                try
                {
                    var tencFrames = id3v2Tag.GetFrames("TENC").ToList();
                    if (tencFrames.Any())
                    {
                        return tencFrames.First().ToString();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error reading ID3v2 TENC frame: {Message}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading encoder info: {Message}", ex.Message);
        }

        return null;
    }
}
