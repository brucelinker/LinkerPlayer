using LinkerPlayer.Core;
using LinkerPlayer.Models;
using System;
using System.Windows;

namespace LinkerPlayer.UserControls;

public partial class TrackInfo
{
    public MediaFile SelectedMediaFile = new();

    public TrackInfo()
    {
        InitializeComponent();
    }

    public void SetSelectedMediaFile(MediaFile mediaFile)
    {
        SelectedMediaFile = mediaFile;

        DisplayTrackImage(mediaFile);

        TrackName.Text = mediaFile.Title;
        TrackArtist.Text = mediaFile.Artists;
        TrackAlbum.Text = mediaFile.Album;
    }

    private void DisplayTrackImage(IMediaFile mediaFile)
    {
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
            TrackImage.Source = DefaultAlbumImage.DefaultImage;
            TrackImageText.Text = "[ No Image ]";
        }
        catch (Exception exc)
        {
            MessageBox.Show(exc.Message);
        }
    }
}