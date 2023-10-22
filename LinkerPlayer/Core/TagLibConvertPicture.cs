using System;
using System.Linq;
using System.Windows.Media.Imaging;

namespace LinkerPlayer.Core
{
    public class TagLibConvertPicture
    {
        public static BitmapImage? GetImageFromTag(TagLib.IPicture[] pictures)
        {
            if (pictures.Length == 0)
            {
                return null;
            }

            byte[] raw = pictures[0].Data.ToArray();
            BitmapImage? img = new BitmapImage();

            try
            {
                img.BeginInit();
                img.UriSource = null;
                img.BaseUri = null;
                img.StreamSource = new System.IO.MemoryStream(raw);
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch (NotSupportedException)
            {
                img = null;
                return img;
            }
            catch (Exception)
            {
                img = null;
                return img;
            }
        }
    }
}
