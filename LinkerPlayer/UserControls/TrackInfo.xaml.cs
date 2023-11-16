using LinkerPlayer.Core;
using LinkerPlayer.Models;
using System;
using System.Windows;
using System.Windows.Media.Imaging;
using Serilog;

namespace LinkerPlayer.UserControls;

public partial class TrackInfo
{
    public MediaFile SelectedMediaFile = new();
    private const string NoAlbumCover = @"pack://application:,,,/LinkerPlayer;component/Images/cdgraphic.png";

    public TrackInfo()
    {
        this.DataContext = this;
        InitializeComponent();
    }

    private static BitmapImage? _defaultAlbumImage;
    public static BitmapImage DefaultAlbumImage
    {
        get
        {
            if (_defaultAlbumImage == null)
                ReloadDefaultAlbumImage();

            return _defaultAlbumImage!;
        }
    }

    public static void ReloadDefaultAlbumImage()
    {
        Log.Information("TrackInfo - ReloadDefaultAlbumImage");

        _defaultAlbumImage = new BitmapImage(new Uri(NoAlbumCover, UriKind.Absolute));
    }

    public void SetSelectedMediaFile(MediaFile mediaFile)
    {
        Log.Information("TrackInfo - SetSelectedMediaFile");

        SelectedMediaFile = mediaFile;

        DisplayTrackImage(mediaFile);

        TrackName.Text = mediaFile.Title;
        TrackArtist.Text = mediaFile.Artists;
        TrackAlbum.Text = mediaFile.Album;
    }

    private void DisplayTrackImage(IMediaFile mediaFile)
    {
        Log.Information("TrackInfo - DisplayTrackImage");

        if (mediaFile.AlbumCover != null)
        {
            TrackImage.Source = mediaFile.AlbumCover;
            TrackImageText.Text = "";
            return;
        }

        try
        {
            TagLib.File tmp = TagLib.File.Create(mediaFile.Path);
            TrackImage.Source = TagLibConvertPicture.GetImageFromTag(tmp.Tag.Pictures);
            TrackImageText.Text = "";
        }
        catch
        {
            TrackImage.Source = null;
        }

        if (TrackImage.Source != null) return;

        try
        {
            TrackImage.Source = DefaultAlbumImage;
            TrackImageText.Text = "[ No Image ]";
        }
        catch (Exception exc)
        {
            MessageBox.Show(exc.Message);
        }
    }
}