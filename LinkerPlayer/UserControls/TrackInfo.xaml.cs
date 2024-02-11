using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Serilog;
using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LinkerPlayer.UserControls;

public partial class TrackInfo
{
    public MediaFile SelectedMediaFile = new();
    private const string NoAlbumCover = @"pack://application:,,,/LinkerPlayer;component/Images/reel.png";

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
        TrackArtist.Text = $"Artist:  {mediaFile.Artists}";
        TrackAlbum.Text = string.IsNullOrWhiteSpace(mediaFile.Album) ? "Album:  <undefined>" : $"Album:  {mediaFile.Album}";
        TrackYear.Text = mediaFile.Year == 0 ? "Year:  <undefined>" : $"Year:  {mediaFile.Year}";
        TrackBitrate.Text = $"Bitrate:  {mediaFile.Bitrate} kbps";
        TrackGenre.Text = string.IsNullOrWhiteSpace(mediaFile.Genres) ? "Genres:  <undefined>" : $"Genres:  {mediaFile.Genres}";
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