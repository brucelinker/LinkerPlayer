using CommunityToolkit.Mvvm.Messaging.Messages;

namespace LinkerPlayer.Messages;

public class ResetSlidersMessage : ValueChangedMessage<double>
{
    public ResetSlidersMessage(double value = 0.0) : base(value)
    {
    }
}