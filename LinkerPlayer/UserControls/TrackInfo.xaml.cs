using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
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
    public readonly AudioEngine? AudioEngine;

    private static int _count;

    public TrackInfo()
    {
        Log.Information($"TRACKINFO - {++_count}");

        this.DataContext = this;
        InitializeComponent();
        Loaded += TrackInfo_Loaded;

        AudioEngine = AudioEngine.Instance;
        Spectrum.RegisterSoundPlayer(AudioEngine);

        WeakReferenceMessenger.Default.Register<SelectedTrackChangedMessage>(this, (_, m) =>
        {
            if (m.Value == null) return;

            OnSelectedTrackChanged(m.Value);
        });
    }

    private static BitmapImage? _defaultAlbumImage;

    static TrackInfo()
    {
        _count = 0;
    }

    public static BitmapImage DefaultAlbumImage
    {
        get
        {
            if (_defaultAlbumImage == null)
                ReloadDefaultAlbumImage();

            return _defaultAlbumImage!;
        }
    }

    private void TrackInfo_Loaded(object sender, RoutedEventArgs e)
    {
        SpectrumAnalyzer? spectrum = FindName("Spectrum") as SpectrumAnalyzer;
        if (spectrum != null)
        {
            spectrum.RegisterSoundPlayer(AudioEngine.Instance);
            Log.Information("TrackInfo: Registered SpectrumAnalyzer with AudioEngine");
        }
        else
        {
            Log.Error("TrackInfo: SpectrumAnalyzer control not found");
        }
    }

    private void OnSelectedTrackChanged(MediaFile mediaFile)
    {
        SetActiveMediaFile(mediaFile);
    }

    public static void ReloadDefaultAlbumImage()
    {
        _defaultAlbumImage = new BitmapImage(new Uri(NoAlbumCover, UriKind.Absolute));
    }

    public void SetActiveMediaFile(MediaFile mediaFile)
    {
        SelectedMediaFile = mediaFile;

        DisplayTrackImage(mediaFile);

        TrackName.Text = mediaFile.Title;
        TrackArtist.Text = $"Artist:  {mediaFile.Artist}";
        TrackAlbum.Text = string.IsNullOrWhiteSpace(mediaFile.Album) ? "Album:  <undefined>" : $"Album:  {mediaFile.Album}";
        TrackYear.Text = mediaFile.Year == 0 ? "Year:  <undefined>" : $"Year:  {mediaFile.Year}";
        TrackBitrate.Text = $"Bitrate:  {mediaFile.Bitrate} kbps";
        TrackGenre.Text = string.IsNullOrWhiteSpace(mediaFile.Genres) ? "Genres:  <undefined>" : $"Genres:  {mediaFile.Genres}";
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
            TrackImage.Source = DefaultAlbumImage;
            TrackImageText.Text = "[ No Image ]";
        }
        catch (Exception exc)
        {
            MessageBox.Show(exc.Message);
        }
    }
}