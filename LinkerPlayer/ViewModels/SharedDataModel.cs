using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace LinkerPlayer.ViewModels;

public partial class SharedDataModel : ObservableRecipient
{
    [ObservableProperty] private int _selectedTrackIndex;
    [ObservableProperty] private MediaFile? _selectedTrack;
    [ObservableProperty] private MediaFile? _activeTrack;
    [ObservableProperty] private ObservableCollection<MediaFile> _selectedTracks = new();

    public void UpdateSelectedTrackIndex(int newIndex)
    {
        Application.Current.Dispatcher.Invoke(() => SelectedTrackIndex = newIndex);
    }

    public void UpdateSelectedTrack(MediaFile track)
    {
        Application.Current.Dispatcher.Invoke(() => SelectedTrack = track);
    }

    public void UpdateActiveTrack(MediaFile track)
    {
        Application.Current.Dispatcher.Invoke(() => ActiveTrack = track);
    }

    public void UpdateSelectedTracks(IEnumerable<MediaFile> tracks)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            SelectedTracks.Clear();
            foreach (var track in tracks)
            {
                SelectedTracks.Add(track);
            }
        });
    }
}
