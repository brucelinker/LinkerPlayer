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

    private static void SafeUiInvoke(Action action)
    {
        // Allow execution when no WPF Application/Dispatcher is available (unit tests)
        if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Application.Current.Dispatcher.Invoke(action);
        }
    }

    public void UpdateSelectedTrackIndex(int newIndex)
    {
        SafeUiInvoke(() => SelectedTrackIndex = newIndex);
    }

    public void UpdateSelectedTrack(MediaFile track)
    {
        SafeUiInvoke(() => SelectedTrack = track);
    }

    public void UpdateActiveTrack(MediaFile track)
    {
        SafeUiInvoke(() => ActiveTrack = track);
    }

    public void UpdateSelectedTracks(IEnumerable<MediaFile> tracks)
    {
        SafeUiInvoke(() =>
        {
            SelectedTracks.Clear();
            foreach (MediaFile track in tracks)
            {
                SelectedTracks.Add(track);
            }
        });
    }
}
