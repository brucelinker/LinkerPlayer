using CommunityToolkit.Mvvm.Messaging.Messages;

namespace LinkerPlayer.Messages;

public class MuteMessage : ValueChangedMessage<bool>
{
    public MuteMessage(bool value) : base(value)
    {
    }
}