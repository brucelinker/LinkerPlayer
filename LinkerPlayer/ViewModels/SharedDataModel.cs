using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel; // for INotifyPropertyChanged
using System.Windows;

namespace LinkerPlayer.ViewModels;

public interface ISharedDataModel : INotifyPropertyChanged
{
    int SelectedTrackIndex { get; }
    MediaFile? SelectedTrack { get; }
    MediaFile? ActiveTrack { get; }
    ReadOnlyObservableCollection<MediaFile> SelectedTracks { get; }
    event NotifyCollectionChangedEventHandler SelectedTracksChanged;
    void UpdateSelectedTrackIndex(int newIndex);
    void UpdateSelectedTrack(MediaFile track);
    void UpdateActiveTrack(MediaFile track);
    void UpdateSelectedTracks(IEnumerable<MediaFile> tracks);
}

public partial class SharedDataModel : ObservableRecipient, ISharedDataModel
{
    [ObservableProperty] private int _selectedTrackIndex;
    [ObservableProperty] private MediaFile? _selectedTrack;
    [ObservableProperty] private MediaFile? _activeTrack;

    private readonly ObservableCollection<MediaFile> _selectedTracksMutable = new();
    private readonly ReadOnlyObservableCollection<MediaFile> _selectedTracks;
    public ReadOnlyObservableCollection<MediaFile> SelectedTracks => _selectedTracks;
    public event NotifyCollectionChangedEventHandler? SelectedTracksChanged;

    public SharedDataModel()
    {
        _selectedTracks = new ReadOnlyObservableCollection<MediaFile>(_selectedTracksMutable);
        _selectedTracksMutable.CollectionChanged += (s, e) => SelectedTracksChanged?.Invoke(this, e);
    }

    private static void SafeUiInvoke(Action action)
    {
        if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Application.Current.Dispatcher.Invoke(action);
        }
    }

    public void UpdateSelectedTrackIndex(int newIndex) => SafeUiInvoke(() => SelectedTrackIndex = newIndex);
    public void UpdateSelectedTrack(MediaFile track) => SafeUiInvoke(() => SelectedTrack = track);
    public void UpdateActiveTrack(MediaFile track) => SafeUiInvoke(() => ActiveTrack = track);
    public void UpdateSelectedTracks(IEnumerable<MediaFile> tracks) => SafeUiInvoke(() =>
    {
        _selectedTracksMutable.Clear();
        foreach (MediaFile track in tracks)
        {
            _selectedTracksMutable.Add(track);
        }
    });
}
