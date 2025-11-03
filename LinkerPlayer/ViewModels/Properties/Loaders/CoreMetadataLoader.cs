using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using File = TagLib.File;
using Tag = TagLib.Tag;

namespace LinkerPlayer.ViewModels.Properties.Loaders;

/// <summary>
/// Loads core metadata fields (Title, Artist, Album, Year, Genre, etc.)
/// </summary>
public class CoreMetadataLoader : IMetadataLoader
{
    private readonly IMediaFileHelper _mediaFileHelper;
    private readonly ILogger<CoreMetadataLoader> _logger;

    public CoreMetadataLoader(IMediaFileHelper mediaFileHelper, ILogger<CoreMetadataLoader> logger)
    {
        _mediaFileHelper = mediaFileHelper;
        _logger = logger;
    }

    public void Load(File audioFile, ObservableCollection<TagItem> targetCollection)
    {
        if (audioFile?.Tag == null)
        {
            _logger.LogWarning("No metadata found for the current file");
            return;
        }

        // DON'T clear - ViewModel handles this
        // targetCollection.Clear();

        _logger.LogInformation("CoreMetadataLoader.Load: Starting with {Count} items in collection", targetCollection.Count);

        Tag tag = audioFile.Tag;

        // Title
        AddMetadataItem(targetCollection, "Title", tag.Title ?? "", true, v =>
           {
               tag.Title = string.IsNullOrEmpty(v) ? null : v;
           });

        _logger.LogInformation("CoreMetadataLoader.Load: After adding Title, collection has {Count} items", targetCollection.Count);

        // Artist - use smart field selection
        string artistValue = _mediaFileHelper.GetBestArtistField(tag);
        AddMetadataItem(targetCollection, "Artist", artistValue, true, v =>
        {
            tag.Performers = string.IsNullOrEmpty(v)
  ? []
             : v.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        });

        // Album
        AddMetadataItem(targetCollection, "Album", tag.Album ?? "", true, v =>
        {
            tag.Album = string.IsNullOrEmpty(v) ? null : v;
        });

        // Album Artist - use smart field selection
        string albumArtistValue = _mediaFileHelper.GetBestAlbumArtistField(tag);
        AddMetadataItem(targetCollection, "Album Artist", albumArtistValue, true, v =>
               {
                   tag.AlbumArtists = string.IsNullOrEmpty(v)
                   ? []
             : v.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
               });

        // Track Number
        AddMetadataItem(targetCollection, "Track Number", tag.Track > 0 ? tag.Track.ToString() : "", true, v =>
        {
            tag.Track = uint.TryParse(v, out uint track) ? track : 0;
        });

        // Total Tracks
        AddMetadataItem(targetCollection, "Total Tracks", tag.TrackCount > 0 ? tag.TrackCount.ToString() : "", true, v =>
 {
     tag.TrackCount = uint.TryParse(v, out uint count) ? count : 0;
 });

        // Disc Number
        AddMetadataItem(targetCollection, "Disc Number", tag.Disc > 0 ? tag.Disc.ToString() : "", true, v =>
        {
            tag.Disc = uint.TryParse(v, out uint disc) ? disc : 0;
        });

        // Total Discs
        AddMetadataItem(targetCollection, "Total Discs", tag.DiscCount > 0 ? tag.DiscCount.ToString() : "", true, v =>
{
    tag.DiscCount = uint.TryParse(v, out uint count) ? count : 0;
});

        // Year
        AddMetadataItem(targetCollection, "Year", tag.Year > 0 ? tag.Year.ToString() : "", true, v =>
          {
              tag.Year = uint.TryParse(v, out uint year) ? year : 0;
          });

        // Genre
        AddMetadataItem(targetCollection, "Genre", tag.FirstGenre ?? string.Join(", ", tag.Genres ?? []), true, v =>
        {
            tag.Genres = string.IsNullOrEmpty(v)
                    ? []
                   : v.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        });

        // Composer
        AddMetadataItem(targetCollection, "Composer", tag.FirstComposer ?? string.Join(", ", tag.Composers ?? []), true, v =>
        {
            tag.Composers = string.IsNullOrEmpty(v)
                ? []
       : v.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        });

        // Copyright
        AddMetadataItem(targetCollection, "Copyright", tag.Copyright ?? "", true, v =>
        {
            tag.Copyright = string.IsNullOrEmpty(v) ? null : v;
        });

        // Beats Per Minute
        AddMetadataItem(targetCollection, "Beats Per Minute", tag.BeatsPerMinute > 0 ? tag.BeatsPerMinute.ToString() : "", true, v =>
        {
            tag.BeatsPerMinute = uint.TryParse(v, out uint bpm) ? bpm : 0;
        });

        // Conductor
        AddMetadataItem(targetCollection, "Conductor", tag.Conductor ?? "", true, v =>
    {
        tag.Conductor = string.IsNullOrEmpty(v) ? null : v;
    });

        // Grouping
        AddMetadataItem(targetCollection, "Grouping", tag.Grouping ?? "", true, v =>
        {
            tag.Grouping = string.IsNullOrEmpty(v) ? null : v;
        });

        // Publisher
        AddMetadataItem(targetCollection, "Publisher", tag.Publisher ?? "", true, v =>
             {
                 tag.Publisher = string.IsNullOrEmpty(v) ? null : v;
             });

        // ISRC (only show if it has a value - most files don't have this)
        if (!string.IsNullOrWhiteSpace(tag.ISRC))
        {
            AddMetadataItem(targetCollection, "ISRC", tag.ISRC, true, v =>
          {
              tag.ISRC = string.IsNullOrEmpty(v) ? null : v;
          });
        }
    }

