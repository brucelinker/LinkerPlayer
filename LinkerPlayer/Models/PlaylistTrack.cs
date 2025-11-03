using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LinkerPlayer.Models;

public class PlaylistTrack
{
    public int PlaylistId
    {
        get; set;
    }

    [StringLength(36, ErrorMessage = "TrackId must be a valid GUID (36 characters)")]
    public string? TrackId
    {
        get; set;
    }

    public int Position
    {
        get; set;
    }

    [ForeignKey(nameof(PlaylistId))]
    public Playlist? Playlist
    {
        get; set;
    }

    [ForeignKey(nameof(TrackId))]
    public MediaFile? Track
    {
        get; set;
    }
}
