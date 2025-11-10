using CommunityToolkit.Mvvm.Messaging.Messages;

namespace LinkerPlayer.Messages;

public class GoToActiveTrackMessage : ValueChangedMessage<bool>
{
    public GoToActiveTrackMessage(bool value) : base(value)
    {
    }
}
