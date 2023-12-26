using CommunityToolkit.Mvvm.Messaging.Messages;
using LinkerPlayer.Models;

namespace LinkerPlayer.Messages;

public class PlaylistSelectionChangedMessage : ValueChangedMessage<MediaFile?>
{
    public PlaylistSelectionChangedMessage(MediaFile? value) : base(value)
    {
    }
}