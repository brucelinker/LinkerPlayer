using ATL;
using LinkerPlayer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace LinkerPlayer.Core;

public class CoverManager
{
    private readonly ILogger<CoverManager> _logger = App.AppHost.Services.GetRequiredService<ILogger<CoverManager>>();

    private readonly ConcurrentDictionary<int, BitmapImage> _storedImages = new();

    // WPF's built-in imaging pipeline only supports these MIME types natively.
    private static readonly HashSet<string> SupportedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/bmp",
        "image/gif", "image/tiff", "image/x-tiff"
    };

    public BitmapImage GetImageFromPictureTag(string fileName)
    {
        try
        {
            Track track = new(fileName);
            PictureInfo? pic = track.EmbeddedPictures?.FirstOrDefault(p => p.PicType is
                PictureInfo.PIC_TYPE.Front or
                PictureInfo.PIC_TYPE.Back or
                PictureInfo.PIC_TYPE.CD or
                PictureInfo.PIC_TYPE.Generic);

            if (pic?.PictureData is not { Length: > 0 })
                return null!;

            // Skip formats WPF can't decode natively (e.g. WebP, AVIF, HEIF)
            if (!string.IsNullOrEmpty(pic.MimeType) && !SupportedMimeTypes.Contains(pic.MimeType))
            {
                _logger.LogDebug("Skipping unsupported cover format '{MimeType}' for {FileName}", pic.MimeType, fileName);
                return null!;
            }

            int hashCode = HashCode.Combine(pic.PictureData.Length, pic.PictureData[0]);
            BitmapImage image = _storedImages.GetOrAdd(hashCode, _ =>
            {
                using MemoryStream ms = new(pic.PictureData);
                BitmapImage bi = new();
                bi.BeginInit();
                bi.CreateOptions = BitmapCreateOptions.DelayCreation;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                return bi;
            });
            return image;
        }
        catch (NotSupportedException)
        {
            // Image data is in a format WPF can't decode (codec not available on this machine)
            _logger.LogDebug("Cover image format not supported by WPF decoder for {FileName}", fileName);
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
