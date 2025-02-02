using LinkerPlayer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using NAudio.Wave;

namespace LinkerPlayer.Core;

public abstract class MusicLibrary
{
    private static readonly string JsonFilePath;
    public static List<MediaFile?> MainLibrary = new();
    public static List<Playlist?> Playlists = new();

    private static string[] _supportedAudioExtensions = [".mp3", ".flac", ".wav"];

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
            JsonSerializerSettings settings = new() { TypeNameHandling = TypeNameHandling.Auto };
            Dictionary<string, object>? data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json, settings);

            if (data != null)
            {
                MainLibrary = ((JArray)data["songs"]).ToObject<List<MediaFile>>()!;
                Playlists = ((JArray)data["playlists"]).ToObject<List<Playlist>>()!;

                if (Playlists == null || !Playlists.Any())
                {
                    Playlists!.Add(new Playlist
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

    public static void SaveToJson()
    {
        Dictionary<string, object> data = new()
        {
            { "songs", JArray.FromObject(MainLibrary) },
            { "playlists", JArray.FromObject(Playlists) }
        };

        JsonSerializerSettings settings = new() { TypeNameHandling = TypeNameHandling.Auto };
        string json = JsonConvert.SerializeObject(data, Formatting.Indented, settings);
        File.WriteAllText(JsonFilePath, json);
    }

    public static bool AddSong(MediaFile mediaFile)
    {
        if (!string.IsNullOrEmpty(mediaFile.Path) && 
            _supportedAudioExtensions.Any(s => s.Contains(Path.GetExtension(mediaFile.Path))))
        {
            mediaFile.UpdateFromFileMetadata();
            MainLibrary.Add(mediaFile.Clone());
            //SaveToJson();

            return true;
        }

        return false;
    }

    public static void RemoveTrackFromPlaylist(string playlistName, string songId)
    {
        int playlistIndex = Playlists.FindIndex(p => p!.Name == playlistName);

        if (Playlists[playlistIndex] != null && Playlists[playlistIndex]!.SongIds!.Contains(songId))
        {
            Playlists[playlistIndex]!.SongIds!.Remove(songId);
            //SaveToJson();
        }

        Log.Information($"Song with id {songId} removed");
    }

    public static bool AddPlaylist(Playlist newPlaylist)
    {
        Log.Information("MusicLibrary - AddPlaylist");

        if (Playlists.Find(p => p!.Name == newPlaylist.Name) == null)
        {
            Playlists.Add(newPlaylist);
            SaveToJson();

            Log.Information($"New playlist \'{newPlaylist.Name}\' added");

            return true;
        }

        return false;
    }

    public static Playlist AddNewPlaylist(string playlistName)
    {
        foreach (Playlist? pl in Playlists)
        {
            if (pl!.Name == playlistName)
            {
                return pl;
            }
        }

        Playlist playlist = new()
        {
            Name = playlistName,
            SongIds = new()
        };

        AddPlaylist(playlist);

        return playlist;
    }

    public static void RemovePlaylist(string playlistName)
    {
        Playlist playlist = Playlists.Find(x => x!.Name == playlistName)!;

        if (playlist.SongIds!.Any())
        {
            foreach (var songId in playlist.SongIds!.ToList())
            {
                playlist.SongIds!.ToList().RemoveAll(x => x == songId);
                MainLibrary.RemoveAll(x => x!.Id == songId);
            }
        }

        Playlists.RemoveAll(p => p!.Name == playlistName);
        SaveToJson();

        Log.Information($"Playlist \'{playlistName}\' removed");
    }

    public static void AddSongToPlaylist(string songId, string? playlistName, int position = -1)
    {
        Playlist? playlist = Playlists.Find(p => p!.Name == playlistName);

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

                //SaveToJson();
            }
        }
    }

    // ReSharper disable once UnusedMember.Global
    public static void MoveSongToPlaylist(string songId, string fromPlaylist, string toPlaylist)
    {
        Log.Information("MusicLibrary - AddPlaylist");

        Playlist? from = Playlists.Find(p => p!.Name == fromPlaylist);
        Playlist? to = Playlists.Find(p => p!.Name == toPlaylist);

        if (from != null && to != null)
        {
            from.SongIds!.Remove(songId);
            to.SongIds!.Add(songId);

            Log.Information($"Song with id {songId} moved from \'{fromPlaylist}\' to \'{toPlaylist}\'");

            //SaveToJson();
        }
    }

    public static bool RenameSong(string songId, string newName)
    {
        MediaFile? song = MainLibrary.Find(s => s!.Id == songId);

        if (song != null && !string.IsNullOrEmpty(newName))
        {
            Log.Information($"Song with id {songId} has been renamed");

            song.Title = newName;
            //SaveToJson();

            return true;
        }

        return false;
    }

    public static bool RenamePlaylist(string oldName, string? newName)
    {
        Playlist? playlist = Playlists.Find(s => s!.Name == oldName);

        if (playlist != null && !string.IsNullOrEmpty(newName) && Playlists.Find(s => s!.Name == newName) == null)
        {
            Log.Information($"Playlist with name {oldName} has been renamed to {newName}");

            playlist.Name = newName;
            //SaveToJson();

            return true;
        }

        return false;
    }

    public static List<MediaFile> GetSongs()
    {
        Log.Information("MusicLibrary - GetSongs");

        List<MediaFile> songs = new();

        if (MainLibrary.Any())
        {
            foreach (MediaFile? song in MainLibrary)
            {
                songs.Add(song!);
            }
        }

        return songs;
    }

    public static List<Playlist> GetPlaylists()
    {
        List<Playlist> playlists = new();

        if (Playlists.Any())
        {
            foreach (Playlist? playlist in Playlists)
            {
                playlists.Add(playlist!);
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
        if (!Playlists.Any()) return null;

        foreach (Playlist? playlist in Playlists)
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

        Playlist? playlist = Playlists.Find(p => p!.Name == playlistName);
        List<MediaFile> songsFromPlaylist = new();

        if (playlist is { SongIds: not null })
        {
            foreach (string songId in playlist.SongIds)
            {
                songsFromPlaylist.Add(MainLibrary.Find(p => p!.Id == songId)!);
            }
        }

        return songsFromPlaylist;
    }

    public static void ClearPlayState()
    {
        foreach(MediaFile? file in MainLibrary)
        {
            file!.State = PlaybackState.Stopped;
        }
    }
}