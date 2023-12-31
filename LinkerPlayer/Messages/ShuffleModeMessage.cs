using CommunityToolkit.Mvvm.Messaging.Messages;

namespace LinkerPlayer.Messages;

public class ShuffleModeMessage : ValueChangedMessage<bool>
{
    public ShuffleModeMessage(bool value) : base(value)
    {
    }
}