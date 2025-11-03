using CommunityToolkit.Mvvm.Messaging.Messages;

namespace LinkerPlayer.Messages;

public class IsMutedMessage : ValueChangedMessage<bool>
{
    public IsMutedMessage(bool value) : base(value)
    {
    }
}
