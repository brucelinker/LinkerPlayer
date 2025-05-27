using CommunityToolkit.Mvvm.Messaging.Messages;
using LinkerPlayer.Models;

namespace LinkerPlayer.Messages;

public class AnimateSlidersMessage : ValueChangedMessage<Preset>
{
    public AnimateSlidersMessage(Preset value) : base(value)
    {
    }
}