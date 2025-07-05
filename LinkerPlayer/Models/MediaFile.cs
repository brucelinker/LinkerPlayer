using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Core;
using ManagedBass;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Windows.Media.Imaging;
using TagLib;
using File = TagLib.File;

namespace LinkerPlayer.Models;

public interface IMediaFile
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
    string Artist { get; }
    string Album { get; }
    string Performers { get; }
    string Composers { get; }
    string Genres { get; }
    TimeSpan Duration { get; }
    string Comment { get; }
    int Bitrate { get; }
    int SampleRate { get; }
    int Channels { get; }
    string Copyright { get; }
    BitmapImage? AlbumCover { get; }
    PlaybackState State { get; set; }
}

[Index(nameof(Path), nameof(Album), nameof(Duration), IsUnique = true)]
public partial class MediaFile : ObservableValidator, IMediaFile
{
    private const string UnknownString = "<Unknown>";
    private readonly ILogger<MediaFile>? _logger;
    private readonly CoverManager _coverManager = new();

    [Key]
    [StringLength(36, ErrorMessage = "Id must be a valid GUID (36 characters)")]
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [StringLength(256, ErrorMessage = "Path cannot exceed 256 characters")]
    [ObservableProperty]
    private string _path = string.Empty;

    [StringLength(255, ErrorMessage = "FileName cannot exceed 255 characters")]
    [ObservableProperty]
    private string _fileName = string.Empty;

    [StringLength(128, ErrorMessage = "Title cannot exceed 128 characters")]
    [ObservableProperty]
    private string _title = string.Empty;

    [StringLength(128, ErrorMessage = "Artist cannot exceed 128 characters")]
    [ObservableProperty]
    private string _artist = string.Empty;

    [StringLength(128, ErrorMessage = "Album cannot exceed 128 characters")]
    [ObservableProperty]
    private string _album = string.Empty;

    [StringLength(256, ErrorMessage = "Performers cannot exceed 256 characters")]
    [ObservableProperty]
    private string _performers = string.Empty;

    [StringLength(256, ErrorMessage = "Composers cannot exceed 256 characters")]
    [ObservableProperty]
    private string _composers = string.Empty;

    [StringLength(128, ErrorMessage = "Genres cannot exceed 128 characters")]
    [ObservableProperty]
    private string _genres = string.Empty;

    [StringLength(128, ErrorMessage = "Copyright cannot exceed 128 characters")]
    [ObservableProperty]
    private string _copyright = string.Empty;

    [StringLength(256, ErrorMessage = "Comment cannot exceed 256 characters")]
    [ObservableProperty]
    private string _comment = string.Empty;

    [ObservableProperty]
    private uint _track;

    [ObservableProperty]
    private uint _trackCount;

    [ObservableProperty]
    private uint _disc;

    [ObservableProperty]
    private uint _discCount;

    [ObservableProperty]
    private uint _year;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private int _bitrate;

    [ObservableProperty]
    private int _sampleRate;

    [ObservableProperty]
    private int _channels;

    [NotMapped]
    [ObservableProperty]
    private BitmapImage? _albumCover;

    [ObservableProperty]
    private PlaybackState _state = PlaybackState.Stopped;

    [NotMapped]
    public List<PlaylistTrack> PlaylistTracks { get; set; } = new();

    public MediaFile() { }

    public MediaFile(string fileName, ILogger<MediaFile>? logger = null)
    {
        _logger = logger;
        Path = fileName;
        FileName = System.IO.Path.GetFileName(fileName);
        UpdateFromFileMetadata(false, minimal: true);
    }

