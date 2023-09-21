using LinkerPlayer.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Serilog;
using TagLib;
using File = TagLib.File;

namespace LinkerPlayer.Models;

public interface IMediaFile : INotifyPropertyChanged
{
    string Id { get; }
    string Path { get; }
    string FileName { get; }
    uint Track { get; }
    uint TrackCount { get; }
    uint Disc { get; }
    uint DiscCount { get; }
    uint Year { get; }
    string Title { get; }
    string Artists { get; }
    string Album { get; }
    string Performers { get; }
    string Composers { get; }
    TimeSpan Duration { get; }
    string Genres { get; }
    string Comment { get; }
    int Bitrate { get; }
    int SampleRate { get; } 
    BitmapImage? AlbumCover { get; }
    int PlayListIndex { get; set; }
    PlayerState State { get; set; }
}

public class MediaFile : IMediaFile
{
    const string UnknownString = "<Unknown>";

    public MediaFile()
    {
    }

    public MediaFile(string fileName)
    {
        Path = fileName;
        FileName = System.IO.Path.GetFileName(fileName);
        UpdateFromTag(false);
    }

    [JsonProperty(Required = Required.AllowNull)]
    public string Id { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string Path { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string FileName { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public uint Track { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public uint TrackCount { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public string Title { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string Artists { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string Album { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string Performers { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string Genres { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string Composers { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public TimeSpan Duration { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public int Bitrate { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public int SampleRate { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public uint DiscCount { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public uint Disc { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public uint Year { get; set; }
    [JsonProperty(Required = Required.AllowNull)]
    public string Copyright { get; set; } = string.Empty;
    [JsonProperty(Required = Required.AllowNull)]
    public string Comment { get; set; } = string.Empty;

    [Browsable(false)]
    [JsonIgnore]
    public BitmapImage? AlbumCover { get; set; }

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
        string fileName = Path;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            using File? file = File.Create(fileName);

            Id = Guid.NewGuid().ToString();
            FileName = System.IO.Path.GetFileName(fileName);

            // Title
            Title = file.Tag.Title;

            // Album
            Album = file.Tag.Album;
            if (string.IsNullOrWhiteSpace(Album))
            {
                Album = UnknownString;
            }

            // Artists
            List<string> albumArtists = file.Tag.AlbumArtists.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            Artists = albumArtists.Count > 1 ? string.Join("/", albumArtists) : file.Tag.FirstAlbumArtist;

            // Performers
            List<string> performers = file.Tag.Performers.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            Performers = performers.Count > 1 ? string.Join("/", performers) : file.Tag.FirstPerformer;

            if (string.IsNullOrWhiteSpace(Artists))
            {
                Artists = string.IsNullOrWhiteSpace(Performers) ? UnknownString : Performers;
            }

            AlbumCover = CoverManager.GetImageFromPictureTag(Path);

            // Comment
            Comment = file.Tag.Comment;

            // Composers
            List<string> composers = file.Tag.Composers.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            Composers = composers.Count > 1 ? string.Join("/", composers) : file.Tag.FirstComposer;

            // Copyright
            Copyright = file.Tag.Copyright;

            // Genres
            Genres = file.Tag.Genres.Length > 1 ? string.Join("/", Genres) : file.Tag.FirstGenre;

            Track = file.Tag.Track;
            TrackCount = file.Tag.TrackCount;
            Disc = file.Tag.Disc;
            DiscCount = file.Tag.DiscCount;
            Year = file.Tag.Year;

            if (file.Properties.MediaTypes != MediaTypes.None)
            {
                Duration = file.Properties.Duration;
                Bitrate = file.Properties.AudioBitrate;
                SampleRate = file.Properties.AudioSampleRate;
            }
        }
        catch (Exception e)
        {
            Log.Error("TagLib.File.Create failed! - {0}", e.Message);
            throw new MediaFileException("TagLib.File.Create failed!", e);
        }

        if (raisePropertyChanged)
        {
            this.OnPropertyChanged();
        }
    }

    public override string ToString()
    {
        return $"{Track} {Artists} - {Title} {Duration:m\\:ss}";
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
            Path = this.Path,
            FileName = this.FileName,
            Track = this.Track,
            TrackCount = this.TrackCount,
            Disc = this.Disc,
            DiscCount = this.DiscCount,
            Year = this.Year,
            Title = this.Title,
            Album = this.Album,
            Artists = this.Artists,
            Performers = this.Performers,
            Composers = this.Composers,
            Genres = this.Genres,
            Comment = this.Comment,
            Duration = this.Duration,
            Bitrate = this.Bitrate,
            SampleRate = this.SampleRate,
            Copyright = this.Copyright,
            AlbumCover = this.AlbumCover,
            PlayListIndex = this.PlayListIndex
        };
    }
}

