using Serilog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using TagLib;
using File = TagLib.File;

namespace LinkerPlayer.Core
{
    public class CoverManager
    {
        private static readonly ConcurrentDictionary<int, BitmapImage> StoredImages = new();

        public static BitmapImage GetImageFromPictureTag(string fileName)
        {
            try
            {
                using File? file = File.Create(fileName);
                IPicture[]? pictures = file.Tag.Pictures;
                IPicture? pic = pictures?.FirstOrDefault(p => p.Type is 
                    PictureType.FrontCover or 
                    PictureType.BackCover or 
                    PictureType.FileIcon or 
                    PictureType.OtherFileIcon or 
                    PictureType.Media or 
                    PictureType.Other);

                if (pic != null)
                {
                    int hashCode = pic.Data.GetHashCode();
                    BitmapImage image = StoredImages.GetOrAdd(hashCode, _ =>
                    {
                        BitmapImage bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CreateOptions = BitmapCreateOptions.DelayCreation;
                        bi.CacheOption = BitmapCacheOption.OnDemand;
                        bi.StreamSource = new MemoryStream(pic.Data.Data);
                        bi.EndInit();
                        bi.Freeze();
                        return bi;
                    });
                    return image;
                }
            }
            catch (Exception e)
            {
                Log.Error("Could not load the cover from picture tag for {0}! - {1}", fileName, e.Message);
            }

            return null!;
        }

        public override string ToString()
        {
            return StoredImages.Count.ToString();
        }
    }
}