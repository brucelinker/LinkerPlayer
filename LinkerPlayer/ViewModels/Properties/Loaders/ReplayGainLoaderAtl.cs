using ATL;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace LinkerPlayer.ViewModels.Properties.Loaders;

/// <summary>
/// Loads ReplayGain tags using ATL library
/// </summary>
public class ReplayGainLoaderAtl : IAtlMetadataLoader
{
    private readonly ILogger<ReplayGainLoaderAtl> _logger;

    public ReplayGainLoaderAtl(ILogger<ReplayGainLoaderAtl> logger)
    {
        _logger = logger;
    }

    public void Load(Track track, ObservableCollection<TagItem> targetCollection)
    {
        if (track == null)
        {
            _logger.LogWarning("No ATL track for ReplayGain information");
            return;
        }

        // ATL provides ReplayGain through AdditionalFields or specific properties
        // Check for common ReplayGain field names
        string? trackGain = GetReplayGainField(track, "REPLAYGAIN_TRACK_GAIN");
        string? trackPeak = GetReplayGainField(track, "REPLAYGAIN_TRACK_PEAK");
        string? albumGain = GetReplayGainField(track, "REPLAYGAIN_ALBUM_GAIN");
        string? albumPeak = GetReplayGainField(track, "REPLAYGAIN_ALBUM_PEAK");

        // Also check for iTunes-style tags in MP4 files
        if (string.IsNullOrEmpty(trackGain))
            trackGain = GetReplayGainField(track, "----:com.apple.iTunes:REPLAYGAIN_TRACK_GAIN");
        if (string.IsNullOrEmpty(trackPeak))
            trackPeak = GetReplayGainField(track, "----:com.apple.iTunes:REPLAYGAIN_TRACK_PEAK");
        if (string.IsNullOrEmpty(albumGain))
            albumGain = GetReplayGainField(track, "----:com.apple.iTunes:REPLAYGAIN_ALBUM_GAIN");
        if (string.IsNullOrEmpty(albumPeak))
            albumPeak = GetReplayGainField(track, "----:com.apple.iTunes:REPLAYGAIN_ALBUM_PEAK");

        // Add the fields - make them editable if the format supports writing
        bool isEditable = IsReplayGainEditable(track);

        AddReplayGainItem(targetCollection, "ReplayGain Track Gain", trackGain ?? "", isEditable,
            isEditable ? v => SetReplayGainField(track, "REPLAYGAIN_TRACK_GAIN", v) : null);

        AddReplayGainItem(targetCollection, "ReplayGain Track Peak", trackPeak ?? "", isEditable,
            isEditable ? v => SetReplayGainField(track, "REPLAYGAIN_TRACK_PEAK", v) : null);

        AddReplayGainItem(targetCollection, "ReplayGain Album Gain", albumGain ?? "", isEditable,
            isEditable ? v => SetReplayGainField(track, "REPLAYGAIN_ALBUM_GAIN", v) : null);

        AddReplayGainItem(targetCollection, "ReplayGain Album Peak", albumPeak ?? "", isEditable,
            isEditable ? v => SetReplayGainField(track, "REPLAYGAIN_ALBUM_PEAK", v) : null);
    }

    public void LoadMultiple(IReadOnlyList<Track> audioFiles, ObservableCollection<TagItem> targetCollection)
    {
        if (audioFiles == null || audioFiles.Count == 0)
        {
            _logger.LogWarning("No ATL tracks provided for ReplayGain information");
            return;
        }

        // For multiple files, show consolidated view
        // Collect all unique values
        var trackGains = audioFiles.Select(t => GetReplayGainField(t, "REPLAYGAIN_TRACK_GAIN")).Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();
        var trackPeaks = audioFiles.Select(t => GetReplayGainField(t, "REPLAYGAIN_TRACK_PEAK")).Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();
        var albumGains = audioFiles.Select(t => GetReplayGainField(t, "REPLAYGAIN_ALBUM_GAIN")).Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();
        var albumPeaks = audioFiles.Select(t => GetReplayGainField(t, "REPLAYGAIN_ALBUM_PEAK")).Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();

        string trackGainDisplay = trackGains.Count == 1 ? trackGains[0] : trackGains.Count > 1 ? "<various>" : "";
        string trackPeakDisplay = trackPeaks.Count == 1 ? trackPeaks[0] : trackPeaks.Count > 1 ? "<various>" : "";
        string albumGainDisplay = albumGains.Count == 1 ? albumGains[0] : albumGains.Count > 1 ? "<various>" : "";
        string albumPeakDisplay = albumPeaks.Count == 1 ? albumPeaks[0] : albumPeaks.Count > 1 ? "<various>" : "";

        // For multiple files, make editable only if all files support it and values are consistent
        bool allEditable = audioFiles.All(t => IsReplayGainEditable(t));
        bool canEditMultiple = allEditable && trackGains.Count <= 1 && trackPeaks.Count <= 1 &&
                              albumGains.Count <= 1 && albumPeaks.Count <= 1;

        AddReplayGainItem(targetCollection, "ReplayGain Track Gain", trackGainDisplay, canEditMultiple,
            canEditMultiple ? v => audioFiles.ToList().ForEach(t => SetReplayGainField(t, "REPLAYGAIN_TRACK_GAIN", v)) : null);

        AddReplayGainItem(targetCollection, "ReplayGain Track Peak", trackPeakDisplay, canEditMultiple,
            canEditMultiple ? v => audioFiles.ToList().ForEach(t => SetReplayGainField(t, "REPLAYGAIN_TRACK_PEAK", v)) : null);

        AddReplayGainItem(targetCollection, "ReplayGain Album Gain", albumGainDisplay, canEditMultiple,
            canEditMultiple ? v => audioFiles.ToList().ForEach(t => SetReplayGainField(t, "REPLAYGAIN_ALBUM_GAIN", v)) : null);

        AddReplayGainItem(targetCollection, "ReplayGain Album Peak", albumPeakDisplay, canEditMultiple,
            canEditMultiple ? v => audioFiles.ToList().ForEach(t => SetReplayGainField(t, "REPLAYGAIN_ALBUM_PEAK", v)) : null);
    }

    private string? GetReplayGainField(Track track, string fieldName)
    {
        // Try AdditionalFields first
        if (track.AdditionalFields != null && track.AdditionalFields.TryGetValue(fieldName, out var value))
        {
            return value;
        }

        // For some formats, check specific properties if available
        // ATL may expose some ReplayGain through dedicated properties in future versions

        return null;
    }

    private void SetReplayGainField(Track track, string fieldName, string value)
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

    private bool IsReplayGainEditable(Track track)
    {
        // Determine if the format supports writing ReplayGain tags
        // Most formats that support reading also support writing
        string extension = System.IO.Path.GetExtension(track.Path).ToLowerInvariant();
        return extension switch
        {
            ".flac" or ".ogg" or ".opus" or ".m4a" or ".mp4" or ".aac" => true,
            ".mp3" => true, // ID3v2 supports custom tags
            _ => false
        };
    }

    private static void AddReplayGainItem(ObservableCollection<TagItem> collection, string name, string value,
        bool isEditable, Action<string>? updateAction)
    {
        collection.Add(new TagItem
        {
            Name = name,
            Value = value,
            IsEditable = isEditable,
            UpdateAction = updateAction
        });
    }
}
