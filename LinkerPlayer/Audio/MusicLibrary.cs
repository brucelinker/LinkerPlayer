using LinkerPlayer.Audio.Log;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LinkerPlayer.Audio;

public class Song
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Path { get; set; }
    public TimeSpan Duration { get; set; }

    public Song Clone()
    {
        return new Song
        {
            Id = this.Id,
            Name = this.Name,
            Path = this.Path,
            Duration = this.Duration
        };
    }
}

public class Playlist
{
    public string? Name { get; set; } = string.Empty;
    public List<string> SongIds { get; set; }

    public Playlist()
    {
        SongIds = new List<string>();
    }

    public Playlist Clone()
    {
        return new Playlist
        {
            Name = this.Name,
            SongIds = this.SongIds.ToList()
        };
    }
}

public class MusicLibrary
{
    private static readonly string JsonFilePath;
    private static List<Song>? _songs = new();
    private static List<Playlist>? _playlists = new();
    private static readonly ILog Log = LogSettings.SelectedLog;

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
                _songs = ((JArray)data["songs"]).ToObject<List<Song>>();
                _playlists = ((JArray)data["playlists"]).ToObject<List<Playlist>>();

                Log.Print("Data loaded from json", LogInfoType.Info);
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(JsonFilePath) ?? string.Empty);
            File.Create(JsonFilePath).Close();

            Log.Print("Empty json file created", LogInfoType.Info);
        }
    }

    private static void SaveToJson()
    {
        if (_songs != null)
        {
            if (_playlists != null)
            {
                Dictionary<string, object> data = new Dictionary<string, object>
                {
                    { "songs", JArray.FromObject(_songs) },
                    { "playlists", JArray.FromObject(_playlists) }
                };

                JsonSerializerSettings settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
                string json = JsonConvert.SerializeObject(data, Formatting.Indented, settings);
                File.WriteAllText(JsonFilePath, json);
            }
        }

        Log.Print("Data saved to json", LogInfoType.Info);
    }

    public static bool AddSong(Song song)
    {
        if (File.Exists(song.Path) && Path.GetExtension(song.Path).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            // Generate a unique ID for the song
            song.Id = Guid.NewGuid().ToString();

            TagLib.File? tagFile = TagLib.File.Create(song.Path);

            song.Name = tagFile.Tag.Title ?? Path.GetFileNameWithoutExtension(song.Path);

            song.Duration = tagFile.Properties.Duration;

            _songs?.Add(song.Clone());

            Log.Print($"New song with id {song.Id} added", LogInfoType.Info);

            SaveToJson();

            return true;
        }

        return false;
    }

    //public static async Task ConvertToMp3(string path, string ffmpegDir)
    //{
    //    string newPath = Path.ChangeExtension(path, ".mp3");

    //    if (File.Exists(newPath))
    //    {
    //        File.Delete(newPath);
    //    }

    //    ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(ffmpegDir, "ffmpeg.exe"))
    //    {
    //        UseShellExecute = false,
    //        CreateNoWindow = true,
    //        Arguments = $" -i \"{path}\" -vn -ar 44100 -ac 2 -ab 192k -f mp3 \"{newPath}\""
    //    };

    //    Process process = new Process { StartInfo = psi };

    //    process.Start();

    //    await process.WaitForExitAsync();

    //    process.Dispose();
    //}

    public static void RemoveSong(string songId)
    {
        _songs?.RemoveAll(s => s.Id == songId);

        if (_playlists != null)
            foreach (Playlist playlist in _playlists)
            {
                playlist.SongIds.Remove(songId);
            }

        Log.Print($"Song with id {songId} removed", LogInfoType.Info);

        SaveToJson();
    }

    public static bool AddPlaylist(Playlist playlist)
    {
        if (_playlists?.Find(p => p.Name == playlist.Name) == null)
        {
            _playlists?.Add(playlist.Clone());

            Log.Print($"New playlist \'{playlist.Name}\' added", LogInfoType.Info);

            SaveToJson();

            return true;
        }

        return false;
    }

    public static void RemovePlaylist(string? playlistName)
    {
        _playlists?.RemoveAll(p => p.Name == playlistName);

        Log.Print($"Playlist \'{playlistName}\' removed", LogInfoType.Info);

        SaveToJson();
    }

    public static void AddSongToPlaylist(string songId, string? playlistName, int position = -1)
    {
        Playlist? playlist = _playlists?.Find(p => p.Name == playlistName);

        if (playlist != null)
        {
            if (!playlist.SongIds.Contains(songId))
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

                Log.Print($"Song with id {songId} added to playlist \'{playlistName}\'", LogInfoType.Info);

                SaveToJson();
            }
        }
    }

    public static void RemoveSongFromPlaylist(string songId, string? playlistName)
    {
        Playlist? playlist = _playlists?.Find(p => p.Name == playlistName);

        if (playlist != null)
        {
            playlist.SongIds.Remove(songId);

            Log.Print($"Song with id {songId} removed from playlist \'{playlistName}\'", LogInfoType.Info);

            SaveToJson();
        }
    }

    // ReSharper disable once UnusedMember.Global
    public static void MoveSongToPlaylist(string songId, string fromPlaylist, string toPlaylist)
    {
        Playlist? from = _playlists?.Find(p => p.Name == fromPlaylist);
        Playlist? to = _playlists?.Find(p => p.Name == toPlaylist);

        if (from != null && to != null)
        {
            from.SongIds.Remove(songId);
            to.SongIds.Add(songId);

            Log.Print($"Song with id {songId} moved from \'{fromPlaylist}\' to \'{toPlaylist}\'", LogInfoType.Info);

            SaveToJson();
        }
    }

    public static bool RenameSong(string songId, string newName)
    {
        Song? song = _songs?.Find(s => s.Id == songId);

        if (song != null && !string.IsNullOrEmpty(newName))
        {
            Log.Print($"Song with id {songId} has been renamed", LogInfoType.Info);

            song.Name = newName;
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
            Log.Print($"Playlist with name {oldName} has been renamed to {newName}", LogInfoType.Info);

            playlist.Name = newName;
            SaveToJson();

            return true;
        }

        return false;
    }

    // ReSharper disable once UnusedMember.Global
    public static List<Song> GetSongs()
    {
        List<Song> songs = new List<Song>();

        if (_songs != null)
            foreach (Song song in _songs)
            {
                songs.Add(song.Clone());
            }

        return songs;
    }

    public static List<Playlist> GetPlaylists()
    {
        List<Playlist> playlists = new List<Playlist>();

        if (_playlists != null)
            foreach (Playlist playlist in _playlists)
            {
                playlists.Add(playlist.Clone());
            }

        return playlists;
    }

    public static List<Song> GetSongsFromPlaylist(string? playlistName)
    {
        Playlist? playlist = _playlists?.Find(p => p.Name == playlistName);
        List<Song> songsFromPlaylist = new List<Song>();

        if (playlist != null)
        {
            foreach (string songId in playlist.SongIds)
            {
                songsFromPlaylist.Add(_songs?.Find(p => p.Id == songId)?.Clone()!);
            }
        }

        return songsFromPlaylist;
    }
}

public class EqualizerLibrary
{
    public static List<BandsSettings>? BandsSettings = new();
    private static readonly string JsonFilePath;
    protected static ILog Log = LogSettings.SelectedLog;

    static EqualizerLibrary()
    {
        JsonFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LinkerPlayer", "bandsSettings.json");
    }

    public static void LoadFromJson()
    {
        if (File.Exists(JsonFilePath))
        {
            string jsonString = File.ReadAllText(JsonFilePath);

            List<BandsSettings>? tempBands = JsonConvert.DeserializeObject<List<BandsSettings>>(jsonString);
            if (tempBands != null)
            {
                BandsSettings = JsonConvert.DeserializeObject<List<BandsSettings>>(jsonString);
            }
            else
            {
                Log.Print("Json is empty", LogInfoType.Warning);
            }

            Log.Print("Load from json", LogInfoType.Info);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(JsonFilePath)!);
            File.Create(JsonFilePath).Close();
        }
    }

    public static void SaveToJson()
    {
        JsonSerializerSettings settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
        string json = JsonConvert.SerializeObject(BandsSettings, Formatting.Indented, settings);
        File.WriteAllText(JsonFilePath, json);

        Log.Print("Save to json", LogInfoType.Info);
    }
}