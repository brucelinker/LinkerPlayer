using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkerPlayer.BassLibs;
using LinkerPlayer.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using TagLib.Id3v2;
using TagLib.Mpeg4;
using TagLib.Ogg;
using File = TagLib.File;
using Tag = TagLib.Tag;
// ReSharper disable InconsistentNaming

namespace LinkerPlayer.ViewModels;

public partial class PropertiesViewModel : ObservableObject
{
    private readonly SharedDataModel _sharedDataModel;
    private readonly IMediaFileHelper _mediaFileHelper;
    private readonly ILogger<PropertiesViewModel> _logger;
    private readonly IBpmDetector? _bpmDetector;
    private File? _audioFile;
    private CancellationTokenSource? _bpmDetectionCts;

    [ObservableProperty] private bool hasUnsavedChanges;
    [ObservableProperty] private bool isBpmDetecting;
    [ObservableProperty] private double bpmDetectionProgress;
    [ObservableProperty] private string bpmDetectionStatus = string.Empty;

    public ObservableCollection<TagItem> MetadataItems { get; } = [];
    public ObservableCollection<TagItem> PropertyItems { get; } = [];
    public ObservableCollection<TagItem> ReplayGainItems { get; } = [];
    public ObservableCollection<TagItem> PictureInfoItems { get; } = [];
    [ObservableProperty] private TagItem _commentItem = new();
    [ObservableProperty] private TagItem _lyricsItem = new();

    public event EventHandler<bool>? CloseRequested;

