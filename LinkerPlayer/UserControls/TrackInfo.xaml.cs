using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LinkerPlayer.UserControls;

public partial class TrackInfo
{
    private readonly AudioEngine _audioEngine;
    public MediaFile? SelectedMediaFile
    {
        get => (MediaFile?)GetValue(SelectedMediaFileProperty);
        set => SetValue(SelectedMediaFileProperty, value);
    }

    public static readonly DependencyProperty SelectedMediaFileProperty =
        DependencyProperty.Register(nameof(SelectedMediaFile), typeof(MediaFile), typeof(TrackInfo), new PropertyMetadata(null));

    private static int _count;

    public TrackInfo()
    {
        _audioEngine = App.AppHost.Services.GetRequiredService<AudioEngine>();
        Log.Information($"TRACKINFO - {Interlocked.Increment(ref _count)}");

        InitializeComponent();
        Loaded += TrackInfo_Loaded;

        Spectrum.RegisterSoundPlayer(_audioEngine);

        WeakReferenceMessenger.Default.Register<SelectedTrackChangedMessage>(this, (_, m) =>
        {
            OnSelectedTrackChanged(m.Value);
        });
    }

    //private static BitmapImage? _defaultAlbumImage;

    //static TrackInfo()
    //{
    //    _count = 0;
    //    ReloadDefaultAlbumImage();
    //}

    //public static BitmapImage DefaultAlbumImage
    //{
    //    get => _defaultAlbumImage!;
    //}

    private void TrackInfo_Loaded(object sender, RoutedEventArgs e)
    {
        if (FindName("Spectrum") is SpectrumAnalyzer spectrum)
        {
            spectrum.RegisterSoundPlayer(_audioEngine);
            Log.Information("TrackInfo: Registered SpectrumAnalyzer with AudioEngine");
        }
        else
        {
            Log.Error("TrackInfo: SpectrumAnalyzer control not found");
        }
    }

    private void OnSelectedTrackChanged(MediaFile? mediaFile)
    {
        SelectedMediaFile = mediaFile;
        if (mediaFile != null)
        {
            if (string.IsNullOrWhiteSpace(mediaFile.Artist) || mediaFile.Bitrate == 0)
            {
                mediaFile.UpdateFullMetadata();
            }
            if (mediaFile.AlbumCover == null)
            {
                mediaFile.LoadAlbumCover();
            }
        }
    }

    //public static void ReloadDefaultAlbumImage()
    //{
    //    _defaultAlbumImage = new BitmapImage(new Uri(NoAlbumCover, UriKind.Absolute));
    //}

    //public void SetTrackInfo(MediaFile? mediaFile)
    //{
    //    SelectedMediaFile = mediaFile;

    //    if (mediaFile == null)
    //    {
    //        TrackName.Text = "No Selection";
    //        TrackArtist.Text = "Artist:";
    //        TrackAlbum.Text = "Album:";
    //        TrackYear.Text = $"Year:  ";
    //        TrackBitrate.Text = $"Bitrate:  ";
    //        TrackGenre.Text = "Genres:  ";

    //        TrackImage.Source = DefaultAlbumImage;
    //        TrackImageText.Text = "[ No Selection ]";
    //        return;
    //    }

    //    if (mediaFile.AlbumCover == null)
    //    {
    //        mediaFile.LoadAlbumCover();
    //    }

    //    TrackImage.Source = mediaFile.AlbumCover ?? DefaultAlbumImage;
    //    TrackImageText.Text = mediaFile.AlbumCover == null ? "[ No Image ]" : "";

    //    TrackName.Text = mediaFile.Title;
    //    TrackArtist.Text = string.IsNullOrWhiteSpace(mediaFile.Artist) ? "Artist: <unknown>" : $"Artist: {mediaFile.Artist}";
    //    TrackAlbum.Text = string.IsNullOrWhiteSpace(mediaFile.Album) ? "Album: <unknown>" : $"Album: {mediaFile.Album}";
    //    TrackYear.Text = mediaFile.Year == 0 ? "Year: <unknown>" : $"Year: {mediaFile.Year}";
    //    TrackBitrate.Text = mediaFile.Bitrate == 0 ? "Bitrate: <unknown>" : $"Bitrate: {mediaFile.Bitrate} kbps";
    //    TrackGenre.Text = string.IsNullOrWhiteSpace(mediaFile.Genres) ? "Genres: <unknown>" : $"Genres: {mediaFile.Genres}";
    //}

    //private void DisplayTrackImage(IMediaFile mediaFile)
    //{
    //    if (mediaFile.AlbumCover != null)
    //    {
    //        TrackImage.Source = mediaFile.AlbumCover;
    //        TrackImageText.Text = "";
    //        return;
    //    }

    //    try
    //    {
    //        TagLib.File tmp = TagLib.File.Create(mediaFile.Path);
    //        TrackImage.Source = TagLibConvertPicture.GetImageFromTag(tmp.Tag.Pictures);
    //        TrackImageText.Text = "";
    //    }
    //    catch
    //    {
    //        TrackImage.Source = null;
    //    }

    //    if (TrackImage.Source != null) return;

    //    try
    //    {
    //        TrackImage.Source = DefaultAlbumImage;
    //        TrackImageText.Text = "[ No Image ]";
    //    }
    //    catch (Exception exc)
    //    {
    //        MessageBox.Show(exc.Message);
    //    }
    //}
}