using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace LinkerPlayer.ViewModels;

public partial class PlaylistTabsViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _selectedPlaylistName;

    [ObservableProperty]
    private static MediaFile? _selectedTrack;

    [ObservableProperty]
    private static int _selectedIndex;

    [ObservableProperty]
    private static PlaylistTab? _selectedPlaylistTab;

    [ObservableProperty]
    private static PlayerState _state;

    [ObservableProperty]
    private static bool _shuffleMode;

    public static ObservableCollection<PlaylistTab> TabList { get; set; } = new();

    private static DataGrid? _dataGrid;

    private readonly List<MediaFile?> _shuffleList = new();

    private int _shuffledIndex = -1;

    public PlaylistTabsViewModel()
    {
        WeakReferenceMessenger.Default.Register<PlayerStateMessage>(this, (_, m) =>
        {
            OnPlayerStateChanged(m.Value);
        });
    }

    public void OnDataGridLoaded(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is DataGrid dataGrid)
        {
            _dataGrid = dataGrid;

            _dataGrid.SelectedItem = SelectedTrack;
            _dataGrid!.SelectedIndex = SelectedIndex;
        }
    }

    public void OnTabSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (sender is TabControl tabControl)
        {
            TabItem? tabItem = tabControl.SelectedItem as TabItem;


            SelectedPlaylistTab = (tabControl.SelectedContent as PlaylistTab);


        }
    }

    public void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _dataGrid = (sender as DataGrid);

        if (_dataGrid != null)
        {
            SelectedTrack = _dataGrid.SelectedItem as MediaFile;
            SelectedIndex = _dataGrid.SelectedIndex;
        }

        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));
    }

    private void OnPlayerStateChanged(PlayerState state)
    {
        State = state;
        if (SelectedTrack != null) SelectedTrack.State = state;
    }

    public MediaFile SelectTrack(MediaFile track)
    {
        SelectedTrack = track;

        SelectedIndex = SelectedPlaylistTab!.Tracks!.ToList()
            .FindIndex(x => x.Id.Contains(track.Id));

        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));

        return SelectedTrack;
    }

    public MediaFile SelectFirstTrack()
    {
        SelectedIndex = 0;
        SelectedTrack = SelectedPlaylistTab!.Tracks![SelectedIndex];


        _dataGrid!.SelectedItem = SelectedTrack;
        _dataGrid!.SelectedIndex = SelectedIndex;

        WeakReferenceMessenger.Default.Send(new SelectedTrackChangedMessage(SelectedTrack));

        return SelectedTrack;
    }

    public MediaFile? PreviousMediaFile()
    {
        if (SelectedTrack != null && _dataGrid != null)
        {
            int newIndex;

            SelectedTrack.State = PlayerState.Stopped;

            if (_shuffleMode)
            {
                newIndex = GetPreviousShuffledIndex();
            }
            else
            {
                if (SelectedIndex == 0)
                    newIndex = SelectedPlaylistTab!.Tracks!.Count - 1;
                else
                    newIndex = SelectedIndex - 1;
            }

            _dataGrid.SelectedIndex = newIndex;
            SelectedTrack = SelectedPlaylistTab!.Tracks![newIndex];

            _dataGrid.SelectedItem = SelectedTrack;
            _dataGrid.ScrollIntoView(_dataGrid.SelectedItem);
            SelectedTrack.State = PlayerState.Playing;
        }

        return SelectedTrack;
    }

    public MediaFile? NextMediaFile()
    {
        if (SelectedTrack != null && _dataGrid != null)
        {
            int newIndex;

            SelectedTrack.State = PlayerState.Stopped;

            if (_shuffleMode)
            {
                newIndex = GetNextShuffledIndex();
            }
            else
            {
                if (SelectedIndex == SelectedPlaylistTab!.Tracks!.Count - 1)
                    newIndex = 0;
                else
                    newIndex = SelectedIndex + 1;
            }

            _dataGrid.SelectedIndex = newIndex;
            SelectedTrack = SelectedPlaylistTab!.Tracks![newIndex];

            _dataGrid.SelectedItem = SelectedTrack;
            _dataGrid.ScrollIntoView(_dataGrid.SelectedItem);
            SelectedTrack.State = PlayerState.Playing;
        }

        return SelectedTrack;
    }

    private int GetNextShuffledIndex()
    {
        if (_shuffledIndex == _shuffleList.Count - 1)
            _shuffledIndex = 0;
        else
            _shuffledIndex += 1;

        int newIndex = SelectedPlaylistTab!.Tracks!.ToList()
            .FindIndex(x => x.Id.Contains(_shuffleList[_shuffledIndex]!.Id));

        return newIndex;
    }

    private int GetPreviousShuffledIndex()
    {
        if (_shuffledIndex == 0)
            _shuffledIndex = _shuffleList.Count - 1;
        else
            _shuffledIndex -= 1;

        int newIndex = SelectedPlaylistTab!.Tracks!.ToList()
            .FindIndex(x => x.Id.Contains(_shuffleList[_shuffledIndex]!.Id));

        return newIndex;
    }

    public static void LoadPlaylists()
    {
        Log.Information("PlaylistsViewModel - LoadPlaylists");

        List<Playlist> playlists = MusicLibrary.GetPlaylists();

        foreach (Playlist p in playlists)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;

            PlaylistTab tab = AddPlaylistTab(p);

            Log.Information($"LoadPlaylists - added PlaylistTab {tab}");
        }
    }

    public static PlaylistTab AddPlaylistTab(Playlist p)
    {
        PlaylistTab tab = new PlaylistTab
        {
            Header = p.Name,
            Tracks = LoadPlaylistTracks(p.Name)
        };

        TabList.Add(tab);
        return tab;
    }

    public static void AddSongToPlaylistTab(MediaFile song, string playlistName)
    {
        Log.Information("MainWindow - LoadPlaylistTracks");

        foreach (PlaylistTab tab in TabList)
        {
            if (tab.Header == playlistName)
            {
                tab.Tracks!.Add(song);
            }
        }
    }

    public void ShuffleTracks(bool shuffleMode)
    {
        _shuffleMode = shuffleMode;

        if (shuffleMode)
        {
            if (SelectedPlaylistTab != null && SelectedPlaylistTab!.Tracks!.Any())
            {
                List<MediaFile> tempList = SelectedPlaylistTab!.Tracks!.ToList();

                Random random = new Random();

                while (tempList.Count > 0)
                {
                    int index = random.Next(0, tempList.Count - 1);

                    _shuffleList.Add(tempList[index]);
                    tempList.RemoveAt(index);
                }
            }
        }
        else
        {
            _shuffleList.Clear();
        }
    }

    private static ObservableCollection<MediaFile> LoadPlaylistTracks(string? playListName)
    {
        Log.Information("MainWindow - LoadPlaylistTracks");

        ObservableCollection<MediaFile> tracks = new();
        List<MediaFile> songs = MusicLibrary.GetSongsFromPlaylist(playListName);

        foreach (MediaFile song in songs)
        {
            tracks.Add(song);
        }

        return tracks;
    }
}