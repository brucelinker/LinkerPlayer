using CommunityToolkit.Mvvm.Messaging.Messages;
using LinkerPlayer.Models;

namespace LinkerPlayer.Messages;

public class BeginEditTabMessage : ValueChangedMessage<PlaylistTab>
{
    public BeginEditTabMessage(PlaylistTab value) : base(value)
    {
    }
}
