using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using File = TagLib.File;

namespace LinkerPlayer.ViewModels.Properties.Loaders;

/// <summary>
/// Loads picture/album art metadata (cover image, dimensions, file info, etc.)
/// </summary>
public class PictureInfoLoader : IMetadataLoader
{
    private readonly ILogger<PictureInfoLoader> _logger;

    public PictureInfoLoader(ILogger<PictureInfoLoader> logger)
    {
        _logger = logger;
    }

    public void Load(File audioFile, ObservableCollection<TagItem> targetCollection)
    {
        if (audioFile?.Tag == null)
        {
            _logger.LogWarning("No tag data found for picture information");
            return;
        }

        targetCollection.Clear();

        TagLib.Tag tag = audioFile.Tag;

        if (tag.Pictures is { Length: > 0 })
        {
            TagLib.IPicture pic = tag.Pictures[0];
            BitmapImage? albumCover = null;

            if (pic.Data?.Data is { Length: > 0 })
            {
                try
                {
                    using MemoryStream ms = new MemoryStream(pic.Data.Data);
                    albumCover = new BitmapImage();
                    albumCover.BeginInit();
                    albumCover.CacheOption = BitmapCacheOption.OnLoad;
                    albumCover.StreamSource = ms;
                    albumCover.EndInit();
                    albumCover.Freeze();

                    // Calculate and add picture size in KB
                    double sizeInKB = pic.Data.Data.Length / 1024.0;
                    AddPictureInfoItem(targetCollection, "Picture Size", $"{sizeInKB:F2} KB", false, null);

                    // Add picture dimensions (width x height)
                    AddPictureInfoItem(targetCollection, "Picture Dimensions",
                            $"{albumCover.PixelWidth} x {albumCover.PixelHeight}", false, null);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error loading album cover image: {Message}", ex.Message);
                }
            }

            AddPictureInfoItem(targetCollection, "Picture Count", tag.Pictures.Length.ToString(), false, null);
            AddPictureInfoItem(targetCollection, "Picture Type", tag.Pictures[0].Type.ToString(), false, null);
            AddPictureInfoItem(targetCollection, "Picture Mime Type", tag.Pictures[0].MimeType ?? "", false, null);

            if (tag.Pictures.Length > 0)
            {
                string filename = string.IsNullOrEmpty(tag.Pictures[0].Filename)
                      ? "<Embedded Image>"
                    : tag.Pictures[0].Filename;
                AddPictureInfoItem(targetCollection, "Picture Filename", filename, false, null);
            }

            // Picture Description is editable
            AddPictureInfoItem(targetCollection, "Picture Description", tag.Pictures[0].Description ?? "", true, v =>
     {
         // Update the picture description in the tag
         TagLib.IPicture[] pictures = tag.Pictures;
         if (pictures.Length > 0)
         {
             TagLib.IPicture existingPic = pictures[0];
             TagLib.Picture newPic = new TagLib.Picture(existingPic.Data)
             {
                 Type = existingPic.Type,
                 MimeType = existingPic.MimeType,
                 Filename = existingPic.Filename,
                 Description = string.IsNullOrEmpty(v) ? null : v
             };
             tag.Pictures = [newPic];
         }
     });
        }
        else
        {
            // No pictures - don't add any items, but note in logs
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

    public void LoadMultiple(IReadOnlyList<File> audioFiles, ObservableCollection<TagItem> targetCollection)
    {
        if (audioFiles == null || audioFiles.Count == 0)
        {
            _logger.LogWarning("No audio files provided for picture information loading");
            return;
        }

        targetCollection.Clear();

        // Aggregate picture metadata across all files
        Dictionary<int, int> pictureCountValues = new Dictionary<int, int>();
        Dictionary<string, int> pictureTypeValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> pictureMimeTypeValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> pictureFilenameValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> pictureDescriptionValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> pictureSizeValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> pictureDimensionsValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // NEW: Track unique picture data hashes to detect if covers are identical
        HashSet<int> pictureDataHashes = new HashSet<int>();
        bool anyFileHasPictures = false;
        bool allCoversSame = true;

        foreach (File audioFile in audioFiles)
        {
            if (audioFile?.Tag == null)
                continue;

            TagLib.Tag tag = audioFile.Tag;

            if (tag.Pictures is { Length: > 0 })
            {
                anyFileHasPictures = true;
                TagLib.IPicture pic = tag.Pictures[0];

                // Track picture count
                int count = tag.Pictures.Length;
                if (!pictureCountValues.ContainsKey(count))
                    pictureCountValues[count] = 0;
                pictureCountValues[count]++;

                // Track picture type
                string type = pic.Type.ToString();
                if (!pictureTypeValues.ContainsKey(type))
                    pictureTypeValues[type] = 0;
                pictureTypeValues[type]++;

                // Track MIME type
                string mimeType = pic.MimeType ?? "";
                if (!pictureMimeTypeValues.ContainsKey(mimeType))
                    pictureMimeTypeValues[mimeType] = 0;
                pictureMimeTypeValues[mimeType]++;

                // Track filename
                string filename = string.IsNullOrEmpty(pic.Filename) ? "<Embedded Image>" : pic.Filename;
                if (!pictureFilenameValues.ContainsKey(filename))
                    pictureFilenameValues[filename] = 0;
                pictureFilenameValues[filename]++;

                // Track description
                string description = pic.Description ?? "";
                if (!pictureDescriptionValues.ContainsKey(description))
                    pictureDescriptionValues[description] = 0;
                pictureDescriptionValues[description]++;

                // NEW: Track picture data hash to detect unique covers
                if (pic.Data?.Data is { Length: > 0 })
                {
                    // Use a simple hash of the image data to detect uniqueness
                    int dataHash = ComputeSimpleHash(pic.Data.Data);
                    pictureDataHashes.Add(dataHash);

                    // Calculate size and dimensions
                    double sizeInKB = pic.Data.Data.Length / 1024.0;
                    string sizeStr = $"{sizeInKB:F2} KB";
                    if (!pictureSizeValues.ContainsKey(sizeStr))
                        pictureSizeValues[sizeStr] = 0;
                    pictureSizeValues[sizeStr]++;

                    // Try to get dimensions
                    try
                    {
                        using MemoryStream ms = new MemoryStream(pic.Data.Data);
                        BitmapImage tempImage = new BitmapImage();
                        tempImage.BeginInit();
                        tempImage.CacheOption = BitmapCacheOption.OnLoad;
                        tempImage.StreamSource = ms;
                        tempImage.EndInit();
                        tempImage.Freeze();

                        string dimensions = $"{tempImage.PixelWidth} x {tempImage.PixelHeight}";
                        if (!pictureDimensionsValues.ContainsKey(dimensions))
                            pictureDimensionsValues[dimensions] = 0;
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
                // This file has no pictures - mark covers as different
                allCoversSame = false;
            }
        }

        if (!anyFileHasPictures)
        {
            _logger.LogDebug("No pictures found in any of the {Count} selected files", audioFiles.Count);
            return;
        }

        // Check if all files have different covers (more than one unique hash)
        if (pictureDataHashes.Count > 1)
        {
            allCoversSame = false;
        }

        // Add picture size
        string pictureSizeDisplay = pictureSizeValues.Count == 1
     ? pictureSizeValues.Keys.First()
   : "<various>";
        AddPictureInfoItem(targetCollection, "Picture Size", pictureSizeDisplay, false, null);

        // Add picture dimensions
        string pictureDimensionsDisplay = pictureDimensionsValues.Count == 1
            ? pictureDimensionsValues.Keys.First()
            : "<various>";
        AddPictureInfoItem(targetCollection, "Picture Dimensions", pictureDimensionsDisplay, false, null);

        // Add picture count
        string pictureCountDisplay = pictureCountValues.Count == 1
             ? pictureCountValues.Keys.First().ToString()
              : "<various>";
        AddPictureInfoItem(targetCollection, "Picture Count", pictureCountDisplay, false, null);

        // Add picture type
        string pictureTypeDisplay = pictureTypeValues.Count == 1
     ? pictureTypeValues.Keys.First()
     : "<various>";
        AddPictureInfoItem(targetCollection, "Picture Type", pictureTypeDisplay, false, null);

        // Add MIME type
        string pictureMimeTypeDisplay = pictureMimeTypeValues.Count == 1
             ? pictureMimeTypeValues.Keys.First()
         : "<various>";
        AddPictureInfoItem(targetCollection, "Picture Mime Type", pictureMimeTypeDisplay, false, null);

        // Add filename
        string pictureFilenameDisplay = pictureFilenameValues.Count == 1
          ? pictureFilenameValues.Keys.First()
            : "<various>";
        AddPictureInfoItem(targetCollection, "Picture Filename", pictureFilenameDisplay, false, null);

        // Add description (read-only for multi-selection)
        string pictureDescriptionDisplay = pictureDescriptionValues.Count == 1
      ? pictureDescriptionValues.Keys.First()
: "<various>";
        AddPictureInfoItem(targetCollection, "Picture Description", pictureDescriptionDisplay, false, null);

        _logger.LogDebug("Loaded picture info for {Count} files with pictures (allSame={AllSame})", audioFiles.Count, allCoversSame);
    }

    /// <summary>
    /// Compute a simple hash of image data to detect unique covers
    /// </summary>
    private static int ComputeSimpleHash(byte[] data)
    {
        if (data == null || data.Length == 0)
            return 0;

        unchecked
        {
            int hash = 17;
            // Sample key points in the data for performance
            int step = Math.Max(1, data.Length / 100); // Sample ~100 points

            for (int i = 0; i < data.Length; i += step)
            {
                hash = hash * 31 + data[i];
            }

            // Also include total length to differentiate images of different sizes
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
