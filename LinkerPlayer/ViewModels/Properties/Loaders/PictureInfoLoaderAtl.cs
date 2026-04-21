using ATL;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;

namespace LinkerPlayer.ViewModels.Properties.Loaders;

/// <summary>
/// Loads picture/album art metadata (cover image, dimensions, file info, etc.) using ATL
/// </summary>
public class PictureInfoLoaderAtl
{
    private readonly ILogger<PictureInfoLoaderAtl> _logger;

    public PictureInfoLoaderAtl(ILogger<PictureInfoLoaderAtl> logger)
    {
        _logger = logger;
    }

    public void Load(Track track, ObservableCollection<TagItem> targetCollection)
    {
        if (track == null)
        {
            _logger.LogWarning("No ATL track for picture information");
            return;
        }

        targetCollection.Clear();

        IList<PictureInfo> pictures = track.EmbeddedPictures;

        if (pictures is { Count: > 0 })
        {
            PictureInfo pic = pictures[0];
            BitmapImage? albumCover = null;

            if (pic.PictureData is { Length: > 0 })
            {
                try
                {
                    using MemoryStream ms = new MemoryStream(pic.PictureData);
                    albumCover = new BitmapImage();
                    albumCover.BeginInit();
                    albumCover.CacheOption = BitmapCacheOption.OnLoad;
                    albumCover.StreamSource = ms;
                    albumCover.EndInit();
                    albumCover.Freeze();

                    double sizeInKB = pic.PictureData.Length / 1024.0;
                    AddPictureInfoItem(targetCollection, "Picture Size", $"{sizeInKB:F2} KB", false, null);

                    AddPictureInfoItem(targetCollection, "Picture Dimensions",
                        $"{albumCover.PixelWidth} x {albumCover.PixelHeight}", false, null);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error loading album cover image: {Message}", ex.Message);
                }
            }

            AddPictureInfoItem(targetCollection, "Picture Count", pictures.Count.ToString(), false, null);
            AddPictureInfoItem(targetCollection, "Picture Type", pic.PicType.ToString(), false, null);
            AddPictureInfoItem(targetCollection, "Picture Mime Type", pic.MimeType ?? "", false, null);

            string filename = string.IsNullOrEmpty(pic.Description)
                ? "<Embedded Image>"
                : pic.Description;
            AddPictureInfoItem(targetCollection, "Picture Filename", filename, false, null);

            // Picture Description is editable
            AddPictureInfoItem(targetCollection, "Picture Description", pic.Description ?? "", true, v =>
            {
                pic.Description = string.IsNullOrEmpty(v) ? null : v;
            });
        }
        else
        {
            _logger.LogDebug("No pictures found in file");
        }

        // Sort picture items: keep regular tags in original order, move custom tags (with angle brackets) to bottom
        List<TagItem> regularPictureTags = targetCollection.Where(item => !item.Name.StartsWith("<")).ToList();
        List<TagItem> customPictureTags = targetCollection.Where(item => item.Name.StartsWith("<")).ToList();

        targetCollection.Clear();
        foreach (TagItem item in regularPictureTags)
        {
            targetCollection.Add(item);
        }
        foreach (TagItem item in customPictureTags)
        {
            targetCollection.Add(item);
        }
    }

