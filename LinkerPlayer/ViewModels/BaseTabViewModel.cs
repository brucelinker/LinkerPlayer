using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Services.Playback;
using ManagedBass;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace LinkerPlayer.ViewModels;

/// <summary>
/// Abstract base class for ViewModels that manage tab content (Library or Playlists).
/// Provides shared functionality for playback state, active track management, and common messaging.
/// </summary>
public abstract partial class BaseTabViewModel : ObservableObject
{
    // Shared services
    protected readonly IPlaybackCoordinator _playbackCoordinator;
    protected readonly ILogger _logger;

    // Shared state
    [ObservableProperty]
    private PlaybackState _state;

    [ObservableProperty]
    private MediaFile? _activeTrack;

    public string Name { get; protected set; } = string.Empty;  // or Title
    public ObservableCollection<MediaFile> Tracks { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _loadingStatus = string.Empty;

    public virtual Task ActivateAsync() => Task.CompletedTask;
    public virtual Task DeactivateAsync() => Task.CompletedTask;
    public virtual Task RestoreSelectionAsync() => Task.CompletedTask;

    protected void RegisterMessages()
    {
        RegisterBaseMessages();
        // derived can add more
    }

    protected BaseTabViewModel(
        IPlaybackCoordinator playbackCoordinator,
        ILogger logger)
    {
        _playbackCoordinator = playbackCoordinator ?? throw new ArgumentNullException(nameof(playbackCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Note: Message registration is handled by derived classes
    }

    /// <summary>
    /// Registers common message handlers for playback state and active track changes.
    /// Derived classes should call this from their own RegisterMessages method.
    /// Guards against duplicate registration by unregistering first (important for tests).
    /// </summary>
    protected void RegisterBaseMessages()
    {
        // Unregister first to avoid duplicate registration errors in tests
        WeakReferenceMessenger.Default.UnregisterAll(this);

        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (_, m) =>
        {
            State = m.Value;
            _logger.LogDebug("BaseTabViewModel: PlaybackState changed to {State}", m.Value);
        });

        WeakReferenceMessenger.Default.Register<ActiveTrackChangedMessage>(this, (_, m) =>
        {
            ActiveTrack = m.Value;
            _logger.LogDebug("BaseTabViewModel: ActiveTrack changed to {Track}", m.Value?.Title ?? "null");
            OnActiveTrackChangedInternal(m.Value);
        });
    }

    /// <summary>
    /// Called when the active track changes via message. Derived classes can override to add additional behavior.
    /// </summary>
    protected virtual void OnActiveTrackChangedInternal(MediaFile? track)
    {
        // Default: no additional action
    }

    /// <summary>
    /// Cleans up message registrations when the ViewModel is disposed.
    /// </summary>
    protected virtual void Cleanup()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
