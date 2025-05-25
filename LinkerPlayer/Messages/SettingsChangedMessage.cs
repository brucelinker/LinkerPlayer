using CommunityToolkit.Mvvm.Messaging.Messages;

namespace LinkerPlayer.Messages;

public class SettingsChangedMessage : ValueChangedMessage<string>
{
    public SettingsChangedMessage(string propertyName) : base(propertyName) { }
}