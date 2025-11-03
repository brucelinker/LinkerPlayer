using CommunityToolkit.Mvvm.Messaging.Messages;
using LinkerPlayer.Models;

namespace LinkerPlayer.Messages;

public class ActiveTrackChangedMessage : ValueChangedMessage<MediaFile?>
{
    public ActiveTrackChangedMessage(MediaFile? value) : base(value)
    {
    }
}
