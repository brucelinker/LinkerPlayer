using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace LinkerPlayer.UserControls;

public partial class TrackInfo
{
    private readonly AudioEngine _audioEngine;
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

    private readonly ILogger<TrackInfo> _logger;

    public TrackInfo()
    {
        _audioEngine = App.AppHost.Services.GetRequiredService<AudioEngine>();
        _logger = App.AppHost.Services.GetRequiredService<ILogger<TrackInfo>>();

        InitializeComponent();
        Loaded += TrackInfo_Loaded;

        Spectrum.RegisterSoundPlayer(_audioEngine);

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
            if (mediaFile.AlbumCover == null)
            {
                mediaFile.LoadAlbumCover();
            }
        }
    }
}