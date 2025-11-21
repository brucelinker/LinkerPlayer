using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkerPlayer.BassLibs;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels.Properties.Loaders;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using File = TagLib.File;

namespace LinkerPlayer.ViewModels;

/// <summary>
/// ViewModel for the Properties window - REFACTORED to use loader pattern with debounced multi-selection
/// </summary>
public partial class PropertiesViewModel : ObservableObject, IDisposable
{
    // Dependencies
    private readonly ISharedDataModel _sharedDataModel; // change to interface
    private readonly ILogger<PropertiesViewModel> _logger;
    private readonly IBpmDetector? _bpmDetector;
    private readonly IReplayGainCalculator? _replayGainCalculator;

    // Loaders - injected for clean separation of concerns
    private readonly CoreMetadataLoader _coreMetadataLoader;
    private readonly CustomMetadataLoader _customMetadataLoader;
    private readonly FilePropertiesLoader _filePropertiesLoader;
    private readonly ReplayGainLoader _replayGainLoader;
    private readonly PictureInfoLoader _pictureInfoLoader;
    private readonly LyricsCommentLoader _lyricsCommentLoader;

    // State
    private File? _audioFile;
    private List<File> _audioFiles = new();
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
        CustomMetadataLoader customMetadataLoader,
        FilePropertiesLoader filePropertiesLoader,
        ReplayGainLoader replayGainLoader,
        PictureInfoLoader pictureInfoLoader,
        LyricsCommentLoader lyricsCommentLoader,
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
            // Cleanup
            _audioFile?.Dispose();
            _audioFile = null;
            foreach (File file in _audioFiles)
            {
                file?.Dispose();
            }
            _audioFiles.Clear();

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
            string[] supportedExtensions = [".mp3", ".flac", ".ogg", ".opus", ".m4a", ".mp4", ".aac", ".wma", ".wav", ".aiff", ".ape", ".wv", ".mpc"];

            if (!supportedExtensions.Contains(extension))
            {
                _logger.LogWarning("Unsupported file format: {Extension} for file: {Path}", extension, path);
                return;
            }

            // Load TagLib file
            try
            {
                _audioFile = File.Create(path);
            }
            catch (TagLib.CorruptFileException ex)
            {
                _logger.LogError(ex, "Corrupted file detected: {Path}", path);
                return;
            }
            catch (TagLib.UnsupportedFormatException ex)
            {
                _logger.LogError(ex, "Unsupported format for file: {Path}", path);
                return;
            }
            catch (ArgumentException ex) when (ex.ParamName == "ident")
            {
                _logger.LogError(ex, "Invalid metadata identifiers in file: {Path}", path);
                return;
            }

