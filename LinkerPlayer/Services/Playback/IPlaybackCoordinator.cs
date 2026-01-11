using LinkerPlayer.Models;
using ManagedBass;

namespace LinkerPlayer.Services.Playback;

public interface IPlaybackCoordinator
{
    PlaybackState PlaybackState { get; }
    PlaybackCursor? PlaybackCursor { get; }

    void SetUserSelection(string playlistName, int trackIndex, MediaFile track);

    void PlaySelected();
    void PlayTrack(string playlistName, int trackIndex, MediaFile track, double positionSeconds = 0);
    void Pause();
    void Resume();
    void Stop();
    void Seek(double positionSeconds);
    void Next();
    void Prev();
}
