using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Core;
using ManagedBass;
using Microsoft.EntityFrameworkCore;
using Serilog;
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
    BitmapImage? AlbumCover { get; }
    PlaybackState State { get; set; }
}

[Index(nameof(Path), nameof(Album), nameof(Duration), IsUnique = true)]
public class MediaFile : ObservableObject, IMediaFile
{
    const string UnknownString = "<Unknown>";

    [Key]
    [StringLength(36, ErrorMessage = "Id must be a valid GUID (36 characters)")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [StringLength(256, ErrorMessage = "Path cannot exceed 256 characters")]
    public string Path { get; set; } = string.Empty;

    [StringLength(255, ErrorMessage = "FileName cannot exceed 255 characters")]
    public string FileName { get; set; } = string.Empty;

    [StringLength(128, ErrorMessage = "Title cannot exceed 128 characters")]
    public string Title { get; set; } = string.Empty;

    private string _artist = string.Empty;
    [StringLength(128, ErrorMessage = "Artist cannot exceed 128 characters")]
    public string Artist
    {
        get => _artist;
        set => SetProperty(ref _artist, value.Length > 128 ? value.Substring(0, 128) : value);
    }

    [StringLength(128, ErrorMessage = "Album cannot exceed 128 characters")]
    public string Album { get; set; } = string.Empty;

    [StringLength(256, ErrorMessage = "Performers cannot exceed 256 characters")]
    public string Performers { get; set; } = string.Empty;

    [StringLength(256, ErrorMessage = "Composers cannot exceed 256 characters")]
    public string Composers { get; set; } = string.Empty;

    [StringLength(128, ErrorMessage = "Genres cannot exceed 128 characters")]
    public string Genres { get; set; } = string.Empty;

    public uint Track { get; set; }
    public uint TrackCount { get; set; }
    public uint Disc { get; set; }
    public uint DiscCount { get; set; }
    public uint Year { get; set; }
    public TimeSpan Duration { get; set; }
    public int Bitrate { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }

    [StringLength(128, ErrorMessage = "Copyright cannot exceed 128 characters")]
    public string Copyright { get; set; } = string.Empty;

    [StringLength(256, ErrorMessage = "Comment cannot exceed 256 characters")]
    public string Comment { get; set; } = string.Empty;

    [NotMapped]
    public BitmapImage? AlbumCover { get; set; }

    public PlaybackState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }
    private PlaybackState _state = PlaybackState.Stopped;

    [NotMapped]
    public List<PlaylistTrack> PlaylistTracks { get; set; } = new();

    public MediaFile() { }

    public MediaFile(string fileName)
    {
        Path = fileName;
        FileName = System.IO.Path.GetFileName(fileName);
        UpdateFromFileMetadata(false, minimal: true);
    }

    public void UpdateFromFileMetadata(bool raisePropertyChanged = true, bool minimal = false)
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
            if (Id.Length > 36) throw new ArgumentException("Id exceeds 36 characters");

            FileName = System.IO.Path.GetFileName(fileName);
            if (FileName.Length > 255) FileName = FileName.Substring(0, 255);


            Title = file.Tag.Title ?? FileName;
            if (Title.Length > 128) Title = Title.Substring(0, 128);

            Album = file.Tag.Album ?? UnknownString;
            if (Album.Length > 128) Album = Album.Substring(0, 128);

            if (!minimal)
            {
                List<string> albumArtists = file.Tag.AlbumArtists.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                Artist = albumArtists.Count > 1 ? string.Join("/", albumArtists) : file.Tag.FirstAlbumArtist ?? UnknownString;
                if (Artist.Length > 128) Artist = Artist.Substring(0, 128);

                List<string> performers = file.Tag.Performers.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                Performers = performers.Count > 1 ? string.Join("/", performers) : file.Tag.FirstPerformer ?? string.Empty;
                if (Performers.Length > 256) Performers = Performers.Substring(0, 256);

                if (string.IsNullOrWhiteSpace(Artist))
                {
                    Artist = string.IsNullOrWhiteSpace(Performers) ? UnknownString : Performers;
                    if (Artist.Length > 128) Artist = Artist.Substring(0, 128);
                }

                Comment = file.Tag.Comment ?? string.Empty;
                if (Comment.Length > 256) Comment = Comment.Substring(0, 256);

                List<string> composers = file.Tag.Composers.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                Composers = composers.Count > 1 ? string.Join("/", composers) : file.Tag.FirstComposer ?? string.Empty;
                if (Composers.Length > 256) Composers = Composers.Substring(0, 256);

                Copyright = file.Tag.Copyright ?? string.Empty;
                if (Copyright.Length > 128) Copyright = Copyright.Substring(0, 128);

                Genres = file.Tag.Genres.Length > 1 ? string.Join("/", file.Tag.Genres) : file.Tag.FirstGenre ?? string.Empty;
                if (Genres.Length > 128) Genres = Genres.Substring(0, 128);

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
        }
        catch (Exception e)
        {
            Log.Error("TagLib.File.Create failed for {FileName}: {Error}", fileName, e.Message);
            Title = FileName;
            if (Title.Length > 128) Title = Title.Substring(0, 128);
            Album = UnknownString;
            if (Album.Length > 128) Album = Album.Substring(0, 128);
            Artist = string.Empty;
            Duration = TimeSpan.FromSeconds(1);
        }

        if (raisePropertyChanged)
        {
            OnPropertyChanged();
        }
    }

    public void LoadAlbumCover()
    {
        try
        {
            AlbumCover = CoverManager.GetImageFromPictureTag(Path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to load album cover for {Path}");
            AlbumCover = null;
        }
        OnPropertyChanged(nameof(AlbumCover));
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
            Artist = this.Artist,
            Performers = this.Performers,
            Composers = this.Composers,
            Genres = this.Genres,
            Comment = this.Comment,
            Duration = this.Duration,
            Bitrate = this.Bitrate,
            SampleRate = this.SampleRate,
            Channels = this.Channels,
            Copyright = this.Copyright,
            AlbumCover = this.AlbumCover,
            State = this.State
        };
    }
}