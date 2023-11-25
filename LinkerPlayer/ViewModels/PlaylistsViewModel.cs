using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using Newtonsoft.Json;
using Serilog;
using Serilog.Formatting.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LinkerPlayer.ViewModels;

public partial class PlayListsViewModel : ObservableObject
{
    [ObservableProperty]
    private Playlist? _playlist;

    [ObservableProperty]
    private static ObservableCollection<PlaylistTab> _tabList = new();

    public void LoadPlaylists()
    {
        Log.Information("PlaylistsViewModel - LoadPlaylists");

        List<Playlist> playlists = MusicLibrary.GetPlaylists();

        foreach (Playlist p in playlists)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;

            PlaylistTab tab = new PlaylistTab
            {
                Header = p.Name,
                Tracks = LoadPlaylistTracks(p.Name)
            };

            TabList.Add(tab);
        }

        JsonValueFormatter formatter = new();
        foreach (PlaylistTab tab in TabList)
        {
            string json = JsonConvert.SerializeObject(tab.Tracks);
            Log.Information("**TABLIST**");
            Log.Information($"{@tab.Header}");
            Log.Information($"{@json}");
        }
    }

    private ObservableCollection<MediaFile> LoadPlaylistTracks(string playListName)
    {
        Log.Information("MainWindow - LoadPlaylistTracks");

        ObservableCollection<MediaFile> tracks = new();
        List<MediaFile> songs = MusicLibrary.GetSongsFromPlaylist(playListName);

        foreach (MediaFile song in songs)
        {
            tracks.Add(song);
        };

        return tracks;
    }
}