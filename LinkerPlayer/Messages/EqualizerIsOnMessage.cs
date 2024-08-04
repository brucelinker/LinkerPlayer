using CommunityToolkit.Mvvm.Messaging.Messages;

namespace LinkerPlayer.Messages;

public class EqualizerIsOnMessage : ValueChangedMessage<bool>
{
    public EqualizerIsOnMessage(bool value) : base(value)
    {
    }
}