    public void UpdateFromFileMetadata(bool raisePropertyChanged = true, bool minimal = false)
    {
        if (string.IsNullOrWhiteSpace(Path))
        {
            return;
        }

        try
        {
            using File? file = File.Create(Path);

            Id = Guid.NewGuid().ToString();
            ValidateStringLength(ref _id, 36, nameof(Id));

            FileName = System.IO.Path.GetFileName(Path);
            ValidateStringLength(ref _fileName, 255, nameof(FileName));

            Title = file.Tag.Title ?? FileName;
            ValidateStringLength(ref _title, 128, nameof(Title));

            Album = file.Tag.Album ?? UnknownString;
            ValidateStringLength(ref _album, 128, nameof(Album));

            if (!minimal)
            {
                List<string> albumArtists = file.Tag.AlbumArtists.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                Artist = albumArtists.Count > 1 ? string.Join("/", albumArtists) : file.Tag.FirstAlbumArtist ?? UnknownString;
                ValidateStringLength(ref _artist, 128, nameof(Artist));

                List<string> performers = file.Tag.Performers.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                Performers = performers.Count > 1 ? string.Join("/", performers) : file.Tag.FirstPerformer ?? string.Empty;
                ValidateStringLength(ref _performers, 256, nameof(Performers));

                if (string.IsNullOrWhiteSpace(Artist))
                {
                    Artist = string.IsNullOrWhiteSpace(Performers) ? UnknownString : Performers;
                    ValidateStringLength(ref _artist, 128, nameof(Artist));
                }

                Comment = file.Tag.Comment ?? string.Empty;
                ValidateStringLength(ref _comment, 256, nameof(Comment));

                List<string> composers = file.Tag.Composers.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                Composers = composers.Count > 1 ? string.Join("/", composers) : file.Tag.FirstComposer ?? string.Empty;
                ValidateStringLength(ref _composers, 256, nameof(Composers));

                Copyright = file.Tag.Copyright ?? string.Empty;
                ValidateStringLength(ref _copyright, 128, nameof(Copyright));

                Genres = file.Tag.Genres.Length > 1 ? string.Join("/", file.Tag.Genres) : file.Tag.FirstGenre ?? string.Empty;
                ValidateStringLength(ref _genres, 128, nameof(Genres));

                Track = file.Tag.Track;
                TrackCount = file.Tag.TrackCount;
                Disc = file.Tag.Disc;
                DiscCount = file.Tag.DiscCount;
                Year = file.Tag.Year;
                Bitrate = file.Properties.AudioBitrate;
                SampleRate = file.Properties.AudioSampleRate;
                Channels = file.Properties.AudioChannels;
            }

            if (file.Properties.MediaTypes != MediaTypes.None)
            {
                Duration = file.Properties.Duration != TimeSpan.Zero ? file.Properties.Duration : TimeSpan.FromSeconds(1);
            }
            else
            {
                Duration = TimeSpan.FromSeconds(1);
            }

            // Validate properties for UI (if needed)
            if (raisePropertyChanged)
            {
                ValidateAllProperties();
            }
        }
        catch (Exception e)
        {
            _logger?.LogError(e, "TagLib.File.Create failed for {FileName}: {Message}", Path, e.Message);
            Title = FileName;
            ValidateStringLength(ref _title, 128, nameof(Title));
            Album = UnknownString;
            ValidateStringLength(ref _album, 128, nameof(Album));
            Artist = string.Empty;
            Duration = TimeSpan.FromSeconds(1);
            if (raisePropertyChanged)
            {
                ValidateAllProperties();
            }
        }
    }

    public void LoadAlbumCover()
    {
        try
        {
            AlbumCover = _coverManager.GetImageFromPictureTag(Path);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load album cover for {Path}", Path);
            AlbumCover = null;
        }
    }

    public void UpdateFullMetadata()
    {
        UpdateFromFileMetadata();
    }

    public override string ToString()
    {
        return $"{Track} {Artist} - {Title} {Duration:m\\:ss}";
    }

    public MediaFile Clone()
    {
        return new MediaFile
        {
            Id = Id,
            Path = Path,
            FileName = FileName,
            Track = Track,
            TrackCount = TrackCount,
            Disc = Disc,
            DiscCount = DiscCount,
            Year = Year,
            Title = Title,
            Album = Album,
            Artist = Artist,
            Performers = Performers,
            Composers = Composers,
            Genres = Genres,
            Comment = Comment,
            Duration = Duration,
            Bitrate = Bitrate,
            SampleRate = SampleRate,
            Channels = Channels,
            Copyright = Copyright,
            AlbumCover = AlbumCover,
            State = State
        };
    }

    private void ValidateStringLength(ref string value, int maxLength, string propertyName)
    {
        if (value.Length > maxLength)
        {
            _logger?.LogWarning("Property {PropertyName} exceeds maximum length of {MaxLength} characters. Truncating.",
                propertyName, maxLength);
            value = value.Substring(0, maxLength);
        }
    }
}