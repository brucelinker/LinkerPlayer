using ATL;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkerPlayer.BassLibs;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels.Properties.Loaders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LinkerPlayer.ViewModels;

public interface IPropertiesViewModel
{
    ObservableCollection<TagItem> MetadataItems { get; }
    ObservableCollection<TagItem> PropertyItems { get; }
    ObservableCollection<TagItem> ReplayGainItems { get; }
    ObservableCollection<TagItem> PictureInfoItems { get; }
    TagItem CommentItem { get; }
    TagItem LyricsItem { get; }
    BitmapImage? AlbumCoverSource { get; }
    bool HasUnsavedChanges { get; }
    bool IsMultipleSelection { get; }
    int SelectedFilesCount { get; }
    event EventHandler<bool>? CloseRequested;
}

/// <summary>
/// ViewModel for the Properties window - REFACTORED to use loader pattern with debounced multi-selection
/// </summary>
public partial class PropertiesViewModel : ObservableObject, IPropertiesViewModel, IDisposable
{
    // Dependencies
    private readonly ISharedDataModel _sharedDataModel; // change to interface
    private readonly ILogger<PropertiesViewModel> _logger;
    private readonly IBpmDetector? _bpmDetector;
    private readonly IReplayGainCalculator? _replayGainCalculator;

    // Loaders - injected for clean separation of concerns
    private readonly CoreMetadataLoader _coreMetadataLoader;
    private readonly CustomMetadataLoaderAtl _customMetadataLoader;
    private readonly FilePropertiesLoader _filePropertiesLoader;
    private readonly ReplayGainLoaderAtl _replayGainLoader;
    private readonly PictureInfoLoaderAtl _pictureInfoLoader;
    private readonly LyricsCommentLoaderAtl _lyricsCommentLoader;

    // State
    private Track? _atlTrack;
    private List<Track> _atlTracks = new();
    private CancellationTokenSource? _bpmDetectionCts;
    private CancellationTokenSource? _replayGainCalculationCts;

    // Track album cover state for proper display
    private BitmapImage? _cachedAlbumCover;
    private bool _coversAreDifferent;

    // Debouncing for multi-selection
    private DispatcherTimer? _selectionDebounceTimer;
    private const int SelectionDebounceMs = 300; // Wait 300ms after last selection change
    private bool _disposed;

    // Observable properties
    [ObservableProperty] private bool hasUnsavedChanges;
    [ObservableProperty] private bool isBpmDetecting;
    [ObservableProperty] private double bpmDetectionProgress;
    [ObservableProperty] private string bpmDetectionStatus = string.Empty;
    [ObservableProperty] private bool isReplayGainCalculating;
    [ObservableProperty] private double replayGainCalculationProgress;
    [ObservableProperty] private string replayGainCalculationStatus = string.Empty;
    [ObservableProperty] private bool isMultipleSelection;
    [ObservableProperty] private int selectedFilesCount;

    // Collections
    public ObservableCollection<TagItem> MetadataItems { get; } = [];
    public ObservableCollection<TagItem> PropertyItems { get; } = [];
    public ObservableCollection<TagItem> ReplayGainItems { get; } = [];
    public ObservableCollection<TagItem> PictureInfoItems { get; } = [];

    [ObservableProperty] private TagItem _commentItem = new();
    [ObservableProperty] private TagItem _lyricsItem = new();

    public event EventHandler<bool>? CloseRequested;

