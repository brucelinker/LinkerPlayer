using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Reflection;
using TagLib.Id3v2;
using TagLib.Mpeg4;
using TagLib.Ogg;
using File = TagLib.File;

namespace LinkerPlayer.ViewModels.Properties.Loaders;

/// <summary>
/// Loads custom/non-standard metadata tags from various formats (ID3v2, Vorbis, APE, iTunes)
/// </summary>
public class CustomMetadataLoader : IMetadataLoader
{
    private readonly ILogger<CustomMetadataLoader> _logger;

    // Known standard fields that we DON'T want to show as custom (already in core metadata)
    private static readonly HashSet<string> StandardFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "TITLE", "ARTIST", "ALBUM", "ALBUMARTIST", "DATE", "YEAR", "GENRE", "COMPOSER",
        "TRACKNUMBER", "TRACK", "TOTALTRACKS", "TRACKCOUNT", "DISCNUMBER", "DISC",
  "TOTALDISCS", "DISCCOUNT", "COMMENT", "COPYRIGHT", "LYRICS", "BPM",
        "BEATSPERMINUTE", "CONDUCTOR", "GROUPING", "PUBLISHER",
     "ENCODER", "ENCODED-BY", "ENCODEDBY", "TOOL", "SOFTWARE", "ENCODING_TOOL",
  "REPLAYGAIN_TRACK_GAIN", "REPLAYGAIN_TRACK_PEAK", "REPLAYGAIN_ALBUM_GAIN", "REPLAYGAIN_ALBUM_PEAK"
    };

    // Fields that should go to Picture section
    private static readonly HashSet<string> PictureFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "METADATA_BLOCK_PICTURE", "COVERART", "COVER_ART", "ALBUMART", "ALBUM_ART", "PICTURE", "APIC"
    };

    public CustomMetadataLoader(ILogger<CustomMetadataLoader> logger)
    {
        _logger = logger;
    }

    public void Load(File audioFile, ObservableCollection<TagItem> targetCollection)
    {
        if (audioFile?.Tag == null)
        {
            _logger.LogWarning("No tag data found for custom metadata");
            return;
        }

        // DON'T clear - ViewModel handles this and CoreMetadataLoader has already added items
        // targetCollection.Clear();

        // Collect all custom fields from all tag formats
        Dictionary<string, List<string>> customFields = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        LoadApeCustomTags(audioFile, customFields);
        LoadVorbisCustomTags(audioFile, customFields);
        LoadITunesCustomTags(audioFile, customFields);
        LoadId3v2CustomTags(audioFile, customFields);

        // Add collected custom fields to UI
        foreach (KeyValuePair<string, List<string>> kvp in customFields.OrderBy(x => x.Key))
        {
            string fieldName = kvp.Key;
            List<string> values = kvp.Value;

            // Skip picture-related fields
            if (PictureFields.Contains(fieldName))
            {
                if (fieldName.Equals("METADATA_BLOCK_PICTURE", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Skip base64 noise
                }

                // Other picture fields could be added to PictureInfoItems if needed
                continue;
            }

            // Combine multiple values with semicolons
            string combinedValue = string.Join("; ", values.Distinct().Where(v => !string.IsNullOrWhiteSpace(v)));

            if (!string.IsNullOrWhiteSpace(combinedValue))
            {
                targetCollection.Add(new TagItem
                {
                    Name = $"<{fieldName}>", // Angle brackets indicate custom field
                    Value = combinedValue,
                    IsEditable = false // Custom tags are read-only for now
                });
            }
        }

        _logger.LogDebug("Loaded {Count} custom metadata fields", targetCollection.Count);
    }

    public void LoadMultiple(IReadOnlyList<File> audioFiles, ObservableCollection<TagItem> targetCollection)
    {
        if (audioFiles == null || audioFiles.Count == 0)
        {
            _logger.LogWarning("No audio files provided for custom metadata loading");
            return;
        }

        // Aggregate custom fields across all files, showing "<various>" when values differ
        Dictionary<string, Dictionary<string, int>> allCustomFields = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        // Track which files have each field (to detect missing fields)
        Dictionary<string, HashSet<int>> fieldPresenceByFile = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        for (int fileIndex = 0; fileIndex < audioFiles.Count; fileIndex++)
        {
            File audioFile = audioFiles[fileIndex];
            if (audioFile?.Tag == null)
            {
                continue;
            }

            Dictionary<string, List<string>> customFields = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            LoadApeCustomTags(audioFile, customFields);
            LoadVorbisCustomTags(audioFile, customFields);
            LoadITunesCustomTags(audioFile, customFields);
            LoadId3v2CustomTags(audioFile, customFields);

            // Aggregate into allCustomFields
            foreach (KeyValuePair<string, List<string>> kvp in customFields)
            {
                string fieldName = kvp.Key;

                // Skip picture-related fields
                if (PictureFields.Contains(fieldName))
                {
                    continue;
                }

                string combinedValue = string.Join("; ", kvp.Value.Distinct().Where(v => !string.IsNullOrWhiteSpace(v)));

                if (!string.IsNullOrWhiteSpace(combinedValue))
                {
                    if (!allCustomFields.ContainsKey(fieldName))
                    {
                        allCustomFields[fieldName] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        fieldPresenceByFile[fieldName] = new HashSet<int>();
                    }

                    if (!allCustomFields[fieldName].ContainsKey(combinedValue))
                    {
                        allCustomFields[fieldName][combinedValue] = 0;
                    }
                    allCustomFields[fieldName][combinedValue]++;
                    fieldPresenceByFile[fieldName].Add(fileIndex);
                }
            }
        }

        // Add aggregated custom fields to UI
        foreach (KeyValuePair<string, Dictionary<string, int>> kvp in allCustomFields.OrderBy(x => x.Key))
        {
            string fieldName = kvp.Key;
            Dictionary<string, int> valueOccurrences = kvp.Value;
            HashSet<int> filesWithField = fieldPresenceByFile[fieldName];

            string displayValue;

            // Check if ALL files have this field
            if (filesWithField.Count < audioFiles.Count)
            {
                // Some files don't have this field at all - show <various>
                displayValue = "<various>";
            }
            else if (valueOccurrences.Count == 1)
            {
                // All files have the same value
                displayValue = valueOccurrences.Keys.First();
            }
            else
            {
                // Multiple different values
                displayValue = "<various>";
            }

            targetCollection.Add(new TagItem
            {
                Name = $"<{fieldName}>", // Angle brackets indicate custom field
                Value = displayValue,
                IsEditable = false // Custom tags are read-only for multi-selection
            });
        }

        _logger.LogDebug("Loaded {Count} custom metadata fields for {FileCount} files", targetCollection.Count, audioFiles.Count);
    }

    private void LoadApeCustomTags(File audioFile, Dictionary<string, List<string>> customFields)
    {
        try
        {
            if (audioFile.GetTag(TagLib.TagTypes.Ape, false) is not TagLib.Ape.Tag apeTag)
            {
                return;
            }

            foreach (string key in apeTag)
            {
                try
                {
                    TagLib.Ape.Item item = apeTag.GetItem(key);
                    if (item != null)
                    {
                        string value = item.ToString();
                        if (!string.IsNullOrWhiteSpace(value) && !StandardFields.Contains(key))
                        {
                            if (!customFields.ContainsKey(key))
                            {
                                customFields[key] = new List<string>();
                            }
                            customFields[key].Add(value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error reading APE item {Key}: {Message}", key, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading APE tags: {Message}", ex.Message);
        }
    }

    private void LoadVorbisCustomTags(File audioFile, Dictionary<string, List<string>> customFields)
    {
        try
        {
            if (audioFile.GetTag(TagLib.TagTypes.Xiph, false) is not XiphComment xiphTag)
            {
                return;
            }

            foreach (string field in xiphTag)
            {
                string[] fieldValues = xiphTag.GetField(field);

                if (fieldValues.Length > 0 && !StandardFields.Contains(field))
                {
                    if (!customFields.ContainsKey(field))
                    {
                        customFields[field] = new List<string>();
                    }

                    foreach (string value in fieldValues)
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            customFields[field].Add(value);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading Vorbis comments: {Message}", ex.Message);
        }
    }

    private void LoadITunesCustomTags(File audioFile, Dictionary<string, List<string>> customFields)
    {
        try
        {
            if (audioFile.GetTag(TagLib.TagTypes.Apple, false) is not AppleTag mp4Tag)
            {
                return;
            }

            // Use reflection to find the internal text dictionary
            IEnumerable<FieldInfo> textFields = mp4Tag.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
  .Where(f => f.FieldType == typeof(Dictionary<string, string[]>));

            foreach (FieldInfo? field in textFields)
            {
                if (field.GetValue(mp4Tag) is not Dictionary<string, string[]> dict)
                {
                    continue;
                }

                foreach (KeyValuePair<string, string[]> kvp in dict)
                {
                    string value = kvp.Value.FirstOrDefault() ?? "";

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        string fieldName = kvp.Key.Replace("----:com.apple.iTunes:", "");
                        if (!StandardFields.Contains(fieldName))
                        {
                            if (!customFields.ContainsKey(fieldName))
                            {
                                customFields[fieldName] = new List<string>();
                            }
                            customFields[fieldName].Add(value);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading iTunes custom tags: {Message}", ex.Message);
        }
    }

    private void LoadId3v2CustomTags(File audioFile, Dictionary<string, List<string>> customFields)
    {
        try
        {
            if (audioFile.GetTag(TagLib.TagTypes.Id3v2, false) is not TagLib.Id3v2.Tag id3v2Tag)
            {
                return;
            }

            try
            {
                Frame[] frames = id3v2Tag.GetFrames().ToArray();

                foreach (Frame frame in frames)
                {
                    try
                    {
                        string frameId = frame.FrameId.ToString();

                        if (IsStandardId3v2Frame(frameId))
                        {
                            continue;
                        }

                        (string? displayName, string? frameValue) = GetId3v2FrameInfo(frame);
                        if (!string.IsNullOrWhiteSpace(frameValue))
                        {
                            string fieldName = displayName.Equals("USER_TEXT", StringComparison.OrdinalIgnoreCase)
                                 ? frameId
                                  : displayName;

                            if (StandardFields.Contains(fieldName))
                            {
                                continue;
                            }

                            if (!customFields.ContainsKey(fieldName))
                            {
                                customFields[fieldName] = new List<string>();
                            }
                            customFields[fieldName].Add(frameValue);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error processing ID3v2 frame: {Message}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error enumerating ID3v2 frames: {Message}", ex.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading ID3v2 custom tags: {Message}", ex.Message);
        }
    }

    private static bool IsStandardId3v2Frame(string frameId)
    {
        string[] standardFrames =
       [
               "TIT2", "TPE1", "TALB", "TPE2", "TYER", "TDRC", "TCON", "TCOM", "TRCK", "TPOS",
            "COMM", "TCOP", "USLT", "TBPM", "TPE3", "TIT1", "TPUB", "TSRC", "APIC", "TENC", "TSSE"
     ];

        return standardFrames.Contains(frameId);
    }

    private (string displayName, string value) GetId3v2FrameInfo(Frame frame)
    {
        try
        {
            if (frame is UserTextInformationFrame userTextFrame)
            {
                string description = userTextFrame.Description ?? "USER_TEXT";
                string text = userTextFrame.Text?.FirstOrDefault() ?? "";
                string fieldName = ExtractMeaningfulFieldName(description);
                return (fieldName, text);
            }

            return frame switch
            {
                TextInformationFrame textFrame =>
       (GetId3v2FrameDisplayName(frame.FrameId.ToString()), textFrame.Text?.FirstOrDefault() ?? ""),
                CommentsFrame commentFrame =>
                        (GetId3v2FrameDisplayName(frame.FrameId.ToString()), commentFrame.Text ?? ""),
                UnsynchronisedLyricsFrame lyricsFrame =>
          (GetId3v2FrameDisplayName(frame.FrameId.ToString()), lyricsFrame.Text ?? ""),
                _ => (GetId3v2FrameDisplayName(frame.FrameId.ToString()), frame.ToString() ?? "")
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error extracting frame info from ID3v2 frame: {Message}", ex.Message);
            return (frame.FrameId.ToString(), "");
        }
    }

    private static string ExtractMeaningfulFieldName(string description)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Equals("USER_TEXT", StringComparison.OrdinalIgnoreCase))
        {
            return "USER_TEXT";
        }

        return description switch
        {
            var d when d.Contains("BASS", StringComparison.OrdinalIgnoreCase) => "BASS GUITAR",
            var d when d.Contains("DRUM", StringComparison.OrdinalIgnoreCase) => "DRUMS",
            var d when d.Contains("GUITAR", StringComparison.OrdinalIgnoreCase) => "GUITAR",
            var d when d.Contains("VOCAL", StringComparison.OrdinalIgnoreCase) => "VOCALS",
            var d when d.Contains("SYNTHESIZER", StringComparison.OrdinalIgnoreCase) => "SYNTHESIZER",
            var d when d.Contains("CLAP", StringComparison.OrdinalIgnoreCase) => "CLAPPING",
            var d when d.Contains("ENGINEER", StringComparison.OrdinalIgnoreCase) => "ENGINEER",
            var d when d.Contains("PRODUCER", StringComparison.OrdinalIgnoreCase) => "PRODUCER",
            var d when d.Contains("MIXER", StringComparison.OrdinalIgnoreCase) => "MIX ENGINEER",
            var d when d.Contains("MASTERING", StringComparison.OrdinalIgnoreCase) => "MASTERING ENGINEER",
            var d when d.Contains("ASSISTANT", StringComparison.OrdinalIgnoreCase) => "ASSISTANT MIXER",
            var d when d.Contains("LYRICIST", StringComparison.OrdinalIgnoreCase) => "LYRICIST",
            var d when d.Contains("COMPOSER", StringComparison.OrdinalIgnoreCase) => "COMPOSER",
            var d when d.Contains("COMPILATION", StringComparison.OrdinalIgnoreCase) => "COMPILATION",
            var d when d.Contains("PROVIDER", StringComparison.OrdinalIgnoreCase) => "PROVIDER",
            var d when d.Contains("COUNTRY", StringComparison.OrdinalIgnoreCase) => "RELEASECOUNTRY",
            var d when d.Contains("UPC", StringComparison.OrdinalIgnoreCase) => "UPC",
            var d when d.Contains("WORK", StringComparison.OrdinalIgnoreCase) => "WORK",
            var d when d.Contains("ENCODER", StringComparison.OrdinalIgnoreCase) => "ENCODERSETTINGS",
            var d when d.Contains("REPLAYGAIN", StringComparison.OrdinalIgnoreCase) => description.ToUpper(),
            _ => description.ToUpper().Replace(" ", "_")
        };
    }

    private static string GetId3v2FrameDisplayName(string frameId)
    {
        return frameId switch
        {
            "TXXX" => "USER_TEXT",
            "TPE4" => "MODIFIER",
            "TOPE" => "ORIGINAL_PERFORMER",
            "TIT3" => "SUBTITLE",
            "TKEY" => "INITIAL_KEY",
            "TLAN" => "LANGUAGE",
            "TLEN" => "LENGTH",
            "TMED" => "MEDIA_TYPE",
            "TMOO" => "MOOD",
            "TOAL" => "ORIGINAL_ALBUM",
            "TOFN" => "ORIGINAL_FILENAME",
            "TOLY" => "ORIGINAL_LYRICIST",
            "TORY" => "ORIGINAL_YEAR",
            "TOWN" => "FILE_OWNER",
            "TPE3" => "CONDUCTOR",
            "TRSN" => "INTERNET_RADIO_STATION",
            "TRSO" => "INTERNET_RADIO_OWNER",
            "TSOA" => "ALBUM_SORT_ORDER",
            "TSOP" => "PERFORMER_SORT_ORDER",
            "TSOT" => "TITLE_SORT_ORDER",
            "TSRC" => "ISRC",
            "TSSE" => "ENCODER_SETTINGS",
            "TSST" => "SET_SUBTITLE",
            "WOAR" => "ARTIST_URL",
            "WOAF" => "AUDIO_FILE_URL",
            "WOAS" => "AUDIO_SOURCE_URL",
            "WORS" => "RADIO_STATION_URL",
            "WPAY" => "PAYMENT_URL",
            "WPUB" => "PUBLISHER_URL",
            _ => frameId
        };
    }
}
