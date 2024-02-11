using CommunityToolkit.Mvvm.Messaging.Messages;
using LinkerPlayer.Models;

namespace LinkerPlayer.Messages;

public class PlayerControlsStateMessage : ValueChangedMessage<PlayerState>
{
    public PlayerControlsStateMessage(PlayerState value) : base(value)
    {
    }
}