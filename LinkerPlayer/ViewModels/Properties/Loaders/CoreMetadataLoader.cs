using ATL;
using LinkerPlayer.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace LinkerPlayer.ViewModels.Properties.Loaders;

/// <summary>
/// Loads core metadata fields using ATL
/// </summary>
public class CoreMetadataLoader : IAtlMetadataLoader
{
    private readonly IMediaFileHelper _mediaFileHelper;
    private readonly ILogger<CoreMetadataLoader> _logger;

    public CoreMetadataLoader(IMediaFileHelper mediaFileHelper, ILogger<CoreMetadataLoader> logger)
    {
        _mediaFileHelper = mediaFileHelper;
        _logger = logger;
    }

    /// <summary>
    /// Load metadata for a single file into the properties window
    /// </summary>
    public void Load(Track track, ObservableCollection<TagItem> targetCollection)
    {
        if (track == null)
        {
            _logger.LogWarning("No metadata found for the current file");
            return;
        }

        //_logger.LogInformation("CoreMetadataLoader.Load: Starting with {Count} items in collection", targetCollection.Count);

        // Title
        AddMetadataItem(targetCollection, "Title", track.Title ?? "", true, v =>
        {
            track.Title = string.IsNullOrEmpty(v) ? null : v;
        });

        // Artist
        string artistValue = _mediaFileHelper.GetBestArtistField(track);
        AddMetadataItem(targetCollection, "Artist", artistValue, true, v =>
        {
            track.Artist = string.IsNullOrEmpty(v) ? null : v;        // ATL uses single string
        });

        // Album
        AddMetadataItem(targetCollection, "Album", track.Album ?? "", true, v =>
        {
            track.Album = string.IsNullOrEmpty(v) ? null : v;
        });

        // Album Artist
        string albumArtistValue = _mediaFileHelper.GetBestAlbumArtistField(track);
        AddMetadataItem(targetCollection, "Album Artist", albumArtistValue, true, v =>
        {
            track.AlbumArtist = string.IsNullOrEmpty(v) ? null : v;
        });

        // Track Number
        AddMetadataItem(targetCollection, "Track Number",
            track.TrackNumber > 0 ? track.TrackNumber.ToString() : "", true, v =>
            {
                track.TrackNumber = int.TryParse(v, out int num) ? num : 0;
            });

        // Total Tracks
        AddMetadataItem(targetCollection, "Total Tracks",
            track.TrackTotal > 0 ? track.TrackTotal.ToString() : "", true, v =>
            {
                track.TrackTotal = int.TryParse(v, out int total) ? total : 0;
            });

        // Disc Number
        AddMetadataItem(targetCollection, "Disc Number",
            track.DiscNumber > 0 ? track.DiscNumber.ToString() : "", true, v =>
            {
                track.DiscNumber = int.TryParse(v, out int num) ? num : 0;
            });

        // Total Discs
        AddMetadataItem(targetCollection, "Total Discs",
            track.DiscTotal > 0 ? track.DiscTotal.ToString() : "", true, v =>
            {
                track.DiscTotal = int.TryParse(v, out int total) ? total : 0;
            });

        // Year
        AddMetadataItem(targetCollection, "Year",
            track.Year > 0 ? track.Year.ToString() : "", true, v =>
            {
                track.Year = int.TryParse(v, out int year) ? year : 0;
            });

        // Genre (ATL uses single string, not array)
        AddMetadataItem(targetCollection, "Genre", track.Genre ?? "", true, v =>
        {
            track.Genre = string.IsNullOrEmpty(v) ? null : v;
        });

        // Composer
        AddMetadataItem(targetCollection, "Composer", track.Composer ?? "", true, v =>
        {
            track.Composer = string.IsNullOrEmpty(v) ? null : v;
        });

        // Copyright
        AddMetadataItem(targetCollection, "Copyright", track.Copyright ?? "", true, v =>
        {
            track.Copyright = string.IsNullOrEmpty(v) ? null : v;
        });

        // Comment
        AddMetadataItem(targetCollection, "Comment", track.Comment ?? "", true, v =>
        {
            track.Comment = string.IsNullOrEmpty(v) ? null : v;
        });

        // Conductor
        AddMetadataItem(targetCollection, "Conductor", track.Conductor ?? "", true, v =>
        {
            track.Conductor = string.IsNullOrEmpty(v) ? null : v;
        });

        // Grouping
        string groupingValue = track.AdditionalFields.TryGetValue("GROUPING", out string? g) ? g : "";
        AddMetadataItem(targetCollection, "Grouping", groupingValue, true, v =>
        {
            if (string.IsNullOrEmpty(v))
                track.AdditionalFields.Remove("GROUPING");
            else
                track.AdditionalFields["GROUPING"] = v;
        });

        // Beats Per Minute
        AddMetadataItem(targetCollection, "Beats Per Minute",
            track.BPM > 0 ? track.BPM.ToString() : "", true, v =>
            {
                track.BPM = uint.TryParse(v, out uint bpm) ? bpm : 0;
            });

        // Publisher (if supported, otherwise you can use AdditionalFields)
        AddMetadataItem(targetCollection, "Publisher", track.Publisher ?? "", true, v =>
        {
            track.Publisher = string.IsNullOrEmpty(v) ? null : v;
        });

        // ISRC
        if (!string.IsNullOrWhiteSpace(track.ISRC))
        {
            AddMetadataItem(targetCollection, "ISRC", track.ISRC, true, v =>
            {
                track.ISRC = string.IsNullOrEmpty(v) ? null : v;
            });
        }

        // Rating (0–5 stars). Popularity↔stars scaling is format-dependent (Vorbis 0–1,
        // ID3v2 0–255); use the shared helper so it round-trips with the library's read path.
        // For Vorbis the fractional rating lives in the RATING additional field (Popularity
        // is rounded by ATL), so mirror the read preference from MediaFile.UpdateFromFileMetadata.
        double scale = Models.MediaFileHelper.RatingScaleForPath(track.Path);
        double ratingStars = 0.0;
        if (scale <= 1.0 && track.AdditionalFields != null
            && track.AdditionalFields.TryGetValue("RATING", out string? ratingStr)
            && double.TryParse(ratingStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double ratingRaw) && ratingRaw > 0)
        {
            double normalized = ratingRaw <= 1.0 ? ratingRaw : ratingRaw / 100.0;
            ratingStars = Math.Round(Math.Clamp(normalized * 5.0, 0.0, 5.0), 1);
        }
        else
        {
            double rawPop = (double)(track.Popularity ?? 0f);
            ratingStars = rawPop > 0 ? Models.MediaFileHelper.PopularityToStars((float)rawPop, track.Path) : 0.0;
        }
        AddMetadataItem(targetCollection, "Rating", ratingStars > 0 ? ratingStars.ToString("0.0") : "", true, v =>
        {
            double stars = 0.0;
            if (double.TryParse(v, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            {
                stars = Math.Clamp(Math.Round(parsed, 1), 0.0, 5.0);
            }

            // Use Popularity field for all writes (ATL's AdditionalFields don't persist for Vorbis).
            // MediaFileHelper.StarsToPopularity handles format-specific scaling.
            track.Popularity = Models.MediaFileHelper.StarsToPopularity(stars, track.Path);

            PersistRatingToLibrary(track.Path, stars);
        });
    }

    /// <summary>
    /// Persists a rating to the in-memory <see cref="MediaFile"/> (which the Library DataGrid
    /// binds to) and the database, raising PropertyChanged so the grid reflects it immediately.
    /// The file tag itself is written by the caller's ATL save path. Resolved by file path so it
    /// works for both single- and multi-selection without changing loader signatures.
    /// </summary>
    private static void PersistRatingToLibrary(string? path, double rating)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        Core.IMusicLibrary? library = App.AppHost?.Services?.GetService<Core.IMusicLibrary>();
        if (library == null)
            return;

        MediaFile? mediaFile = library.MainLibrary.FirstOrDefault(t =>
            string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (mediaFile == null)
            return;

        string trackId = mediaFile.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                await library.UpdateRatingAsync(trackId, rating).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                App.AppHost?.Services?.GetService<ILogger<CoreMetadataLoader>>()
                    ?.LogError(ex, "Failed to persist rating {Rating} for {Path}", rating, path);
            }
        });
    }

    /// <summary>
    /// Load metadata for multiple files (shows &lt;various&gt; when values differ)
    /// </summary>
    public void LoadMultiple(IReadOnlyList<Track> audioFiles, ObservableCollection<TagItem> targetCollection)
    {
        if (audioFiles.Count == 0)
            return;

        // Title
        AddMetadataItemMultiple(targetCollection, audioFiles, "Title", f => f.Title);
        AddMetadataItemMultiple(targetCollection, audioFiles, "Artist", f => _mediaFileHelper.GetBestArtistField(f));
        AddMetadataItemMultiple(targetCollection, audioFiles, "Album", f => f.Album);
        AddMetadataItemMultiple(targetCollection, audioFiles, "Album Artist", f => _mediaFileHelper.GetBestAlbumArtistField(f));
        AddMetadataItemMultiple(targetCollection, audioFiles, "Track Number", f => f.TrackNumber > 0 ? f.TrackNumber.ToString() : "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Total Tracks", f => f.TrackTotal > 0 ? f.TrackTotal.ToString() : "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Disc Number", f => f.DiscNumber > 0 ? f.DiscNumber.ToString() : "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Total Discs", f => f.DiscTotal > 0 ? f.DiscTotal.ToString() : "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Year", f => f.Year > 0 ? f.Year.ToString() : "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Genre", f => f.Genre ?? "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Composer", f => f.Composer ?? "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Copyright", f => f.Copyright ?? "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Comment", f => f.Comment ?? "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Conductor", f => f.Conductor ?? "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Grouping", f => ""); // ATL doesn't have Grouping
        AddMetadataItemMultiple(targetCollection, audioFiles, "Beats Per Minute", f => ""); // ATL doesn't have BPM
        AddMetadataItemMultiple(targetCollection, audioFiles, "Publisher", f => f.Publisher ?? "");
        AddMetadataItemMultiple(targetCollection, audioFiles, "Rating",
            f => (f.Popularity ?? 0f) > 0 ? Math.Round((double)f.Popularity! * 5.0 / 255.0, 1).ToString("0.0") : "");

        _logger.LogDebug("Loaded core metadata for {Count} files", audioFiles.Count);
    }

    // ==================================================================
    // Helper methods (mostly unchanged, except parameter types)
    // ==================================================================

    private static void AddMetadataItem(ObservableCollection<TagItem> collection, string name, string? value,
        bool isEditable, Action<string> updateAction)
    {
        collection.Add(new TagItem
        {
            Name = name,
            Value = value ?? string.Empty,
            IsEditable = isEditable,
            UpdateAction = isEditable ? updateAction : null
        });
    }

    private void AddMetadataItemMultiple(ObservableCollection<TagItem> collection, IReadOnlyList<Track> files,
        string name, Func<Track, string?> getValue)
    {
        List<string?> values = files.Select(getValue).ToList();
        List<string> distinctValues = values.Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList()!;
        bool hasMultiple = distinctValues.Count > 1;

        string displayValue = distinctValues.Count switch
        {
            0 => "",
            1 => distinctValues[0] ?? "",
            _ => $"<various> {string.Join("; ", distinctValues)}"
        };

        collection.Add(new TagItem
        {
            Name = name,
            Value = displayValue,
            OriginalValue = displayValue,
            IsEditable = true,
            HasMultipleValues = hasMultiple,
            UpdateAction = v =>
            {
                // Strip the <various> prefix if the user didn't change the value
                string cleanValue = v.StartsWith("<various> ", StringComparison.Ordinal)
                    ? v["<various> ".Length..]
                    : v;
                UpdateAllFiles(files, name, cleanValue);
            }
        });
    }

    private void UpdateAllFiles(IReadOnlyList<Track> files, string fieldName, string value)
    {
        foreach (Track track in files)
        {
            switch (fieldName)
            {
                case "Title":
                    track.Title = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Artist":
                    track.Artist = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Album":
                    track.Album = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Album Artist":
                    track.AlbumArtist = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Track Number":
                    track.TrackNumber = int.TryParse(value, out int trackNum) ? trackNum : 0;
                    break;
                case "Total Tracks":
                    track.TrackTotal = int.TryParse(value, out int totalTracks) ? totalTracks : 0;
                    break;
                case "Disc Number":
                    track.DiscNumber = int.TryParse(value, out int disc) ? disc : 0;
                    break;
                case "Total Discs":
                    track.DiscTotal = int.TryParse(value, out int totalDiscs) ? totalDiscs : 0;
                    break;
                case "Year":
                    track.Year = int.TryParse(value, out int year) ? year : 0;
                    break;
                case "Genre":
                    track.Genre = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Composer":
                    track.Composer = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Copyright":
                    track.Copyright = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Comment":
                    track.Comment = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Conductor":
                    track.Conductor = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Publisher":
                    track.Publisher = string.IsNullOrEmpty(value) ? null : value;
                    break;
                case "Rating":
                    double ratingStars = 0.0;
                    if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                    {
                        ratingStars = Math.Clamp(Math.Round(parsed, 1), 0.0, 5.0);
                    }
                    // Use the helper to get correct format-specific scaling (0-1 for Vorbis, 0-255 for ID3v2)
                    track.Popularity = Models.MediaFileHelper.StarsToPopularity(ratingStars, track.Path);
                    PersistRatingToLibrary(track.Path, ratingStars);
                    break;
            }
        }

        _logger.LogDebug("Updated field '{FieldName}' to '{Value}' for {Count} files", fieldName, value, files.Count);
    }
}
