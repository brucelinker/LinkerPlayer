using CommunityToolkit.Mvvm.Messaging.Messages;

namespace LinkerPlayer.Messages;

public class UpdateColumnsMessage : ValueChangedMessage<List<string>>
{
    public List<string> SelectedColumns { get; }

    public UpdateColumnsMessage(List<string> selectedColumns) : base(selectedColumns)
    {
        SelectedColumns = selectedColumns;
    }
}
