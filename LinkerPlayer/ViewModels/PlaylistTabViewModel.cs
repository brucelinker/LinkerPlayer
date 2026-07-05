using CommunityToolkit.Mvvm.ComponentModel;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.Services.Playback;
using Microsoft.Extensions.Logging;

namespace LinkerPlayer.ViewModels;

/// <summary>
/// ViewModel for a single Playlist tab. Symmetric to LibraryTabViewModel.
/// Owns its tracks, selection, and dirty state.
/// </summary>
public partial class PlaylistTabViewModel : BaseTabViewModel
{
    private readonly IPlaylistManagerService _playlistManagerService;
    private readonly ISelectionService _selectionService;

    [ObservableProperty]
    private PlaylistTab? _playlistTab;

    [ObservableProperty]
    private MediaFile? _selectedTrack;

    [ObservableProperty]
    private int _selectedTrackIndex = -1;

    [ObservableProperty]
    private bool _isDirty;

    public PlaylistTabViewModel(
        IPlaylistManagerService playlistManagerService,
        ISelectionService selectionService,
        IPlaybackCoordinator playbackCoordinator,
        ILogger<PlaylistTabViewModel> logger)
        : base(playbackCoordinator, logger)
    {
        _playlistManagerService = playlistManagerService ?? throw new ArgumentNullException(nameof(playlistManagerService));
        _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));

        RegisterMessages();
        _logger.LogDebug("PlaylistTabViewModel created");
    }

    public void InitializeFromPlaylist(Playlist playlist)
    {
        PlaylistTab = new PlaylistTab { Name = playlist.Name };
        Name = playlist.Name;
        // Tracks loaded lazily later
    }

    public override async Task ActivateAsync()
    {
        await RestoreSelectionAsync();
    }

    public override Task RestoreSelectionAsync()
    {
        // TODO: Implement based on your existing RestorePlaylistSelection logic
        // Example:
        // if (PlaylistTab == null) return Task.CompletedTask;
        // string? savedId = GetSavedTrackIdForPlaylist(PlaylistTab.Name);
        // ... find and set SelectedTrack ...

        return Task.CompletedTask;
    }
}
