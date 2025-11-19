using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace LinkerPlayer.Services;

public interface ISelectionService : INotifyPropertyChanged
{
    PlaylistTab? CurrentTab { get; }
    MediaFile? CurrentTrack { get; }
    IReadOnlyList<MediaFile> MultiSelection { get; }
    int CurrentTrackIndex { get; }

    event EventHandler<MediaFile?>? TrackChanged;
    event EventHandler<PlaylistTab?>? TabChanged;
    event EventHandler<IReadOnlyList<MediaFile>>? MultiSelectionChanged;

    void SetTab(PlaylistTab? tab);
    void SetTrack(MediaFile? track, int index);
    void SetMultiSelection(IEnumerable<MediaFile> tracks);
}

public class SelectionService : ISelectionService
{
    private readonly SharedDataModel _shared;
    private PlaylistTab? _currentTab;
    private IReadOnlyList<MediaFile> _multiSelection = Array.Empty<MediaFile>();

    public SelectionService(SharedDataModel shared)
    {
        _shared = shared;
    }

    public PlaylistTab? CurrentTab => _currentTab;
    public MediaFile? CurrentTrack => _shared.SelectedTrack;
    public int CurrentTrackIndex => _shared.SelectedTrackIndex;
    public IReadOnlyList<MediaFile> MultiSelection => _multiSelection;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<MediaFile?>? TrackChanged;
    public event EventHandler<PlaylistTab?>? TabChanged;
    public event EventHandler<IReadOnlyList<MediaFile>>? MultiSelectionChanged;

    protected void OnPropertyChanged(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void SetTab(PlaylistTab? tab)
    {
        if (ReferenceEquals(_currentTab, tab))
        {
            return;
        }
        _currentTab = tab;
        TabChanged?.Invoke(this, _currentTab);
        OnPropertyChanged(nameof(CurrentTab));
    }

    public void SetTrack(MediaFile? track, int index)
    {
        MediaFile? previous = _shared.SelectedTrack;

        if (track != null)
        {
            if (previous != null && ReferenceEquals(previous, track) && _shared.SelectedTrackIndex == index)
            {
                return; // no effective change
            }
            _shared.UpdateSelectedTrack(track);
            _shared.UpdateSelectedTrackIndex(index);
        }
        else
        {
            if (previous == null && _shared.SelectedTrackIndex == -1)
            {
                return; // already null selection
            }
            _shared.UpdateSelectedTrackIndex(-1);
            _shared.UpdateSelectedTrack(null!);
        }

        TrackChanged?.Invoke(this, CurrentTrack);
        OnPropertyChanged(nameof(CurrentTrack));
        OnPropertyChanged(nameof(CurrentTrackIndex));
    }

    public void SetMultiSelection(IEnumerable<MediaFile> tracks)
    {
        IReadOnlyList<MediaFile> newList = tracks.ToList();
        // quick reference equality / count check first
        if (newList.Count == _multiSelection.Count && newList.SequenceEqual(_multiSelection))
        {
            return;
        }
        _multiSelection = newList;
        _shared.UpdateSelectedTracks(newList);
        MultiSelectionChanged?.Invoke(this, _multiSelection);
        OnPropertyChanged(nameof(MultiSelection));
    }
}
