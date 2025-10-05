using LinkerPlayer.Models;
using System.Collections.ObjectModel;

namespace LinkerPlayer.Tests.Helpers;

public static class TestDataHelper
{
    /// <summary>
    /// Creates a MediaFile for testing - now safe because CoverManager is lazy-initialized
    /// </summary>
    public static MediaFile CreateTestMediaFile(string id = "test-id", string title = "Test Song", string artist = "Test Artist")
    {
        return new MediaFile
        {
            Id = id,
            Title = title,
            Artist = artist,
            Album = "Test Album",
            Path = $"C:\\Music\\{title}.mp3",
            FileName = $"{title}.mp3",
            Duration = TimeSpan.FromMinutes(3),
            Track = 1,
            Year = 2023,
            Bitrate = 320,
            SampleRate = 44100,
            Channels = 2
        };
    }

    public static List<MediaFile> CreateTestMediaFiles(int count = 3)
    {
        List<MediaFile> files = new List<MediaFile>();
        for (int i = 1; i <= count; i++)
        {
            files.Add(CreateTestMediaFile($"id-{i}", $"Song {i}", $"Artist {i}"));
        }
        return files;
    }

    public static Playlist CreateTestPlaylist(string name = "Test Playlist", params string[] trackIds)
    {
        Playlist playlist = new Playlist
        {
            Name = name,
            TrackIds = new ObservableCollection<string>(trackIds)
        };

        if (trackIds.Length > 0)
        {
            playlist.SelectedTrackId = trackIds[0];
        }

        return playlist;
    }

    public static PlaylistTab CreateTestPlaylistTab(string name = "Test Tab", int trackCount = 3)
    {
        List<MediaFile> tracks = CreateTestMediaFiles(trackCount);
        return new PlaylistTab
        {
            Name = name,
            Tracks = new ObservableCollection<MediaFile>(tracks)
        };
    }

    public static ProgressData CreateTestProgressData(bool isProcessing = false, int processed = 0, int total = 100, string status = "")
    {
        return new ProgressData
        {
            IsProcessing = isProcessing,
            ProcessedTracks = processed,
            TotalTracks = total,
            Status = status,
            Phase = isProcessing ? "Testing" : ""
        };
    }
}