using CommunityToolkit.Mvvm.Messaging.Messages;
using LinkerPlayer.Models;
using ManagedBass;

namespace LinkerPlayer.Messages;

public class OutputModeChangedMessage : ValueChangedMessage<OutputMode>
{
    public OutputModeChangedMessage(OutputMode value) : base(value)
    {
    }
}