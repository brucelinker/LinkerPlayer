using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using LinkerPlayer.Models;

namespace LinkerPlayer.Windows;

public class PlaylistImportLogViewModel
{
    public string Title { get; set; } = "Playlist Import Report";
    public string Summary { get; set; } = string.Empty;
    public ObservableCollection<PlaylistImportLogEntry> Entries { get; } = new();
}

public partial class PlaylistImportLogWindow : Window
{
    private readonly PlaylistImportLogViewModel _viewModel;

    public PlaylistImportLogWindow(string playlistName, int totalTracks, int recovered, IEnumerable<PlaylistImportLogEntry> entries)
    {
        InitializeComponent();

        List<PlaylistImportLogEntry> list = entries.ToList();

        _viewModel = new PlaylistImportLogViewModel
        {
            Title = $"Import Report - {playlistName}",
            Summary = BuildSummary(totalTracks, recovered, list)
        };

        foreach (PlaylistImportLogEntry entry in list)
            _viewModel.Entries.Add(entry);

        DataContext = _viewModel;
    }

    private static string BuildSummary(int totalTracks, int recovered, List<PlaylistImportLogEntry> entries)
    {
        int missing = entries.Count(e => e.Action == PlaylistImportAction.Missing);
        int skipped = entries.Count(e => e.Action == PlaylistImportAction.Skipped);

        List<string> parts = new List<string> { $"{totalTracks} tracks loaded" };
        if (recovered > 0) parts.Add($"{recovered} recovered by fuzzy match");
        if (missing > 0)   parts.Add($"{missing} not found on disk");
        if (skipped > 0)   parts.Add($"{skipped} skipped (no metadata)");
        parts.Add("The .m3u file may need to be updated.");

        return string.Join("  |  ", parts);
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(_viewModel.Summary);
        sb.AppendLine();
        foreach (PlaylistImportLogEntry entry in _viewModel.Entries)
            sb.AppendLine($"{entry.ActionLabel,-10}  {entry.Detail}");
        if (sb.Length > 0)
        {
            Clipboard.SetText(sb.ToString());
            LogList.SelectAll();
            LogList.Focus();
        }
    }

    private void LogList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Home && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[0]);
            e.Handled = true;
        }
        else if (e.Key == Key.End && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            LogList.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            System.Collections.IList items = LogList.SelectedItems.Count > 0
                ? LogList.SelectedItems
                : (System.Collections.IList)LogList.Items;
            StringBuilder sb = new StringBuilder();
            foreach (PlaylistImportLogEntry entry in items.OfType<PlaylistImportLogEntry>())
                sb.AppendLine($"{entry.ActionLabel,-10}  {entry.Detail}");
            if (sb.Length > 0) Clipboard.SetText(sb.ToString());
            e.Handled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) { }
}
