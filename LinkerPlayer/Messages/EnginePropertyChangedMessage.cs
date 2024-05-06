using CommunityToolkit.Mvvm.Messaging.Messages;

namespace LinkerPlayer.Messages;

public class EnginePropertyChangedMessage : ValueChangedMessage<string>
{
    public EnginePropertyChangedMessage(string value) : base(value)
    {
    }
}