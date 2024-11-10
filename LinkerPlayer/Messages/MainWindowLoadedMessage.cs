using CommunityToolkit.Mvvm.Messaging.Messages;

namespace LinkerPlayer.Messages;

public class MainWindowLoadedMessage : ValueChangedMessage<bool>
{
    public MainWindowLoadedMessage(bool value) : base(value)
    {
    }
}