    public PropertiesViewModel(SharedDataModel sharedDataModel, IMediaFileHelper mediaFileHelper, ILogger<PropertiesViewModel> logger, IBpmDetector? bpmDetector = null)
    {
        _sharedDataModel = sharedDataModel;
        _mediaFileHelper = mediaFileHelper;
        _logger = logger;
        _bpmDetector = bpmDetector;

        _sharedDataModel.PropertyChanged += SharedDataModel_PropertyChanged!;

        if (_sharedDataModel.SelectedTrack != null)
        {
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
                return; // Don't close if user chooses not to discard
            }
        }
        CloseRequested?.Invoke(this, false);
    }

    [RelayCommand(CanExecute = nameof(CanDetectBpm))]
    private async Task DetectBpmAsync()
    {
        if (_bpmDetector == null)
        {
            MessageBox.Show("BPM detection is not available. The BASS audio library may not be properly initialized.",
                "BPM Detection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_sharedDataModel.SelectedTrack == null)
        {
            return;
        }

        string filePath = _sharedDataModel.SelectedTrack.Path;

        try
        {
            IsBpmDetecting = true;
            BpmDetectionProgress = 0;
            BpmDetectionStatus = "Analyzing audio file...";

            _bpmDetectionCts = new CancellationTokenSource();

            var progress = new Progress<double>(value =>
       {
           BpmDetectionProgress = value * 100; // Convert to percentage
           BpmDetectionStatus = $"Detecting BPM... {BpmDetectionProgress:F0}%";
       });

            double? detectedBpm = await _bpmDetector.DetectBpmAsync(filePath, progress, _bpmDetectionCts.Token);

            if (_bpmDetectionCts.Token.IsCancellationRequested)
            {
                BpmDetectionStatus = "Detection cancelled";
                return;
            }

            if (detectedBpm.HasValue)
            {
                // Find the BPM metadata item and update it
                var bpmItem = MetadataItems.FirstOrDefault(item => item.Name == "Beats Per Minute");
                if (bpmItem != null)
                {
                    bpmItem.Value = ((uint)detectedBpm.Value).ToString();
                    BpmDetectionStatus = $"BPM detected: {detectedBpm.Value:F0}";
                }
                else
                {
                    BpmDetectionStatus = $"BPM detected: {detectedBpm.Value:F0} (unable to update field)";
                }

                _logger.LogInformation("BPM detection completed: {BPM}", detectedBpm.Value);
            }
            else
            {
                BpmDetectionStatus = "Could not detect BPM";
                MessageBox.Show("Unable to detect BPM for this track. The file may not have a clear rhythmic pattern.",
                  "BPM Detection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during BPM detection");
            BpmDetectionStatus = "Detection failed";
            MessageBox.Show($"An error occurred during BPM detection:\n{ex.Message}",
               "BPM Detection Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBpmDetecting = false;
            _bpmDetectionCts?.Dispose();
            _bpmDetectionCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelBpmDetection))]
    private void CancelBpmDetection()
    {
        _bpmDetectionCts?.Cancel();
        BpmDetectionStatus = "Cancelling...";
    }

    private bool CanDetectBpm() => !IsBpmDetecting && _sharedDataModel.SelectedTrack != null && _bpmDetector != null;

    private bool CanCancelBpmDetection() => IsBpmDetecting;

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
                        return; // Don't switch if save fails
                    }
                    UpdateTrackMetadata();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return; // Don't switch if user cancels
                }
            }
            LoadTrackData(_sharedDataModel.SelectedTrack.Path);
        }
    }

    private void LoadTrackData(string path)
    {
        try
        {
            // Dispose of previous file if exists
            _audioFile?.Dispose();
            _audioFile = null;

            // Validate file exists and has content
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

            // Check if file extension is supported by TagLib
            string extension = Path.GetExtension(path).ToLowerInvariant();
            string[] supportedExtensions = [".mp3", ".flac", ".ogg", ".opus", ".m4a", ".mp4", ".aac", ".wma", ".wav", ".aiff", ".ape", ".wv", ".mpc"];

            if (!supportedExtensions.Contains(extension))
            {
                _logger.LogWarning("Unsupported file format: {Extension} for file: {Path}", extension, path);
                return;
            }

            // Try to create TagLib file with additional error handling
            try
            {
                _audioFile = File.Create(path);
            }
            catch (TagLib.CorruptFileException ex)
            {
                _logger.LogError(ex, "Corrupted file detected: {Path} - {Message}", path, ex.Message);
                return;
            }
            catch (TagLib.UnsupportedFormatException ex)
            {
                _logger.LogError(ex, "Unsupported format for file: {Path} - {Message}", path, ex.Message);
                return;
            }
            catch (ArgumentException ex) when (ex.ParamName == "ident" && ex.Message.Contains("identifier must be four bytes long"))
            {
                _logger.LogError(ex, "Invalid metadata identifiers in file: {Path} - {Message}", path, ex.Message);
                return;
            }

            // Clear collections
            MetadataItems.Clear();
            PropertyItems.Clear();
            ReplayGainItems.Clear();
            PictureInfoItems.Clear();
            CommentItem.Value = "[ No comment available. ]";
            LyricsItem.Value = "[ No lyrics available. ]";

            // Load data with clean separation of concerns
            try
            {
                LoadCoreMetadata();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading core metadata for file: {Path} - {Message}", path, ex.Message);
            }

            try
            {
                LoadCustomMetadata();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading custom metadata for file: {Path} - {Message}", path, ex.Message);
            }

            // Sort metadata items: keep regular tags in original order, move custom tags (with angle brackets) to bottom
            var regularTags = MetadataItems.Where(item => !item.Name.StartsWith("<")).ToList();
            var customTags = MetadataItems.Where(item => item.Name.StartsWith("<")).ToList();

            MetadataItems.Clear();
            foreach (var item in regularTags)
            {
                MetadataItems.Add(item);
            }
            foreach (var item in customTags)
            {
                MetadataItems.Add(item);
            }

            try
            {
                LoadFileProperties();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading file properties for file: {Path} - {Message}", path, ex.Message);
            }

            try
            {
                LoadReplayGain();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading ReplayGain data for file: {Path} - {Message}", path, ex.Message);
            }

            try
            {
                LoadPictureInfo();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading picture information for file: {Path} - {Message}", path, ex.Message);
            }

            try
            {
                LoadCommentItem();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading comment for file: {Path} - {Message}", path, ex.Message);
            }

            try
            {
                LoadLyricsItem();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading lyrics for file: {Path} - {Message}", path, ex.Message);
            }

            HasUnsavedChanges = false;
            //_logger.LogInformation("Successfully loaded track data for: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading track data for file: {Path} - {Message}", path, ex.Message);
        }
    }

    private void LoadCoreMetadata()
    {
        if (_audioFile?.Tag == null)
        {
            _logger.LogWarning("No metadata found for the current file");
            return;
        }

        Tag tag = _audioFile.Tag;

        AddMetadataItem("Title", tag.Title ?? "", true, v => { tag.Title = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });

        // Smart artist field selection - use the first non-empty field
        string artistValue = _mediaFileHelper.GetBestArtistField(tag);
        AddMetadataItem("Artist", artistValue, true, v =>
        {
            tag.Performers = string.IsNullOrEmpty(v) ? [] : v.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            HasUnsavedChanges = true;
        });

        AddMetadataItem("Album", tag.Album ?? "", true, v => { tag.Album = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });

        // Smart album artist field selection
        string albumArtistValue = _mediaFileHelper.GetBestAlbumArtistField(tag);
        AddMetadataItem("Album Artist", albumArtistValue, true, v =>
        {
            tag.AlbumArtists = string.IsNullOrEmpty(v) ? [] : v.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            HasUnsavedChanges = true;
        });

        AddMetadataItem("Track Number", tag.Track > 0 ? tag.Track.ToString() : "", true, v => { tag.Track = uint.TryParse(v, out uint track) ? track : 0; HasUnsavedChanges = true; });
        AddMetadataItem("Total Tracks", tag.TrackCount > 0 ? tag.TrackCount.ToString() : "", true, v => { tag.TrackCount = uint.TryParse(v, out uint count) ? count : 0; HasUnsavedChanges = true; });
        AddMetadataItem("Disc Number", tag.Disc > 0 ? tag.Disc.ToString() : "", true, v => { tag.Disc = uint.TryParse(v, out uint disc) ? disc : 0; HasUnsavedChanges = true; });
        AddMetadataItem("Total Discs", tag.DiscCount > 0 ? tag.DiscCount.ToString() : "", true, v => { tag.DiscCount = uint.TryParse(v, out uint count) ? count : 0; HasUnsavedChanges = true; });

        AddMetadataItem("Year", tag.Year > 0 ? tag.Year.ToString() : "", true, v => { tag.Year = uint.TryParse(v, out uint year) ? year : 0; HasUnsavedChanges = true; });
        AddMetadataItem("Genre", tag.FirstGenre ?? string.Join(", ", tag.Genres ?? []), true, v => { tag.Genres = string.IsNullOrEmpty(v) ? [] : v.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray(); HasUnsavedChanges = true; });
        // Comment moved to separate section - see LoadCommentItem()

        AddMetadataItem("Composer", tag.FirstComposer ?? string.Join(", ", tag.Composers ?? []), true, v => { tag.Composers = string.IsNullOrEmpty(v) ? [] : v.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray(); HasUnsavedChanges = true; });
        AddMetadataItem("Copyright", tag.Copyright ?? "", true, v => { tag.Copyright = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });
        AddMetadataItem("Beats Per Minute", tag.BeatsPerMinute > 0 ? tag.BeatsPerMinute.ToString() : "", true, v => { tag.BeatsPerMinute = uint.TryParse(v, out uint bpm) ? bpm : 0; HasUnsavedChanges = true; });
        AddMetadataItem("Conductor", tag.Conductor ?? "", true, v => { tag.Conductor = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });
        AddMetadataItem("Grouping", tag.Grouping ?? "", true, v => { tag.Grouping = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });
        AddMetadataItem("Publisher", tag.Publisher ?? "", true, v => { tag.Publisher = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });

        // Only show ISRC if it has a value (most files don't have this)
        if (!string.IsNullOrWhiteSpace(tag.ISRC))
        {
            AddMetadataItem("ISRC", tag.ISRC, true, v => { tag.ISRC = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });
        }
    }

    private void LoadCustomMetadata()
    {
        if (_audioFile?.Tag == null)
        {
            _logger.LogWarning("No tag data found for custom metadata");
            return;
        }

        // Known standard fields that we DON'T want to show as custom (already in core metadata)
        HashSet<string> standardFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "TITLE", "ARTIST", "ALBUM", "ALBUMARTIST", "DATE", "YEAR", "GENRE", "COMPOSER",
            "TRACKNUMBER", "TRACK", "TOTALTRACKS", "TRACKCOUNT", "DISCNUMBER", "DISC",
            "TOTALDISCS", "DISCCOUNT", "COMMENT", "COPYRIGHT", "LYRICS", "BPM",
            "BEATSPERMINUTE", "CONDUCTOR", "GROUPING", "PUBLISHER", "ISRC",
            // Also filter out technical fields that belong in Properties
            "ENCODER", "ENCODED-BY", "ENCODEDBY", "TOOL", "SOFTWARE", "ENCODING_TOOL",
            // ReplayGain fields (handled in separate ReplayGain section)
            "REPLAYGAIN_TRACK_GAIN", "REPLAYGAIN_TRACK_PEAK", "REPLAYGAIN_ALBUM_GAIN", "REPLAYGAIN_ALBUM_PEAK"
        };

        // Fields that should go to Picture section
        HashSet<string> pictureFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "METADATA_BLOCK_PICTURE", "COVERART", "COVER_ART", "ALBUMART", "ALBUM_ART",
            "PICTURE", "APIC"
        };

        // Use a dictionary to collect all custom fields with their values
        Dictionary<string, List<string>> customFields = new(StringComparer.OrdinalIgnoreCase);

        //_logger.LogInformation("Main tag type: {TagType}, Available tag types: {TagTypes}", 
        //_audioFile.Tag.GetType().Name, _audioFile.TagTypes);

        try
        {
            // APE tags (MPC/Musepack, APE, WavPack, etc.) - add this FIRST since MPC uses APE tags
            if (_audioFile.GetTag(TagLib.TagTypes.Ape, false) is TagLib.Ape.Tag apeTag)
            {
                //_logger.LogDebug("Found APE tag");
                // APE tags are accessed via enumeration of keys
                foreach (string key in apeTag)
                {
                    try
                    {
                        var item = apeTag.GetItem(key);
                        if (item != null)
                        {
                            string value = item.ToString();

                            if (!string.IsNullOrWhiteSpace(value) && !standardFields.Contains(key))
                            {
                                if (!customFields.ContainsKey(key))
                                {
                                    customFields[key] = new List<string>();
                                }
                                customFields[key].Add(value);
                            }
                            else if (standardFields.Contains(key))
                            {
                                _logger.LogDebug("Skipped standard APE field: {Field}", key);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error reading APE item {Key}: {Message}", key, ex.Message);
                    }
                }
            }
            else
            {
                //_logger.LogDebug("No APE tag found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading APE tags: {Message}", ex.Message);
        }

        try
        {
            // FLAC/OGG/Opus (Vorbis Comments) - get the specific tag directly
            if (_audioFile.GetTag(TagLib.TagTypes.Xiph, false) is XiphComment xiphTag)
            {
                //_logger.LogDebug("Found Xiph tag with {Count} fields", xiphTag.FieldCount);
                foreach (string field in xiphTag)
                {
                    // Get all values for this field (not just the first one)
                    string[] fieldValues = xiphTag.GetField(field);

                    if (fieldValues.Length > 0 && !standardFields.Contains(field))
                    {
                        if (!customFields.ContainsKey(field))
                        {
                            customFields[field] = new List<string>();
                        }

                        // Add all non-empty values
                        foreach (string value in fieldValues)
                        {
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                customFields[field].Add(value);
                            }
                        }
                    }
                    else if (standardFields.Contains(field))
                    {
                        _logger.LogDebug("Skipped standard Vorbis field: {Field}", field);
                    }
                }
            }
            else
            {
                //_logger.LogDebug("No Xiph tag found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading Vorbis comments: {Message}", ex.Message);
        }

        try
        {
            // MP4/M4A/AAC (iTunes-style tags) - get the specific tag directly
            if (_audioFile.GetTag(TagLib.TagTypes.Apple, false) is AppleTag mp4Tag)
            {
                // Use reflection to find the internal text dictionary
                IEnumerable<FieldInfo> textFields = mp4Tag.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(f => f.FieldType == typeof(Dictionary<string, string[]>));

                foreach (FieldInfo field in textFields)
                {
                    if (field.GetValue(mp4Tag) is Dictionary<string, string[]> dict)
                    {
                        foreach (KeyValuePair<string, string[]> kvp in dict)
                        {
                            string value = kvp.Value.FirstOrDefault() ?? "";

                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                // Clean up iTunes field names and check if it's standard
                                string fieldName = kvp.Key.Replace("----:com.apple.iTunes:", "");
                                if (!standardFields.Contains(fieldName))
                                {
                                    if (!customFields.ContainsKey(fieldName))
                                    {
                                        customFields[fieldName] = new List<string>();
                                    }
                                    customFields[fieldName].Add(value);
                                }
                                else
                                {
                                    _logger.LogDebug("Skipped standard MP4 field: {Field}", fieldName);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                //_logger.LogDebug("No Apple tag found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading iTunes custom tags: {Message}", ex.Message);
        }

        try
        {
            // MP3 (ID3v2) - get the specific tag directly
            if (_audioFile.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3v2Tag)
            {
                try
                {
                    // Try to safely enumerate through all frames
                    Frame[] frames = id3v2Tag.GetFrames().ToArray();

                    foreach (Frame frame in frames)
                    {
                        try
                        {
                            string frameId = frame.FrameId.ToString();

                            // Skip standard frames that we handle in core metadata
                            if (IsStandardId3v2Frame(frameId))
                            {
                                continue;
                            }

                            // Get both display name and value from the frame
                            (string displayName, string frameValue) = GetId3v2FrameInfo(frame);
                            if (!string.IsNullOrWhiteSpace(frameValue))
                            {
                                // Use the actual field description for TXXX frames, fallback to converted frame ID
                                string fieldName = displayName.Equals("USER_TEXT", StringComparison.OrdinalIgnoreCase)
                                    ? frameId // This shouldn't happen with new logic, but keep as fallback
                                    : displayName;

                                // Additional check: filter out standard fields by name (important for TXXX frames)
                                if (standardFields.Contains(fieldName))
                                {
                                    continue;
                                }

                                if (!customFields.ContainsKey(fieldName))
                                {
                                    customFields[fieldName] = new List<string>();
                                }
                                customFields[fieldName].Add(frameValue);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Error processing ID3v2 frame: {Message}", ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error enumerating ID3v2 frames: {Message}", ex.Message);
                }
            }
            else
            {
                //_logger.LogDebug("No ID3v2 tag found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading ID3v2 custom tags: {Message}", ex.Message);
        }

        // Now add the collected custom fields to the UI, combining multiple values with semicolons
        foreach (KeyValuePair<string, List<string>> kvp in customFields.OrderBy(x => x.Key))
        {
            string fieldName = kvp.Key;
            List<string> values = kvp.Value;

            // Skip picture-related fields - they belong in the Picture section
            if (pictureFields.Contains(fieldName))
            {
                // Skip METADATA_BLOCK_PICTURE entirely - it's just base64 noise, not useful to display
                if (fieldName.Equals("METADATA_BLOCK_PICTURE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Add other picture fields to Picture section
                string pictureValue = string.Join("; ", values.Distinct().Where(v => !string.IsNullOrWhiteSpace(v)));
                if (!string.IsNullOrWhiteSpace(pictureValue))
                {
                    // Truncate very long values (like base64 images) for display
                    string displayValue = pictureValue.Length > 100
             ? pictureValue.Substring(0, 100) + $"... [{pictureValue.Length} characters total]"
           : pictureValue;
                    AddPictureInfoItem($"<{fieldName}>", displayValue, false, null);
                }
                continue;
            }

            // Remove duplicates and combine with semicolons
            string combinedValue = string.Join("; ", values.Distinct().Where(v => !string.IsNullOrWhiteSpace(v)));

            if (!string.IsNullOrWhiteSpace(combinedValue))
            {
                AddMetadataItem($"<{fieldName}>", combinedValue, false, null);
            }
        }
    }

    private void LoadFileProperties()
    {
        if (_audioFile?.Properties == null)
        {
            _logger.LogWarning("No properties found for the current file");
            return;
        }

        // EXPLICIT FILE PROPERTIES - Technical information about the file
        TagLib.Properties? props = _audioFile.Properties;
        AddPropertyItem("Duration", props.Duration.ToString(@"mm\:ss"), false);
        AddPropertyItem("Bitrate", props.AudioBitrate > 0 ? props.AudioBitrate.ToString() + " kbps" : "", false);
        AddPropertyItem("Sample Rate", props.AudioSampleRate > 0 ? props.AudioSampleRate.ToString() + " Hz" : "", false);
        AddPropertyItem("Channels", props.AudioChannels > 0 ? props.AudioChannels.ToString() : "", false);
        AddPropertyItem("Bits Per Sample", props.BitsPerSample > 0 ? props.BitsPerSample.ToString() : "", false);
        AddPropertyItem("Media Types", props.MediaTypes.ToString(), false);
        AddPropertyItem("Codec", props.Description ?? props.Codecs?.FirstOrDefault()?.Description ?? "", false);

        // Tag format information
        AddPropertyItem("Tag Types", _audioFile.TagTypes.ToString(), false);

        // Format-specific technical info
        try
        {
            if (_audioFile.Tag is TagLib.Id3v2.Tag id3v2Tag)
            {
                AddPropertyItem("ID3v2 Version", $"2.{id3v2Tag.Version}", false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading ID3v2 version: {Message}", ex.Message);
        }

        // Encoding tool information (belongs in Properties, not custom metadata)
        try
        {
            string? encoderInfo = GetEncoderInfo(_audioFile.Tag);
            if (!string.IsNullOrWhiteSpace(encoderInfo))
            {
                AddPropertyItem("Encoder", encoderInfo, false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading encoder info: {Message}", ex.Message);
        }
    }

    private void LoadReplayGain()
    {
        if (_audioFile?.Tag == null)
        {
            _logger.LogWarning("No tag data found for ReplayGain information");
            return;
        }

        Tag? tag = _audioFile.Tag;

        // Format-specific ReplayGain handling
        if (_audioFile.Tag is TagLib.Id3v2.Tag)
        {
            AddReplayGainItem("ReplayGain Track Gain", "", false, null);
            AddReplayGainItem("ReplayGain Track Peak", "", false, null);
        }
        else if (_audioFile.Tag is XiphComment xiphComment)
        {
            AddReplayGainItem("ReplayGain Track Gain", xiphComment.GetFirstField("REPLAYGAIN_TRACK_GAIN") ?? "", true,
                v => { xiphComment.SetField("REPLAYGAIN_TRACK_GAIN", string.IsNullOrEmpty(v) ? null : v); HasUnsavedChanges = true; });
            AddReplayGainItem("ReplayGain Track Peak", xiphComment.GetFirstField("REPLAYGAIN_TRACK_PEAK") ?? "", false, null);
            AddReplayGainItem("ReplayGain Album Gain", xiphComment.GetFirstField("REPLAYGAIN_ALBUM_GAIN") ?? "", true,
                v => { xiphComment.SetField("REPLAYGAIN_ALBUM_GAIN", string.IsNullOrEmpty(v) ? null : v); HasUnsavedChanges = true; });
            AddReplayGainItem("ReplayGain Album Peak", xiphComment.GetFirstField("REPLAYGAIN_ALBUM_PEAK") ?? "", false, null);
        }
        else if (_audioFile.Tag is AppleTag mp4Tag)
        {
            AddReplayGainItem("ReplayGain Track Gain", mp4Tag.GetText("----:com.apple.iTunes:REPLAYGAIN_TRACK_GAIN")?.FirstOrDefault() ?? "", true,
                v => { mp4Tag.SetText("----:com.apple.iTunes:REPLAYGAIN_TRACK_GAIN", string.IsNullOrEmpty(v) ? null : [v]); HasUnsavedChanges = true; });
            AddReplayGainItem("ReplayGain Track Peak", mp4Tag.GetText("----:com.apple.iTunes:REPLAYGAIN_TRACK_PEAK")?.FirstOrDefault() ?? "", false, null);
            AddReplayGainItem("ReplayGain Album Gain", mp4Tag.GetText("----:com.apple.iTunes:REPLAYGAIN_ALBUM_GAIN")?.FirstOrDefault() ?? "", true,
                v => { mp4Tag.SetText("----:com.apple.iTunes:REPLAYGAIN_ALBUM_GAIN", string.IsNullOrEmpty(v) ? null : [v]); HasUnsavedChanges = true; });
            AddReplayGainItem("ReplayGain Album Peak", mp4Tag.GetText("----:com.apple.iTunes:REPLAYGAIN_ALBUM_PEAK")?.FirstOrDefault() ?? "", false, null);
        }
        else
        {
            AddReplayGainItem("Track Gain", FormatGainToString(tag.ReplayGainTrackGain), false, null);
            AddReplayGainItem("Track Peak", FormatPeakToString(tag.ReplayGainTrackPeak), false, null);
            AddReplayGainItem("Album Gain", FormatGainToString(tag.ReplayGainAlbumGain), false, null);
            AddReplayGainItem("Album Peak", FormatPeakToString(tag.ReplayGainAlbumPeak), false, null);
        }
    }

    private static string FormatPeakToString(double peak)
    {
        if (double.IsNaN(peak)) return string.Empty;
        return peak.ToString("F6");
    }

    private static string FormatGainToString(double gain)
    {
        if (double.IsNaN(gain)) return string.Empty;
        return gain.ToString("F") + " dB";
    }

    private void LoadPictureInfo()
    {
        if (_audioFile?.Tag == null)
        {
            _logger.LogWarning("No tag data found for picture information");
            return;
        }

        Tag? tag = _audioFile.Tag;

        if (tag.Pictures is { Length: > 0 })
        {
            // Add album cover image as a new TagItem with AlbumCoverSource property
            var pic = tag.Pictures[0];
            BitmapImage? albumCover = null;
            if (pic.Data?.Data is { Length: > 0 })
            {
                try
                {
                    using var ms = new MemoryStream(pic.Data.Data);
                    albumCover = new BitmapImage();
                    albumCover.BeginInit();
                    albumCover.CacheOption = BitmapCacheOption.OnLoad;
                    albumCover.StreamSource = ms;
                    albumCover.EndInit();
                    albumCover.Freeze();

                    PictureInfoItems.Add(new TagItem
                    {
                        Name = "Album Cover",
                        Value = string.Empty,
                        IsEditable = false,
                        UpdateAction = null,
                        AlbumCoverSource = albumCover
                    });

                    // Calculate and add picture size in KB
                    double sizeInKB = pic.Data.Data.Length / 1024.0;
                    AddPictureInfoItem("Picture Size", $"{sizeInKB:F2} KB", false, null);

                    // Add picture dimensions (width x height)
                    AddPictureInfoItem("Picture Dimensions", $"{albumCover.PixelWidth} x {albumCover.PixelHeight}", false, null);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error loading album cover image: {Message}", ex.Message);
                }
            }

            AddPictureInfoItem("Picture Count", tag.Pictures.Length.ToString(), false, null);
            AddPictureInfoItem("Picture Type", tag.Pictures[0].Type.ToString(), false, null);
            AddPictureInfoItem("Picture Mime Type", tag.Pictures[0].MimeType ?? "", false, null);

            if (tag.Pictures.Length > 0)
            {
                if (string.IsNullOrEmpty(tag.Pictures[0].Filename))
                {
                    AddPictureInfoItem("Picture Filename", "<Embedded Image>", false, null);
                }
                else
                {
                    AddPictureInfoItem("Picture Filename", tag.Pictures[0].Filename, false, null);
                }
            }

            AddPictureInfoItem("Picture Description", tag.Pictures[0].Description ?? "", true, v =>
            {
                // Update the picture description in the tag
                var pictures = tag.Pictures;
                if (pictures.Length > 0)
                {
                    var existingPic = pictures[0];
                    var newPic = new TagLib.Picture(existingPic.Data)
                    {
                        Type = existingPic.Type,
                        MimeType = existingPic.MimeType,
                        Filename = existingPic.Filename,
                        Description = string.IsNullOrEmpty(v) ? null : v
                    };
                    tag.Pictures = [newPic];
                    HasUnsavedChanges = true;
                }
            });

        }
        else
        {
            //_logger.LogDebug("No pictures found in tag data");
        }

        // Sort picture items: keep regular tags in original order, move custom tags (with angle brackets) to bottom
        var regularPictureTags = PictureInfoItems.Where(item => !item.Name.StartsWith("<")).ToList();
        var customPictureTags = PictureInfoItems.Where(item => item.Name.StartsWith("<")).ToList();

        PictureInfoItems.Clear();
        foreach (var item in regularPictureTags)
        {
            PictureInfoItems.Add(item);
        }
        foreach (var item in customPictureTags)
        {
            PictureInfoItems.Add(item);
        }

        // Notify that AlbumCoverSource has changed
        OnPropertyChanged(nameof(AlbumCoverSource));
    }

    private void LoadCommentItem()
    {
        if (_audioFile?.Tag == null)
        {
            _logger.LogWarning("No tag data found for comment information");
            return;
        }

        Tag? tag = _audioFile.Tag;
        string commentValue = tag.Comment ?? "[ No comment available. ]";

        AddCommentItem("Comment", commentValue, true, v =>
        {
            // Don't update if the value is the placeholder text
            if (v == "[ No comment available. ]")
                tag.Lyrics = null;
            else
                tag.Comment = string.IsNullOrEmpty(v) ? null : v;
            HasUnsavedChanges = true;
        });
    }

    private void AddCommentItem(string name, string value, bool isEditable, Action<string>? updateAction)
    {
        TagItem item = new()
        {
            Name = name,
            Value = value,
            IsEditable = isEditable,
            UpdateAction = isEditable ? updateAction : null
        };

        if (isEditable)
        {
            item.PropertyChanged += TagItem_PropertyChanged!;
        }

        CommentItem = item;
    }

    private void LoadLyricsItem()
    {
        if (_audioFile?.Tag == null)
        {
            _logger.LogWarning("No tag data found for lyrics information");
            return;
        }

        Tag? tag = _audioFile.Tag;
        string lyricsValue = tag.Lyrics ?? "[ No lyrics available. ]";

        AddLyricsItem("Lyrics", lyricsValue, true, v =>
        {
            // Don't update if the value is the placeholder text
            if (v == "[ No lyrics available. ]")
                tag.Lyrics = null;
            else
                tag.Lyrics = string.IsNullOrEmpty(v) ? null : v;
            HasUnsavedChanges = true;
        });
    }

    private void AddLyricsItem(string name, string value, bool isEditable, Action<string>? updateAction)
    {
        TagItem item = new()
        {
            Name = name,
            Value = value,
            IsEditable = isEditable,
            UpdateAction = isEditable ? updateAction : null
        };

        if (isEditable)
        {
            item.PropertyChanged += TagItem_PropertyChanged!;
        }

        LyricsItem = item;
    }

    private void AddMetadataItem(string name, string value, bool isEditable, Action<string>? updateAction)
    {
        TagItem item = new()
        {
            Name = name,
            Value = value,
            IsEditable = isEditable,
            UpdateAction = isEditable ? updateAction : null
        };

        if (isEditable)
        {
            item.PropertyChanged += TagItem_PropertyChanged!;
        }

        MetadataItems.Add(item);
    }

    private void AddPropertyItem(string name, string value, bool isEditable)
    {
        // Only add if there's actually a value (avoid empty entries)
        if (!string.IsNullOrWhiteSpace(value))
        {
            PropertyItems.Add(new TagItem
            {
                Name = name,
                Value = value,
                IsEditable = isEditable
            });
        }
    }

    private void AddReplayGainItem(string name, string value, bool isEditable, Action<string>? updateAction)
    {
        ReplayGainItems.Add(new TagItem
        {
            Name = name,
            Value = value,
            IsEditable = isEditable,
            UpdateAction = isEditable ? updateAction : null
        });
    }

    private void AddPictureInfoItem(string name, string value, bool isEditable, Action<string>? updateAction)
    {
        TagItem item = new()
        {
            Name = name,
            Value = value,
            IsEditable = isEditable,
            UpdateAction = isEditable ? updateAction : null
        };

        if (isEditable)
        {
            item.PropertyChanged += TagItem_PropertyChanged!;
        }

        PictureInfoItems.Add(item);
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
            foreach (TagItem item in MetadataItems.Where(i => i.IsEditable))
            {
                if (string.IsNullOrWhiteSpace(item.Value) &&
                    (item.Name == "Year" || item.Name == "Track Number" ||
                     item.Name == "Total Tracks" || item.Name == "Disc Number" ||
                     item.Name == "Total Discs" || item.Name == "Beats Per Minute"))
                {
                    item.Value = "0"; // Set to 0 for numeric fields if empty
                }
                item.UpdateAction?.Invoke(item.Value);
            }

            // Apply ReplayGain changes too
            foreach (TagItem item in ReplayGainItems.Where(i => i.IsEditable))
            {
                item.UpdateAction?.Invoke(item.Value);
            }

            // Apply picture changes (like Picture Description)
            foreach (TagItem item in PictureInfoItems.Where(i => i.IsEditable))
            {
                item.UpdateAction?.Invoke(item.Value);
            }

            // Apply comment changes
            if (CommentItem.IsEditable)
            {
                CommentItem.UpdateAction?.Invoke(CommentItem.Value);
            }

            // Apply lyrics changes
            if (LyricsItem.IsEditable)
            {
                LyricsItem.UpdateAction?.Invoke(LyricsItem.Value);
            }

            _audioFile!.Save();
            HasUnsavedChanges = false;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying changes to metadata: {Message}", ex.Message);
            MessageBox.Show($"Error applying changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void UpdateTrackMetadata()
    {
        if (_sharedDataModel.SelectedTrack == null)
            return;

        _sharedDataModel.SelectedTrack.UpdateFromFileMetadata();

        if (_sharedDataModel.ActiveTrack == _sharedDataModel.SelectedTrack)
        {
            _sharedDataModel.ActiveTrack.UpdateFromFileMetadata();
        }
    }

    private string? GetEncoderInfo(Tag tag)
    {
        // List of possible encoder field names (case-insensitive)
        string[] possibleKeys =
        [
            "ENCODED_BY", "ENCODEDBY", "ENCODER", "TOOL", "SOFTWARE",
            "WRITING_LIBRARY", "WRITINGLIBRARY", "ENCODING_TOOL"
        ];

        // Try different tag formats
        try
        {
            // Vorbis Comments (FLAC, OGG)
            if (tag is XiphComment xiphTag)
            {
                foreach (string key in possibleKeys)
                {
                    string? value = xiphTag.GetFirstField(key);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            // iTunes tags (MP4, M4A)
            if (tag is AppleTag mp4Tag)
            {
                foreach (string key in possibleKeys)
                {
                    string? value = mp4Tag.GetText("----:com.apple.iTunes:" + key)?.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            // ID3v2 tags (MP3) - be careful
            if (tag is TagLib.Id3v2.Tag id3v2Tag)
            {
                try
                {
                    // Try TENC frame (safer than general text search)
                    IEnumerable<Frame> tencFrames = id3v2Tag.GetFrames("TENC").ToList();
                    if (tencFrames.Any())
                    {
                        return tencFrames.First().ToString();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error reading ID3v2 TENC frame: {Message}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading encoder info: {Message}", ex.Message);
        }

        return null;
    }

    private bool IsStandardId3v2Frame(string frameId)
    {
        // Standard frames that we handle in core metadata - don't show as custom
        string[] standardFrames =
        [
            "TIT2", // Title
            "TPE1", // Artist/Performer
            "TALB", // Album
            "TPE2", // Album Artist
            "TYER", "TDRC", // Year/Date
            "TCON", // Genre
            "TCOM", // Composer
            "TRCK", // Track Number
            "TPOS", // Disc Number
            "COMM", // Comment
            "TCOP", // Copyright
            "USLT", // Lyrics
            "TBPM", // BPM
            "TPE3", // Conductor
            "TIT1", // Grouping
            "TPUB", // Publisher
            "TSRC", // ISRC
            "APIC", // Picture
            "TENC", // Encoder (handled in Properties)
            "TSSE"  // Software/Encoder settings (handled in Properties)
        ];

        return standardFrames.Contains(frameId);
    }

    private string ExtractMeaningfulFieldName(string description)
    {
        // If description looks like a specific field name, use it
        if (!string.IsNullOrWhiteSpace(description) &&
            !description.Equals("USER_TEXT", StringComparison.OrdinalIgnoreCase))
        {
            // Handle common ID3v2 patterns
            return description switch
            {
                // Instrument/role mappings - try to match what FLAC shows
                var d when d.Contains("BASS", StringComparison.OrdinalIgnoreCase) => "BASS GUITAR",
                var d when d.Contains("DRUM", StringComparison.OrdinalIgnoreCase) => "DRUMS",
                var d when d.Contains("GUITAR", StringComparison.OrdinalIgnoreCase) => "GUITAR",
                var d when d.Contains("VOCAL", StringComparison.OrdinalIgnoreCase) => "VOCALS",
                var d when d.Contains("SYNTHESIZER", StringComparison.OrdinalIgnoreCase) => "SYNTHESIZER",
                var d when d.Contains("CLAP", StringComparison.OrdinalIgnoreCase) => "CLAPPING",


                // Role mappings
                var d when d.Contains("ENGINEER", StringComparison.OrdinalIgnoreCase) => "ENGINEER",
                var d when d.Contains("PRODUCER", StringComparison.OrdinalIgnoreCase) => "PRODUCER",
                var d when d.Contains("MIXER", StringComparison.OrdinalIgnoreCase) => "MIX ENGINEER",
                var d when d.Contains("MASTERING", StringComparison.OrdinalIgnoreCase) => "MASTERING ENGINEER",
                var d when d.Contains("ASSISTANT", StringComparison.OrdinalIgnoreCase) => "ASSISTANT MIXER",

                // Other common fields
                var d when d.Contains("LYRICIST", StringComparison.OrdinalIgnoreCase) => "LYRICIST",
                var d when d.Contains("COMPOSER", StringComparison.OrdinalIgnoreCase) => "COMPOSER",
                var d when d.Contains("COMPILATION", StringComparison.OrdinalIgnoreCase) => "COMPILATION",
                var d when d.Contains("PROVIDER", StringComparison.OrdinalIgnoreCase) => "PROVIDER",
                var d when d.Contains("COUNTRY", StringComparison.OrdinalIgnoreCase) => "RELEASECOUNTRY",
                var d when d.Contains("UPC", StringComparison.OrdinalIgnoreCase) => "UPC",
                var d when d.Contains("WORK", StringComparison.OrdinalIgnoreCase) => "WORK",
                var d when d.Contains("ENCODER", StringComparison.OrdinalIgnoreCase) => "ENCODERSETTINGS",
                var d when d.Contains("REPLAYGAIN", StringComparison.OrdinalIgnoreCase) => description.ToUpper(),

                // Default: clean up the description
                _ => description.ToUpper().Replace(" ", "_")
            };
        }

        // Fallback to generic name
        return "USER_TEXT";
    }

    private (string displayName, string value) GetId3v2FrameInfo(Frame frame)
    {
        try
        {
            // Handle UserTextInformationFrame (TXXX) specially
            if (frame is UserTextInformationFrame userTextFrame)
            {
                string description = userTextFrame.Description ?? "USER_TEXT";
                string text = userTextFrame.Text?.FirstOrDefault() ?? "";

                // Try to extract more meaningful field names from the description or text
                string fieldName = ExtractMeaningfulFieldName(description);

                return (fieldName, text);
            }

            // Handle other frame types
            return frame switch
            {
                TextInformationFrame textFrame =>
                    (GetId3v2FrameDisplayName(frame.FrameId.ToString()), textFrame.Text?.FirstOrDefault() ?? ""),
                CommentsFrame commentFrame =>
                    (GetId3v2FrameDisplayName(frame.FrameId.ToString()), commentFrame.Text ?? ""),
                UnsynchronisedLyricsFrame lyricsFrame =>
                    (GetId3v2FrameDisplayName(frame.FrameId.ToString()), lyricsFrame.Text ?? ""),
                _ => (GetId3v2FrameDisplayName(frame.FrameId.ToString()), frame.ToString() ?? "")
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error extracting frame info from ID3v2 frame: {Message}", ex.Message);
            return (frame.FrameId.ToString(), "");
        }
    }

    private string GetId3v2FrameDisplayName(string frameId)
    {
        // Convert ID3v2 frame IDs to user-friendly names
        return frameId switch
        {
            "TXXX" => "USER_TEXT",
            "TPE4" => "MODIFIER",
            "TOPE" => "ORIGINAL_PERFORMER",
            "TIT3" => "SUBTITLE",
            "TKEY" => "INITIAL_KEY",
            "TLAN" => "LANGUAGE",
            "TLEN" => "LENGTH",
            "TMED" => "MEDIA_TYPE",
            "TMOO" => "MOOD",
            "TOAL" => "ORIGINAL_ALBUM",
            "TOFN" => "ORIGINAL_FILENAME",
            "TOLY" => "ORIGINAL_LYRICIST",
            "TORY" => "ORIGINAL_YEAR",
            "TOWN" => "FILE_OWNER",
            "TPE3" => "CONDUCTOR",
            "TRSN" => "INTERNET_RADIO_STATION",
            "TRSO" => "INTERNET_RADIO_OWNER",
            "TSOA" => "ALBUM_SORT_ORDER",
            "TSOP" => "PERFORMER_SORT_ORDER",
            "TSOT" => "TITLE_SORT_ORDER",
            "TSRC" => "ISRC",
            "TSSE" => "ENCODER_SETTINGS",
            "TSST" => "SET_SUBTITLE",
            "WOAR" => "ARTIST_URL",
            "WOAF" => "AUDIO_FILE_URL",
            "WOAS" => "AUDIO_SOURCE_URL",
            "WORS" => "RADIO_STATION_URL",
            "WPAY" => "PAYMENT_URL",
            "WPUB" => "PUBLISHER_URL",
            _ => frameId // Use the frame ID if we don't have a mapping
        };
    }

    public BitmapImage? AlbumCoverSource => PictureInfoItems.FirstOrDefault(item => item.Name == "Album Cover")?.AlbumCoverSource;
}