            // Load all sections using loaders
            LoadAllSections(_audioFile);

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
            // Cleanup
            _audioFile?.Dispose();
            _audioFile = null;
            foreach (File file in _audioFiles)
            {
                file?.Dispose();
            }
            _audioFiles.Clear();

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
                    File audioFile = File.Create(track.Path);
                    _audioFiles.Add(audioFile);
                    _logger.LogDebug("LoadMultipleTracksData: Loaded file {Path}", track.Path);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading file: {Path}", track.Path);
                }
            }

            if (_audioFiles.Count == 0)
            {
                _logger.LogError("No files could be loaded");
                return;
            }

            _logger.LogDebug("LoadMultipleTracksData: Successfully loaded {Count} files, starting to load sections", _audioFiles.Count);

            // Load all sections using loaders (multi-file mode)
            LoadAllSectionsMultiple(_audioFiles);

            HasUnsavedChanges = false;
            _logger.LogDebug("Successfully loaded data for {Count} tracks", _audioFiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading multiple tracks data");
        }
    }

    private void LoadAllSections(File audioFile)
    {
        // Clear all collections ONCE before loading
        MetadataItems.Clear();
        PropertyItems.Clear();
        ReplayGainItems.Clear();
        PictureInfoItems.Clear();

        _logger.LogDebug("LoadAllSections: Starting to load metadata for file");

        try
        {
            _coreMetadataLoader.Load(audioFile, MetadataItems);
            _logger.LogDebug("LoadAllSections: Loaded {Count} core metadata items", MetadataItems.Count);

            // Subscribe to PropertyChanged for all editable items
            foreach (TagItem? item in MetadataItems.Where(i => i.IsEditable))
            {
                item.PropertyChanged += TagItem_PropertyChanged!;
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading core metadata"); }

        try
        {
            _customMetadataLoader.Load(audioFile, MetadataItems);
            _logger.LogDebug("LoadAllSections: Total metadata items after custom: {Count}", MetadataItems.Count);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading custom metadata"); }

        try
        {
            _filePropertiesLoader.Load(audioFile, PropertyItems);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading file properties"); }

        try
        {
            _replayGainLoader.Load(audioFile, ReplayGainItems);
            // Subscribe to PropertyChanged for editable ReplayGain items
            foreach (TagItem? item in ReplayGainItems.Where(i => i.IsEditable))
            {
                item.PropertyChanged += TagItem_PropertyChanged!;
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading ReplayGain"); }

        try
        {
            _pictureInfoLoader.Load(audioFile, PictureInfoItems);
            // Subscribe to PropertyChanged for editable picture items
            foreach (TagItem? item in PictureInfoItems.Where(i => i.IsEditable))
            {
                item.PropertyChanged += TagItem_PropertyChanged!;
            }

            // Load album cover for single file
            _coversAreDifferent = false;
            _cachedAlbumCover = audioFile.Tag.Pictures is { Length: > 0 }
       ? LoadAlbumCoverFromTag(audioFile.Tag.Pictures[0])
              : null;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading picture info"); }

        try
        {
            CommentItem = _lyricsCommentLoader.LoadComment(audioFile);
            CommentItem.PropertyChanged += TagItem_PropertyChanged!;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading comment"); }

        try
        {
            LyricsItem = _lyricsCommentLoader.LoadLyrics(audioFile);
            LyricsItem.PropertyChanged += TagItem_PropertyChanged!;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading lyrics"); }

        OnPropertyChanged(nameof(AlbumCoverSource));
    }

    private void LoadAllSectionsMultiple(IReadOnlyList<File> audioFiles)
    {
        // Clear all collections ONCE before loading
        MetadataItems.Clear();
        PropertyItems.Clear();
        ReplayGainItems.Clear();
        PictureInfoItems.Clear();

        _logger.LogDebug("LoadAllSectionsMultiple: Starting to load metadata for {Count} files", audioFiles.Count);

        try
        {
            _coreMetadataLoader.LoadMultiple(audioFiles, MetadataItems);
            _logger.LogDebug("LoadAllSectionsMultiple: Loaded {Count} core metadata items", MetadataItems.Count);

            // Subscribe to PropertyChanged for all editable items
            foreach (TagItem? item in MetadataItems.Where(i => i.IsEditable))
            {
                item.PropertyChanged += TagItem_PropertyChanged!;
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading core metadata (multiple)"); }

        try
        {
            _customMetadataLoader.LoadMultiple(audioFiles, MetadataItems);
            _logger.LogDebug("LoadAllSectionsMultiple: Total metadata items after custom: {Count}", MetadataItems.Count);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading custom metadata (multiple)"); }

        try
        {
            _filePropertiesLoader.LoadMultiple(audioFiles, PropertyItems);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading file properties (multiple)"); }

        try
        {
            _replayGainLoader.LoadMultiple(audioFiles, ReplayGainItems);
            // Subscribe to PropertyChanged for editable ReplayGain items
            foreach (TagItem? item in ReplayGainItems.Where(i => i.IsEditable))
            {
                item.PropertyChanged += TagItem_PropertyChanged!;
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading ReplayGain (multiple)"); }

        try
        {
            _pictureInfoLoader.LoadMultiple(audioFiles, PictureInfoItems);

            // Check if covers are different by looking for <various> in any picture metadata
            _coversAreDifferent = PictureInfoItems.Any(item => item.Value == "<various>");

            // Load first file's cover if all covers are the same
            if (!_coversAreDifferent && audioFiles.Count > 0 && audioFiles[0].Tag.Pictures is { Length: > 0 })
            {
                _cachedAlbumCover = LoadAlbumCoverFromTag(audioFiles[0].Tag.Pictures[0]);
            }
            else
            {
                _cachedAlbumCover = null;
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading picture info (multiple)"); }

        try
        {
            CommentItem = _lyricsCommentLoader.LoadCommentMultiple(audioFiles);
            CommentItem.PropertyChanged += TagItem_PropertyChanged!;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading comment (multiple)"); }

        try
        {
            LyricsItem = _lyricsCommentLoader.LoadLyricsMultiple(audioFiles);
            LyricsItem.PropertyChanged += TagItem_PropertyChanged!;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error loading lyrics (multiple)"); }

        OnPropertyChanged(nameof(AlbumCoverSource));
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

    private static BitmapImage? LoadAlbumCoverFromTag(TagLib.IPicture picture)
    {
        try
        {
            if (picture.Data?.Data is { Length: > 0 })
            {
                using MemoryStream ms = new MemoryStream(picture.Data.Data);
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
                // Save all files
                foreach (File file in _audioFiles)
                {
                    file.Save();
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

                _audioFile!.Save();
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
        }
        else
        {
            if (_sharedDataModel.SelectedTrack == null)
            {
                return;
            }

            _sharedDataModel.SelectedTrack.UpdateFromFileMetadata();

            if (_sharedDataModel.ActiveTrack == _sharedDataModel.SelectedTrack)
            {
                _sharedDataModel.ActiveTrack.UpdateFromFileMetadata();
            }
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

        // Dispose audio files
        _audioFile?.Dispose();
        foreach (File file in _audioFiles)
        {
            file?.Dispose();
        }
        _audioFiles.Clear();

        _logger.LogDebug("PropertiesViewModel disposed");
    }

    // BPM and ReplayGain commands remain in Part 2...
}
