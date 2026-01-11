using LinkerPlayer.Models;
using LinkerPlayer.Services.Playback;
using ManagedBass;

namespace LinkerPlayer.Tests.Mocks;

public sealed class TestPlaybackCoordinator : IPlaybackCoordinator
{
    public PlaybackState PlaybackState { get; private set; }

    public PlaybackCursor? PlaybackCursor { get; private set; }

    public void SetUserSelection(string playlistName, int trackIndex, MediaFile track)
    {
    }

    public void PlaySelected()
    {
    }

    public void PlayTrack(string playlistName, int trackIndex, MediaFile track, double positionSeconds = 0)
    {
    }

    public void Pause()
    {
        PlaybackState = PlaybackState.Paused;
    }

    public void Resume()
    {
        PlaybackState = PlaybackState.Playing;
    }

    public void Stop()
    {
        PlaybackState = PlaybackState.Stopped;
        PlaybackCursor = null;
    }

    public void Seek(double positionSeconds)
    {
    }

    public void Next()
    {
    }

    public void Prev()
    {
    }
}
