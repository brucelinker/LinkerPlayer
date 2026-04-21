using LinkerPlayer.Services;
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
        catch (TagLib.UnsupportedFormatException ex)
        {
            // Record unsupported format in the import error logger and log
            _logger.LogWarning(ex, "TagLib unsupported format when loading cover from {Filename}: {Message}", fileName, ex.Message);
            try
            {
                IImportErrorLogger? importLogger = App.AppHost?.Services?.GetService(typeof(Services.IImportErrorLogger)) as Services.IImportErrorLogger;
                importLogger?.Log(fileName, ex);
            }
            catch { }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not load the cover from picture tag for {Filename}! - {Message}", fileName, e.Message);
            try
            {
                IImportErrorLogger? importLogger = App.AppHost?.Services?.GetService(typeof(Services.IImportErrorLogger)) as Services.IImportErrorLogger;
                importLogger?.Log(fileName, e);
            }
            catch { }
        }

        return null!;
    }

    public override string ToString()
    {
        return _storedImages.Count.ToString();
    }
}
