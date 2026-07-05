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
using System.Windows.Interop;
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
    private MediaTabViewModel? _playlistVm;
    private bool _isClosing;
    private IntPtr _smallIconHandle;
    private IntPtr _bigIconHandle;

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
            SourceInitialized += MainWindow_SourceInitialized;

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

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            string? processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            {
                return;
            }

            using System.Drawing.Icon? applicationIcon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
            if (applicationIcon == null)
            {
                return;
            }

            _smallIconHandle = CopyImage(applicationIcon.Handle, IMAGE_ICON, 16, 16, 0);
            _bigIconHandle = CopyImage(applicationIcon.Handle, IMAGE_ICON, 32, 32, 0);

            if (_smallIconHandle != IntPtr.Zero)
            {
                SendMessage(handle, WM_SETICON, ICON_SMALL, _smallIconHandle);
            }

            if (_bigIconHandle != IntPtr.Zero)
            {
                SendMessage(handle, WM_SETICON, ICON_BIG, _bigIconHandle);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set taskbar icons for MainWindow");
        }
    }

    private const uint WM_SETICON = 0x0080;
    private const uint IMAGE_ICON = 1;
    private static readonly IntPtr ICON_SMALL = new(0);
    private static readonly IntPtr ICON_BIG = new(1);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CopyImage(IntPtr hImage, uint uType, int cxDesired, int cyDesired, uint fuFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

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
            if (PlaylistTabs.DataContext is MediaTabViewModel playlistVm)
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

        if (_smallIconHandle != IntPtr.Zero)
        {
            DestroyIcon(_smallIconHandle);
            _smallIconHandle = IntPtr.Zero;
        }

        if (_bigIconHandle != IntPtr.Zero)
        {
            DestroyIcon(_bigIconHandle);
            _bigIconHandle = IntPtr.Zero;
        }

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
                MaxWidth = (mi.rcWork.right - mi.rcWork.left) * dpiScale;
                MaxHeight = (mi.rcWork.bottom - mi.rcWork.top) * dpiScale;
            }
        }
        else
        {
            MaxWidth = double.PositiveInfinity;
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
            IMediaTabViewModel? vm = App.AppHost?.Services?.GetService<IMediaTabViewModel>();
            if (vm is MediaTabViewModel ptvm)
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

    private void UpdateTrackInfoLibraryMode(MediaTabViewModel vm)
    {
        bool isLibrary = vm.SelectedTab is LibraryTab;
        _logger.LogInformation("UpdateTrackInfoLibraryMode: SelectedTab={Tab}, IsLibrary={IsLibrary}", vm.SelectedTab?.Name ?? "null", isLibrary);
        TrackInfo.IsLibraryMode = isLibrary;
    }

    private void PlaylistVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaTabViewModel.SelectedTab) && sender is MediaTabViewModel vm)
        {
            _logger.LogInformation("PlaylistVm_PropertyChanged: SelectedTab changed");
            UpdateTrackInfoLibraryMode(vm);
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isClosing)
            return;

        // Always silently save Library changes (ratings, metadata, etc.)
        try
        {
            IMusicLibrary? musicLibrary = App.AppHost?.Services?.GetService<IMusicLibrary>();
            if (musicLibrary != null)
            {
                await musicLibrary.SaveToDatabaseAsync();
                _logger.LogInformation("Library auto-saved on close.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library save failed on close");
        }

        // *** MAY NOT NEED THIS SINCE THE PROPERTIES WINDOW HANDLES THIS ***
        // === ONLY prompt for Playlist-specific dirty tracks ===
        //if (_playlistVm?.HasDirtyTracks == true)
        //{
        //    List<LinkerPlayer.Models.MediaFile> dirty = _playlistVm.DirtyTracks;
        //    _logger.LogInformation("Close requested with {Count} dirty track(s) in playlist:", dirty.Count);

        //    // Keep your existing detailed MessageBox logic here unchanged
        //    const int maxListed = 20;
        //    System.Text.StringBuilder sb = new();
        //    sb.AppendLine($"You have {dirty.Count} unsaved track change(s).");
        //    sb.AppendLine();
        //    sb.AppendLine("Tracks with changes:");
        //    for (int i = 0; i < Math.Min(dirty.Count, maxListed); i++)
        //    {
        //        LinkerPlayer.Models.MediaFile t = dirty[i];
        //        string props = string.Join(", ", t.DirtyProperties);
        //        string name = !string.IsNullOrWhiteSpace(t.Title) ? t.Title : t.FileName;
        //        sb.AppendLine($"  • {name}  [{props}]");
        //    }
        //    if (dirty.Count > maxListed)
        //        sb.AppendLine($"  … and {dirty.Count - maxListed} more.");
        //    sb.AppendLine();
        //    sb.AppendLine("Yes = save changes   |   No = discard all changes   |   Cancel = go back");

        //    MessageBoxResult result = MessageBox.Show(
        //        sb.ToString(),
        //        "Unsaved Changes",
        //        MessageBoxButton.YesNoCancel,
        //        MessageBoxImage.Warning);

        //    if (result == MessageBoxResult.Yes)
        //    {
        //        e.Cancel = true;
        //        _isClosing = true;
        //        await _playlistVm.SaveDirtyTracksCommand.ExecuteAsync(null);
        //        Application.Current.Shutdown();
        //        return;
        //    }
        //    else if (result == MessageBoxResult.No)
        //    {
        //        foreach (LinkerPlayer.Models.MediaFile t in dirty)
        //            t.ClearDirty();
        //        _logger.LogInformation("User discarded {Count} unsaved playlist change(s) on close.", dirty.Count);
        //        // fall through to allow close
        //    }
        //    else // Cancel
        //    {
        //        e.Cancel = true;
        //        return;
        //    }
        //}

        // No playlist changes → clean shutdown
        _isClosing = true;
    }
}
