using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkerPlayer.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Navigation;
using TagLib;
using Tag = TagLib.Tag;
// ReSharper disable InconsistentNaming

namespace LinkerPlayer.ViewModels
{
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

            Tag? tag = _audioFile.Tag;
            // Include tags even if empty
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

            // Format-specific tags
            if (_audioFile.Tag is TagLib.Id3v2.Tag id3v2Tag)
            {
                // MP3: ID3v2 TXXX frames
                AddMetadataItem("Mood", id3v2Tag.GetTextAsString("TXXX:MOOD") ?? "", true, v => { id3v2Tag.SetTextFrame("TXXX:MOOD", string.IsNullOrEmpty(v) ? null : v); HasUnsavedChanges = true; });
                AddMetadataItem("Energy", id3v2Tag.GetTextAsString("TXXX:ENERGY") ?? "", true, v => { id3v2Tag.SetTextFrame("TXXX:ENERGY", string.IsNullOrEmpty(v) ? null : v); HasUnsavedChanges = true; });
            }
            else if (_audioFile.Tag is TagLib.Ogg.XiphComment xiphComment)
            {
                // FLAC/Ogg: Vorbis comments
                AddMetadataItem("Mood", xiphComment.GetFirstField("MOOD") ?? "", true, v => { xiphComment.SetField("MOOD", string.IsNullOrEmpty(v) ? null : v); HasUnsavedChanges = true; });
                AddMetadataItem("Energy", xiphComment.GetFirstField("ENERGY") ?? "", true, v => { xiphComment.SetField("ENERGY", string.IsNullOrEmpty(v) ? null : v); HasUnsavedChanges = true; });
            }
            else if (_audioFile.Tag is TagLib.Mpeg4.AppleTag mp4Tag)
            {
                // AAC/MP4: Apple-specific tags
                AddMetadataItem("Mood", mp4Tag.GetText("----:com.apple.iTunes:MOOD")?.FirstOrDefault() ?? "", true, v => { mp4Tag.SetText("----:com.apple.iTunes:MOOD", string.IsNullOrEmpty(v) ? null : [ v ]); HasUnsavedChanges = true; });
                AddMetadataItem("Energy", mp4Tag.GetText("----:com.apple.iTunes:ENERGY")?.FirstOrDefault() ?? "", true, v => { mp4Tag.SetText("----:com.apple.iTunes:ENERGY", string.IsNullOrEmpty(v) ? null : [ v ]); HasUnsavedChanges = true; });
            }
            else
            {
                // Fallback for unsupported formats (e.g., WAV, WMA)
                AddMetadataItem("Mood", "", false, null);
                AddMetadataItem("Energy", "", false, null);
            }
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
    }
}