using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.ViewModels;

namespace LinkerPlayer.Tests.Mocks;

public sealed class TestSelectionService : ISelectionService
{
    public PlaylistTab? CurrentTab { get; private set; }
    public MediaFile? CurrentTrack { get; private set; }
    public IReadOnlyList<MediaFile> MultiSelection { get; private set; } = Array.Empty<MediaFile>();
    public int CurrentTrackIndex { get; private set; } = -1;

    public event EventHandler<MediaFile?>? TrackChanged;
    public event EventHandler<PlaylistTab?>? TabChanged;
    public event EventHandler<IReadOnlyList<MediaFile>>? MultiSelectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void SetTab(PlaylistTab? tab)
    {
        if (ReferenceEquals(CurrentTab, tab)) return;
        CurrentTab = tab;
        TabChanged?.Invoke(this, tab);
        OnPropertyChanged(nameof(CurrentTab));
    }

    public void SetTrack(MediaFile? track, int index)
    {
        if (ReferenceEquals(CurrentTrack, track) && CurrentTrackIndex == index) return;
        CurrentTrack = track;
        CurrentTrackIndex = index;
        TrackChanged?.Invoke(this, track);
        OnPropertyChanged(nameof(CurrentTrack));
        OnPropertyChanged(nameof(CurrentTrackIndex));
    }

    public void SetMultiSelection(IEnumerable<MediaFile> tracks)
    {
        IReadOnlyList<MediaFile> list = tracks.ToList();
        MultiSelection = list;
        MultiSelectionChanged?.Invoke(this, MultiSelection);
        OnPropertyChanged(nameof(MultiSelection));
    }
}
