using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkerPlayer.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
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
    private File? _audioFile;
    [ObservableProperty] private bool hasUnsavedChanges;

    public ObservableCollection<TagItem> MetadataItems { get; } = [];
    public ObservableCollection<TagItem> PropertyItems { get; } = [];
    public ObservableCollection<TagItem> ReplayGainItems { get; } = [];
    public ObservableCollection<TagItem> PictureInfoItems { get; } = [];

    public event EventHandler<bool>? CloseRequested;

    public PropertiesViewModel(SharedDataModel sharedDataModel)
    {
        _sharedDataModel = sharedDataModel;

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
            _audioFile?.Dispose();
            _audioFile = File.Create(path);
            MetadataItems.Clear();
            PropertyItems.Clear();
            ReplayGainItems.Clear();
            PictureInfoItems.Clear();

            LoadMetadata();
            LoadProperties();
            LoadReplayGain();
            LoadPictureInfo();

            HasUnsavedChanges = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading track data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadMetadata()
    {
        if (_audioFile?.Tag == null)
        {
            MessageBox.Show("No metadata found for this file.", "Metadata Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? tagType = _audioFile.Tag.GetType().FullName;
        AddMetadataItem("Tag Type", tagType ?? string.Empty, false, null);

        DumpAudioFileJson();

        Tag tag = _audioFile.Tag;
        // Standard fields
        AddMetadataItem("Title", tag.Title ?? "", true, v => { tag.Title = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });
        AddMetadataItem("Artist", tag.FirstPerformer ?? string.Join(", ", tag.Performers ?? []), true, v => { tag.Performers = string.IsNullOrEmpty(v) ? [] : v.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray(); HasUnsavedChanges = true; });
        AddMetadataItem("Album", tag.Album ?? "", true, v => { tag.Album = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });
        AddMetadataItem("Album Artist", tag.FirstAlbumArtist ?? string.Join(", ", tag.AlbumArtists ?? []), true, v => { tag.AlbumArtists = string.IsNullOrEmpty(v) ? [] : v.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray(); HasUnsavedChanges = true; });
        AddMetadataItem("Year", tag.Year > 0 ? tag.Year.ToString() : "", true, v => { tag.Year = uint.TryParse(v, out uint year) ? year : 0; HasUnsavedChanges = true; });
        AddMetadataItem("Genre", tag.FirstGenre ?? string.Join(", ", tag.Genres ?? []), true, v => { tag.Genres = string.IsNullOrEmpty(v) ? [] : v.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray(); HasUnsavedChanges = true; });
        AddMetadataItem("Composer", tag.FirstComposer ?? string.Join(", ", tag.Composers ?? []), true, v => { tag.Composers = string.IsNullOrEmpty(v) ? [] : v.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray(); HasUnsavedChanges = true; });
        AddMetadataItem("Track Number", tag.Track > 0 ? tag.Track.ToString() : "", true, v => { tag.Track = uint.TryParse(v, out uint track) ? track : 0; HasUnsavedChanges = true; });
        AddMetadataItem("Total Tracks", tag.TrackCount > 0 ? tag.TrackCount.ToString() : "", true, v => { tag.TrackCount = uint.TryParse(v, out uint count) ? count : 0; HasUnsavedChanges = true; });
        AddMetadataItem("Disc Number", tag.Disc > 0 ? tag.Disc.ToString() : "", true, v => { tag.Disc = uint.TryParse(v, out uint disc) ? disc : 0; HasUnsavedChanges = true; });
        AddMetadataItem("Total Discs", tag.DiscCount > 0 ? tag.DiscCount.ToString() : "", true, v => { tag.DiscCount = uint.TryParse(v, out uint count) ? count : 0; HasUnsavedChanges = true; });
        AddMetadataItem("Comment", tag.Comment ?? "", true, v => { tag.Comment = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });
        AddMetadataItem("Copyright", tag.Copyright ?? "", true, v => { tag.Copyright = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });
        AddMetadataItem("Lyrics", tag.Lyrics ?? "", true, v => { tag.Lyrics = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });
        AddMetadataItem("Beats Per Minute", tag.BeatsPerMinute > 0 ? tag.BeatsPerMinute.ToString() : "", true, v => { tag.BeatsPerMinute = uint.TryParse(v, out uint bpm) ? bpm : 0; HasUnsavedChanges = true; });
        AddMetadataItem("Conductor", tag.Conductor ?? "", true, v => { tag.Conductor = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });
        AddMetadataItem("Grouping", tag.Grouping ?? "", true, v => { tag.Grouping = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });
        AddMetadataItem("Publisher", tag.Publisher ?? "", true, v => { tag.Publisher = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });
        AddMetadataItem("ISRC", tag.ISRC ?? "", true, v => { tag.ISRC = string.IsNullOrEmpty(v) ? null : v; HasUnsavedChanges = true; });

        // Format-specific fields
        // MP3 (ID3v2)
        TagLib.Id3v2.Tag? id3v2Tag = _audioFile.GetTag(TagLib.TagTypes.Id3v2, false) as TagLib.Id3v2.Tag;
        if (id3v2Tag != null)
        {
            AddMetadataItem("Mood", id3v2Tag.GetTextAsString("TXXX:MOOD") ?? "", true, v => { id3v2Tag.SetTextFrame("TXXX:MOOD", string.IsNullOrEmpty(v) ? null : v); HasUnsavedChanges = true; });
            AddMetadataItem("Energy", id3v2Tag.GetTextAsString("TXXX:ENERGY") ?? "", true, v => { id3v2Tag.SetTextFrame("TXXX:ENERGY", string.IsNullOrEmpty(v) ? null : v); HasUnsavedChanges = true; });

            // Encoder info: TENC frame
            Frame? tencFrame = id3v2Tag.GetFrames().FirstOrDefault(f => Encoding.UTF8.GetString(f.FrameId.Data) == "TENC");
            if (tencFrame != null)
                AddMetadataItem("Encoded By (TENC)", tencFrame.ToString()!, false, null);

            // Encoder info: TXXX frames mentioning "Exact Audio Copy"
            foreach (UserTextInformationFrame? userFrame in id3v2Tag.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
            {
                if (userFrame.Description.Contains("Exact Audio Copy", StringComparison.OrdinalIgnoreCase) ||
                    userFrame.Text.Any(t => t.Contains("Exact Audio Copy", StringComparison.OrdinalIgnoreCase)))
                {
                    AddMetadataItem($"TXXX:{userFrame.Description}", string.Join(", ", userFrame.Text), false, null);
                }
            }
        }

        // FLAC/OGG/Opus (Vorbis)
        XiphComment? xiphTag = _audioFile.GetTag(TagLib.TagTypes.Xiph, false) as TagLib.Ogg.XiphComment;
        if (xiphTag != null)
        {
            AddMetadataItem("Mood", xiphTag.GetFirstField("MOOD") ?? "", true, v => { xiphTag.SetField("MOOD", string.IsNullOrEmpty(v) ? null : v); HasUnsavedChanges = true; });
            AddMetadataItem("Energy", xiphTag.GetFirstField("ENERGY") ?? "", true, v => { xiphTag.SetField("ENERGY", string.IsNullOrEmpty(v) ? null : v); HasUnsavedChanges = true; });

            // Encoder info: common Vorbis fields
            string[] encoderFields = new[] { "ENCODED_BY", "ENCODER", "TOOL", "SOFTWARE", "COMMENT" };
            foreach (string key in encoderFields)
            {
                string? value = xiphTag.GetFirstField(key);
                if (!string.IsNullOrWhiteSpace(value))
                    AddMetadataItem($"Vorbis:{key}", value, false, null);
            }
        }

        // MP4/M4A/AAC (AppleTag)
        AppleTag? mp4Tag = _audioFile.GetTag(TagLib.TagTypes.Apple, false) as TagLib.Mpeg4.AppleTag;
        if (mp4Tag != null)
        {
            AddMetadataItem("Mood", mp4Tag.GetText("----:com.apple.iTunes:MOOD")?.FirstOrDefault() ?? "", true, v => { mp4Tag.SetText("----:com.apple.iTunes:MOOD", string.IsNullOrEmpty(v) ? null : [v]); HasUnsavedChanges = true; });
            AddMetadataItem("Energy", mp4Tag.GetText("----:com.apple.iTunes:ENERGY")?.FirstOrDefault() ?? "", true, v => { mp4Tag.SetText("----:com.apple.iTunes:ENERGY", string.IsNullOrEmpty(v) ? null : [v]); HasUnsavedChanges = true; });

            string[] keys = new[] { "----:com.apple.iTunes:ENCODEDBY", "----:com.apple.iTunes:TOOL", "----:com.apple.iTunes:SOFTWARE" };
            foreach (string key in keys)
            {
                string? value = mp4Tag.GetText(key)?.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                    AddMetadataItem($"MP4:{key}", value, false, null);
            }
        }

        // WMA/ASF
        TagLib.Asf.Tag? asfTag = _audioFile.GetTag(TagLib.TagTypes.Asf, false) as TagLib.Asf.Tag;
        if (asfTag != null)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TagLib.Asf.Tag public properties:");
            foreach (PropertyInfo prop in asfTag.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                try
                {
                    object? value = prop.GetValue(asfTag);
                    sb.AppendLine($"{prop.Name}: {value?.ToString() ?? "<null>"}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"{prop.Name}: <exception: {ex.Message}>");
                }
            }

            sb.AppendLine("TagLib.Asf.Tag public methods:");
            foreach (MethodInfo method in asfTag.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                sb.AppendLine($"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
            }

            // Write to a file for inspection
            System.IO.File.WriteAllText("AsfTagMembers.txt", sb.ToString());
        }

        // Fallback for unsupported formats (e.g., WAV)
        if (id3v2Tag == null && xiphTag == null && mp4Tag == null && asfTag == null)
        {
            AddMetadataItem("Mood", "", false, null);
            AddMetadataItem("Energy", "", false, null);
        }

        AddAllTagFieldsToMetadata(tag);
        AddAllRawTagFieldsToMetadata(tag);
    }

    private void LoadProperties()
    {
        if (_audioFile?.Properties == null)
        {
            MessageBox.Show("No properties found for this file.", "Properties Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TagLib.Properties? props = _audioFile.Properties;
        AddPropertyItem("Duration", props.Duration.ToString(@"mm\:ss"), false);
        AddPropertyItem("Bitrate", props.AudioBitrate > 0 ? props.AudioBitrate.ToString() + " kbps" : "", false);
        AddPropertyItem("Sample Rate", props.AudioSampleRate > 0 ? props.AudioSampleRate.ToString() + " Hz" : "", false);
        AddPropertyItem("Channels", props.AudioChannels > 0 ? props.AudioChannels.ToString() : "", false);
        AddPropertyItem("Media Types", props.MediaTypes.ToString(), false);
        AddPropertyItem("Description", props.Description ?? "", false);
        AddPropertyItem("Codec", props.Description ?? props.Codecs?.FirstOrDefault()?.Description ?? "", false);
        AddPropertyItem("Bits Per Sample", props.BitsPerSample > 0 ? props.BitsPerSample.ToString() : "", false);
    }

    private void LoadReplayGain()
    {
        if (_audioFile?.Tag == null)
        {
            MessageBox.Show("No picture data found for this file.", "Picture Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Tag? tag = _audioFile.Tag;

        // Format-specific tags
        if (_audioFile.Tag is TagLib.Id3v2.Tag id3v2Tag)
        {
            // MP3: ID3v2 TXXX frames
            AddReplayGainItem("ReplayGain Track Gain", id3v2Tag.GetTextAsString("TXXX:REPLAYGAIN_TRACK_GAIN") ?? "", true, v => { id3v2Tag.SetTextFrame("TXXX:REPLAYGAIN_TRACK_GAIN", string.IsNullOrEmpty(v) ? null : v); HasUnsavedChanges = true; });
            AddReplayGainItem("ReplayGain Track Peak", id3v2Tag.GetTextAsString("TXXX:REPLAYGAIN_TRACK_PEAK") ?? "", false, null);
        }
        else if (_audioFile.Tag is TagLib.Ogg.XiphComment xiphComment)
        {
            // FLAC/Ogg: Vorbis comments
            AddReplayGainItem("ReplayGain Track Gain", xiphComment.GetFirstField("REPLAYGAIN_TRACK_GAIN") ?? "", true, v => { xiphComment.SetField("REPLAYGAIN_TRACK_GAIN", string.IsNullOrEmpty(v) ? null : v); HasUnsavedChanges = true; });
            AddReplayGainItem("ReplayGain Track Peak", xiphComment.GetFirstField("REPLAYGAIN_TRACK_PEAK") ?? "", false, null);
        }
        else if (_audioFile.Tag is TagLib.Mpeg4.AppleTag mp4Tag)
        {
            // AAC/MP4: Apple-specific tags
            AddReplayGainItem("ReplayGain Track Gain", mp4Tag.GetText("----:com.apple.iTunes:REPLAYGAIN_TRACK_GAIN")?.FirstOrDefault() ?? "", true, v => { mp4Tag.SetText("----:com.apple.iTunes:REPLAYGAIN_TRACK_GAIN", string.IsNullOrEmpty(v) ? null : [v]); HasUnsavedChanges = true; });
            AddReplayGainItem("ReplayGain Track Peak", mp4Tag.GetText("----:com.apple.iTunes:REPLAYGAIN_TRACK_PEAK")?.FirstOrDefault() ?? "", false, null);
        }
        else
        {
            // Fallback for unsupported formats (e.g., WAV, WMA)
            AddReplayGainItem("Track Gain", FormatGainToString(tag.ReplayGainTrackGain), false, null);
            AddReplayGainItem("Track Peak", FormatPeakToString(tag.ReplayGainTrackPeak), false, null);
            AddReplayGainItem("Album Gain", FormatGainToString(tag.ReplayGainAlbumGain), false, null);
            AddReplayGainItem("Album Peak", FormatPeakToString(tag.ReplayGainAlbumPeak), false, null);
        }
    }

    private void AddAllTagFieldsToMetadata(Tag tag)
    {
        // HashSet to avoid duplicates
        HashSet<string> existingNames = new HashSet<string>(MetadataItems.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);

        // 1. Add all public string properties from Tag
        PropertyInfo[] tagProps = tag.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (PropertyInfo prop in tagProps)
        {
            if (!prop.CanRead) continue;
            if (existingNames.Contains(prop.Name)) continue;
            // Only add string, uint, or string[] properties
            Type type = prop.PropertyType;
            object? value = null;
            try { value = prop.GetValue(tag); } catch { continue; }
            string displayValue = value switch
            {
                null => "",
                string s => s,
                uint u => u.ToString(),
                string[] arr => string.Join(", ", arr),
                _ => value.ToString() ?? ""
            };
            AddMetadataItem(prop.Name, displayValue, false, null);
            existingNames.Add(prop.Name);
        }

        // 2. Add custom fields from ID3v2 (MP3)
        if (tag is TagLib.Id3v2.Tag id3v2Tag)
        {
            foreach (Frame? frame in id3v2Tag.GetFrames())
            {
                string frameId = frame.FrameId.ToString();
                if (existingNames.Contains(frameId)) continue;
                string value = frame.ToString()!;
                AddMetadataItem(frameId, value, false, null);
                existingNames.Add(frameId);
            }
        }

        // 3. Add custom fields from XiphComment (FLAC/OGG)
        if (tag is TagLib.Ogg.XiphComment xiphTag)
        {
            foreach (string? field in xiphTag)
            {
                if (existingNames.Contains(field)) continue;
                string value = xiphTag.GetFirstField(field) ?? "";
                AddMetadataItem(field, value, false, null);
                existingNames.Add(field);
            }
        }

        // 4. Add custom fields from AppleTag (MP4)
        if (tag is TagLib.Mpeg4.AppleTag mp4Tag)
        {
            // Try to enumerate all possible custom keys using reflection
            IEnumerable<FieldInfo> textFields = mp4Tag.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(Dictionary<string, string[]>));
            foreach (FieldInfo field in textFields)
            {
                Dictionary<string, string[]>? dict = field.GetValue(mp4Tag) as Dictionary<string, string[]>;
                if (dict != null)
                {
                    foreach (KeyValuePair<string, string[]> kvp in dict)
                    {
                        if (existingNames.Contains(kvp.Key)) continue;
                        string value = kvp.Value?.FirstOrDefault() ?? "";
                        AddMetadataItem(kvp.Key, value, false, null);
                        existingNames.Add(kvp.Key);
                    }
                }
            }

            // Fallback: Try some common keys
            string[] commonKeys = new[] {
                "----:com.apple.iTunes:ENCODEDBY", "----:com.apple.iTunes:TOOL", "----:com.apple.iTunes:SOFTWARE"
            };
            foreach (string key in commonKeys)
            {
                if (existingNames.Contains(key)) continue;
                string value = mp4Tag.GetText(key)?.FirstOrDefault() ?? "";
                if (!string.IsNullOrWhiteSpace(value))
                {
                    AddMetadataItem(key, value, false, null);
                    existingNames.Add(key);
                }
            }
        }

        // 5. Add generic FieldList/Text dictionary fields
        Type typeObj = tag.GetType();
        PropertyInfo? fieldListProp = typeObj.GetProperty("FieldList");
        if (fieldListProp != null)
        {
            IEnumerable<KeyValuePair<string, string[]>>? fieldList = fieldListProp.GetValue(tag) as IEnumerable<KeyValuePair<string, string[]>>;
            if (fieldList != null)
            {
                foreach (KeyValuePair<string, string[]> kvp in fieldList)
                {
                    if (existingNames.Contains(kvp.Key)) continue;
                    string value = kvp.Value?.FirstOrDefault() ?? "";
                    AddMetadataItem(kvp.Key, value, false, null);
                    existingNames.Add(kvp.Key);
                }
            }
        }
        PropertyInfo? textProp = typeObj.GetProperty("Text");
        if (textProp != null)
        {
            IDictionary<string, string[]>? textDict = textProp.GetValue(tag) as IDictionary<string, string[]>;
            if (textDict != null)
            {
                foreach (KeyValuePair<string, string[]> kvp in textDict)
                {
                    if (existingNames.Contains(kvp.Key)) continue;
                    string value = kvp.Value?.FirstOrDefault() ?? "";
                    AddMetadataItem(kvp.Key, value, false, null);
                    existingNames.Add(kvp.Key);
                }
            }
        }
    }

    private void AddAllRawTagFieldsToMetadata(Tag tag)
    {
        if (_audioFile!.Tag is TagLib.Id3v2.Tag id3v2Tag)
        {
            // Find the TENC frame (Encoded by)
            Frame? tencFrame = id3v2Tag.GetFrames()
                .FirstOrDefault(f => Encoding.UTF8.GetString(f.FrameId.Data) == "TENC");
            if (tencFrame != null)
            {
                AddMetadataItem("Encoded By (TENC)", tencFrame.ToString()!, false, null);
            }

            // Also look for TXXX frames mentioning "Exact Audio Copy"
            foreach (UserTextInformationFrame? userFrame in id3v2Tag.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
            {
                if (userFrame.Description.Contains("Exact Audio Copy", StringComparison.OrdinalIgnoreCase) ||
                    userFrame.Text.Any(t => t.Contains("Exact Audio Copy", StringComparison.OrdinalIgnoreCase)))
                {
                    AddMetadataItem($"TXXX:{userFrame.Description}", string.Join(", ", userFrame.Text), false, null);
                }
            }
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
            MessageBox.Show("No picture data found for this file.", "Picture Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Tag? tag = _audioFile.Tag;

        if (tag.Pictures is { Length: > 0 })
        {
            AddPictureInfoItem("Picture Mime Type", tag.Pictures[0].MimeType ?? "", false, null);
            AddPictureInfoItem("Picture Type", tag.Pictures[0].Type.ToString(), false, null);
            AddPictureInfoItem("Picture Filename", tag.Pictures[0].Filename ?? "", false, null);
            AddPictureInfoItem("Picture Description", tag.Pictures[0].Description ?? "", false, null);
        }
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
        PropertyItems.Add(new TagItem
        {
            Name = name,
            Value = value,
            IsEditable = isEditable
        });
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
        PictureInfoItems.Add(new TagItem
        {
            Name = name,
            Value = value,
            IsEditable = isEditable,
            UpdateAction = isEditable ? updateAction : null
        });
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
            _audioFile!.Save();
            HasUnsavedChanges = false;
//                MessageBox.Show("Changes were applied successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
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

    private object? CreateSampledObjectRecursive(object? obj, int arraySampleSize = 5)
    {
        if (obj == null) return null;
        Type type = obj.GetType();

        // Primitive types and strings: return as-is
        if (type.IsPrimitive || obj is string || obj is DateTime || obj is decimal)
            return obj;

        // Arrays/collections: sample, unless named "Data"
        if (obj is System.Collections.IEnumerable enumerable && !(obj is string))
        {
            // If this is a property/field named "Data", skip it (handled in parent)
            List<object?> sampleList = new List<object?>();
            int count = 0;
            foreach (object? item in enumerable)
            {
                if (count++ >= arraySampleSize) break;
                sampleList.Add(CreateSampledObjectRecursive(item, arraySampleSize));
            }
            return sampleList;
        }

        Dictionary<string, object?> result = new Dictionary<string, object?>();

        // Properties
        foreach (PropertyInfo prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;
            if (string.Equals(prop.Name, "Data", StringComparison.OrdinalIgnoreCase)) continue; // Omit "Data"
            object? value = null;
            try { value = prop.GetValue(obj); } catch { continue; }
            result[prop.Name] = CreateSampledObjectRecursive(value, arraySampleSize);
        }

        // Fields
        foreach (FieldInfo field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (string.Equals(field.Name, "Data", StringComparison.OrdinalIgnoreCase)) continue; // Omit "Data"
            object? value = null;
            try { value = field.GetValue(obj); } catch { continue; }
            result[field.Name] = CreateSampledObjectRecursive(value, arraySampleSize);
        }

        return result;
    }

    private string? GetEncoderInfo(Tag tag)
    {
        // List of possible field/frame names (case-insensitive)
        string[] possibleKeys = new[]
        {
        "ENCODED_BY", "ENCODEDBY", "ENCODER", "TOOL", "SOFTWARE", "WRITING LIBRARY", "WRITINGLIBRARY", "CREATOR", "COMMENT"
    };

        // ID3v2 (MP3)
        if (tag is TagLib.Id3v2.Tag id3v2Tag)
        {
            foreach (string key in possibleKeys)
            {
                string? value = id3v2Tag.GetTextAsString(key);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        // XiphComment (FLAC, OGG)
        if (tag is TagLib.Ogg.XiphComment xiphTag)
        {
            foreach (string key in possibleKeys)
            {
                string? value = xiphTag.GetFirstField(key);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        // MP4 (AppleTag)
        if (tag is TagLib.Mpeg4.AppleTag mp4Tag)
        {
            foreach (string key in possibleKeys)
            {
                string? value = mp4Tag.GetText("----:com.apple.iTunes:" + key)?.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        // Generic: Search FieldList/Text for possible keys
        Type type = tag.GetType();
        PropertyInfo? fieldListProp = type.GetProperty("FieldList");
        if (fieldListProp != null)
        {
            IEnumerable<KeyValuePair<string, string[]>>? fieldList = fieldListProp.GetValue(tag) as IEnumerable<KeyValuePair<string, string[]>>;
            if (fieldList != null)
            {
                foreach (KeyValuePair<string, string[]> kvp in fieldList)
                {
                    if (possibleKeys.Any(k => kvp.Key.Equals(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        string? value = kvp.Value?.FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }
                }
            }
        }

        PropertyInfo? textProp = type.GetProperty("Text");
        if (textProp != null)
        {
            IDictionary<string, string[]>? textDict = textProp.GetValue(tag) as IDictionary<string, string[]>;
            if (textDict != null)
            {
                foreach (KeyValuePair<string, string[]> kvp in textDict)
                {
                    if (possibleKeys.Any(k => kvp.Key.Equals(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        string? value = kvp.Value?.FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }
                }
            }
        }

        // Sometimes encoder info is in the comment field
        if (!string.IsNullOrWhiteSpace(tag.Comment))
        {
            foreach (string key in possibleKeys)
            {
                if (tag.Comment.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return tag.Comment;
            }
        }

        return null;
    }

    private void DumpAudioFileJson()
    {
        if (_audioFile == null) return;

        object? tagSample = CreateSampledObjectRecursive(_audioFile.Tag, 5);
        object? propsSample = CreateSampledObjectRecursive(_audioFile.Properties, 5);

        // Common fields
        Dictionary<string, object?> dump = new Dictionary<string, object?>
        {
            ["Tag"] = tagSample,
            ["Properties"] = propsSample,
            ["TagTypes"] = _audioFile.TagTypes.ToString(),
            ["Duration"] = _audioFile.Properties.Duration.ToString(@"mm\:ss")
        };

        // Try to get "Encoded by" (ID3v2, MP4, etc.)
        string? encodedBy = null;
        if (_audioFile.Tag is TagLib.Id3v2.Tag id3v2Tag)
            encodedBy = id3v2Tag.GetTextAsString("TENC"); // TENC frame for "Encoded by"
        else if (_audioFile.Tag is TagLib.Mpeg4.AppleTag mp4Tag)
            encodedBy = mp4Tag.GetText("----:com.apple.iTunes:ENCODEDBY")?.FirstOrDefault();
        // Add other format checks as needed
        if (!string.IsNullOrWhiteSpace(encodedBy))
            dump["EncodedBy"] = encodedBy;

        // Format-specific: MP3 (MPEG)
        if (_audioFile is TagLib.Mpeg.File mpegFile)
        {
            dump["Bitrate"] = mpegFile.Properties.AudioBitrate;
            dump["SampleRate"] = mpegFile.Properties.AudioSampleRate;
            dump["Channels"] = mpegFile.Properties.AudioChannels;
            dump["Description"] = mpegFile.Properties.Description; // Contains MPEG version, layer, stereo mode, etc.
            // If you want to extract the MPEG version, you may need to parse this string.
        }

        // Format-specific: FLAC
        if (_audioFile is TagLib.Flac.File flacFile)
        {
            dump["FlacSampleRate"] = flacFile.Properties.AudioSampleRate;
            dump["FlacChannels"] = flacFile.Properties.AudioChannels;
            dump["FlacBitsPerSample"] = flacFile.Properties.BitsPerSample;
            dump["FlacDescription"] = flacFile.Properties.Description;
        }

        // Format-specific: MP4/AAC
        if (_audioFile is TagLib.Mpeg4.File mp4File)
        {
            dump["Mp4Description"] = mp4File.Properties.Description;
            dump["Mp4Bitrate"] = mp4File.Properties.AudioBitrate;
            dump["Mp4SampleRate"] = mp4File.Properties.AudioSampleRate;
            dump["Mp4Channels"] = mp4File.Properties.AudioChannels;
        }

        // Format-specific: OGG/Opus/Vorbis
        if (_audioFile is TagLib.Ogg.File oggFile)
        {
            dump["OggDescription"] = oggFile.Properties.Description;
            dump["OggBitrate"] = oggFile.Properties.AudioBitrate;
            dump["OggSampleRate"] = oggFile.Properties.AudioSampleRate;
            dump["OggChannels"] = oggFile.Properties.AudioChannels;
        }

        // Format-specific: WMA/ASF
        if (_audioFile is TagLib.Asf.File asfFile)
        {
            dump["AsfDescription"] = asfFile.Properties.Description;
            dump["AsfBitrate"] = asfFile.Properties.AudioBitrate;
            dump["AsfSampleRate"] = asfFile.Properties.AudioSampleRate;
            dump["AsfChannels"] = asfFile.Properties.AudioChannels;
        }

        encodedBy = GetEncoderInfo(_audioFile.Tag);
        if (!string.IsNullOrWhiteSpace(encodedBy))
            dump["EncodedBy"] = encodedBy;

        // Save JSON
        JsonSerializerSettings settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        string json = JsonConvert.SerializeObject(dump, settings);
        System.IO.File.WriteAllText($"{_audioFile.Tag.FirstPerformer}-{_audioFile.Tag.Title}.json", json);
    }
}