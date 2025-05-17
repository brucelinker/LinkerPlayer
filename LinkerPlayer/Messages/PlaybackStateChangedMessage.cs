using CommunityToolkit.Mvvm.Messaging.Messages;
using ManagedBass;

namespace LinkerPlayer.Messages;

public class PlaybackStateChangedMessage : ValueChangedMessage<PlaybackState>
{
    public PlaybackStateChangedMessage(PlaybackState value) : base(value)
    {
    }
}