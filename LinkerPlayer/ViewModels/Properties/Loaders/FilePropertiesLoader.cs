using ATL;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace LinkerPlayer.ViewModels.Properties.Loaders;

/// <summary>
/// Loads file properties (duration, bitrate, codec, etc.) - read-only technical information using ATL
/// </summary>
public class FilePropertiesLoader : IAtlMetadataLoader
{
    private readonly ILogger<FilePropertiesLoader> _logger;

    public FilePropertiesLoader(ILogger<FilePropertiesLoader> logger)
    {
        _logger = logger;
    }

    public void Load(Track track, ObservableCollection<TagItem> targetCollection)
    {
        if (track == null)
        {
            _logger.LogWarning("No properties found for the current file");
            return;
        }

        // Technical file properties
        // ATL.Track exposes Duration in milliseconds (int) and sample/bitrate fields via properties
        try
        {
            long durationMs = track.Duration;
            AddPropertyItem(targetCollection, "Duration", TimeSpan.FromMilliseconds(durationMs).ToString(@"mm\:ss"));
        }
        catch { }

        try
        { AddPropertyItem(targetCollection, "Bitrate", track.Bitrate > 0 ? track.Bitrate.ToString() + " kbps" : ""); }
        catch { }
        try
        { AddPropertyItem(targetCollection, "Sample Rate", track.SampleRate > 0 ? track.SampleRate.ToString() + " Hz" : ""); }
        catch { }
        try
        { AddPropertyItem(targetCollection, "Channels", track.ChannelsArrangement.NbChannels.ToString()); }
        catch { }
        try
        { AddPropertyItem(targetCollection, "Codec", track.AudioFormat?.Name ?? track.CodecFamily.ToString() ?? ""); }
        catch { }
    }

    public void LoadMultiple(IReadOnlyList<Track> audioFiles, ObservableCollection<TagItem> targetCollection)
    {
        if (audioFiles.Count == 0)
        {
            _logger.LogWarning("No files provided for multiple file properties loading");
            return;
        }

        AddPropertyItemMultiple(targetCollection, audioFiles, "Duration", f => TimeSpan.FromMilliseconds(f.Duration).ToString(@"mm\:ss"));
        AddPropertyItemMultiple(targetCollection, audioFiles, "Bitrate", f => f.Bitrate > 0 ? f.Bitrate.ToString() + " kbps" : "");
        AddPropertyItemMultiple(targetCollection, audioFiles, "Sample Rate", f => f.SampleRate > 0 ? f.SampleRate.ToString() + " Hz" : "");
        AddPropertyItemMultiple(targetCollection, audioFiles, "Channels", f => f.ChannelsArrangement.NbChannels.ToString());
        AddPropertyItemMultiple(targetCollection, audioFiles, "Codec", f => f.AudioFormat?.Name ?? f.CodecFamily.ToString() ?? "");
    }

    private static void AddPropertyItem(ObservableCollection<TagItem> collection, string name, string value)
    {
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

    private static void AddPropertyItemMultiple(ObservableCollection<TagItem> collection, IReadOnlyList<Track> files, string name, Func<Track, string> getValue)
    {
        List<string> values = files.Select(getValue).ToList();
        List<string> distinctValues = values.Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();

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
}
