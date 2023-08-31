using System.Collections.Generic;
using System.Linq;

namespace LinkerPlayer.Models;

public class Playlist
{
    public string? Name { get; set; } = string.Empty;
    public List<string> SongIds { get; set; }

    public Playlist()
    {
        SongIds = new List<string>();
    }

    public Playlist Clone()
    {
        return new Playlist
        {
            Name = this.Name,
            SongIds = this.SongIds.ToList()
        };
    }
}