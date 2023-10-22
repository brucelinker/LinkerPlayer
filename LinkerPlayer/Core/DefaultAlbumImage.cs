using LinkerPlayer.Models;
using LinkerPlayer.Properties;
using System.Windows.Media.Imaging;

namespace LinkerPlayer.Core;

public class DefaultAlbumImage
{
    private static BitmapImage? _defaultImage;
    private const string NoAlbumCoverLight = @"pack://application:,,,/LinkerPlayer;component/Images/no_album_cover_light.jpg";
    private const string NoAlbumCoverDark = @"pack://application:,,,/LinkerPlayer;component/Images/no_album_cover_dark.jpg";
    private const string NoAlbumCover = @"pack://application:,,,/LinkerPlayer;component/Images/cdgraphic.png";

    public static BitmapImage GetImage(string resourceString)
    {
        return new BitmapImage(new System.Uri(resourceString, System.UriKind.Absolute));
    }

    public static void Reload()
    {
        string noAlbumCoverUri = NoAlbumCover;

        //if (Settings.Default.SelectedTheme == ThemeColors.Midnight.ToString())
        //    noAlbumCoverUri = NoAlbumCoverDark;

        _defaultImage = new BitmapImage(new System.Uri(noAlbumCoverUri, System.UriKind.Absolute));
    }

    public static BitmapImage DefaultImage
    {
        get
        {
            if (_defaultImage == null)
                Reload();

            return _defaultImage!;
        }
    }
}