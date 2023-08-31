using System;

namespace LinkerPlayer.Models;

public class Song
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Path { get; set; }
    public TimeSpan Duration { get; set; }

    public Song Clone()
    {
        return new Song
        {
            Id = this.Id,
            Name = this.Name,
            Path = this.Path,
            Duration = this.Duration
        };
    }
}