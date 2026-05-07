using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace LinkerPlayer.Models;

/// <summary>
/// Common interface for all tab types (PlaylistTab, MusicLibraryTab)
/// </summary>
public interface ITabData
{
    string Name { get; }
    ObservableCollection<MediaFile> Tracks { get; }
    MediaFile? SelectedTrack { get; set; }
    int? SelectedIndex { get; set; }
}

public partial class PlaylistTab : ObservableObject, ITabData
{
    [ObservableProperty] private string _name = "New Playlist";
    private readonly ObservableCollection<MediaFile> _tracks = new();
    public ObservableCollection<MediaFile> Tracks => _tracks;
    [ObservableProperty] private MediaFile? _selectedTrack;
    [ObservableProperty] private int? _selectedIndex;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _loadingProgress;
    [ObservableProperty] private int _loadingTotal;
    [ObservableProperty] private string _loadingStatus = string.Empty;

    // Implements ITabData.Name via generated property
    string ITabData.Name => Name;

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
