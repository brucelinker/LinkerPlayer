using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Threading;
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
            Log.Information($"TrackInfo.SelectedMediaFile set to: {(value != null ? value.Title : "null")}");
        }
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