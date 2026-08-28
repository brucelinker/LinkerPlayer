using LinkerPlayer.Core;
using LinkerPlayer.Models;
using System.Collections.ObjectModel;

namespace LinkerPlayer.Tests.Fakes;

public sealed class FakeMusicLibrary : IMusicLibrary
{
    public RangeObservableCollection<MediaFile> MainLibrary { get; } = new();

    public ObservableCollection<Playlist> Playlists { get; } = new ObservableCollection<Playlist>();

    public Func<string?, List<MediaFile>> GetTracksFromPlaylistFunc { get; set; } = _ => new List<MediaFile>();

    public Task<MediaFile?> AddTrackToLibraryAsync(MediaFile mediaFile, bool saveImmediately = true) => Task.FromResult<MediaFile?>(mediaFile);

    public Task AddTracksToLibraryBatchAsync(IEnumerable<MediaFile> mediaFiles) => Task.CompletedTask;

    public Task RemoveTrackFromLibraryAsync(string trackId)
    {
        MediaFile? track = MainLibrary.FirstOrDefault(t => t.Id == trackId);
        if (track != null)
        {
            MainLibrary.Remove(track);
        }
        return Task.CompletedTask;
    }

    public Task RemoveTrackFromPlaylistAsync(string playlistName, string trackId) => Task.CompletedTask;

    public Task<Playlist> AddNewPlaylistAsync(string playlistName) => Task.FromResult(new Playlist { Name = playlistName });

    public Task<bool> AddPlaylistAsync(Playlist newPlaylist) => Task.FromResult(true);

    public Task RemovePlaylistAsync(string playlistName) => Task.CompletedTask;

    public Task AddTracksToPlaylistAsync(IList<string> trackIds, string playlistName, bool saveImmediately = true) => Task.CompletedTask;

    public Task AddTrackToPlaylistAsync(string trackId, string playlistName, bool saveImmediately = true, int position = -1) => Task.CompletedTask;

    public MediaFile? IsTrackInLibrary(MediaFile mediaFile) => mediaFile;

    public List<Playlist> GetPlaylists() => Playlists.ToList();

    public List<string> GetPlaylistsContainingTrack(string trackId)
    {
        return Playlists
            .Where(p => p.TrackIds.Contains(trackId))
            .Select(p => p.Name)
            .ToList();
    }

    public List<MediaFile> GetTracksFromPlaylist(string? playlistName) => GetTracksFromPlaylistFunc(playlistName);

    public Task SaveTracksBatchAsync(IEnumerable<MediaFile> tracks) => Task.CompletedTask;

    public Task SaveToDatabaseAsync() => Task.CompletedTask;

    public void SaveToDatabase()
    {
    }

    public void MarkLibraryDirty()
    {
    }

    public event EventHandler? LibraryLoaded
    {
        add { }
        remove { }
    }
    public Task LoadFromDatabaseAsync() => Task.CompletedTask;

    public Task LoadFullLibraryAsync() => Task.CompletedTask;

    public Task RemoveTracksAsync(IEnumerable<string> trackIds) => Task.CompletedTask;

    public Task CleanOrphanedTracksAsync() => Task.CompletedTask;

    public Task UpdateTracksAsync(IEnumerable<MediaFile> tracks, bool updateMetadata = true, bool updateAnalysis = true) => Task.CompletedTask;

    public Task UpdateRatingAsync(string trackId, double rating) => Task.CompletedTask;

    public Task BackfillEmbeddedCoverAsync() => Task.CompletedTask;
    public Task BackfillMp3VbrAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<int> RemoveTracksFromFolderAsync(string folderPath)
    {
        string normalizedFolder = folderPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            + System.IO.Path.DirectorySeparatorChar;
        List<MediaFile> toRemove = MainLibrary
            .Where(t => t.Path.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (MediaFile track in toRemove)
            MainLibrary.Remove(track);
        return Task.FromResult(toRemove.Count);
    }
}
