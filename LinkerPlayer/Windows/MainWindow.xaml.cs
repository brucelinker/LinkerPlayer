using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Core;
using LinkerPlayer.Interop;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace LinkerPlayer.Windows;

public partial class MainWindow : Window
{
    public static MainWindow? Instance
    {
        get; private set;
    }
    private readonly MainViewModel _mainViewModel;
    private readonly ILogger<MainWindow> _logger;
    private readonly ISettingsManager _settingsManager;
    private readonly IImportCancellationService _importCancellationService;
    private PlaylistTabsViewModel? _playlistVm;
    private bool _isClosing;

    public MainWindow(IServiceProvider serviceProvider, ILogger<MainWindow> logger)
    {
        _logger = logger;

        try
        {
            Instance = this;
            InitializeComponent();

            _logger.LogInformation("MainWindow: Regular WPF Window initialized");

            _mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
            _settingsManager = serviceProvider.GetRequiredService<ISettingsManager>();
            _importCancellationService = serviceProvider.GetRequiredService<IImportCancellationService>();
            DataContext = _mainViewModel;

            ((App)Application.Current).WindowPlace.Register(this, "MainWindow");

            // Track monitor changes to persist which display MainWindow is on
            Loaded += (_, _) => UpdateCurrentMonitorSetting();
            LocationChanged += (_, _) => UpdateCurrentMonitorSetting();
            StateChanged += (_, _) => UpdateCurrentMonitorSetting();
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Closing += MainWindow_Closing;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error in MainWindow constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in MainWindow constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
    }

    private void UpdateCurrentMonitorSetting()
    {
        try
        {
            string? deviceName = Interop.MonitorHelper.GetDeviceName(this);
            if (!string.IsNullOrWhiteSpace(deviceName) &&
                !string.Equals(_settingsManager.Settings.LastMainWindowMonitorDeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                _settingsManager.Settings.LastMainWindowMonitorDeviceName = deviceName;
                _settingsManager.SaveSettings(nameof(Models.AppSettings.LastMainWindowMonitorDeviceName));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update last monitor device name");
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("MainWindow: Window_Loaded event fired");

        // Initialize the view model
        _mainViewModel.OnWindowLoaded();
        WeakReferenceMessenger.Default.Send(new MainWindowLoadedMessage(true));

        // Wire up TrackInfo IsLibraryMode after all controls are loaded
        Dispatcher.BeginInvoke(() =>
        {
            if (PlaylistTabs.DataContext is PlaylistTabsViewModel playlistVm)
            {
                _playlistVm = playlistVm;
                UpdateTrackInfoLibraryMode(playlistVm);
                playlistVm.PropertyChanged += PlaylistVm_PropertyChanged;
            }
        }, DispatcherPriority.Loaded);

        _logger.LogInformation("MainWindow: Regular WPF Window loaded successfully");
    }

    private void OnMainWindowClose(object sender, EventArgs e)
    {
        _logger.LogInformation("MainWindow: OnMainWindowClose called");

        WeakReferenceMessenger.Default.Send(new MainWindowClosingMessage(true));
        _mainViewModel.OnWindowClosing();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _logger.LogInformation("MainWindow: Shutting down application");

        base.OnClosing(e);
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        _logger.LogInformation("MainWindow: Window state changed to: {State}", WindowState);

        // WindowStyle="None" removes OS chrome, so WPF maximizes to the full screen
        // rect instead of the work area (screen minus taskbar).  Fix it manually.
        if (WindowState == WindowState.Maximized)
        {
            // Use the work area of the monitor this window is on.
            System.Windows.Interop.WindowInteropHelper helper = new(this);
            nint hMonitor = NativeMethods.MonitorFromWindow(
                helper.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);

            NativeMethods.MONITORINFO mi = new();
            mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(mi);
            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                // rcWork is in physical pixels; convert to WPF device-independent units
                double dpiScale = PresentationSource.FromVisual(this)
                                      ?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
                MaxWidth  = (mi.rcWork.right  - mi.rcWork.left) * dpiScale;
                MaxHeight = (mi.rcWork.bottom - mi.rcWork.top)  * dpiScale;
            }
        }
        else
        {
            MaxWidth  = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+S — save dirty library edits
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            IPlaylistTabsViewModel? vm = App.AppHost?.Services?.GetService<IPlaylistTabsViewModel>();
            if (vm is PlaylistTabsViewModel ptvm)
            {
                _ = ptvm.SaveDirtyTracksCommand.ExecuteAsync(null);
            }
            return;
        }

        if (e.Key != Key.Escape || !_importCancellationService.IsImporting)
            return;

        e.Handled = true;

        // Pause the import — the current file will finish before pausing
        _importCancellationService.RequestPause();

        MessageBoxResult result = MessageBox.Show(
            "Import is in progress. Would you like to continue or cancel?",
            "Import Paused",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.OK)
        {
            _importCancellationService.Resume();
        }
        else
        {
            _importCancellationService.Cancel();
        }
    }

    private void UpdateTrackInfoLibraryMode(PlaylistTabsViewModel vm)
    {
        bool isLibrary = vm.SelectedTab is MusicLibraryTab;
        _logger.LogInformation("UpdateTrackInfoLibraryMode: SelectedTab={Tab}, IsLibrary={IsLibrary}", vm.SelectedTab?.Name ?? "null", isLibrary);
        TrackInfo.IsLibraryMode = isLibrary;
    }

    private void PlaylistVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistTabsViewModel.SelectedTab) && sender is PlaylistTabsViewModel vm)
        {
            _logger.LogInformation("PlaylistVm_PropertyChanged: SelectedTab changed");
            UpdateTrackInfoLibraryMode(vm);
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // If we're already in the process of saving-then-closing, just let it proceed.
        // A second click during the async save would re-enter here and WPF would throw
        // because you can't show a MessageBox while a window is already closing.
        if (_isClosing)
            return;

        if (_playlistVm?.HasDirtyTracks == true)
        {
            List<LinkerPlayer.Models.MediaFile> dirty = _playlistVm.DirtyTracks;
            _logger.LogInformation("Close requested with {Count} dirty track(s):", dirty.Count);
            foreach (LinkerPlayer.Models.MediaFile t in dirty)
                _logger.LogInformation("  DIRTY  [{Props}]  {Path}", string.Join(", ", t.DirtyProperties), t.Path);

            // Build a readable list of tracks for the user — cap at 20 to keep the box manageable.
            const int maxListed = 20;
            System.Text.StringBuilder sb = new();
            sb.AppendLine($"You have {dirty.Count} unsaved track change(s).");
            sb.AppendLine();
            sb.AppendLine("Tracks with changes:");
            for (int i = 0; i < Math.Min(dirty.Count, maxListed); i++)
            {
                LinkerPlayer.Models.MediaFile t = dirty[i];
                string props = string.Join(", ", t.DirtyProperties);
                string name = !string.IsNullOrWhiteSpace(t.Title) ? t.Title : t.FileName;
                sb.AppendLine($"  • {name}  [{props}]");
            }
            if (dirty.Count > maxListed)
                sb.AppendLine($"  … and {dirty.Count - maxListed} more.");
            sb.AppendLine();
            sb.AppendLine("Yes = save changes   |   No = discard all changes   |   Cancel = go back");

            MessageBoxResult result = MessageBox.Show(
                sb.ToString(),
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // Cancel this close event, save, then shut down for real.
                e.Cancel = true;
                _isClosing = true;
                await _playlistVm.SaveDirtyTracksCommand.ExecuteAsync(null);
                Application.Current.Shutdown();
            }
            else if (result == MessageBoxResult.No)
            {
                // Discard all in-memory changes so the next startup starts clean.
                foreach (LinkerPlayer.Models.MediaFile t in dirty)
                    t.ClearDirty();
                _logger.LogInformation("User discarded {Count} unsaved change(s) on close.", dirty.Count);
                // fall through → allow close
            }
            else // Cancel
            {
                e.Cancel = true;
            }
        }
    }
}
