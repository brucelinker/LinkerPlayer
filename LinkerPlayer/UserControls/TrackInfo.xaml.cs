using LinkerPlayer.Audio;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using static LinkerPlayer.Audio.SpectrumAnalyzer;

namespace LinkerPlayer.UserControls;

public partial class TrackInfo
{
    private readonly AudioEngine _audioEngine;
    private readonly ILogger<TrackInfo> _logger;
    private readonly ISelectionService _selectionService;
    private const string NoAlbumCover = @"pack://application:,,,/LinkerPlayer;component/Images/reel.png";

    public MediaFile? SelectedMediaFile
    {
        get => (MediaFile?)GetValue(SelectedMediaFileProperty);
        set { SetValue(SelectedMediaFileProperty, value); }
    }

    public static readonly DependencyProperty SelectedMediaFileProperty =
        DependencyProperty.Register(nameof(SelectedMediaFile), typeof(MediaFile), typeof(TrackInfo), new PropertyMetadata(null));

    public TrackInfo()
    {
        _audioEngine = App.AppHost.Services.GetRequiredService<AudioEngine>();
        _logger = App.AppHost.Services.GetRequiredService<ILogger<TrackInfo>>();
        _selectionService = App.AppHost.Services.GetRequiredService<ISelectionService>();

        InitializeComponent();
        Loaded += TrackInfo_Loaded;
        Unloaded += TrackInfo_Unloaded;

        Spectrum.RegisterSoundPlayer(_audioEngine);
        VuMeter.RegisterSoundPlayer(_audioEngine);
        SpectrumButton.Content = nameof(BarHeightScalingStyles.Decibel);
        Spectrum.BarHeightScaling = BarHeightScalingStyles.Decibel;

        // Subscribe to selection changes
        _selectionService.TrackChanged += SelectionService_TrackChanged;
    }

    private void TrackInfo_Unloaded(object sender, RoutedEventArgs e)
    {
        _selectionService.TrackChanged -= SelectionService_TrackChanged;
    }

    private void SelectionService_TrackChanged(object? sender, MediaFile? e)
    {
        OnSelectedTrackChanged(e);
    }

    private void TrackInfo_Loaded(object sender, RoutedEventArgs e)
    {
        if (FindName("Spectrum") is SpectrumAnalyzer spectrum)
        {
            spectrum.RegisterSoundPlayer(_audioEngine);
        }
        else
        {
            _logger.LogError("TrackInfo: SpectrumAnalyzer control not found");
        }

        if (FindName("VuMeter") is VuMeter vuMeter)
        {
            vuMeter.RegisterSoundPlayer(_audioEngine);
        }
        else
        {
            _logger.LogError("TrackInfo: VuMeter control not found");
        }

        // Initialize from current selection
        if (_selectionService.CurrentTrack != null)
        {
            OnSelectedTrackChanged(_selectionService.CurrentTrack);
        }
    }

    private async void OnSelectedTrackChanged(MediaFile? mediaFile)
    {
        if (mediaFile != null)
        {
            SelectedMediaFile = mediaFile;

            if (mediaFile.AlbumCover == null)
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    try { mediaFile.LoadAlbumCover(); }
                    catch (System.Exception ex) { _logger.LogWarning(ex, "Failed to load album cover for {Title}", mediaFile.Title); }
                });
            }

            bool isInvalidImage = mediaFile.AlbumCover == null;
            if (mediaFile.AlbumCover is BitmapImage bitmap && !bitmap.IsDownloading)
            {
                try { _ = bitmap.PixelWidth; isInvalidImage = bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0; }
                catch (System.Exception ex) { _logger.LogWarning(ex, "Invalid BitmapImage detected for {MediaFileTitle}", mediaFile.Title); isInvalidImage = true; }
            }

            if (isInvalidImage)
            {
                mediaFile.AlbumCover = GetDefaultAlbumImage();
                if (FindName("TrackImageText") is TextBlock trackImageText) { trackImageText.Text = "[No Image]"; }
            }
            else if (FindName("TrackImageText") is TextBlock okText)
            {
                okText.Text = string.Empty;
            }

            if (FindName("TrackImage") is Image trackImage)
            {
                trackImage.Source = mediaFile.AlbumCover ?? GetDefaultAlbumImage();
            }
        }
        else
        {
            SelectedMediaFile = null;
            if (FindName("TrackImage") is Image trackImage) { trackImage.Source = GetDefaultAlbumImage(); }
            if (FindName("TrackImageText") is TextBlock trackImageText) { trackImageText.Text = "[ No Selection ]"; }
        }
    }

    private static BitmapImage GetDefaultAlbumImage()
    {
        try { return new BitmapImage(new System.Uri(NoAlbumCover, System.UriKind.Absolute)); }
        catch (System.Exception ex)
        {
            App.AppHost.Services.GetRequiredService<ILogger<TrackInfo>>().LogError(ex, "Failed to load default album image");
            return new BitmapImage();
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
