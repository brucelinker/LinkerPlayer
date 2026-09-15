using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using static LinkerPlayer.Audio.SpectrumAnalyzer;
using static MaterialDesignThemes.Wpf.Theme;

namespace LinkerPlayer.UserControls;

public partial class TrackInfo : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly IMusicLibrary _musicLibrary = App.AppHost.Services.GetRequiredService<IMusicLibrary>();

    private readonly IAudioEngine _audioEngine;
    private readonly ILogger<TrackInfo> _logger;
    private readonly ISelectionService _selectionService;
    private readonly SharedDataModel _sharedDataModel;
    private const string NoAlbumCover = @"pack://application:,,,/LinkerPlayer;component/Images/reel.png";

    private BitmapImage? _albumCoverSource;
    public BitmapImage? AlbumCoverSource
    {
        get => _albumCoverSource;
        private set
        {
            if (ReferenceEquals(_albumCoverSource, value))
                return;

            _albumCoverSource = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlbumCoverSource)));
        }
    }

    private string _albumCoverText = "[ No Selection ]";
    public string AlbumCoverText
    {
        get => _albumCoverText;
        private set
        {
            if (_albumCoverText == value)
                return;

            _albumCoverText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlbumCoverText)));
        }
    }

    private MediaFile? _selectedMediaFile;

    // ── SelectedMediaFile ────────────────────────────────────────────────────

    public MediaFile? SelectedMediaFile
    {
        get => (MediaFile?)GetValue(SelectedMediaFileProperty);
        set
        {
            MediaFile? old = _selectedMediaFile;
            if (old != null)
                old.PropertyChanged -= OnSelectedMediaFilePropertyChanged;

            _selectedMediaFile = value;
            SetValue(SelectedMediaFileProperty, value);

            if (value != null)
                value.PropertyChanged += OnSelectedMediaFilePropertyChanged;
        }
    }

    public static readonly DependencyProperty SelectedMediaFileProperty =
        DependencyProperty.Register(nameof(SelectedMediaFile), typeof(MediaFile), typeof(TrackInfo), new PropertyMetadata(null));

    private void OnSelectedMediaFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MediaFile.AlbumCover) || sender is not MediaFile mediaFile)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(mediaFile, _selectedMediaFile))
                return;

            UpdateAlbumCoverDisplay(mediaFile);
            RaiseHasAlbumCoverChanged();
        });
    }

    // ── IsLibraryMode ────────────────────────────────────────────────────────

    public bool IsLibraryMode
    {
        get => (bool)GetValue(IsLibraryModeProperty);
        set => SetValue(IsLibraryModeProperty, value);
    }

    public static readonly DependencyProperty IsLibraryModeProperty =
        DependencyProperty.Register(nameof(IsLibraryMode), typeof(bool), typeof(TrackInfo), new PropertyMetadata(false));

    // ── HasAlbumCover — drives context menu IsEnabled ────────────────────────

    /// <summary>True when the currently displayed track has a real (non-default) album cover.</summary>
    public bool HasAlbumCover => _selectedMediaFile?.AlbumCover != null && !ReferenceEquals(_selectedMediaFile.AlbumCover, _defaultImage);

    private void RaiseHasAlbumCoverChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAlbumCover)));

    // ── Lazy default image — shared, created once ────────────────────────────

    private static BitmapImage? _defaultImage;
    private static BitmapImage GetDefaultAlbumImage()
    {
        if (_defaultImage != null)
            return _defaultImage;
        try
        {
            BitmapImage bi = new(new System.Uri(NoAlbumCover, System.UriKind.Absolute));
            bi.Freeze();
            _defaultImage = bi;
            return _defaultImage;
        }
        catch (System.Exception ex)
        {
            App.AppHost.Services.GetRequiredService<ILogger<TrackInfo>>().LogError(ex, "Failed to load default album image");
            return new BitmapImage();
        }
    }

    private void UpdateAlbumCoverDisplay(MediaFile? mediaFile)
    {
        if (mediaFile == null)
        {
            AlbumCoverSource = GetDefaultAlbumImage();
            AlbumCoverText = "[ No Selection ]";
            return;
        }

        if (mediaFile.AlbumCover != null)
        {
            AlbumCoverSource = mediaFile.AlbumCover;
            AlbumCoverText = string.Empty;
            return;
        }

        AlbumCoverSource = GetDefaultAlbumImage();
        AlbumCoverText = mediaFile.UnsupportedCoverFormat != null
            ? $"[{mediaFile.UnsupportedCoverFormat} — Not Supported]"
            : "[No Image]";
    }

    private MediaFile? _lastDisplayedTrack;

    public TrackInfo()
    {
        _audioEngine = App.AppHost.Services.GetRequiredService<IAudioEngine>();
        _logger = App.AppHost.Services.GetRequiredService<ILogger<TrackInfo>>();
        _selectionService = App.AppHost.Services.GetRequiredService<ISelectionService>();
        _sharedDataModel = App.AppHost.Services.GetRequiredService<SharedDataModel>();

        InitializeComponent();
        Loaded += TrackInfo_Loaded;
        Unloaded += TrackInfo_Unloaded;

        Spectrum.RegisterSoundPlayer(_audioEngine);
        VuMeter.RegisterSoundPlayer(_audioEngine);
        SpectrumButton.Content = nameof(BarHeightScalingStyles.Decibel);
        Spectrum.BarHeightScaling = BarHeightScalingStyles.Decibel;

        // Subscribe to selection changes
        _selectionService.TrackChanged += SelectionService_TrackChanged;
        _sharedDataModel.PropertyChanged += SharedDataModel_PropertyChanged;
    }

    private void TrackInfo_Unloaded(object sender, RoutedEventArgs e)
    {
        _selectionService.TrackChanged -= SelectionService_TrackChanged;
        _sharedDataModel.PropertyChanged -= SharedDataModel_PropertyChanged;
    }

    private void SharedDataModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedDataModel.ActiveTrack))
        {
            UpdateDisplayedTrack();
        }
    }

    private void UpdateDisplayedTrack()
    {
        MediaFile? activeTrack = _sharedDataModel.ActiveTrack;
        if (activeTrack != null)
        {
            _lastDisplayedTrack = activeTrack;
            OnSelectedTrackChanged(activeTrack);
            return;
        }

        // Stopped (or transitioning to stopped): keep showing the last active track until the user changes selection.
        if (!_audioEngine.IsPlaying)
        {
            return;
        }

        MediaFile? selected = _selectionService.CurrentTrack;
        _lastDisplayedTrack = selected;
        OnSelectedTrackChanged(selected);
    }

    private void SelectionService_TrackChanged(object? sender, MediaFile? e)
    {
        // Only react to selection changes when not actively playing.
        if (!_audioEngine.IsPlaying)
        {
            _lastDisplayedTrack = e;
            OnSelectedTrackChanged(e);
        }
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

        UpdateDisplayedTrack();
    }

    private async void OnSelectedTrackChanged(MediaFile? mediaFile)
    {
        if (mediaFile != null)
        {
            SelectedMediaFile = mediaFile;

            // Ensure VuMeter always reflects the current channel count
            if (FindName("VuMeter") is VuMeter vuMeter && _audioEngine != null)
            {
                vuMeter.ChannelCount = _audioEngine.ChannelCount;
                vuMeter.SafeUpdateLayout();
            }

            if (mediaFile.AlbumCover == null)
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    { mediaFile.LoadAlbumCover(); }
                    catch (System.Exception ex) { _logger.LogWarning(ex, "Failed to load album cover for {Title}", mediaFile.Title); }
                });
            }

            if (ReferenceEquals(SelectedMediaFile, mediaFile))
            {
                UpdateAlbumCoverDisplay(mediaFile);
                RaiseHasAlbumCoverChanged();
            }
        }
        else
        {
            SelectedMediaFile = null;
            UpdateAlbumCoverDisplay(null);
            RaiseHasAlbumCoverChanged();
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsImageFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif";
    }

    private static bool IsImageUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Load image bytes from a file off the UI thread, then decode on the UI thread.
    /// Returns null if loading fails.
    /// </summary>
    private async System.Threading.Tasks.Task<BitmapImage?> LoadImageFromFileAsync(string filePath)
    {
        try
        {
            byte[] bytes = await System.Threading.Tasks.Task.Run(() => File.ReadAllBytes(filePath));
            return DecodeBitmapFromBytes(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load image file {File}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Download image bytes off the UI thread, then decode on the UI thread.
    /// Returns null if download or decode fails.
    /// </summary>
    private async System.Threading.Tasks.Task<BitmapImage?> DownloadImageAsync(string url)
    {
        try
        {
            using HttpClient client = new();
            byte[] bytes = await client.GetByteArrayAsync(url);
            BitmapImage? result = DecodeBitmapFromBytes(bytes);
            if (result == null || result.PixelWidth < 10 || result.PixelHeight < 10)
            {
                _logger.LogWarning("Downloaded image from {Url} is invalid or too small", url);
                return null;
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download image from {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Decode raw bytes into a frozen BitmapImage on the calling (UI) thread.
    /// Returns null if the bytes cannot be decoded.
    /// </summary>
    private static BitmapImage? DecodeBitmapFromBytes(byte[] bytes)
    {
        try
        {
            using MemoryStream ms = new(bytes);
            BitmapImage bi = new();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Convert any BitmapSource (e.g. from Clipboard) to a frozen BitmapImage via PNG round-trip.
    /// </summary>
    private static BitmapImage? ToBitmapImage(System.Windows.Media.ImageSource? source)
    {
        if (source is BitmapImage already && already.IsFrozen)
            return already;

        if (source is System.Windows.Media.Imaging.BitmapSource bitmapSource)
        {
            try
            {
                PngBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                using MemoryStream ms = new();
                encoder.Save(ms);
                return DecodeBitmapFromBytes(ms.ToArray());
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Apply the bitmap to every selected track, mark dirty, refresh display, and notify the VM.
    /// Pass null to remove the cover.
    /// </summary>
    private void ApplyAlbumCoverToSelection(BitmapImage? bitmap)
    {
        IReadOnlyList<MediaFile> selection = _selectionService.MultiSelection;

        if (selection.Count > 1)
        {
            string action = bitmap == null ? "remove the album cover from" : "apply this album cover to";
            MessageBoxResult confirm = MessageBox.Show(
                $"You are about to {action} {selection.Count} selected tracks.\n\nContinue?",
                "Apply to Multiple Tracks",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;
        }

        foreach (MediaFile mf in selection)
        {
            mf.AlbumCover = bitmap;
            mf.MarkPropertyDirty(nameof(MediaFile.AlbumCover));
        }

        // Refresh the display for the currently shown track immediately.
        if (SelectedMediaFile != null && selection.Contains(SelectedMediaFile))
        {
            UpdateAlbumCoverDisplay(SelectedMediaFile);
            RaiseHasAlbumCoverChanged();
        }

        // Let the VM know dirty state changed so the Save button enables.
        IMediaTabViewModel? vm = App.AppHost?.Services?.GetService<IMediaTabViewModel>();
        if (vm is MediaTabViewModel ptvm)
            ptvm.NotifyDirtyStateChanged();
    }

    // ── Drag & Drop ──────────────────────────────────────────────────────────

    private void CoverGrid_DragOver(object sender, DragEventArgs e)
    {
        bool isValid = false;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
            isValid = files != null && files.Length == 1 && IsImageFile(files[0]);
        }
        else if (e.Data.GetDataPresent(DataFormats.Text))
        {
            string? text = e.Data.GetData(DataFormats.Text) as string;
            isValid = !string.IsNullOrWhiteSpace(text) && IsImageUrl(text);
        }

        e.Effects = isValid && IsLibraryMode && _selectionService.MultiSelection.Count > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void CoverGrid_Drop(object sender, DragEventArgs e)
    {
        if (!IsLibraryMode || _selectionService.MultiSelection.Count == 0)
        {
            e.Handled = true;
            return;
        }

        BitmapImage? bitmap = null;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files is [string file] && IsImageFile(file))
                bitmap = await LoadImageFromFileAsync(file);
        }
        else if (e.Data.GetDataPresent(DataFormats.Text))
        {
            string? text = e.Data.GetData(DataFormats.Text) as string;
            if (!string.IsNullOrWhiteSpace(text) && IsImageUrl(text))
                bitmap = await DownloadImageAsync(text);
        }

        if (bitmap != null)
            ApplyAlbumCoverToSelection(bitmap);

        e.Handled = true;
    }

    // ── Context Menu ─────────────────────────────────────────────────────────

    private void CopyImageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMediaFile?.AlbumCover != null)
            Clipboard.SetImage(SelectedMediaFile.AlbumCover);
    }

    private void PasteImageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLibraryMode || _selectionService.MultiSelection.Count == 0 || !Clipboard.ContainsImage())
            return;

        BitmapImage? bitmap = ToBitmapImage(Clipboard.GetImage());
        if (bitmap != null)
            ApplyAlbumCoverToSelection(bitmap);
    }

    private void SaveImageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMediaFile?.AlbumCover == null)
            return;

        SaveFileDialog dlg = new()
        {
            Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp",
            DefaultExt = "png",
            FileName = $"{SelectedMediaFile.Artist} - {SelectedMediaFile.Title}"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            BitmapEncoder encoder = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
                ".bmp"            => new BmpBitmapEncoder(),
                _                 => new PngBitmapEncoder()
            };
            encoder.Frames.Add(BitmapFrame.Create(SelectedMediaFile.AlbumCover));
            using FileStream stream = File.Create(dlg.FileName);
            encoder.Save(stream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save image to {File}", dlg.FileName);
        }
    }

    private void RemoveImageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLibraryMode || _selectionService.MultiSelection.Count == 0)
            return;

        ApplyAlbumCoverToSelection(null);
    }
}
