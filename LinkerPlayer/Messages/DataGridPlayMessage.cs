using CommunityToolkit.Mvvm.Messaging.Messages;
using ManagedBass;

namespace LinkerPlayer.Messages;

public class DataGridPlayMessage : ValueChangedMessage<PlaybackState>
{
    public DataGridPlayMessage(PlaybackState value) : base(value)
    {
    }
}
