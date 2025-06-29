using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LinkerPlayer.Models;

[Index(nameof(Name), IsUnique = true)]
public partial class Playlist : ObservableValidator
{
    [Key]
    public int Id { get; set; }

    [StringLength(100, ErrorMessage = "Playlist name cannot exceed 100 characters")]
    [ObservableProperty]
    private string _name = "New Playlist";

    [StringLength(36, ErrorMessage = "SelectedTrack must be a valid GUID (36 characters)")]
    [ObservableProperty]
    private string? _selectedTrack;

    public ObservableCollection<string> TrackIds { get; set; } = new();

    [ForeignKey(nameof(SelectedTrack))]
    public MediaFile? SelectedTrackNavigation { get; set; }

    public List<PlaylistTrack> PlaylistTracks { get; set; } = new();
}