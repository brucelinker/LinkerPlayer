using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;
using TagLib;
using File = TagLib.File;

namespace LinkerPlayer.Core;

public class CoverManager
{
    private readonly ILogger<CoverManager> _logger = App.AppHost.Services.GetRequiredService<ILogger<CoverManager>>();

    private readonly ConcurrentDictionary<int, BitmapImage> _storedImages = new();

    public BitmapImage GetImageFromPictureTag(string fileName)
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
                BitmapImage image = _storedImages.GetOrAdd(hashCode, _ =>
                {
                    BitmapImage bi = new();
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
            _logger.LogError("Could not load the cover from picture tag for {Filename}! - {Message}", fileName, e.Message);
        }

        return null!;
    }

    public override string ToString()
    {
        return _storedImages.Count.ToString();
    }
}
