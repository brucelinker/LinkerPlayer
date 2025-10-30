using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkerPlayer.BassLibs;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels.Properties.Loaders;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using File = TagLib.File;

namespace LinkerPlayer.ViewModels;

/// <summary>
/// ViewModel for the Properties window - REFACTORED to use loader pattern
/// </summary>
public partial class PropertiesViewModel : ObservableObject
{
    // Dependencies
    private readonly SharedDataModel _sharedDataModel;
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
        SharedDataModel sharedDataModel,
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

      _sharedDataModel.PropertyChanged += SharedDataModel_PropertyChanged!;
        
        // ALSO subscribe to SelectedTracks collection changes
        _sharedDataModel.SelectedTracks.CollectionChanged += SelectedTracks_CollectionChanged!;

// DIAGNOSTIC: Log selection state at constructor time
  _logger.LogInformation("PropertiesViewModel constructor: SelectedTracks.Count = {Count}, SelectedTrack = {Track}",
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
        _logger.LogInformation("SelectedTracks_CollectionChanged: New count = {Count}", _sharedDataModel.SelectedTracks.Count);
        
        // If multiple tracks are now selected, reload in multi-selection mode
        if (_sharedDataModel.SelectedTracks.Count > 1)
    {
          _logger.LogInformation("Switching to multi-selection mode with {Count} tracks", _sharedDataModel.SelectedTracks.Count);
            LoadMultipleTracksData(_sharedDataModel.SelectedTracks);
  SortMetadataItems(); // Don't forget to sort after loading!
      }
        // If selection dropped back to single track, reload single file
   else if (_sharedDataModel.SelectedTracks.Count == 1 && _sharedDataModel.SelectedTrack != null)
        {
       _logger.LogInformation("Switching to single-selection mode for {Track}", _sharedDataModel.SelectedTrack.Title);
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
        if (e.PropertyName == nameof(SharedDataModel.SelectedTrack) && _sharedDataModel.SelectedTrack != null)
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
            foreach (var file in _audioFiles)
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
     foreach (var file in _audioFiles)
            {
         file?.Dispose();
         }
            _audioFiles.Clear();

   var trackList = tracks.ToList();
  IsMultipleSelection = true;
      SelectedFilesCount = trackList.Count;

            _logger.LogInformation("LoadMultipleTracksData: Loading {Count} tracks", trackList.Count);

   // Load all files
          foreach (var track in trackList)
          {
        if (!System.IO.File.Exists(track.Path))
       {
      _logger.LogWarning("File not found: {Path}", track.Path);
   continue;
         }

        try
           {
    var audioFile = File.Create(track.Path);
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

   _logger.LogInformation("LoadMultipleTracksData: Successfully loaded {Count} files, starting to load sections", _audioFiles.Count);

            // Load all sections using loaders (multi-file mode)
        LoadAllSectionsMultiple(_audioFiles);

            HasUnsavedChanges = false;
        _logger.LogInformation("Successfully loaded data for {Count} tracks", _audioFiles.Count);
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

  _logger.LogInformation("LoadAllSections: Starting to load metadata for file");
 _logger.LogInformation("LoadAllSections: MetadataItems count before loading: {Count}", MetadataItems.Count);

        try 
   { 
         _coreMetadataLoader.Load(audioFile, MetadataItems);
            _logger.LogInformation("LoadAllSections: MetadataItems count after CoreMetadataLoader: {Count}", MetadataItems.Count);
            
            // Subscribe to PropertyChanged for all editable items
     foreach (var item in MetadataItems.Where(i => i.IsEditable))
  {
                item.PropertyChanged += TagItem_PropertyChanged!;
            }
  }
     catch (Exception ex) { _logger.LogError(ex, "Error loading core metadata"); }

        try 
        { 
      _customMetadataLoader.Load(audioFile, MetadataItems);
      _logger.LogInformation("LoadAllSections: MetadataItems count after CustomMetadataLoader: {Count}", MetadataItems.Count);
     // Custom metadata items are typically not editable
    }
        catch (Exception ex) { _logger.LogError(ex, "Error loading custom metadata"); }

   try { _filePropertiesLoader.Load(audioFile, PropertyItems); }
   catch (Exception ex) { _logger.LogError(ex, "Error loading file properties"); }

    try 
  { 
            _replayGainLoader.Load(audioFile, ReplayGainItems);
   // Subscribe to PropertyChanged for editable ReplayGain items
  foreach (var item in ReplayGainItems.Where(i => i.IsEditable))
       {
      item.PropertyChanged += TagItem_PropertyChanged!;
    }
  }
    catch (Exception ex) { _logger.LogError(ex, "Error loading ReplayGain"); }

        try 
        { 
      _pictureInfoLoader.Load(audioFile, PictureInfoItems);
      // Subscribe to PropertyChanged for editable picture items
            foreach (var item in PictureInfoItems.Where(i => i.IsEditable))
     {
           item.PropertyChanged += TagItem_PropertyChanged!;
     }
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

        _logger.LogInformation("LoadAllSections: Final MetadataItems count BEFORE sorting: {Count}", MetadataItems.Count);

        OnPropertyChanged(nameof(AlbumCoverSource));
    }

    private void LoadAllSectionsMultiple(IReadOnlyList<File> audioFiles)
    {
   // Clear all collections ONCE before loading
        MetadataItems.Clear();
   PropertyItems.Clear();
        ReplayGainItems.Clear();
        PictureInfoItems.Clear();

      _logger.LogInformation("LoadAllSectionsMultiple: Starting to load metadata for {Count} files", audioFiles.Count);
    _logger.LogInformation("LoadAllSectionsMultiple: MetadataItems count before loading: {Count}", MetadataItems.Count);

        try 
   { 
         _coreMetadataLoader.LoadMultiple(audioFiles, MetadataItems);
         _logger.LogInformation("LoadAllSectionsMultiple: MetadataItems count after CoreMetadataLoader: {Count}", MetadataItems.Count);
            
      // Subscribe to PropertyChanged for all editable items
       foreach (var item in MetadataItems.Where(i => i.IsEditable))
        {
    item.PropertyChanged += TagItem_PropertyChanged!;
    }
   }
        catch (Exception ex) { _logger.LogError(ex, "Error loading core metadata (multiple)"); }

      try 
 { 
            _customMetadataLoader.LoadMultiple(audioFiles, MetadataItems);
            _logger.LogInformation("LoadAllSectionsMultiple: MetadataItems count after CustomMetadataLoader: {Count}", MetadataItems.Count);
     }
        catch (Exception ex) { _logger.LogError(ex, "Error loading custom metadata (multiple)"); }

        try { _filePropertiesLoader.LoadMultiple(audioFiles, PropertyItems); }
        catch (Exception ex) { _logger.LogError(ex, "Error loading file properties (multiple)"); }

        try 
      { 
            _replayGainLoader.LoadMultiple(audioFiles, ReplayGainItems);
     // Subscribe to PropertyChanged for editable ReplayGain items
            foreach (var item in ReplayGainItems.Where(i => i.IsEditable))
     {
     item.PropertyChanged += TagItem_PropertyChanged!;
  }
        }
   catch (Exception ex) { _logger.LogError(ex, "Error loading ReplayGain (multiple)"); }

      try { _pictureInfoLoader.LoadMultiple(audioFiles, PictureInfoItems); }
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

        _logger.LogInformation("LoadAllSectionsMultiple: Final MetadataItems count BEFORE sorting: {Count}", MetadataItems.Count);
    }

    private void SortMetadataItems()
    {
        _logger.LogInformation("SortMetadataItems: Starting with {Count} items", MetadataItems.Count);
        
        // Split into core and custom tags
   var coreTags = MetadataItems.Where(item => !item.Name.StartsWith("<")).ToList();
var customTags = MetadataItems.Where(item => item.Name.StartsWith("<"))
            .OrderBy(item => item.Name)  // Sort custom tags alphabetically
    .ToList();

        _logger.LogInformation("SortMetadataItems: Core tags: {CoreCount}, Custom tags: {CustomCount}", 
    coreTags.Count, customTags.Count);
        _logger.LogInformation("SortMetadataItems: First 5 core items: {Items}", 
            string.Join(", ", coreTags.Take(5).Select(i => i.Name)));

      // Clear and re-add: core tags in original order, then custom tags alphabetically
  MetadataItems.Clear();
 
   // Add core tags first (in original order from CoreMetadataLoader)
        foreach (var item in coreTags)
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
        foreach (var item in customTags)
        {
            MetadataItems.Add(item);
    // Custom tags are typically not editable, but check anyway
  if (item.IsEditable)
    {
                item.PropertyChanged -= TagItem_PropertyChanged!;
        item.PropertyChanged += TagItem_PropertyChanged!;
         }
        }
        
      _logger.LogInformation("SortMetadataItems: Final MetadataItems count: {Count}", MetadataItems.Count);
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
                foreach (var file in _audioFiles)
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
            foreach (var track in _sharedDataModel.SelectedTracks)
            {
                track.UpdateFromFileMetadata();
            }
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
        }
    }

    public System.Windows.Media.Imaging.BitmapImage? AlbumCoverSource =>
        PictureInfoItems.FirstOrDefault(item => item.Name == "Album Cover")?.AlbumCoverSource;

    // BPM and ReplayGain commands remain in Part 2...
}
