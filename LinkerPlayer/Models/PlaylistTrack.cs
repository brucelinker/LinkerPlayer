using System.ComponentModel.DataAnnotations.Schema;

namespace LinkerPlayer.Models;

public class PlaylistTrack
{
    public int PlaylistId { get; set; }
    public string? TrackId { get; set; }
    public int Position { get; set; }

    [ForeignKey(nameof(PlaylistId))]
    public Playlist? Playlist { get; set; }

    [ForeignKey(nameof(TrackId))]
    public MediaFile? Track { get; set; }
}