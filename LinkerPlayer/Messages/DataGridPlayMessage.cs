using CommunityToolkit.Mvvm.Messaging.Messages;
using LinkerPlayer.Models;

namespace LinkerPlayer.Messages;

public class DataGridPlayMessage : ValueChangedMessage<PlayerState>
{
    public DataGridPlayMessage(PlayerState value) : base(value)
    {
    }
}