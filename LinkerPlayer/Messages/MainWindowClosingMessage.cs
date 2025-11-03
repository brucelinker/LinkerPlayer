using CommunityToolkit.Mvvm.Messaging.Messages;

namespace LinkerPlayer.Messages;

public class MainWindowClosingMessage : ValueChangedMessage<bool>
{
    public MainWindowClosingMessage(bool value) : base(value)
    {
    }
}
