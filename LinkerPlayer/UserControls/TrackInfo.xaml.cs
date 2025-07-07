using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using static LinkerPlayer.Audio.SpectrumAnalyzer;

namespace LinkerPlayer.UserControls;

public partial class TrackInfo
{
    private readonly AudioEngine _audioEngine;
    private readonly ILogger<TrackInfo> _logger;
    private const string NoAlbumCover = @"pack://application:,,,/LinkerPlayer;component/Images/reel.png";

    public MediaFile? SelectedMediaFile
    {
        get => (MediaFile?)GetValue(SelectedMediaFileProperty);
        set
        {
            SetValue(SelectedMediaFileProperty, value);
            _logger.LogInformation("TrackInfo.SelectedMediaFile set to: {ValueTitle}", value != null ? value.Title : "null");
        }
    }

    public static readonly DependencyProperty SelectedMediaFileProperty =
        DependencyProperty.Register(nameof(SelectedMediaFile), typeof(MediaFile), typeof(TrackInfo), new PropertyMetadata(null));

    public TrackInfo()
    {
        _audioEngine = App.AppHost.Services.GetRequiredService<AudioEngine>();
        _logger = App.AppHost.Services.GetRequiredService<ILogger<TrackInfo>>();

        InitializeComponent();
        Loaded += TrackInfo_Loaded;

        Spectrum.RegisterSoundPlayer(_audioEngine);
        SpectrumButton.Content = nameof(BarHeightScalingStyles.Decibel);
        Spectrum.BarHeightScaling = BarHeightScalingStyles.Decibel;

        WeakReferenceMessenger.Default.Register<SelectedTrackChangedMessage>(this, (_, m) =>
        {
            OnSelectedTrackChanged(m.Value);
        });
    }

    private void TrackInfo_Loaded(object sender, RoutedEventArgs e)
    {
        if (FindName("Spectrum") is SpectrumAnalyzer spectrum)
        {
            spectrum.RegisterSoundPlayer(_audioEngine);
            _logger.LogInformation("TrackInfo: Registered SpectrumAnalyzer with AudioEngine");
        }
        else
        {
            _logger.LogError("TrackInfo: SpectrumAnalyzer control not found");
        }
    }

    private void OnSelectedTrackChanged(MediaFile? mediaFile)
    {
        if (mediaFile != null)
        {
            SelectedMediaFile = mediaFile;

            if (string.IsNullOrWhiteSpace(mediaFile.Artist) || mediaFile.Bitrate == 0)
            {
                mediaFile.UpdateFullMetadata();
            }

            // Always attempt to load AlbumCover to ensure fresh state
            mediaFile.LoadAlbumCover();

            // Check if AlbumCover is null or invalid
            bool isInvalidImage = mediaFile.AlbumCover == null;
            if (mediaFile.AlbumCover is BitmapImage bitmap && !bitmap.IsDownloading)
            {
                try
                {
                    _ = bitmap.PixelWidth; // Force access to detect errors
                    isInvalidImage = bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Invalid BitmapImage detected for {MediaFileTitle}", mediaFile.Title);
                    isInvalidImage = true;
                }
            }

            if (isInvalidImage)
            {
                mediaFile.AlbumCover = GetDefaultAlbumImage();
                _logger.LogInformation("TrackInfo: Set default album cover for {MediaFileTitle}", mediaFile.Title);
                if (FindName("TrackImageText") is TextBlock trackImageText)
                {
                    trackImageText.Text = "[No Image]";
                }
            }
            else if (FindName("TrackImageText") is TextBlock trackImageText)
            {
                trackImageText.Text = string.Empty; // Clear text for valid image
            }
        }
        else
        {
            SelectedMediaFile = null;
            if (FindName("TrackImage") is Image trackImage)
            {
                trackImage.Source = GetDefaultAlbumImage();
            }
            if (FindName("TrackImageText") is TextBlock trackImageText)
            {
                trackImageText.Text = "[ No Selection ]";
            }
        }
    }

    private static BitmapImage GetDefaultAlbumImage()
    {
        try
        {
            return new BitmapImage(new Uri(NoAlbumCover, UriKind.Absolute));
        }
        catch (Exception ex)
        {
            App.AppHost.Services.GetRequiredService<ILogger<TrackInfo>>()
                .LogError(ex, "Failed to load default album image");
            return new BitmapImage(); // Fallback to empty image
        }
    }

    private void SpectrumButton_Click(object sender, RoutedEventArgs e)
    {
        if (Spectrum.BarHeightScaling == BarHeightScalingStyles.Decibel)
        {
            Spectrum.BarHeightScaling = BarHeightScalingStyles.Sqrt;
            SpectrumButton.Content = nameof(BarHeightScalingStyles.Sqrt);
        }
        else if (Spectrum.BarHeightScaling == BarHeightScalingStyles.Sqrt)
        {
            Spectrum.BarHeightScaling = BarHeightScalingStyles.Linear;
            SpectrumButton.Content = nameof(BarHeightScalingStyles.Linear);
        }
        else if (Spectrum.BarHeightScaling == BarHeightScalingStyles.Linear)
        {
            Spectrum.BarHeightScaling = BarHeightScalingStyles.Mel;
            SpectrumButton.Content = nameof(BarHeightScalingStyles.Mel);
        }
        else if (Spectrum.BarHeightScaling == BarHeightScalingStyles.Mel)
        {
            Spectrum.BarHeightScaling = BarHeightScalingStyles.Bark;
            SpectrumButton.Content = nameof(BarHeightScalingStyles.Bark);
        }
        else if (Spectrum.BarHeightScaling == BarHeightScalingStyles.Bark)
        {
            Spectrum.BarHeightScaling = BarHeightScalingStyles.Power;
            SpectrumButton.Content = nameof(BarHeightScalingStyles.Power);
        }
        else if (Spectrum.BarHeightScaling == BarHeightScalingStyles.Power)
        {
            Spectrum.BarHeightScaling = BarHeightScalingStyles.LogFrequency;
            SpectrumButton.Content = nameof(BarHeightScalingStyles.LogFrequency);
        }
        else if (Spectrum.BarHeightScaling == BarHeightScalingStyles.LogFrequency)
        {
            Spectrum.BarHeightScaling = BarHeightScalingStyles.Decibel;
            SpectrumButton.Content = nameof(BarHeightScalingStyles.Decibel);
        }

        Spectrum.UpdateLayout();
    }
}