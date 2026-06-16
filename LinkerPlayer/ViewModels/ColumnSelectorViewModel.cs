// ColumnSelectorViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
using System.Collections.ObjectModel;

namespace LinkerPlayer.ViewModels;

public class ColumnSelectorItem : ObservableObject
{
    public string DisplayName { get; }
    public string PropertyName { get; }

    private bool _isVisible;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public ColumnSelectorItem(string displayName, string propertyName, bool defaultVisible = true)
    {
        DisplayName = displayName;
        PropertyName = propertyName;
        IsVisible = defaultVisible;
    }
}

public partial class ColumnSelectorViewModel : ObservableObject
{
    public ObservableCollection<ColumnSelectorItem> Columns { get; } = new();

    public ColumnSelectorViewModel()
    {
        Columns.Add(new ColumnSelectorItem("Track #", "Track", false));
        Columns.Add(new ColumnSelectorItem("Title", "Title", true));
        Columns.Add(new ColumnSelectorItem("Artist", "Artist", true));
        Columns.Add(new ColumnSelectorItem("Album", "Album", true));
        Columns.Add(new ColumnSelectorItem("Album Artist", "AlbumArtist", false));
        Columns.Add(new ColumnSelectorItem("Genre", "Genres", false));
        Columns.Add(new ColumnSelectorItem("Track Count", "TrackCount", false));
        Columns.Add(new ColumnSelectorItem("Disc #", "Disc", false));
        Columns.Add(new ColumnSelectorItem("Disc Count", "DiscCount", false));
        Columns.Add(new ColumnSelectorItem("Composers", "Composers", false));
        Columns.Add(new ColumnSelectorItem("Comment", "Comment", false));
        Columns.Add(new ColumnSelectorItem("Copyright", "Copyright", false));
        Columns.Add(new ColumnSelectorItem("Duration", "Duration", true));
        Columns.Add(new ColumnSelectorItem("Year", "Year", false));
        Columns.Add(new ColumnSelectorItem("Bitrate", "Bitrate", false));
        Columns.Add(new ColumnSelectorItem("Sample Rate", "SampleRate", false));
        Columns.Add(new ColumnSelectorItem("Channels", "Channels", false));
        Columns.Add(new ColumnSelectorItem("Codec", "Codec", false));
        Columns.Add(new ColumnSelectorItem("File Name", "FileName", false));
        Columns.Add(new ColumnSelectorItem("Path", "Path", false));
        Columns.Add(new ColumnSelectorItem("Rating", "Rating", true));
    }

    [RelayCommand]
    private void ConfirmSelection()
    {
        List<string> selected = Columns.Where(c => c.IsVisible).Select(c => c.PropertyName).ToList();
        WeakReferenceMessenger.Default.Send(new UpdateColumnsMessage(selected));
    }
}
