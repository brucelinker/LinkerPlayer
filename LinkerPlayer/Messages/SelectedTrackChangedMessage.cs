using CommunityToolkit.Mvvm.Messaging.Messages;
using LinkerPlayer.Models;

namespace LinkerPlayer.Messages;

public class SelectedTrackChangedMessage : ValueChangedMessage<MediaFile?>
{
    public SelectedTrackChangedMessage(MediaFile? value) : base(value)
    {
    }
}