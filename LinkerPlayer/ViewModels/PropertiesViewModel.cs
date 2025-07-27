using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkerPlayer.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using TagLib;

namespace LinkerPlayer.ViewModels
{
    public partial class PropertiesViewModel : ObservableObject
    {
        private readonly SharedDataModel _sharedDataModel;
        private TagLib.File _audioFile;
        private bool _hasUnsavedChanges;

        public ObservableCollection<TagItem> MetadataItems { get; } = new ObservableCollection<TagItem>();
        public ObservableCollection<TagItem> PropertyItems { get; } = new ObservableCollection<TagItem>();

        public event EventHandler<bool> CloseRequested;

        public PropertiesViewModel(SharedDataModel sharedDataModel)
        {
            _sharedDataModel = sharedDataModel;
            _sharedDataModel.PropertyChanged += SharedDataModel_PropertyChanged;

            if (_sharedDataModel.SelectedTrack != null)
            {
                LoadTrackData(_sharedDataModel.SelectedTrack.Path);
            }
        }

        [RelayCommand]
        public void Save()
        {
            if (SaveChanges())
            {
                UpdateTrackMetadata();
                CloseRequested?.Invoke(this, true);
            }
        }

        [RelayCommand]
        public void Cancel()
        {
            if (_hasUnsavedChanges)
            {
                var result = MessageBox.Show("You have unsaved changes. Discard changes?",
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
                if (_hasUnsavedChanges)
                {
                    var result = MessageBox.Show("You have unsaved changes. Save before switching tracks?",
                        "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Yes)
                    {
                        if (!SaveChanges())
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
                _audioFile = TagLib.File.Create(path);
                MetadataItems.Clear();
                PropertyItems.Clear();

                LoadMetadata();
                LoadProperties();

                _hasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading track data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadMetadata()
        {
            var tag = _audioFile.Tag;
            // Include tags even if empty
            AddMetadataItem("Title", tag.Title ?? "", true, v => { tag.Title = string.IsNullOrEmpty(v) ? null : v; _hasUnsavedChanges = true; });
            AddMetadataItem("Artist", tag.FirstPerformer ?? string.Join(", ", tag.Performers ?? Array.Empty<string>()), true, v => { tag.Performers = string.IsNullOrEmpty(v) ? Array.Empty<string>() : v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray(); _hasUnsavedChanges = true; });
            AddMetadataItem("Album", tag.Album ?? "", true, v => { tag.Album = string.IsNullOrEmpty(v) ? null : v; _hasUnsavedChanges = true; });
            AddMetadataItem("Album Artist", tag.FirstAlbumArtist ?? string.Join(", ", tag.AlbumArtists ?? Array.Empty<string>()), true, v => { tag.AlbumArtists = string.IsNullOrEmpty(v) ? Array.Empty<string>() : v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray(); _hasUnsavedChanges = true; });
            AddMetadataItem("Year", tag.Year > 0 ? tag.Year.ToString() : "", true, v => { tag.Year = uint.TryParse(v, out var year) ? year : 0; _hasUnsavedChanges = true; });
            AddMetadataItem("Genre", tag.FirstGenre ?? string.Join(", ", tag.Genres ?? Array.Empty<string>()), true, v => { tag.Genres = string.IsNullOrEmpty(v) ? Array.Empty<string>() : v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray(); _hasUnsavedChanges = true; });
            AddMetadataItem("Composer", tag.FirstComposer ?? string.Join(", ", tag.Composers ?? Array.Empty<string>()), true, v => { tag.Composers = string.IsNullOrEmpty(v) ? Array.Empty<string>() : v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray(); _hasUnsavedChanges = true; });
            AddMetadataItem("Track Number", tag.Track > 0 ? tag.Track.ToString() : "", true, v => { tag.Track = uint.TryParse(v, out var track) ? track : 0; _hasUnsavedChanges = true; });
            AddMetadataItem("Total Tracks", tag.TrackCount > 0 ? tag.TrackCount.ToString() : "", true, v => { tag.TrackCount = uint.TryParse(v, out var count) ? count : 0; _hasUnsavedChanges = true; });
            AddMetadataItem("Disc Number", tag.Disc > 0 ? tag.Disc.ToString() : "", true, v => { tag.Disc = uint.TryParse(v, out var disc) ? disc : 0; _hasUnsavedChanges = true; });
            AddMetadataItem("Total Discs", tag.DiscCount > 0 ? tag.DiscCount.ToString() : "", true, v => { tag.DiscCount = uint.TryParse(v, out var count) ? count : 0; _hasUnsavedChanges = true; });
            AddMetadataItem("Comment", tag.Comment ?? "", true, v => { tag.Comment = string.IsNullOrEmpty(v) ? null : v; _hasUnsavedChanges = true; });
            AddMetadataItem("Copyright", tag.Copyright ?? "", true, v => { tag.Copyright = string.IsNullOrEmpty(v) ? null : v; _hasUnsavedChanges = true; });
            AddMetadataItem("Lyrics", tag.Lyrics ?? "", true, v => { tag.Lyrics = string.IsNullOrEmpty(v) ? null : v; _hasUnsavedChanges = true; });
            AddMetadataItem("Beats Per Minute", tag.BeatsPerMinute > 0 ? tag.BeatsPerMinute.ToString() : "", true, v => { tag.BeatsPerMinute = uint.TryParse(v, out var bpm) ? bpm : 0; _hasUnsavedChanges = true; });
            AddMetadataItem("Conductor", tag.Conductor ?? "", true, v => { tag.Conductor = string.IsNullOrEmpty(v) ? null : v; _hasUnsavedChanges = true; });
            AddMetadataItem("Grouping", tag.Grouping ?? "", true, v => { tag.Grouping = string.IsNullOrEmpty(v) ? null : v; _hasUnsavedChanges = true; });
            AddMetadataItem("Publisher", tag.Publisher ?? "", true, v => { tag.Publisher = string.IsNullOrEmpty(v) ? null : v; _hasUnsavedChanges = true; });
            AddMetadataItem("ISRC", tag.ISRC ?? "", true, v => { tag.ISRC = string.IsNullOrEmpty(v) ? null : v; _hasUnsavedChanges = true; });

            // Format-specific tags
            if (_audioFile.Tag is TagLib.Id3v2.Tag id3v2Tag)
            {
                // MP3: ID3v2 TXXX frames
                AddMetadataItem("ReplayGain Track Gain", id3v2Tag.GetTextAsString("TXXX:REPLAYGAIN_TRACK_GAIN") ?? "", true, v => { id3v2Tag.SetTextFrame("TXXX:REPLAYGAIN_TRACK_GAIN", string.IsNullOrEmpty(v) ? null : v); _hasUnsavedChanges = true; });
                AddMetadataItem("ReplayGain Track Peak", id3v2Tag.GetTextAsString("TXXX:REPLAYGAIN_TRACK_PEAK") ?? "", false, null);
                AddMetadataItem("Mood", id3v2Tag.GetTextAsString("TXXX:MOOD") ?? "", true, v => { id3v2Tag.SetTextFrame("TXXX:MOOD", string.IsNullOrEmpty(v) ? null : v); _hasUnsavedChanges = true; });
                AddMetadataItem("Energy", id3v2Tag.GetTextAsString("TXXX:ENERGY") ?? "", true, v => { id3v2Tag.SetTextFrame("TXXX:ENERGY", string.IsNullOrEmpty(v) ? null : v); _hasUnsavedChanges = true; });
            }
            else if (_audioFile.Tag is TagLib.Ogg.XiphComment xiphComment)
            {
                // FLAC/Ogg: Vorbis comments
                AddMetadataItem("ReplayGain Track Gain", xiphComment.GetFirstField("REPLAYGAIN_TRACK_GAIN") ?? "", true, v => { xiphComment.SetField("REPLAYGAIN_TRACK_GAIN", string.IsNullOrEmpty(v) ? null : v); _hasUnsavedChanges = true; });
                AddMetadataItem("ReplayGain Track Peak", xiphComment.GetFirstField("REPLAYGAIN_TRACK_PEAK") ?? "", false, null);
                AddMetadataItem("Mood", xiphComment.GetFirstField("MOOD") ?? "", true, v => { xiphComment.SetField("MOOD", string.IsNullOrEmpty(v) ? null : v); _hasUnsavedChanges = true; });
                AddMetadataItem("Energy", xiphComment.GetFirstField("ENERGY") ?? "", true, v => { xiphComment.SetField("ENERGY", string.IsNullOrEmpty(v) ? null : v); _hasUnsavedChanges = true; });
            }
            else if (_audioFile.Tag is TagLib.Mpeg4.AppleTag mp4Tag)
            {
                // AAC/MP4: Apple-specific tags
                AddMetadataItem("ReplayGain Track Gain", mp4Tag.GetText("----:com.apple.iTunes:REPLAYGAIN_TRACK_GAIN")?.FirstOrDefault() ?? "", true, v => { mp4Tag.SetText("----:com.apple.iTunes:REPLAYGAIN_TRACK_GAIN", string.IsNullOrEmpty(v) ? null : new[] { v }); _hasUnsavedChanges = true; });
                AddMetadataItem("ReplayGain Track Peak", mp4Tag.GetText("----:com.apple.iTunes:REPLAYGAIN_TRACK_PEAK")?.FirstOrDefault() ?? "", false, null);
                AddMetadataItem("Mood", mp4Tag.GetText("----:com.apple.iTunes:MOOD")?.FirstOrDefault() ?? "", true, v => { mp4Tag.SetText("----:com.apple.iTunes:MOOD", string.IsNullOrEmpty(v) ? null : new[] { v }); _hasUnsavedChanges = true; });
                AddMetadataItem("Energy", mp4Tag.GetText("----:com.apple.iTunes:ENERGY")?.FirstOrDefault() ?? "", true, v => { mp4Tag.SetText("----:com.apple.iTunes:ENERGY", string.IsNullOrEmpty(v) ? null : new[] { v }); _hasUnsavedChanges = true; });
            }
            else
            {
                // Fallback for unsupported formats (e.g., WAV, WMA)
                AddMetadataItem("ReplayGain Track Gain", "", false, null);
                AddMetadataItem("ReplayGain Track Peak", "", false, null);
                AddMetadataItem("Mood", "", false, null);
                AddMetadataItem("Energy", "", false, null);
            }

            if (tag.Pictures != null && tag.Pictures.Length > 0)
            {
                AddMetadataItem("Picture Mime Type", tag.Pictures[0].MimeType ?? "", false);
                AddMetadataItem("Picture Type", tag.Pictures[0].Type.ToString() ?? "", false);
                AddMetadataItem("Picture Filename", tag.Pictures[0].Filename ?? "", false);
                AddMetadataItem("Picture Description", tag.Pictures[0].Description ?? "", false);
            }
        }

        private void LoadProperties()
        {
            var props = _audioFile.Properties;
            AddPropertyItem("Duration", props.Duration.ToString(@"mm\:ss") ?? "", false);
            AddPropertyItem("Bitrate", props.AudioBitrate > 0 ? props.AudioBitrate.ToString() + " kbps" : "", false);
            AddPropertyItem("Sample Rate", props.AudioSampleRate > 0 ? props.AudioSampleRate.ToString() + " Hz" : "", false);
            AddPropertyItem("Channels", props.AudioChannels > 0 ? props.AudioChannels.ToString() : "", false);
            AddPropertyItem("Media Types", props.MediaTypes.ToString() ?? "", false);
            AddPropertyItem("Description", props.Description ?? "", false);
            AddPropertyItem("Codec", props.Description ?? props.Codecs?.FirstOrDefault()?.Description ?? "", false);
            AddPropertyItem("Bits Per Sample", props.BitsPerSample > 0 ? props.BitsPerSample.ToString() : "", false);
        }

        private void AddMetadataItem(string name, string value, bool isEditable, Action<string> updateAction = null)
        {
            // Include tags even if empty
            var item = new TagItem
            {
                Name = name,
                Value = value ?? "",
                IsEditable = isEditable,
                UpdateAction = isEditable ? updateAction : null
            };
            if (isEditable)
            {
                item.PropertyChanged += TagItem_PropertyChanged;
            }
            MetadataItems.Add(item);
        }

        private void AddPropertyItem(string name, string value, bool isEditable)
        {
            // Include properties even if empty
            PropertyItems.Add(new TagItem
            {
                Name = name,
                Value = value ?? "",
                IsEditable = isEditable
            });
        }

        private void TagItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TagItem.Value))
            {
                _hasUnsavedChanges = true;
            }
        }

        private bool SaveChanges()
        {
            try
            {
                foreach (var item in MetadataItems.Where(i => i.IsEditable))
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
                _audioFile.Save();
                _hasUnsavedChanges = false;
                MessageBox.Show("Changes saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void UpdateTrackMetadata()
        {
            if (_sharedDataModel.SelectedTrack == null || _audioFile == null)
                return;

            _sharedDataModel.SelectedTrack.UpdateFromFileMetadata(true);

            if (_sharedDataModel.ActiveTrack == _sharedDataModel.SelectedTrack)
            {
                _sharedDataModel.ActiveTrack.UpdateFromFileMetadata(true);
            }
        }
    }
}