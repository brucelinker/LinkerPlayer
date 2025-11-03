using ManagedBass;
using Microsoft.Extensions.Logging;

namespace LinkerPlayer.Audio;

public partial class AudioEngine
{
    private bool StartDirectSoundPlayback()
    {
        _logger.LogInformation("Starting DirectSound playback");
        bool success = Bass.ChannelPlay(CurrentStream);
        if (!success && Bass.LastError == Errors.Busy)
        {
            _logger.LogError("Failed to start DirectSound playback - device is busy");
            MarkDeviceBusyAndNotify("Playback cannot start.");
            return false;
        }
        return success;
    }

    private void PauseDirectSound()
    {
        if (!Bass.ChannelPause(CurrentStream))
        {
            _logger.LogError($"Failed to pause stream: {Bass.LastError}");
        }
    }

    private bool ResumeDirectSound()
    {
        if (!Bass.ChannelPlay(CurrentStream))
        {
            _logger.LogError($"Failed to resume stream: {Bass.LastError}");
            return false;
        }
        return true;
    }

    private bool SeekDirectSound(double position)
    {
        PlaybackState state = Bass.ChannelIsActive(CurrentStream);
        bool wasPlaying = state == PlaybackState.Playing;
        if (wasPlaying)
        {
            Bass.ChannelPause(CurrentStream);
        }

        long bytePosition = Bass.ChannelSeconds2Bytes(CurrentStream, position);
        if (bytePosition < 0)
        {
            _logger.LogError($"Failed to convert position {position} to bytes: {Bass.LastError}");
            return false;
        }

        if (!Bass.ChannelSetPosition(CurrentStream, bytePosition))
        {
            _logger.LogError($"Failed to seek to position {position}: {Bass.LastError}");
            return false;
        }

        if (wasPlaying)
        {
            Bass.ChannelPlay(CurrentStream);
        }

        double actualPosition = Bass.ChannelBytes2Seconds(CurrentStream, Bass.ChannelGetPosition(CurrentStream));
        if (!double.IsNaN(actualPosition) && actualPosition >= 0)
        {
            CurrentTrackPosition = actualPosition;
        }
        return true;
    }
}