    public PropertiesViewModel(
        ISharedDataModel sharedDataModel,
        CoreMetadataLoader coreMetadataLoader,
        CustomMetadataLoaderAtl customMetadataLoader,
        FilePropertiesLoader filePropertiesLoader,
        ReplayGainLoaderAtl replayGainLoader,
        PictureInfoLoaderAtl pictureInfoLoader,
        LyricsCommentLoaderAtl lyricsCommentLoader,
        ILogger<PropertiesViewModel> logger,
        IBpmDetector? bpmDetector = null,
        IReplayGainCalculator? replayGainCalculator = null)
    {
        _sharedDataModel = sharedDataModel;
        _coreMetadataLoader = coreMetadataLoader;
        _customMetadataLoader = customMetadataLoader;
        _filePropertiesLoader = filePropertiesLoader;
        _replayGainLoader = replayGainLoader;
        _pictureInfoLoader = pictureInfoLoader;
        _lyricsCommentLoader = lyricsCommentLoader;
        _logger = logger;
        _bpmDetector = bpmDetector;
        _replayGainCalculator = replayGainCalculator;

        // Initialize debounce timer
        _selectionDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SelectionDebounceMs)
        };
        _selectionDebounceTimer.Tick += SelectionDebounceTimer_Tick;

        _sharedDataModel.PropertyChanged += SharedDataModel_PropertyChanged!;
        _sharedDataModel.SelectedTracksChanged += SelectedTracks_CollectionChanged!;

        // Subscribe to SelectedTracks collection changes
        //_sharedDataModel.SelectedTracks.CollectionChanged += SelectedTracks_CollectionChanged!;

        _logger.LogDebug("PropertiesViewModel constructor: SelectedTracks.Count = {Count}, SelectedTrack = {Track}",
            _sharedDataModel.SelectedTracks.Count,
         _sharedDataModel.SelectedTrack?.Title ?? "null");

        // Check for multi-selection on initialization
        if (_sharedDataModel.SelectedTracks.Count > 1)
        {
            LoadMultipleTracksData(_sharedDataModel.SelectedTracks);
        }
        else if (_sharedDataModel.SelectedTrack != null)
        {
            LoadTrackData(_sharedDataModel.SelectedTrack.Path);
        }
    }

    private void SelectedTracks_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // PERFORMANCE FIX: Debounce rapid selection changes (e.g., Ctrl+clicking multiple tracks)
        // Instead of reloading metadata on every single selection change, wait for user to finish selecting

        _logger.LogDebug("SelectedTracks_CollectionChanged: New count = {Count}", _sharedDataModel.SelectedTracks.Count);

        // Reset the debounce timer - this delays the actual load until selections stabilize
        _selectionDebounceTimer?.Stop();
        _selectionDebounceTimer?.Start();
    }

    private void SelectionDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _selectionDebounceTimer?.Stop();

        // Now actually load the data after selections have stabilized
        if (_sharedDataModel.SelectedTracks.Count > 1)
        {
            _logger.LogDebug("Debounced load: Switching to multi-selection mode with {Count} tracks", _sharedDataModel.SelectedTracks.Count);
            LoadMultipleTracksData(_sharedDataModel.SelectedTracks);
            SortMetadataItems();
        }
        else if (_sharedDataModel.SelectedTracks.Count == 1 && _sharedDataModel.SelectedTrack != null)
        {
            _logger.LogDebug("Debounced load: Switching to single-selection mode for {Track}", _sharedDataModel.SelectedTrack.Title);
            LoadTrackData(_sharedDataModel.SelectedTrack.Path);
        }
    }

    [RelayCommand]
    public void Ok()
    {
        if (ApplyChanges())
        {
            UpdateTrackMetadata();
            CloseRequested?.Invoke(this, true);
        }
    }

    [RelayCommand]
    public void Apply()
    {
        if (ApplyChanges())
        {
            UpdateTrackMetadata();
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        if (HasUnsavedChanges)
        {
            MessageBoxResult result = MessageBox.Show("You have unsaved changes. Discard changes?",
          "Unsaved Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.No)
            {
                return;
            }
        }
        CloseRequested?.Invoke(this, false);
    }

    private void SharedDataModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISharedDataModel.SelectedTrack) && _sharedDataModel.SelectedTrack != null)
        {
            if (HasUnsavedChanges)
            {
                MessageBoxResult result = MessageBox.Show("You have unsaved changes. Apply before switching tracks?",
              "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    if (!ApplyChanges())
                    {
                        return;
                    }
                    UpdateTrackMetadata();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            // Check for multi-selection
            if (_sharedDataModel.SelectedTracks.Count > 1)
            {
                LoadMultipleTracksData(_sharedDataModel.SelectedTracks);
            }
            else
            {
                LoadTrackData(_sharedDataModel.SelectedTrack.Path);
            }
        }
    }

    private void LoadTrackData(string path)
    {
        try
        {
            // Set single-file mode
            IsMultipleSelection = false;
            SelectedFilesCount = 1;

            // Validate file
            if (!System.IO.File.Exists(path))
            {
                _logger.LogError("File not found: {Path}", path);
                return;
            }

            FileInfo fileInfo = new(path);
            if (fileInfo.Length == 0)
            {
                _logger.LogError("File is empty: {Path}", path);
                return;
            }

            // Check extension
            string extension = Path.GetExtension(path).ToLowerInvariant();

            if (!MusicLibrary._supportedAudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Unsupported file format: {Extension} for file: {Path}", extension, path);
                return;
            }

            try
            {
                _atlTrack = new Track(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open file for metadata: {Path}", path);
                return;
            }

            LoadAllSectionsAtl(_atlTrack);

            // Sort metadata: regular tags first, custom tags (with angle brackets) last
            SortMetadataItems();

            HasUnsavedChanges = false;
            _logger.LogDebug("Successfully loaded track data for: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading track data for file: {Path}", path);
        }
    }

    private void LoadMultipleTracksData(IEnumerable<MediaFile> tracks)
    {
        try
        {
            _atlTracks.Clear();

            List<MediaFile> trackList = tracks.ToList();
            IsMultipleSelection = true;
            SelectedFilesCount = trackList.Count;

            _logger.LogDebug("LoadMultipleTracksData: Loading {Count} tracks", trackList.Count);

            // Load all files
            foreach (MediaFile track in trackList)
            {
                if (!System.IO.File.Exists(track.Path))
                {
                    _logger.LogWarning("File not found: {Path}", track.Path);
                    continue;
                }

                try
                {
                    Track atl = new Track(track.Path);
                    _atlTracks.Add(atl);
                    _logger.LogDebug("LoadMultipleTracksData: Loaded ATL track {Path}", track.Path);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading file: {Path}", track.Path);
                }
            }

            if (_atlTracks.Count == 0)
            {
                _logger.LogError("No files could be loaded");
                return;
            }

            _logger.LogDebug("LoadMultipleTracksData: Successfully loaded {AtlCount} ATL tracks, starting to load sections", _atlTracks.Count);

            LoadAllSectionsMultipleAtl(_atlTracks);

            HasUnsavedChanges = false;
            _logger.LogDebug("Successfully loaded data for {Count} tracks", _atlTracks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading multiple tracks data");
        }
    }

    private void LoadAllSectionsAtl(Track track)
    {
        // Clear collections first
        MetadataItems.Clear();
        PropertyItems.Clear();
        ReplayGainItems.Clear();
        PictureInfoItems.Clear();

        try
        {
            // Use ATL loaders where available
            // Core metadata
            _coreMetadataLoader.Load(track, MetadataItems);
            foreach (TagItem? item in MetadataItems.Where(i => i.IsEditable))
            {
                item.PropertyChanged += TagItem_PropertyChanged!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading core metadata from ATL track");
        }

        try
        {
            // File properties
            _filePropertiesLoader.Load(track, PropertyItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading file properties from ATL track");
        }

        try
        {
            // Custom/additional metadata
            _customMetadataLoader.Load(track, MetadataItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading custom metadata from ATL track");
        }

        try
        {
            // ReplayGain
            _replayGainLoader.Load(track, ReplayGainItems);
            foreach (TagItem? item in ReplayGainItems.Where(i => i.IsEditable))
            {
                item.PropertyChanged += TagItem_PropertyChanged!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading ReplayGain from ATL track");
        }

        try
        {
            // Picture info
            _pictureInfoLoader.Load(track, PictureInfoItems);
            foreach (TagItem? item in PictureInfoItems.Where(i => i.IsEditable))
            {
                item.PropertyChanged += TagItem_PropertyChanged!;
            }

            // Load album cover for single file
            _coversAreDifferent = false;
            _cachedAlbumCover = track.EmbeddedPictures is { Count: > 0 }
                ? LoadAlbumCoverFromPictureInfo(track.EmbeddedPictures[0])
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading picture info from ATL track");
        }

        try
        {
            // Comment
            CommentItem = _lyricsCommentLoader.LoadComment(track);
            CommentItem.PropertyChanged += TagItem_PropertyChanged!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading comment from ATL track");
        }

        try
        {
            // Lyrics
            LyricsItem = _lyricsCommentLoader.LoadLyrics(track);
            LyricsItem.PropertyChanged += TagItem_PropertyChanged!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading lyrics from ATL track");
        }

        OnPropertyChanged(nameof(AlbumCoverSource));

        // Rating (special handling because ATL uses Popularity 0-255)
        double currentRating = _atlTrack?.Popularity != null
            ? Math.Round(_atlTrack.Popularity.Value * 5.0 / 255.0, 1)
            : 0.0;

        TagItem ratingItem = new TagItem
        {
            Name = "Rating",
            Value = currentRating.ToString("0.0"),
            OriginalValue = currentRating.ToString("0.0"),
            IsEditable = true,
            UpdateAction = newValue =>
            {
                if (double.TryParse(newValue, out double r))
                {
                    double clamped = Math.Round(Math.Clamp(r, 0.0, 5.0), 1);
                    _atlTrack!.Popularity = (float)(clamped * 255.0 / 5.0);
                }
            }
        };

        MetadataItems.Add(ratingItem);
        ratingItem.PropertyChanged += TagItem_PropertyChanged!;
        IMusicLibrary musicLibrary = App.AppHost.Services.GetRequiredService<IMusicLibrary>();
        musicLibrary.MarkLibraryDirty();
    }

    private void LoadAllSectionsMultipleAtl(IReadOnlyList<Track> atlTracks)
    {
        // Clear all collections ONCE before loading
        MetadataItems.Clear();
        PropertyItems.Clear();
        ReplayGainItems.Clear();
        PictureInfoItems.Clear();

        _logger.LogDebug("LoadAllSectionsMultipleAtl: Starting to load metadata for {Count} ATL tracks", atlTracks.Count);

        try
        {
            _coreMetadataLoader.LoadMultiple(atlTracks, MetadataItems);
            _logger.LogDebug("LoadAllSectionsMultipleAtl: Loaded {Count} core metadata items", MetadataItems.Count);

            foreach (TagItem? item in MetadataItems.Where(i => i.IsEditable))
            {
                item.PropertyChanged += TagItem_PropertyChanged!;
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading core metadata (multiple ATL)"); }

        try
        {
            _customMetadataLoader.LoadMultiple(atlTracks, MetadataItems);
            _logger.LogDebug("LoadAllSectionsMultipleAtl: Total metadata items after custom: {Count}", MetadataItems.Count);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading custom metadata (multiple ATL)"); }

        try
        {
            _filePropertiesLoader.LoadMultiple(atlTracks, PropertyItems);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading file properties (multiple ATL)"); }

        try
        {
            _replayGainLoader.LoadMultiple(atlTracks, ReplayGainItems);
            foreach (TagItem? item in ReplayGainItems.Where(i => i.IsEditable))
            {
                item.PropertyChanged += TagItem_PropertyChanged!;
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading ReplayGain (multiple ATL)"); }

        try
        {
            _pictureInfoLoader.LoadMultiple(atlTracks, PictureInfoItems);

            _coversAreDifferent = PictureInfoItems.Any(item => item.Value == "<various>");

            if (!_coversAreDifferent && atlTracks.Count > 0 && atlTracks[0].EmbeddedPictures is { Count: > 0 })
            {
                _cachedAlbumCover = LoadAlbumCoverFromPictureInfo(atlTracks[0].EmbeddedPictures[0]);
            }
            else
            {
                _cachedAlbumCover = null;
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading picture info (multiple ATL)"); }

        try
        {
            CommentItem = _lyricsCommentLoader.LoadCommentMultiple(atlTracks);
            CommentItem.PropertyChanged += TagItem_PropertyChanged!;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading comment (multiple ATL)"); }

        try
        {
            LyricsItem = _lyricsCommentLoader.LoadLyricsMultiple(atlTracks);
            LyricsItem.PropertyChanged += TagItem_PropertyChanged!;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading lyrics (multiple ATL)"); }

        OnPropertyChanged(nameof(AlbumCoverSource));

        // Rating (special handling because ATL uses Popularity 0-255)
        double currentRating = _atlTrack?.Popularity != null
            ? Math.Round(_atlTrack.Popularity.Value * 5.0 / 255.0, 1)
            : 0.0;

        TagItem ratingItem = new TagItem
        {
            Name = "Rating",
            Value = currentRating.ToString("0.0"),
            OriginalValue = currentRating.ToString("0.0"),
            IsEditable = true,
            UpdateAction = newValue =>
            {
                if (double.TryParse(newValue, out double r))
                {
                    double clamped = Math.Round(Math.Clamp(r, 0.0, 5.0), 1);
                    _atlTrack!.Popularity = (float)(clamped * 255.0 / 5.0);
                }
            }
        };

        MetadataItems.Add(ratingItem);
        ratingItem.PropertyChanged += TagItem_PropertyChanged!;
        IMusicLibrary musicLibrary = App.AppHost.Services.GetRequiredService<IMusicLibrary>();
        musicLibrary.MarkLibraryDirty();
    }

    public System.Windows.Media.Imaging.BitmapImage? AlbumCoverSource
    {
        get
        {
            if (_coversAreDifferent || (_cachedAlbumCover == null && IsMultipleSelection))
            {
                // Different covers or no cover in multi-selection - show reel.png
                return LoadReelPlaceholder();
            }

            // Return cached cover (could be null for single file with no cover)
            return _cachedAlbumCover ?? LoadReelPlaceholder();
        }
    }

    private static BitmapImage? LoadReelPlaceholder()
    {
        try
        {
            BitmapImage reelImage = new BitmapImage();
            reelImage.BeginInit();
            reelImage.UriSource = new Uri("pack://application:,,,/LinkerPlayer;component/Images/reel.png", UriKind.Absolute);
            reelImage.CacheOption = BitmapCacheOption.OnLoad;
            reelImage.EndInit();
            reelImage.Freeze();
            return reelImage;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static BitmapImage? LoadAlbumCoverFromPictureInfo(ATL.PictureInfo picture)
    {
        try
        {
            if (picture.PictureData is { Length: > 0 })
            {
                using MemoryStream ms = new MemoryStream(picture.PictureData);
                BitmapImage albumCover = new BitmapImage();
                albumCover.BeginInit();
                albumCover.CacheOption = BitmapCacheOption.OnLoad;
                albumCover.StreamSource = ms;
                albumCover.EndInit();
                albumCover.Freeze();
                return albumCover;
            }
        }
        catch (Exception)
        {
            // Ignore errors loading cover
        }
        return null;
    }

    private void SortMetadataItems()
    {
        _logger.LogDebug("SortMetadataItems: Sorting {Count} items", MetadataItems.Count);

        // Split into core and custom tags
        List<TagItem> coreTags = MetadataItems.Where(item => !item.Name.StartsWith("<")).ToList();
        List<TagItem> customTags = MetadataItems.Where(item => item.Name.StartsWith("<"))
          .OrderBy(item => item.Name)
            .ToList();

        // Clear and re-add: core tags in original order, then custom tags alphabetically
        MetadataItems.Clear();

        // Add core tags first (in original order from CoreMetadataLoader)
        foreach (TagItem item in coreTags)
        {
            MetadataItems.Add(item);
            // Re-subscribe to PropertyChanged if editable
            if (item.IsEditable)
            {
                // Remove old subscription first to avoid duplicates
                item.PropertyChanged -= TagItem_PropertyChanged!;
                item.PropertyChanged += TagItem_PropertyChanged!;
            }
        }

        // Add custom tags second (alphabetically sorted)
        foreach (TagItem item in customTags)
        {
            MetadataItems.Add(item);
            // Custom tags are typically not editable, but check anyway
            if (item.IsEditable)
            {
                item.PropertyChanged -= TagItem_PropertyChanged!;
                item.PropertyChanged += TagItem_PropertyChanged!;
            }
        }
    }

    private void TagItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagItem.Value))
        {
            HasUnsavedChanges = true;
        }
    }

    private bool ApplyChanges()
    {
        try
        {
            if (IsMultipleSelection)
            {
                // Check if any multi-value fields were edited
                bool hasEditedMultiValueFields = MetadataItems
                    .Where(i => i.HasMultipleValues && i.Value != i.OriginalValue)
                    .Any();

                if (hasEditedMultiValueFields)
                {
                    MessageBoxResult confirm = MessageBox.Show(
                        "You have edited fields that had different values across the selected files. " +
                        "Saving will apply the same value to ALL selected files.\n\nContinue?",
                        "Overwrite Multiple Values",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (confirm != MessageBoxResult.Yes)
                    {
                        return false;
                    }
                }

                // Only apply update actions for items the user actually changed
                foreach (TagItem item in MetadataItems.Where(i => i.IsEditable && i.Value != i.OriginalValue))
                {
                    if (string.IsNullOrWhiteSpace(item.Value) &&
                          (item.Name == "Year" || item.Name == "Track Number" ||
                    item.Name == "Total Tracks" || item.Name == "Disc Number" ||
                      item.Name == "Total Discs" || item.Name == "Beats Per Minute"))
                    {
                        item.Value = "0";
                    }
                    item.UpdateAction?.Invoke(item.Value);
                }

                foreach (TagItem item in ReplayGainItems.Where(i => i.IsEditable && i.Value != i.OriginalValue))
                {
                    item.UpdateAction?.Invoke(item.Value);
                }

                foreach (TagItem item in PictureInfoItems.Where(i => i.IsEditable && i.Value != i.OriginalValue))
                {
                    item.UpdateAction?.Invoke(item.Value);
                }

                if (CommentItem.IsEditable && CommentItem.Value != CommentItem.OriginalValue)
                {
                    CommentItem.UpdateAction?.Invoke(CommentItem.Value);
                }

                if (LyricsItem.IsEditable && LyricsItem.Value != LyricsItem.OriginalValue)
                {
                    LyricsItem.UpdateAction?.Invoke(LyricsItem.Value);
                }

                // Save all ATL tracks
                foreach (Track track in _atlTracks)
                {
                    track.Save();
                }
            }
            else
            {
                // Apply all pending update actions
                foreach (TagItem item in MetadataItems.Where(i => i.IsEditable))
                {
                    if (string.IsNullOrWhiteSpace(item.Value) &&
                          (item.Name == "Year" || item.Name == "Track Number" ||
                    item.Name == "Total Tracks" || item.Name == "Disc Number" ||
                      item.Name == "Total Discs" || item.Name == "Beats Per Minute"))
                    {
                        item.Value = "0";
                    }
                    item.UpdateAction?.Invoke(item.Value);
                }

                foreach (TagItem item in ReplayGainItems.Where(i => i.IsEditable))
                {
                    item.UpdateAction?.Invoke(item.Value);
                }

                foreach (TagItem item in PictureInfoItems.Where(i => i.IsEditable))
                {
                    item.UpdateAction?.Invoke(item.Value);
                }

                if (CommentItem.IsEditable)
                {
                    CommentItem.UpdateAction?.Invoke(CommentItem.Value);
                }

                if (LyricsItem.IsEditable)
                {
                    LyricsItem.UpdateAction?.Invoke(LyricsItem.Value);
                }

                _atlTrack!.Save();
            }

            HasUnsavedChanges = false;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying changes to metadata");
            MessageBox.Show($"Error applying changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void UpdateTrackMetadata()
    {
        if (IsMultipleSelection)
        {
            foreach (MediaFile track in _sharedDataModel.SelectedTracks)
            {
                track.UpdateFromFileMetadata();
            }

            try
            {
                IMusicLibrary musicLibrary = App.AppHost.Services.GetRequiredService<IMusicLibrary>();
                _ = Task.Run(async () =>
                    await musicLibrary.UpdateTracksAsync(_sharedDataModel.SelectedTracks,
                        updateMetadata: true, updateAnalysis: false));
            }
            catch { }
        }
        else
        {
            if (_sharedDataModel.SelectedTrack == null)
                return;

            _sharedDataModel.SelectedTrack.UpdateFromFileMetadata();

            if (_sharedDataModel.ActiveTrack == _sharedDataModel.SelectedTrack)
            {
                _sharedDataModel.ActiveTrack.UpdateFromFileMetadata();
            }

            try
            {
                IMusicLibrary musicLibrary = App.AppHost.Services.GetRequiredService<IMusicLibrary>();
                List<MediaFile> updated = new List<MediaFile> { _sharedDataModel.SelectedTrack };

                if (_sharedDataModel.ActiveTrack != null &&
                    !_sharedDataModel.ActiveTrack.Id.Equals(_sharedDataModel.SelectedTrack.Id, StringComparison.Ordinal))
                {
                    updated.Add(_sharedDataModel.ActiveTrack);
                }

                _ = Task.Run(async () =>
                    await musicLibrary.UpdateTracksAsync(updated,
                        updateMetadata: true, updateAnalysis: false));
            }
            catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Stop and dispose debounce timer
        if (_selectionDebounceTimer != null)
        {
            _selectionDebounceTimer.Stop();
            _selectionDebounceTimer.Tick -= SelectionDebounceTimer_Tick;
            _selectionDebounceTimer = null;
        }

        // Unsubscribe from events to prevent memory leaks
        _sharedDataModel.PropertyChanged -= SharedDataModel_PropertyChanged!;
        _sharedDataModel.SelectedTracksChanged -= SelectedTracks_CollectionChanged!;

        // Cancel any ongoing operations
        _bpmDetectionCts?.Cancel();
        _bpmDetectionCts?.Dispose();
        _replayGainCalculationCts?.Cancel();
        _replayGainCalculationCts?.Dispose();



        _logger.LogDebug("PropertiesViewModel disposed");
    }

    // BPM and ReplayGain commands remain in Part 2...
}
