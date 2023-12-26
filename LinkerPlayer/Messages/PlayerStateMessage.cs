using CommunityToolkit.Mvvm.Messaging.Messages;
using LinkerPlayer.Models;

namespace LinkerPlayer.Messages;

public class PlayerStateMessage : ValueChangedMessage<PlayerState>
{
    public PlayerStateMessage(PlayerState value) : base(value)
    {
    }
}