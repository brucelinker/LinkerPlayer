using System.Collections.Specialized;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using LinkerPlayer.Services;

namespace LinkerPlayer.Windows;

public partial class RescanLogWindow : Window
{
    private static readonly string[] SpinnerFrames = ["|", "/", "—", "\\"];
    private static readonly Brush SpinnerActiveBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0xAC, 0xF0));

    private readonly IRescanLogger _rescanLogger;
    private readonly DispatcherTimer _spinner;
    private int _spinnerFrame;
    private bool _isAtBottom = true;
    private ScrollViewer? _logScrollViewer;

    public RescanLogWindow()
    {
        _rescanLogger = App.AppHost.Services.GetRequiredService<IRescanLogger>();
        InitializeComponent();
        DataContext = _rescanLogger;

        _rescanLogger.Entries.CollectionChanged += Entries_CollectionChanged;

        _spinner = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _spinner.Tick += Spinner_Tick;

        Loaded += (_, _) =>
        {
            _logScrollViewer = FindScrollViewer(LogList);
            _logScrollViewer?.ScrollToBottom();
        };
    }

    public void StartSpinner()
    {
        _spinnerFrame = 0;
        SpinnerText.Foreground = SpinnerActiveBrush;
        SpinnerText.Text = SpinnerFrames[0];
        SpinnerText.Visibility = Visibility.Visible;
        StatusText.Text = "Scanning…";
        _spinner.Start();
    }

    public void StopSpinner()
    {
        _spinner.Stop();
        SpinnerText.Foreground = Brushes.LightGreen;
        SpinnerText.Text = "✓";
        StatusText.Text = "Done";

        DispatcherTimer hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            SpinnerText.Visibility = Visibility.Collapsed;
            SpinnerText.Foreground = SpinnerActiveBrush;
            StatusText.Text = string.Empty;
        };
        hideTimer.Start();
    }

    private void Spinner_Tick(object? sender, EventArgs e)
    {
        _spinnerFrame = (_spinnerFrame + 1) % SpinnerFrames.Length;
        SpinnerText.Text = SpinnerFrames[_spinnerFrame];
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && _isAtBottom)
            _logScrollViewer?.ScrollToBottom();
    }

    private void LogList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Use the event args directly — no OriginalSource cast needed.
        // At the bottom when there is no more room to scroll down (with 1px tolerance).
        _isAtBottom = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 1.0;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            ScrollViewer? found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _rescanLogger.Entries.Clear();
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        StringBuilder sb = new StringBuilder();
        foreach (Models.RescanLogEntry entry in _rescanLogger.Entries)
            sb.AppendLine($"{entry.Time:HH:mm:ss}  {entry.ActionLabel,-8}  {entry.Detail}");
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
            // Copy selected rows; fall back to all if nothing selected
            System.Collections.IList items = LogList.SelectedItems.Count > 0
                ? LogList.SelectedItems
                : (System.Collections.IList)LogList.Items;
            StringBuilder sb = new StringBuilder();
            foreach (Models.RescanLogEntry entry in items.OfType<Models.RescanLogEntry>())
                sb.AppendLine($"{entry.Time:HH:mm:ss}  {entry.ActionLabel,-8}  {entry.Detail}");
            if (sb.Length > 0) Clipboard.SetText(sb.ToString());
            e.Handled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
