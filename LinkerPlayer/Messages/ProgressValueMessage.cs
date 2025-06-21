using CommunityToolkit.Mvvm.Messaging.Messages;
using LinkerPlayer.Models;

namespace LinkerPlayer.Messages;

public class ProgressValueMessage : ValueChangedMessage<ProgressData>
{
    public ProgressValueMessage(ProgressData value) : base(value) { }
}