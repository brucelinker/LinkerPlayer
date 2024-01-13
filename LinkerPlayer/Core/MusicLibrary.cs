using LinkerPlayer.Models;
using MahApps.Metro.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace LinkerPlayer.Core;

public class MusicLibrary
{
    private static readonly string JsonFilePath;
    private static List<MediaFile?> _mainLibrary = new();
    private static List<Playlist?> _playlists = new();

    static MusicLibrary()
    {
        JsonFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LinkerPlayer", "music_library.json");

        LoadFromJson();
    }

    private static void LoadFromJson()
    {
        if (File.Exists(JsonFilePath))
        {
            string json = File.ReadAllText(JsonFilePath);
            JsonSerializerSettings settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            Dictionary<string, object>? data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json, settings);

            if (data != null)
            {
                _mainLibrary = ((JArray)data["songs"]).ToObject<List<MediaFile>>();
                _playlists = ((JArray)data["playlists"]).ToObject<List<Playlist>>();

                if (_playlists == null || !_playlists.Any())
                {
                    _playlists!.Add(new Playlist
                    {
                        Name = "New Playlist",
                        SongIds = new ObservableCollection<string>()
                    });

                    SaveToJson();
                }
                Log.Information("Data loaded from json");
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(JsonFilePath) ?? string.Empty);
            File.Create(JsonFilePath).Close();

            Log.Information("Empty json file created");
        }
    }

    private static void SaveToJson()
    {
        if (_mainLibrary != null)
        {
            if (_playlists != null)
            {
                Dictionary<string, object> data = new Dictionary<string, object>
                {
                    { "songs", JArray.FromObject(_mainLibrary) },
                    { "playlists", JArray.FromObject(_playlists) }
                };

                JsonSerializerSettings settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
                string json = JsonConvert.SerializeObject(data, Formatting.Indented, settings);
                File.WriteAllText(JsonFilePath, json);
            }
        }

        Log.Information("Data saved to json");
    }

    public static bool AddSong(MediaFile mediaFile)
    {
        if (!string.IsNullOrEmpty(mediaFile.Path) && Path.GetExtension(mediaFile.Path).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            mediaFile.UpdateFromFileMetadata(true);
            _mainLibrary?.Add(mediaFile.Clone());
            SaveToJson();

            return true;
        }

        return false;
    }

    public static void RemoveTrackFromPlaylist(string playlistName, string songId)
    {
        int playlistIndex = (int)_playlists?.FindIndex(p => p.Name == playlistName);

        if (_playlists[playlistIndex] != null && _playlists[playlistIndex]!.SongIds!.Contains(songId))
        {
            _playlists[playlistIndex]!.SongIds!.Remove(songId);
            SaveToJson();
        }

        Log.Information($"Song with id {songId} removed");
    }

    public static bool AddPlaylist(Playlist newPlaylist)
    {
        Log.Information("MusicLibrary - AddPlaylist");

        if (_playlists?.Find(p => p.Name == newPlaylist.Name) == null)
        {
            _playlists?.Add(newPlaylist);
            SaveToJson();

            Log.Information($"New playlist \'{newPlaylist.Name}\' added");

            return true;
        }

        return false;
    }

    public static void RemovePlaylist(string? playlistName)
    {
        _playlists?.RemoveAll(p => p.Name == playlistName);
        SaveToJson();

        Log.Information($"Playlist \'{playlistName}\' removed");
    }

    public static void AddSongToPlaylist(string songId, string? playlistName, int position = -1)
    {
        Log.Information("MusicLibrary - AddSongToPlaylist");

        Playlist? playlist = _playlists?.Find(p => p.Name == playlistName);

        if (playlist != null)
        {
            if (!playlist.SongIds!.Contains(songId))
            {
                if (position == -1)
                {
                    playlist.SongIds.Add(songId);
                }
                else
                {
                    if (position >= 0 && position <= playlist.SongIds.Count)
                    {
                        playlist.SongIds.Insert(position, songId);
                    }
                }

                SaveToJson();
            }
        }
    }

    public static void RemoveSongFromPlaylist(string songId, string? playlistName)
    {
        Log.Information("MusicLibrary - RemoveSongFromPlaylist");

        Playlist? playlist = _playlists?.Find(p => p.Name == playlistName);

        if (playlist != null)
        {
            playlist.SongIds.Remove(songId);

            Log.Information($"Song with id {songId} removed from playlist \'{playlistName}\'");

            SaveToJson();
        }
    }

    // ReSharper disable once UnusedMember.Global
    public static void MoveSongToPlaylist(string songId, string fromPlaylist, string toPlaylist)
    {
        Log.Information("MusicLibrary - AddPlaylist");

        Playlist? from = _playlists?.Find(p => p.Name == fromPlaylist);
        Playlist? to = _playlists?.Find(p => p.Name == toPlaylist);

        if (from != null && to != null)
        {
            from.SongIds.Remove(songId);
            to.SongIds.Add(songId);

            Log.Information($"Song with id {songId} moved from \'{fromPlaylist}\' to \'{toPlaylist}\'");

            SaveToJson();
        }
    }

    public static bool RenameSong(string songId, string newName)
    {
        MediaFile? song = _mainLibrary?.Find(s => s.Id == songId);

        if (song != null && !string.IsNullOrEmpty(newName))
        {
            Log.Information($"Song with id {songId} has been renamed");

            song.Title = newName;
            SaveToJson();

            return true;
        }

        return false;
    }

    public static bool RenamePlaylist(string oldName, string? newName)
    {
        Playlist? playlist = _playlists?.Find(s => s.Name == oldName);

        if (playlist != null && !string.IsNullOrEmpty(newName) && _playlists?.Find(s => s.Name == newName) == null)
        {
            Log.Information($"Playlist with name {oldName} has been renamed to {newName}");

            playlist.Name = newName;
            SaveToJson();

            return true;
        }

        return false;
    }

    // ReSharper disable once UnusedMember.Global
    public static List<MediaFile> GetSongs()
    {
        Log.Information("MusicLibrary - GetSongs");

        List<MediaFile> songs = new List<MediaFile>();

        if (_mainLibrary != null)
            foreach (MediaFile song in _mainLibrary)
            {
                songs.Add(song);
            }

        return songs;
    }

    public static List<Playlist> GetPlaylists()
    {
        Log.Information("MusicLibrary - GetPlaylists");

        List<Playlist> playlists = new List<Playlist>();

        if (_playlists != null && _playlists.Any())
        {
            foreach (Playlist playlist in _playlists)
            {
                playlists.Add(playlist);
            }
        }
        else
        {
            playlists.Add(new Playlist
            {
                Name = "NewPlaylist",
                SongIds = new ObservableCollection<string>()
            });
        }

        return playlists;
    }

    public static Playlist? GetPlaylistByName(string name)
    {
        //Log.Information("MusicLibrary - GetPlaylistByName");
        if (!_playlists.Any()) return null;

        foreach (Playlist? playlist in _playlists)
        {
            if (playlist!.Name == name)
            {
                return playlist;
            }
        }

        return null;
    }

    public static List<MediaFile> GetSongsFromPlaylist(string? playlistName)
    {
        Log.Information("MusicLibrary - GetSongsFromPlaylist");

        Playlist? playlist = _playlists?.Find(p => p.Name == playlistName);
        List<MediaFile> songsFromPlaylist = new List<MediaFile>();

        if (playlist != null && playlist.SongIds != null)
        {
            foreach (string songId in playlist.SongIds)
            {
                songsFromPlaylist.Add(_mainLibrary?.Find(p => p.Id == songId)!);
            }
        }

        return songsFromPlaylist;
    }

    public static Playlist CreatePlaylist(string playlistName)
    {
        foreach (Playlist pl in _playlists)
        {
            if (pl.Name == playlistName)
            {
                return pl;
            }
        }

        Playlist playlist = new Playlist
        {
            Name = playlistName,
            SongIds = new()
        };

        AddPlaylist(playlist);

        return playlist;
    }
}