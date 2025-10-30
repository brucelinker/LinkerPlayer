using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

        var tag = audioFile.Tag;

        if (tag.Pictures is { Length: > 0 })
     {
            var pic = tag.Pictures[0];
      BitmapImage? albumCover = null;

       if (pic.Data?.Data is { Length: > 0 })
        {
                try
       {
                using var ms = new MemoryStream(pic.Data.Data);
       albumCover = new BitmapImage();
           albumCover.BeginInit();
          albumCover.CacheOption = BitmapCacheOption.OnLoad;
           albumCover.StreamSource = ms;
     albumCover.EndInit();
   albumCover.Freeze();

        // Add album cover as a special TagItem with image
            targetCollection.Add(new TagItem
           {
             Name = "Album Cover",
   Value = string.Empty,
     IsEditable = false,
        UpdateAction = null,
               AlbumCoverSource = albumCover
        });

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
         var pictures = tag.Pictures;
              if (pictures.Length > 0)
 {
           var existingPic = pictures[0];
      var newPic = new TagLib.Picture(existingPic.Data)
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

        // Sort picture items: keep regular tags in original order, move custom tags (with angle brackets) to bottom
        var regularPictureTags = targetCollection.Where(item => !item.Name.StartsWith("<")).ToList();
        var customPictureTags = targetCollection.Where(item => item.Name.StartsWith("<")).ToList();

        targetCollection.Clear();
     foreach (var item in regularPictureTags)
     {
            targetCollection.Add(item);
 }
        foreach (var item in customPictureTags)
        {
            targetCollection.Add(item);
        }
    }

    public void LoadMultiple(IReadOnlyList<File> audioFiles, ObservableCollection<TagItem> targetCollection)
    {
        // For multiple files, don't show picture info (too complex to compare images)
        targetCollection.Clear();
    _logger.LogDebug("Picture info not displayed for multiple file selection");
    }

    private static void AddPictureInfoItem(ObservableCollection<TagItem> collection, string name, string value, 
        bool isEditable, Action<string>? updateAction)
    {
        var item = new TagItem
{
            Name = name,
   Value = value,
            IsEditable = isEditable,
            UpdateAction = isEditable ? updateAction : null
        };

 collection.Add(item);
    }
}