    public void LoadMultiple(IReadOnlyList<File> audioFiles, ObservableCollection<TagItem> targetCollection)
    {
        if (audioFiles.Count == 0)
        {
            _logger.LogWarning("No files provided for multiple core metadata loading");
            return;
        }

        // DON'T clear - ViewModel handles this
        // targetCollection.Clear();

        // For each metadata field, check if all files have the same value
        // If they differ, show "<various>"
        AddMetadataItemMultiple(targetCollection, audioFiles, "Title", f => f.Tag.Title);
        AddMetadataItemMultiple(targetCollection, audioFiles, "Artist", f => _mediaFileHelper.GetBestArtistField(f.Tag));
        AddMetadataItemMultiple(targetCollection, audioFiles, "Album", f => f.Tag.Album);
        AddMetadataItemMultiple(targetCollection, audioFiles, "Album Artist", f => _mediaFileHelper.GetBestAlbumArtistField(f.Tag));
        AddMetadataItemMultiple(targetCollection, audioFiles, "Track Number", f => f.Tag.Track > 0 ? f.Tag.Track.ToString() : "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Total Tracks", f => f.Tag.TrackCount > 0 ? f.Tag.TrackCount.ToString() : "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Disc Number", f => f.Tag.Disc > 0 ? f.Tag.Disc.ToString() : "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Total Discs", f => f.Tag.DiscCount > 0 ? f.Tag.DiscCount.ToString() : "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Year", f => f.Tag.Year > 0 ? f.Tag.Year.ToString() : "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Genre", f => f.Tag.FirstGenre ?? string.Join(", ", f.Tag.Genres ?? []));
        AddMetadataItemMultiple(targetCollection, audioFiles, "Composer", f => f.Tag.FirstComposer ?? string.Join(", ", f.Tag.Composers ?? []));
        AddMetadataItemMultiple(targetCollection, audioFiles, "Copyright", f => f.Tag.Copyright);
        AddMetadataItemMultiple(targetCollection, audioFiles, "Beats Per Minute", f => f.Tag.BeatsPerMinute > 0 ? f.Tag.BeatsPerMinute.ToString() : "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Conductor", f => f.Tag.Conductor);
        AddMetadataItemMultiple(targetCollection, audioFiles, "Grouping", f => f.Tag.Grouping);
        AddMetadataItemMultiple(targetCollection, audioFiles, "Publisher", f => f.Tag.Publisher);

        _logger.LogDebug("Loaded core metadata for {Count} files", audioFiles.Count);
    }

    private static void AddMetadataItem(ObservableCollection<TagItem> collection, string name, string value,
      bool isEditable, Action<string> updateAction)
    {
        collection.Add(new TagItem
        {
            Name = name,
            Value = value,
            IsEditable = isEditable,
            UpdateAction = isEditable ? updateAction : null
        });
    }

    private void AddMetadataItemMultiple(ObservableCollection<TagItem> collection, IReadOnlyList<File> files,
        string name, Func<File, string?> getValue)
    {
        var values = files.Select(getValue).ToList();
        var distinctValues = values.Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();

        string displayValue = distinctValues.Count switch
        {
            0 => "",
            1 => distinctValues[0] ?? "",
            _ => "<various>"
        };

        collection.Add(new TagItem
        {
            Name = name,
            Value = displayValue,
            IsEditable = true,
            UpdateAction = v =>
       {
           // Only update if user changed the value (not <various>)
           if (v != "<various>")
           {
               UpdateAllFiles(files, name, v);
           }
       }
        });
    }

    private void UpdateAllFiles(IReadOnlyList<File> files, string fieldName, string value)
    {
        foreach (var file in files)
        {
            var tag = file.Tag;

            switch (fieldName)
            {
                case "Title":
                    tag.Title = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Artist":
                    tag.Performers = string.IsNullOrEmpty(value)
                ? []
            : value.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                    break;
                case "Album":
                    tag.Album = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Album Artist":
                    tag.AlbumArtists = string.IsNullOrEmpty(value)
                        ? []
                    : value.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                    break;
                case "Track Number":
                    tag.Track = uint.TryParse(value, out uint track) ? track : 0;
                    break;
                case "Total Tracks":
                    tag.TrackCount = uint.TryParse(value, out uint trackCount) ? trackCount : 0;
                    break;
                case "Disc Number":
                    tag.Disc = uint.TryParse(value, out uint disc) ? disc : 0;
                    break;
                case "Total Discs":
                    tag.DiscCount = uint.TryParse(value, out uint discCount) ? discCount : 0;
                    break;
                case "Year":
                    tag.Year = uint.TryParse(value, out uint year) ? year : 0;
                    break;
                case "Genre":
                    tag.Genres = string.IsNullOrEmpty(value)
                    ? []
                   : value.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                    break;
                case "Composer":
                    tag.Composers = string.IsNullOrEmpty(value)
                    ? []
                     : value.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                    break;
                case "Copyright":
                    tag.Copyright = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Beats Per Minute":
                    tag.BeatsPerMinute = uint.TryParse(value, out uint bpm) ? bpm : 0;
                    break;
                case "Conductor":
                    tag.Conductor = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Grouping":
                    tag.Grouping = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Publisher":
                    tag.Publisher = string.IsNullOrEmpty(value) ? null : value;
                    break;
            }
        }

        _logger.LogDebug("Updated field '{FieldName}' to '{Value}' for {Count} files", fieldName, value, files.Count);
    }
}
