using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace LinkerPlayer.Models;

public partial class PlaylistTab : ObservableObject
{
    [ObservableProperty] private string _name = "New Playlist";
    private readonly ObservableCollection<MediaFile> _tracks = new();
    public ObservableCollection<MediaFile> Tracks => _tracks;
    [ObservableProperty] private MediaFile? _selectedTrack;
    [ObservableProperty] private int? _selectedIndex;

    // Adds a single track if not already present; selects it if none selected
    public void AddTrack(MediaFile track)
    {
        if (track == null)
        {
            throw new ArgumentNullException(nameof(track));
        }

        if (_tracks.Any(t => t.Id == track.Id))
        {
            return; // avoid duplicates by Id
        }

        _tracks.Add(track);
        if (SelectedTrack == null)
        {
            SelectedTrack = track;
            SelectedIndex = _tracks.Count - 1;
        }
    }

    // Adds multiple tracks using AddTrack logic
    public void AddTracks(IEnumerable<MediaFile> tracks)
    {
        if (tracks == null)
        {
            throw new ArgumentNullException(nameof(tracks));
        }

        foreach (MediaFile track in tracks)
        {
            AddTrack(track);
        }
    }
}
