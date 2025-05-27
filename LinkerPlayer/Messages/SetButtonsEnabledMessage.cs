namespace LinkerPlayer.Messages;

public class SetButtonsEnabledMessage
{
    public bool IsEnabled { get; }
    public SetButtonsEnabledMessage(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }
}