    public void LoadMultiple(IReadOnlyList<Track> tracks, ObservableCollection<TagItem> targetCollection)
    {
        if (tracks == null || tracks.Count == 0)
        {
            _logger.LogWarning("No ATL tracks provided for picture information loading");
            return;
        }

        targetCollection.Clear();

        Dictionary<int, int> pictureCountValues = new Dictionary<int, int>();
        Dictionary<string, int> pictureTypeValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> pictureMimeTypeValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> pictureFilenameValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> pictureDescriptionValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> pictureSizeValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> pictureDimensionsValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        HashSet<int> pictureDataHashes = new HashSet<int>();
        bool anyFileHasPictures = false;
        bool allCoversSame = true;

        foreach (Track track in tracks)
        {
            if (track == null)
            {
                continue;
            }

            IList<PictureInfo> pictures = track.EmbeddedPictures;

            if (pictures is { Count: > 0 })
            {
                anyFileHasPictures = true;
                PictureInfo pic = pictures[0];

                int count = pictures.Count;
                if (!pictureCountValues.ContainsKey(count))
                {
                    pictureCountValues[count] = 0;
                }
                pictureCountValues[count]++;

                string type = pic.PicType.ToString();
                if (!pictureTypeValues.ContainsKey(type))
                {
                    pictureTypeValues[type] = 0;
                }
                pictureTypeValues[type]++;

                string mimeType = pic.MimeType ?? "";
                if (!pictureMimeTypeValues.ContainsKey(mimeType))
                {
                    pictureMimeTypeValues[mimeType] = 0;
                }
                pictureMimeTypeValues[mimeType]++;

                string filename = string.IsNullOrEmpty(pic.Description) ? "<Embedded Image>" : pic.Description;
                if (!pictureFilenameValues.ContainsKey(filename))
                {
                    pictureFilenameValues[filename] = 0;
                }
                pictureFilenameValues[filename]++;

                string description = pic.Description ?? "";
                if (!pictureDescriptionValues.ContainsKey(description))
                {
                    pictureDescriptionValues[description] = 0;
                }
                pictureDescriptionValues[description]++;

                if (pic.PictureData is { Length: > 0 })
                {
                    int dataHash = ComputeSimpleHash(pic.PictureData);
                    pictureDataHashes.Add(dataHash);

                    double sizeInKB = pic.PictureData.Length / 1024.0;
                    string sizeStr = $"{sizeInKB:F2} KB";
                    if (!pictureSizeValues.ContainsKey(sizeStr))
                    {
                        pictureSizeValues[sizeStr] = 0;
                    }
                    pictureSizeValues[sizeStr]++;

                    try
                    {
                        using MemoryStream ms = new MemoryStream(pic.PictureData);
                        BitmapImage tempImage = new BitmapImage();
                        tempImage.BeginInit();
                        tempImage.CacheOption = BitmapCacheOption.OnLoad;
                        tempImage.StreamSource = ms;
                        tempImage.EndInit();
                        tempImage.Freeze();

                        string dimensions = $"{tempImage.PixelWidth} x {tempImage.PixelHeight}";
                        if (!pictureDimensionsValues.ContainsKey(dimensions))
                        {
                            pictureDimensionsValues[dimensions] = 0;
                        }
                        pictureDimensionsValues[dimensions]++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error loading dimensions for multi-selection: {Message}", ex.Message);
                    }
                }
            }
            else
            {
                allCoversSame = false;
            }
        }

        if (!anyFileHasPictures)
        {
            _logger.LogDebug("No pictures found in any of the {Count} selected tracks", tracks.Count);
            return;
        }

        if (pictureDataHashes.Count > 1)
        {
            allCoversSame = false;
        }

        string pictureSizeDisplay = pictureSizeValues.Count == 1
            ? pictureSizeValues.Keys.First()
            : "<various>";
        AddPictureInfoItem(targetCollection, "Picture Size", pictureSizeDisplay, false, null);

        string pictureDimensionsDisplay = pictureDimensionsValues.Count == 1
            ? pictureDimensionsValues.Keys.First()
            : "<various>";
        AddPictureInfoItem(targetCollection, "Picture Dimensions", pictureDimensionsDisplay, false, null);

        string pictureCountDisplay = pictureCountValues.Count == 1
            ? pictureCountValues.Keys.First().ToString()
            : "<various>";
        AddPictureInfoItem(targetCollection, "Picture Count", pictureCountDisplay, false, null);

        string pictureTypeDisplay = pictureTypeValues.Count == 1
            ? pictureTypeValues.Keys.First()
            : "<various>";
        AddPictureInfoItem(targetCollection, "Picture Type", pictureTypeDisplay, false, null);

        string pictureMimeTypeDisplay = pictureMimeTypeValues.Count == 1
            ? pictureMimeTypeValues.Keys.First()
            : "<various>";
        AddPictureInfoItem(targetCollection, "Picture Mime Type", pictureMimeTypeDisplay, false, null);

        string pictureFilenameDisplay = pictureFilenameValues.Count == 1
            ? pictureFilenameValues.Keys.First()
            : "<various>";
        AddPictureInfoItem(targetCollection, "Picture Filename", pictureFilenameDisplay, false, null);

        string pictureDescriptionDisplay = pictureDescriptionValues.Count == 1
            ? pictureDescriptionValues.Keys.First()
            : "<various>";
        AddPictureInfoItem(targetCollection, "Picture Description", pictureDescriptionDisplay, false, null);

        _logger.LogDebug("Loaded picture info for {Count} tracks with pictures (allSame={AllSame})", tracks.Count, allCoversSame);
    }

    private static int ComputeSimpleHash(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return 0;
        }

        unchecked
        {
            int hash = 17;
            int step = Math.Max(1, data.Length / 100);

            for (int i = 0; i < data.Length; i += step)
            {
                hash = hash * 31 + data[i];
            }

            hash = hash * 31 + data.Length;

            return hash;
        }
    }

    private static void AddPictureInfoItem(ObservableCollection<TagItem> collection, string name, string value,
        bool isEditable, Action<string>? updateAction)
    {
        TagItem item = new TagItem
        {
            Name = name,
            Value = value,
            IsEditable = isEditable,
            UpdateAction = isEditable ? updateAction : null
        };

        collection.Add(item);
    }
}
