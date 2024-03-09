using CommunityToolkit.Mvvm.Messaging.Messages;

namespace LinkerPlayer.Messages;

public class PlaybackStoppedMessage : ValueChangedMessage<bool>
{
    public PlaybackStoppedMessage(bool value) : base(value)
    {
    }
}