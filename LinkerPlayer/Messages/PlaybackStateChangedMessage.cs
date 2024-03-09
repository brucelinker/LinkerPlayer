using CommunityToolkit.Mvvm.Messaging.Messages;
using LinkerPlayer.Models;
using NAudio.Wave;

namespace LinkerPlayer.Messages;

public class PlaybackStateChangedMessage : ValueChangedMessage<PlaybackState>
{
    public PlaybackStateChangedMessage(PlaybackState value) : base(value)
    {
    }
}