using ATL;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace LinkerPlayer.ViewModels.Properties.Loaders;

/// <summary>
/// Loads custom/non-standard metadata tags using ATL library
/// </summary>
public class CustomMetadataLoaderAtl : IAtlMetadataLoader
{
    private readonly ILogger<CustomMetadataLoaderAtl> _logger;

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

    public CustomMetadataLoaderAtl(ILogger<CustomMetadataLoaderAtl> logger)
    {
        _logger = logger;
    }

    public void Load(Track track, ObservableCollection<TagItem> targetCollection)
    {
        if (track == null)
        {
            _logger.LogWarning("No ATL track for custom metadata");
            return;
        }

        if (track.AdditionalFields == null || track.AdditionalFields.Count == 0)
        {
            _logger.LogDebug("No additional fields found in ATL track");
            return;
        }

        // Add custom fields from AdditionalFields
        foreach (var field in track.AdditionalFields.OrderBy(f => f.Key))
        {
            string fieldName = field.Key;
            string fieldValue = field.Value;

            // Skip standard fields
            if (StandardFields.Contains(fieldName))
            {
                continue;
            }

            // Skip picture-related fields
            if (PictureFields.Contains(fieldName))
            {
                continue;
            }

            // Skip empty values
            if (string.IsNullOrWhiteSpace(fieldValue))
            {
                continue;
            }

            targetCollection.Add(new TagItem
            {
                Name = $"<{fieldName}>", // Angle brackets indicate custom field
                Value = fieldValue,
                IsEditable = IsFieldEditable(track, fieldName),
                UpdateAction = IsFieldEditable(track, fieldName) ? v => UpdateField(track, fieldName, v) : null
            });
        }

        _logger.LogDebug("Loaded {Count} custom metadata fields from ATL track", targetCollection.Count);
    }

    public void LoadMultiple(IReadOnlyList<Track> audioFiles, ObservableCollection<TagItem> targetCollection)
    {
        if (audioFiles == null || audioFiles.Count == 0)
        {
            _logger.LogWarning("No ATL tracks provided for custom metadata loading");
            return;
        }

        // Aggregate custom fields across all files, showing "<various>" when values differ
        Dictionary<string, Dictionary<string, int>> allCustomFields = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<int>> fieldPresenceByFile = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        for (int fileIndex = 0; fileIndex < audioFiles.Count; fileIndex++)
        {
            Track track = audioFiles[fileIndex];
            if (track?.AdditionalFields == null || track.AdditionalFields.Count == 0)
            {
                continue;
            }

            foreach (var field in track.AdditionalFields)
            {
                string fieldName = field.Key;
                string fieldValue = field.Value;

                // Skip standard and picture fields
                if (StandardFields.Contains(fieldName) || PictureFields.Contains(fieldName))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(fieldValue))
                {
                    continue;
                }

                if (!allCustomFields.ContainsKey(fieldName))
                {
                    allCustomFields[fieldName] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    fieldPresenceByFile[fieldName] = new HashSet<int>();
                }

                if (!allCustomFields[fieldName].ContainsKey(fieldValue))
                {
                    allCustomFields[fieldName][fieldValue] = 0;
                }
                allCustomFields[fieldName][fieldValue]++;
                fieldPresenceByFile[fieldName].Add(fileIndex);
            }
        }

        // Add aggregated fields to collection
        foreach (var fieldGroup in allCustomFields.OrderBy(f => f.Key))
        {
            string fieldName = fieldGroup.Key;
            var valueCounts = fieldGroup.Value;

            string displayValue;
            bool isEditable = false;
            Action<string>? updateAction = null;

            if (valueCounts.Count == 1 && fieldPresenceByFile[fieldName].Count == audioFiles.Count)
            {
                // All files have the same value
                displayValue = valueCounts.Keys.First();
                isEditable = audioFiles.All(t => IsFieldEditable(t, fieldName));
                if (isEditable)
                {
                    updateAction = v => audioFiles.ToList().ForEach(t => UpdateField(t, fieldName, v));
                }
            }
            else if (valueCounts.Count > 1)
            {
                // Multiple different values
                displayValue = "<various>";
            }
            else
            {
                // Some files have values, some don't
                displayValue = "<various>";
            }

            targetCollection.Add(new TagItem
            {
                Name = $"<{fieldName}>",
                Value = displayValue,
                IsEditable = isEditable,
                UpdateAction = updateAction
            });
        }

        _logger.LogDebug("Loaded {Count} aggregated custom metadata fields from {FileCount} ATL tracks",
            targetCollection.Count, audioFiles.Count);
    }

    private bool IsFieldEditable(Track track, string fieldName)
    {
        // Determine if the format supports writing custom fields
        // Most formats that support AdditionalFields also support writing them
        string extension = System.IO.Path.GetExtension(track.Path).ToLowerInvariant();
        return extension switch
        {
            ".flac" or ".ogg" or ".opus" => true, // Vorbis comments
            ".mp3" => true, // ID3v2 custom tags
            ".m4a" or ".mp4" or ".aac" => true, // iTunes-style custom tags
            ".ape" => true, // APE tags
            _ => false
        };
    }

    private void UpdateField(Track track, string fieldName, string value)
    {
        if (track.AdditionalFields == null)
        {
            track.AdditionalFields = new Dictionary<string, string>();
        }

        if (string.IsNullOrEmpty(value))
        {
            track.AdditionalFields.Remove(fieldName);
        }
        else
        {
            track.AdditionalFields[fieldName] = value;
        }
    }
}
