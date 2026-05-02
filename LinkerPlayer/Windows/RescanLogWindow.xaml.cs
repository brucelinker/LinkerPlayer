using System.Collections.Specialized;
using System.Windows;
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

    public RescanLogWindow()
    {
        _rescanLogger = App.AppHost.Services.GetRequiredService<IRescanLogger>();
        InitializeComponent();
        DataContext = _rescanLogger;

        _rescanLogger.Entries.CollectionChanged += Entries_CollectionChanged;

        _spinner = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _spinner.Tick += Spinner_Tick;
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
        if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
            LogList.ScrollIntoView(LogList.Items[^1]);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _rescanLogger.Entries.Clear();
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
