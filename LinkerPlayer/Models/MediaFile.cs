using LinkerPlayer.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using TagLib;
using File = TagLib.File;

namespace LinkerPlayer.Models;

public interface IMediaFile : INotifyPropertyChanged
{
    string Id { get; }
    string FullFileName { get; }
    int PlayListIndex { get; set; }
    PlayerState State { get; set; }
    uint Track { get; }
    string TrackInfo { get; }
    uint Disc { get; }
    string Title { get; }
    BitmapImage AlbumCover { get; }
    string Album { get; }
    string FirstPerformer { get; }
    string FirstGenre { get; }
    bool IsVbr { get; }
}

public class MediaFile : IMediaFile
{
    const string UnknownString = "<Unknown>";

    public MediaFile()
    {

    }

    public MediaFile(string fileName)
    {
        FullFileName = fileName;
        FileName = Path.GetFileName(fileName);
        UpdateFromTag(false);
    }

    [JsonProperty(Required = Required.AllowNull)]
    public string Id { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public uint Track { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public uint TrackCount { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public string TrackInfo { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string TitleSort { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string Title { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string FirstAlbumArtist { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string FirstAlbumArtistSort { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string AlbumSort { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string Album { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string FirstPerformerSort { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string FirstPerformer { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string FirstGenre { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string FirstComposerSort { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string FirstComposer { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string FirstPerformerAndTitle { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string FirstPerformerAndAlbum { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public TimeSpan Duration { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public bool IsVbr { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public int AudioBitrate { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public int AudioSampleRate { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public uint DiscCount { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public uint Disc { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public uint Year { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public uint BeatsPerMinute { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public string Copyright { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string Comment { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string Conductor { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string Grouping { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string FullFileName { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string FileName { get; set; } = string.Empty;

    [Browsable(false)]
    [JsonIgnore]
    public BitmapImage AlbumCover { get; set; }

    PlayerState _state = PlayerState.Stopped;
    [Browsable(false)]
    [JsonIgnore]
    public PlayerState State
    {
        get => _state;
        set
        {
            if (_state == value) return;

            _state = value;
            OnPropertyChanged();
        }
    }

    int _playListIndex;
    [Browsable(false)]
    [JsonIgnore]
    public int PlayListIndex
    {
        get => _playListIndex;
        set
        {
            if (_playListIndex == value) return;

            _playListIndex = value;
            OnPropertyChanged();
        }
    }

    public void UpdateFromTag(bool raisePropertyChanged = true)
    {
        string fileName = FullFileName;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            using File? file = File.Create(fileName);

            Id = Guid.NewGuid().ToString();

            // Album
            Album = file.Tag.Album;
            if (string.IsNullOrWhiteSpace(Album))
            {
                Album = UnknownString;
            }
            AlbumSort = file.Tag.AlbumSort;

            // AlbumArtist
            List<string> albumArtists = file.Tag.AlbumArtists.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            List<string> albumArtistsSort = file.Tag.AlbumArtistsSort.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            FirstAlbumArtist = albumArtists.Count > 1 ? string.Join("/", albumArtists) : file.Tag.FirstAlbumArtist;
            FirstAlbumArtistSort = albumArtistsSort.Count > 1 ? string.Join("/", albumArtistsSort) : file.Tag.FirstAlbumArtistSort;

            // Artist/Performer
            List<string> performers = file.Tag.Performers.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            List<string> performersSort = file.Tag.PerformersSort.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            FirstPerformer = performers.Count > 1 ? string.Join("/", performers) : file.Tag.FirstPerformer;
            if (string.IsNullOrWhiteSpace(FirstPerformer))
            {
                FirstPerformer = UnknownString;
            }
            FirstPerformerSort = performersSort.Count > 1 ? string.Join("/", performersSort) : file.Tag.FirstPerformerSort;
            if (string.IsNullOrWhiteSpace(FirstPerformerSort))
            {
                FirstPerformerSort = UnknownString;
            }

            AlbumCover = CoverManager.GetImageFromPictureTag(FullFileName);

            // BeatsPerMinute
            BeatsPerMinute = file.Tag.BeatsPerMinute;

            // Comment
            Comment = file.Tag.Comment;

            // Composer
            List<string> composers = file.Tag.Composers.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            List<string> composersSort = file.Tag.ComposersSort.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            FirstComposer = composers.Count > 1 ? string.Join("/", composers) : file.Tag.FirstComposer;
            FirstComposerSort = composersSort.Count > 1 ? string.Join("/", composersSort) : file.Tag.FirstComposerSort;

            // Conductor
            Conductor = file.Tag.Conductor;

            // Copyright
            Copyright = file.Tag.Copyright;

            // Title
            Title = file.Tag.Title;
            TitleSort = file.Tag.TitleSort;

            // Genres
            List<string> genres = file.Tag.Genres.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            FirstGenre = genres.Count > 1 ? string.Join("/", genres) : file.Tag.FirstGenre;
            if (string.IsNullOrWhiteSpace(FirstGenre))
            {
                FirstGenre = UnknownString;
            }

            Track = file.Tag.Track;
            TrackCount = file.Tag.TrackCount;
            Disc = file.Tag.Disc;
            DiscCount = file.Tag.DiscCount;
            string trackFormat = TrackCount > 0 ? string.Format("Track {0}/{1}", Track, TrackCount) : string.Format("Track {0}", Track);
            TrackInfo = DiscCount > 0 ? string.Format("{0}  Disc {1}/{2}", trackFormat, Disc, DiscCount) : trackFormat;
            Year = file.Tag.Year;
            Grouping = file.Tag.Grouping;

            bool isFirstPerformerEmpty = string.IsNullOrWhiteSpace(FirstPerformer) || Equals(FirstPerformer, UnknownString);
            bool isTitleEmpty = string.IsNullOrWhiteSpace(Title) || Equals(FirstPerformer, UnknownString);
            if (!isFirstPerformerEmpty && !isTitleEmpty)
            {
                FirstPerformerAndTitle = string.Concat(FirstPerformer, " - ", Title);
            }
            else if (!isFirstPerformerEmpty)
            {
                FirstPerformerAndTitle = FirstPerformer;
            }
            else if (!isTitleEmpty)
            {
                FirstPerformerAndTitle = Title;
            }
            else
            {
                FirstPerformerAndTitle = Path.GetFileNameWithoutExtension(FileName);
            }

            bool isAlbumEmpty = string.IsNullOrWhiteSpace(Album) || Equals(Album, UnknownString);
            if (!isFirstPerformerEmpty && !isAlbumEmpty)
            {
                FirstPerformerAndAlbum = string.Concat(FirstPerformer, " - ", Album);
            }
            else if (!isFirstPerformerEmpty)
            {
                FirstPerformerAndAlbum = FirstPerformer;
            }
            else if (!isAlbumEmpty)
            {
                FirstPerformerAndAlbum = Album;
            }
            else
            {
                FirstPerformerAndAlbum = Path.GetFileNameWithoutExtension(FileName);
            }

            if (file.Properties.MediaTypes != MediaTypes.None)
            {
                Duration = file.Properties.Duration;
                ICodec? codec = file.Properties.Codecs.FirstOrDefault(c => c is TagLib.Mpeg.AudioHeader);
                IsVbr = codec != null && (((TagLib.Mpeg.AudioHeader)codec).VBRIHeader.Present || ((TagLib.Mpeg.AudioHeader)codec).XingHeader.Present);

                AudioBitrate = file.Properties.AudioBitrate;
                AudioSampleRate = file.Properties.AudioSampleRate;
            }
        }
        catch (Exception e)
        {
            throw new MediaFileException("TagLib.File.Create failes!", e);
        }

        if (raisePropertyChanged)
        {
            this.OnPropertyChanged();
        }
    }

    public override string ToString()
    {
        return $"{Track} {FirstAlbumArtist} - {Title} {Duration:m\\:ss}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public MediaFile Clone()
    {
        return new MediaFile()
        {
            Id = this.Id,
            FirstPerformerSort = this.FirstPerformerSort,
            FirstPerformer = this.FirstPerformer,
            FirstGenre = this.FirstGenre,
            FirstComposerSort = this.FirstComposerSort,
            FirstComposer = this.FirstComposer,
            FirstAlbumArtistSort = this.FirstAlbumArtistSort,
            FirstAlbumArtist = this.FirstAlbumArtist,
            FirstPerformerAndTitle = this.FirstPerformerAndTitle,
            FirstPerformerAndAlbum = this.FirstPerformerAndAlbum,
            Duration = this.Duration,
            IsVbr = this.IsVbr,
            AudioBitrate = this.AudioBitrate,
            AudioSampleRate = this.AudioSampleRate,
            DiscCount = this.DiscCount,
            Disc = this.Disc,
            TrackCount = this.TrackCount,
            Track = this.Track,
            TrackInfo = this.TrackInfo,
            Year = this.Year,
            BeatsPerMinute = this.BeatsPerMinute,
            Copyright = this.Copyright,
            Comment = this.Comment,
            AlbumSort = this.AlbumSort,
            Album = this.Album,
            Conductor = this.Conductor,
            TitleSort = this.TitleSort,
            Title = this.Title,
            Grouping = this.Grouping,
            FullFileName = this.FullFileName,
            FileName = this.FileName,
            AlbumCover = this.AlbumCover,
            PlayListIndex = this.PlayListIndex
        };
    }
}

