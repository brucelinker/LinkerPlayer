using LinkerPlayer.Models;
using ManagedBass;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace LinkerPlayer.Core;

public abstract class MusicLibrary
{
    private static readonly string JsonFilePath;
    public static List<MediaFile?> MainLibrary = new();
    public static List<Playlist?> Playlists = new();

    private static readonly string[] SupportedAudioExtensions = [".mp3", ".flac", ".wav"];

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
                MainLibrary = ((JArray)data["tracks"]).ToObject<List<MediaFile>>()!;
                Playlists = ((JArray)data["playlists"]).ToObject<List<Playlist>>()!;

                ClearPlayState();

                RemoveDuplicatesFromMainLibrary();

                if (Playlists == null || !Playlists.Any())
                {
                    Playlists!.Add(new Playlist
                    {
                        Name = "New Playlist",
                        TrackIds = new ObservableCollection<string>()
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
        ClearPlayState();

        Dictionary<string, object> data = new()
        {
            { "tracks", JArray.FromObject(MainLibrary) },
            { "playlists", JArray.FromObject(Playlists) }
        };

        JsonSerializerSettings settings = new() { TypeNameHandling = TypeNameHandling.Auto };
        string json = JsonConvert.SerializeObject(data, Formatting.Indented, settings);
        File.WriteAllText(JsonFilePath, json);

        Log.Information("Data saved to json");
    }

    private static void RemoveDuplicatesFromMainLibrary()
    {
        List<MediaFile?> duplicates = MainLibrary
            .GroupBy(item => new { item!.Path, item.Album, item.Duration })
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToList();

        int count = 0;
        foreach (MediaFile? item in duplicates)
        {
            Log.Information($"Duplicate: {item!.Path}, {item.Album}, {item.Duration}");
            ++count;

            MainLibrary.Remove(item);
        }

        Log.Information($"Found {count} duplicates");
    }

    public static MediaFile? IsTrackAlreadyInLibrary(MediaFile mediaFile)
    {
        MediaFile? mFile = MainLibrary.Find(s =>
            s!.Path == mediaFile.Path &&
            s.Album == mediaFile.Album &&
            s.Duration == mediaFile.Duration);

        return mFile;
    }

    public static MediaFile? AddTrackToLibrary(MediaFile mediaFile)
    {
        MediaFile? file = IsTrackAlreadyInLibrary(mediaFile);
        
        if(file != null)
        {
            return file;
        }

        if (!string.IsNullOrEmpty(mediaFile.Path) && 
            SupportedAudioExtensions.Any(s => s.Contains(Path.GetExtension(mediaFile.Path))))
        {
            mediaFile.UpdateFromFileMetadata();
            MainLibrary.Add(mediaFile.Clone());

            return mediaFile;
        }

        return null;
    }

    public static void RemoveTrackFromPlaylist(string playlistName, string trackId)
    {
        int playlistIndex = Playlists.FindIndex(p => p!.Name == playlistName);

        if (Playlists[playlistIndex] != null && Playlists[playlistIndex]!.TrackIds.Contains(trackId))
        {
            Playlists[playlistIndex]!.TrackIds.Remove(trackId);
        }

        Log.Information($"Track with id {trackId} removed");
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
            TrackIds = new()
        };

        AddPlaylist(playlist);

        return playlist;
    }

    public static void RemovePlaylist(string playlistName)
    {
        Playlist playlist = Playlists.Find(x => x!.Name == playlistName)!;

        if (playlist.TrackIds.Any())
        {
            foreach (string trackId in playlist.TrackIds.ToList())
            {
                playlist.TrackIds.ToList().RemoveAll(x => x == trackId);
                MainLibrary.RemoveAll(x => x!.Id == trackId);
            }
        }

        Playlists.RemoveAll(p => p!.Name == playlistName);
        SaveToJson();

        Log.Information($"Playlist \'{playlistName}\' removed");
    }

    public static void AddTrackToPlaylist(string trackId, string? playlistName, int position = -1)
    {
        Playlist? playlist = Playlists.Find(p => p!.Name == playlistName);

        if (playlist != null)
        {
            if (!playlist.TrackIds.Contains(trackId))
            {
                if (position == -1)
                {
                    playlist.TrackIds.Add(trackId);
                }
                else
                {
                    if (position >= 0 && position <= playlist.TrackIds.Count)
                    {
                        playlist.TrackIds.Insert(position, trackId);
                    }
                }
            }
        }
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
                TrackIds = new ObservableCollection<string>()
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

    public static List<MediaFile> GetTracksFromPlaylist(string? playlistName)
    {
        Log.Information("MusicLibrary - GetTracksFromPlaylist");

        Playlist? playlist = Playlists.Find(p => p!.Name == playlistName);
        List<MediaFile> tracksFromPlaylist = new();

        if (playlist is not null)
        {
            foreach (string trackId in playlist.TrackIds)
            {
                tracksFromPlaylist.Add(MainLibrary.Find(p => p!.Id == trackId)!);
            }
        }

        return tracksFromPlaylist;
    }

    public static void ClearPlayState()
    {
        foreach(MediaFile? file in MainLibrary)
        {
            file!.State = PlaybackState.Stopped;
        }
    }
}