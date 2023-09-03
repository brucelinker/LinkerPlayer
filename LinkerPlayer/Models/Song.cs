using System;
using System.Collections.Generic;
using TagLib;

namespace LinkerPlayer.Models;

public class Song
{
    public string Id { get; set; } = string.Empty;
    public uint Track { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string[] AlbumArtists { get; set; } = Array.Empty<string>();
    public string[] Composers { get; set; } = Array.Empty<string>();
    public string[] Genres { get; set; } = Array.Empty<string>();
    public uint Year { get; set; }
    public string? Path { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public int BitRate { get; set; }
    public int Channels { get; set; }
    public int SampleRate { get; set; }
    public int BitsPerSample { get; set; }
    public IEnumerable<ICodec> Codecs { get; set; } = new List<ICodec>();
    public string Description { get; set; } = string.Empty;

    public Song Clone()
    {
        return new Song
        {
            Id = this.Id,
            Track = this.Track,
            Title = this.Title,
            Artist = this.Artist,
            Album = this.Album,
            AlbumArtists = this.AlbumArtists,
            Composers = this.Composers,
            Genres = this.Genres,
            Year = this.Year,
            Path = this.Path,
            Duration = this.Duration,
            BitRate = this.BitRate,
            Channels = this.Channels,
            SampleRate = this.SampleRate,
            BitsPerSample = this.BitsPerSample,
            Codecs = this.Codecs,
            Description = this.Description,
        };
    }
}
