using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Core;
using ManagedBass;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    string Track { get; }
    uint TrackCount { get; }
    uint Disc { get; }
    uint DiscCount { get; }
    uint Year { get; }
    string Title { get; }
    string Artist { get; }
    string Album { get; }
    string Performers { get; }
    string Composers { get; }
    TimeSpan Duration { get; }
    string Genres { get; }
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
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Track { get; set; } = string.Empty;
    public uint TrackCount { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string Performers { get; set; } = string.Empty;
    public string Genres { get; set; } = string.Empty;
    public string Composers { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public int Bitrate { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public uint DiscCount { get; set; }
    public uint Disc { get; set; }
    public uint Year { get; set; }
    public string Copyright { get; set; } = string.Empty;
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
        UpdateFromFileMetadata(false);
    }

    public void UpdateFromFileMetadata(bool raisePropertyChanged = true)
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

            Title = file.Tag.Title ?? FileName;

            Album = file.Tag.Album ?? UnknownString;

            List<string> albumArtists = file.Tag.AlbumArtists.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            Artist = albumArtists.Count > 1 ? string.Join("/", albumArtists) : file.Tag.FirstAlbumArtist ?? UnknownString;

            List<string> performers = file.Tag.Performers.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            Performers = performers.Count > 1 ? string.Join("/", performers) : file.Tag.FirstPerformer ?? string.Empty;

            if (string.IsNullOrWhiteSpace(Artist))
            {
                Artist = string.IsNullOrWhiteSpace(Performers) ? UnknownString : Performers;
            }

            AlbumCover = CoverManager.GetImageFromPictureTag(Path);

            Comment = file.Tag.Comment ?? string.Empty;

            List<string> composers = file.Tag.Composers.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            Composers = composers.Count > 1 ? string.Join("/", composers) : file.Tag.FirstComposer ?? string.Empty;

            Copyright = file.Tag.Copyright ?? string.Empty;

            Genres = file.Tag.Genres.Length > 1 ? string.Join("/", file.Tag.Genres) : file.Tag.FirstGenre ?? string.Empty;

            Track = file.Tag.Track == 0 ? string.Empty : $"{file.Tag.Track}";
            TrackCount = file.Tag.TrackCount;
            Disc = file.Tag.Disc;
            DiscCount = file.Tag.DiscCount;
            Year = file.Tag.Year;

            if (file.Properties.MediaTypes != MediaTypes.None)
            {
                Duration = file.Properties.Duration != TimeSpan.Zero ? file.Properties.Duration : TimeSpan.FromSeconds(1);
                Bitrate = file.Properties.AudioBitrate;
                SampleRate = file.Properties.AudioSampleRate;
                Channels = file.Properties.AudioChannels;
            }
            else
            {
                Duration = TimeSpan.FromSeconds(1); // Default to prevent uniqueness constraint issues
            }
        }
        catch (Exception e)
        {
            Log.Error("TagLib.File.Create failed for {FileName}: {Error}", fileName, e.Message);
            // Set defaults to allow track addition
            Title = FileName;
            Album = UnknownString;
            Artist = UnknownString;
            Duration = TimeSpan.FromSeconds(1);
        }

        if (raisePropertyChanged)
        {
            OnPropertyChanged();
        }
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