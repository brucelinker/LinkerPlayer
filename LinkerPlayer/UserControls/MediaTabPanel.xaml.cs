using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Converters;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using ManagedBass;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LinkerPlayer.UserControls;

[ObservableObject]
public partial class PlaylistTabs
{
    private readonly ILogger<PlaylistTabs> _logger;
    private ITabData? _draggedTab; // Can be PlaylistTab or MusicLibraryTab
    private DropIndicatorAdorner? _dropIndicatorAdorner;
    private readonly Dictionary<ITabData, double> _tabVerticalOffsets = new();

    private bool _isExplicitCentering;
    private Popup? _columnSelectorPopup;
    private readonly DispatcherTimer _columnLayoutSaveTimer;
    private bool _suppressNextContextMenu; // prevents row context menu after header right-click
    private bool _openPopupOnRightButtonUp; // after opening on Down, switch StaysOpen off on Up
    private bool _allowSelectionSyncFromUserInput;
    private bool _tabSwitchInProgress; // true when a tab is being clicked, suppresses mouse-state check in selection
    private readonly Dictionary<DataGridColumn, object?> _baseHeaderByColumn = new();
    private readonly HashSet<DataGrid> _initializedGrids = new(); // tracks grids that have already been initialized (for cached tab content)

    public PlaylistTabs()
    {
        // In unit tests, Application.Current may be null; skip XAML initialization to avoid NREs
        if (Application.Current != null)
        {
            InitializeComponent();
        }

        if (App.AppHost?.Services != null)
        {
            _logger = App.AppHost.Services.GetRequiredService<ILogger<PlaylistTabs>>();
        }
        else
        {
            _logger = LoggerFactory.Create(builder => { }).CreateLogger<PlaylistTabs>();
        }

        _columnLayoutSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _columnLayoutSaveTimer.Tick += ColumnLayoutSaveTimer_Tick;

        Loaded += PlaylistTabs_Loaded;

        WeakReferenceMessenger.Default.Register<ActiveTrackChangedMessage>(this, (_, m) => OnActiveTrackChanged(m.Value));
        WeakReferenceMessenger.Default.Register<GoToActiveTrackMessage>(this, (_, m) => OnGoToActiveTrack(m.Value));
        WeakReferenceMessenger.Default.Register<UpdateColumnsMessage>(this, (_, m) => OnUpdateColumns(m));
    }

    internal void RegenerateColumns(DataGrid dg)
    {
        if (!dg.Dispatcher.CheckAccess())
        {
            dg.Dispatcher.Invoke(() => RegenerateColumns(dg));
            return;
        }

        MediaTabViewModel vm = DataContext as MediaTabViewModel ?? throw new InvalidOperationException("DataContext is not PlaylistTabsViewModel");
        ISettingsManager? settingsManager = App.AppHost?.Services?.GetService<ISettingsManager>();
        Dictionary<string, AppSettings.ColumnInfo> savedInfo = settingsManager?.Settings.ColumnSettings ?? new Dictionary<string, AppSettings.ColumnInfo>();

        // If this DataGrid is hosting the Music Library (its DataContext will be MusicLibraryTab),
        // use the library's VisibleColumns instead of the shared playlist SelectedColumnNames.
        HashSet<string> visibleProps;
        LibraryTab? libTab = dg.DataContext as LibraryTab;
        if (libTab != null)
        {
            // If the library tab has no visible columns configured, try to restore from settings
            if ((libTab.VisibleColumns == null || libTab.VisibleColumns.Count == 0) &&
                settingsManager?.Settings.LibraryVisibleColumns != null &&
                settingsManager.Settings.LibraryVisibleColumns.Count > 0)
            {
                libTab.VisibleColumns = new List<string>(settingsManager.Settings.LibraryVisibleColumns);
            }

            visibleProps = new HashSet<string>(libTab.VisibleColumns ?? new List<string>());
            // Ensure Year is always visible in the library
            if (!visibleProps.Contains("Year"))
            {
                visibleProps.Add("Year");
                libTab.VisibleColumns = visibleProps.ToList();
                settingsManager?.Settings.LibraryVisibleColumns = libTab.VisibleColumns;
                settingsManager?.SaveSettings(nameof(AppSettings.LibraryVisibleColumns));
            }
            // Load saved layout specifically for the library if available
            savedInfo = settingsManager?.Settings.LibraryColumnSettings ?? new Dictionary<string, AppSettings.ColumnInfo>();
        }
        else
        {
            visibleProps = vm.SelectedColumnNames.ToHashSet();
        }

        // === 1. Determine how many static columns exist in XAML (play icon and/or cover indicator) ===
        int staticColumnsToPreserve = 0;
        if (dg.Columns.Count > 0 && dg.Columns[0] is DataGridTemplateColumn)
        {
            staticColumnsToPreserve = 1;
            // Library grids also get a cover-indicator column at index 1
            if (libTab != null && dg.Columns.Count > 1 && dg.Columns[1] is DataGridTemplateColumn)
                staticColumnsToPreserve = 2;
        }

        // === 2. If no play/pause column exists, add it properly using dg's resource scope ===
        if (staticColumnsToPreserve == 0)
        {
            DataTemplate? playTemplate = dg.TryFindResource("PlayPauseCellTemplate") as DataTemplate
                              ?? Application.Current?.TryFindResource("PlayPauseCellTemplate") as DataTemplate;

            if (playTemplate == null)
            {
                // Fallback: create a minimal placeholder template for tests/runtime without resources
                FrameworkElementFactory gridFactory = new FrameworkElementFactory(typeof(Grid));
                playTemplate = new DataTemplate { VisualTree = gridFactory };
            }

            DataGridTemplateColumn playCol = new DataGridTemplateColumn
            {
                Header = "",
                Width = new DataGridLength(36),
                MaxWidth = 36,
                CanUserResize = false,
                IsReadOnly = true,
                CellTemplate = playTemplate
            };
            dg.Columns.Insert(0, playCol);
            staticColumnsToPreserve = 1;
        }

        // === 2b. Library only: ensure the album-cover indicator column is at index 1 ===
        if (libTab != null && staticColumnsToPreserve == 1)
        {
            DataTemplate? coverTemplate = dg.TryFindResource("AlbumCoverCellTemplate") as DataTemplate
                               ?? Application.Current?.TryFindResource("AlbumCoverCellTemplate") as DataTemplate;

            if (coverTemplate == null)
            {
                FrameworkElementFactory factory = new FrameworkElementFactory(typeof(Grid));
                coverTemplate = new DataTemplate { VisualTree = factory };
            }

            DataGridTemplateColumn coverCol = new DataGridTemplateColumn
            {
                Header = "",
                Width = new DataGridLength(22),
                MaxWidth = 22,
                CanUserResize = false,
                IsReadOnly = true,
                CellTemplate = coverTemplate
            };
            dg.Columns.Insert(1, coverCol);
            staticColumnsToPreserve = 2;
        }

        // === 3. Remove only dynamic columns ===
        for (int i = dg.Columns.Count - 1; i >= staticColumnsToPreserve; i--)
        {
            dg.Columns.RemoveAt(i);
        }

        // === 4. Add visible columns (skip if already exists as static, e.g. Track #) ===
        (string prop, string header, double defWidth)[] defaultColumns = new (string prop, string header, double defWidth)[]
        {
            ("Track",       "Track #",       80),
            ("Title",       "Title",        300),
            ("Artist",      "Artist",       200),
            ("Album",       "Album",        200),
            ("AlbumArtist", "Album Artist", 180),
            ("Genres",      "Genre",        120),
            ("TrackCount",  "Track Count",   80),
            ("Disc",        "Disc #",        60),
            ("DiscCount",   "Disc Count",    80),
            ("Composers",   "Composers",    180),
            ("Comment",     "Comment",      200),
            ("Copyright",   "Copyright",    150),
            ("Duration",    "Duration",     100),
            ("Year",        "Year",          80),
            ("Bitrate",     "Bitrate",       90),
            ("SampleRate",  "Sample Rate",   90),
            ("Channels",    "Channels",      80),
            ("Codec",       "Codec",        100),
            ("ReplayGain",  "ReplayGain",    90),
            ("FileName",    "File Name",    200),
            ("Path",        "Path",         350)
        };

        // Read-only properties that cannot be edited inline
        HashSet<string> readOnlyProps = new()
        {
            "Duration", "Bitrate", "SampleRate", "Channels", "Codec", "ReplayGain", "FileName", "Path"
        };

        // Columns where 0 means "not set" and should display as blank
        HashSet<string> zeroToEmptyProps = new()
        {
            "Track", "TrackCount", "Disc", "DiscCount", "Year", "Channels"
        };

        foreach ((string prop, string header, double defWidth) in defaultColumns)
        {
            if (!visibleProps.Contains(prop))
            {
                continue;
            }

            // Skip if already exists (e.g. Track # column preserved from XAML)
            if (dg.Columns.Any(c => c is DataGridBoundColumn bc && bc.Binding is Binding b && b.Path.Path == prop))
            {
                continue;
            }

            bool isReadOnly = readOnlyProps.Contains(prop) || libTab == null;

            Binding binding = new Binding(prop);

            if (!isReadOnly)
            {
                binding.Mode = BindingMode.TwoWay;
                binding.UpdateSourceTrigger = UpdateSourceTrigger.LostFocus;
            }

            if (zeroToEmptyProps.Contains(prop))
            {
                binding.Converter = new Converters.ZeroToEmptyConverter();
            }
            else if (prop == "Year")
            {
                binding.TargetNullValue = "";
            }

            if (prop == "Duration")
            {
                binding.Converter = new Converters.DurationConverter();
                binding.TargetNullValue = "";
            }

            if (prop == "Bitrate")
            {
                binding.Converter = new Converters.ZeroToEmptyConverter();
                binding.StringFormat = "{0} kbps";
                binding.TargetNullValue = "";
            }

            if (prop == "SampleRate")
            {
                binding.Converter = new Converters.ZeroToEmptyConverter();
                binding.StringFormat = "{0:N0} Hz";
                binding.TargetNullValue = "";
            }

            DataGridTextColumn col = new DataGridTextColumn
            {
                Header = header,
                Binding = binding,
                IsReadOnly = isReadOnly
            };

            // Apply dirty cell style for editable columns on the Library DataGrid
            if (!isReadOnly && libTab != null)
            {
                col.CellStyle = CreateDirtyCellStyle(prop);
            }

            if (libTab != null && (prop == "Album" || prop == "AlbumArtist"))
            {
                ApplyPathToolTipStyles(col);
            }

            double width = savedInfo.TryGetValue(prop, out AppSettings.ColumnInfo? ci) && ci.Width > 10 ? ci.Width : defWidth;
            col.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);

            HookColumnEvents(col, prop);
            dg.Columns.Add(col);
        }

        // === 4b. Rating column: lightweight read-only star text for display ===
        // A full StarRatingControl UserControl per visible cell is too heavy for smooth
        // scrolling on large libraries, so the grid renders plain star text. Interactive
        // rating editing is still available in the track Properties window.
        if (visibleProps.Contains("Rating") &&
            !dg.Columns.Any(c => c is DataGridTextColumn bc && bc.Binding is Binding b && b.Path.Path == "Rating"))
        {
            // Unrated (0) shows five full stars rendered dim/disabled, matching the convention
            // where empty outline stars mean "no rating yet"; fractional ratings show half stars.
            Style ratingCellStyle = new Style(typeof(TextBlock));
            DataTrigger unratedTrigger = new DataTrigger
            {
                Binding = new Binding("Rating"),
                Value = 0.0
            };
            unratedTrigger.Setters.Add(new Setter(TextBlock.OpacityProperty, 0.3));
            ratingCellStyle.Triggers.Add(unratedTrigger);

            DataGridTextColumn ratingCol = new DataGridTextColumn
            {
                Header = "Rating",
                SortMemberPath = "Rating",
                IsReadOnly = true,
                ElementStyle = ratingCellStyle,
                Binding = new Binding("Rating")
                {
                    Mode = BindingMode.OneWay,
                    Converter = new Converters.FractionalRatingToStarsConverter()
                }
            };

            double ratingWidth = savedInfo.TryGetValue("Rating", out AppSettings.ColumnInfo? rci) && rci.Width > 10
                ? rci.Width : 90;
            ratingCol.Width = new DataGridLength(ratingWidth, DataGridLengthUnitType.Pixel);

            HookColumnEvents(ratingCol, "Rating");
            dg.Columns.Add(ratingCol);
        }

        // === 5. Restore saved order ===
        int displayIndex = staticColumnsToPreserve;
        if (savedInfo != null && savedInfo.Count > 0)
        {
            List<DataGridColumn> ordered = dg.Columns.Skip(staticColumnsToPreserve)
                .OrderBy(col =>
                {
                    if (col is DataGridTextColumn txtCol && txtCol.Binding is Binding bind && bind.Path?.Path != null)
                    {
                        string key = bind.Path.Path;
                        return savedInfo.TryGetValue(key, out AppSettings.ColumnInfo? ci) && ci.Position >= 0 ? ci.Position : int.MaxValue;
                    }
                    if (col is DataGridTemplateColumn tmpl && !string.IsNullOrEmpty(tmpl.SortMemberPath))
                    {
                        return savedInfo.TryGetValue(tmpl.SortMemberPath, out AppSettings.ColumnInfo? ci) && ci.Position >= 0 ? ci.Position : int.MaxValue;
                    }
                    return int.MaxValue;
                })
                .ToList();

            foreach (DataGridColumn col in ordered)
                col.DisplayIndex = displayIndex++;
        }

        // === 6. Fix horizontal scroll jump ===
        if (FindDescendant<ScrollViewer>(dg) is ScrollViewer sv)
            sv.ScrollToHorizontalOffset(0);
    }

    // ==================================================================
    //  Dirty cell style factory for inline Library editing
    // ==================================================================
    private static readonly SolidColorBrush DirtyCellBrush = new(Color.FromArgb(60, 0, 180, 0));

    //static PlaylistTabs()
    //{
    //    DirtyCellBrush.Freeze();
    //}

    private static Style CreateDirtyCellStyle(string propertyName)
    {
        Style style = new Style(typeof(DataGridCell), (Style)Application.Current.FindResource("SharedDataGridCellStyle"));

        // MediaFile raises an "IsDirty_{PropertyName}" change notification whenever a
        // property becomes dirty (and when ClearDirty runs), so bind the trigger directly
        // to that boolean — no MultiBinding, converter, or lock in the per-cell hot path.
        DataTrigger dirtyTrigger = new DataTrigger
        {
            Binding = new Binding($"IsDirty_{propertyName}"),
            Value = true
        };
        dirtyTrigger.Setters.Add(new Setter(DataGridCell.BackgroundProperty, DirtyCellBrush));
        style.Triggers.Add(dirtyTrigger);

        return style;
    }

    private static void ApplyPathToolTipStyles(DataGridTextColumn column)
    {
        Style textBlockStyle = new Style(typeof(TextBlock));
        textBlockStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("Path")));
        column.ElementStyle = textBlockStyle;

        Style textBoxStyle = new Style(typeof(TextBox));
        textBoxStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("Path")));
        column.EditingElementStyle = textBoxStyle;
    }

    // ==================================================================
    //  Hook width & reorder changes → debounced save
    // ==================================================================
    private void HookColumnEvents(DataGridColumn col, string propertyName)
    {
        DependencyPropertyDescriptor? widthDpd = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
        widthDpd?.AddValueChanged(col, (object? s, EventArgs e) => DebounceSaveColumnLayout());

        DependencyPropertyDescriptor? indexDpd = DependencyPropertyDescriptor.FromProperty(DataGridColumn.DisplayIndexProperty, typeof(DataGridColumn));
        indexDpd?.AddValueChanged(col, (object? s, EventArgs e) => DebounceSaveColumnLayout());
    }

    private void DebounceSaveColumnLayout()
    {
        _columnLayoutSaveTimer.Stop();
        _columnLayoutSaveTimer.Start();
    }

    private void ColumnLayoutSaveTimer_Tick(object? sender, EventArgs e)
    {
        _columnLayoutSaveTimer.Stop();

        DataGrid? dg = GetActiveDataGrid();
        if (dg == null)
            return;

        ISettingsManager? settingsManager = App.AppHost?.Services?.GetService<ISettingsManager>();
        if (settingsManager == null)
        {
            return; // no settings available (e.g., unit tests)
        }

        Dictionary<string, AppSettings.ColumnInfo> info = new Dictionary<string, AppSettings.ColumnInfo>();

        for (int i = 1; i < dg.Columns.Count; i++) // skip icon column
        {
            DataGridColumn col = dg.Columns[i];
            if (col is DataGridTextColumn txt && txt.Binding is Binding b && b.Path?.Path != null)
            {
                string key = b.Path.Path;
                info[key] = new AppSettings.ColumnInfo
                {
                    Width = col.ActualWidth,
                    Position = col.DisplayIndex
                };
            }
            else if (col is DataGridTemplateColumn tmpl && !string.IsNullOrEmpty(tmpl.SortMemberPath))
            {
                info[tmpl.SortMemberPath] = new AppSettings.ColumnInfo
                {
                    Width = col.ActualWidth,
                    Position = col.DisplayIndex
                };
            }
        }

        // If the active DataGrid belongs to the Music Library, persist to library-specific settings
        if (DataContext is MediaTabViewModel vm && vm.SelectedTab is LibraryTab libTab)
        {
            // Persist library visible columns in display order
            List<string> visible = dg.Columns.Skip(1)
                .Where(c =>
                    (c is DataGridTextColumn txtC && txtC.Binding is Binding bindC && bindC.Path?.Path != null) ||
                    (c is DataGridTemplateColumn tmplC && !string.IsNullOrEmpty(tmplC.SortMemberPath)))
                .OrderBy(c => c.DisplayIndex)
                .Select(c =>
                {
                    if (c is DataGridTextColumn txtC2 && txtC2.Binding is Binding bindC2)
                        return bindC2.Path.Path;
                    if (c is DataGridTemplateColumn tmplC2 && !string.IsNullOrEmpty(tmplC2.SortMemberPath))
                        return tmplC2.SortMemberPath;
                    return string.Empty;
                })
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();

            settingsManager.Settings.LibraryVisibleColumns = visible;
            settingsManager.Settings.LibraryColumnSettings = info;
            libTab.VisibleColumns = new List<string>(visible);
            settingsManager.SaveSettings(nameof(AppSettings.LibraryVisibleColumns));
            settingsManager.SaveSettings(nameof(AppSettings.LibraryColumnSettings));
        }
        else
        {
            settingsManager.Settings.ColumnSettings = info;
            settingsManager.SaveSettings(nameof(AppSettings.ColumnSettings));
        }
    }

    // ==================================================================
    //  RIGHT-CLICK COLUMN HEADER – open on MouseRightButtonDown with StaysOpen=true
    //  On MouseRightButtonUp switch StaysOpen=false; suppress DataGrid row context menu
    // ==================================================================
    private void DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? origin = e.OriginalSource as DependencyObject;
        if (origin == null)
        {
            // not a header; ensure suppression is cleared so row context menu can open
            _suppressNextContextMenu = false;
            return;
        }

        DataGridColumnHeader? header = FindAncestor<DataGridColumnHeader>(origin);
        if (header != null && header.Column != null)
        {
            e.Handled = true;
            _suppressNextContextMenu = true; // keep suppression until ContextMenuOpening consumes it

            Point mouseScreenPos = header.PointToScreen(e.GetPosition(header));
            OpenColumnSelectorPopupAt(mouseScreenPos, header, staysOpen: true);
            _openPopupOnRightButtonUp = true;
            return;
        }

        // If click is on the column header row empty area (presenter), treat like a header click
        DataGridColumnHeadersPresenter? headersPresenter = FindAncestor<DataGridColumnHeadersPresenter>(origin);
        if (headersPresenter != null)
        {
            e.Handled = true;
            _suppressNextContextMenu = true;

            Point mouseScreenPos = headersPresenter.PointToScreen(e.GetPosition(headersPresenter));
            OpenColumnSelectorPopupAt(mouseScreenPos, headersPresenter, staysOpen: true);
            _openPopupOnRightButtonUp = true;
            return;
        }

        // Fallback: if origin did not resolve, but pointer is within headers presenter bounds, treat as header
        if (sender is DataGrid dg)
        {
            DataGridColumnHeadersPresenter? presenter = FindDescendant<DataGridColumnHeadersPresenter>(dg);
            if (presenter != null)
            {
                Point posInPresenter = e.GetPosition(presenter);
                if (posInPresenter.X >= 0 && posInPresenter.X <= presenter.ActualWidth &&
                    posInPresenter.Y >= 0 && posInPresenter.Y <= presenter.ActualHeight)
                {
                    e.Handled = true;
                    _suppressNextContextMenu = true;

                    Point mouseScreenPos = presenter.PointToScreen(posInPresenter);
                    OpenColumnSelectorPopupAt(mouseScreenPos, presenter, staysOpen: true);
                    _openPopupOnRightButtonUp = true;
                    return;
                }
            }
        }

        // not in header region at all; allow normal row context menu
        _suppressNextContextMenu = false;
    }

    private void DataGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_openPopupOnRightButtonUp)
        {
            e.Handled = true;
            _openPopupOnRightButtonUp = false;
            // Do NOT clear _suppressNextContextMenu here; let ContextMenuOpening consume it reliably

            if (_columnSelectorPopup != null)
            {
                _columnSelectorPopup.StaysOpen = false;
            }
        }
        else if (_suppressNextContextMenu)
        {
            e.Handled = true;
            // Keep suppression until ContextMenuOpening fires
        }
    }

    private void DataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_suppressNextContextMenu)
        {
            e.Handled = true;
            _suppressNextContextMenu = false; // consume suppression after reliably blocking
        }
    }

    private void OpenColumnSelectorPopupAt(Point screenPos, DependencyObject dpiContext, bool staysOpen)
    {
        CloseColumnPopup();

        ColumnSelectorViewModel selectorVm = new ColumnSelectorViewModel();

        // Pre-populate the selector with the current visible columns for the target DataGrid
        DataGrid? targetGrid = FindAncestor<DataGrid>(dpiContext);
        if (targetGrid != null)
        {
            LibraryTab? libTab = targetGrid.DataContext as LibraryTab;
            if (libTab != null)
            {
                List<string> current = libTab.VisibleColumns ?? new List<string>();
                foreach (ColumnSelectorItem item in selectorVm.Columns)
                    item.IsVisible = current.Contains(item.PropertyName);
            }
            else if (DataContext is MediaTabViewModel vm)
            {
                List<string> current = vm.SelectedColumnNames;
                foreach (ColumnSelectorItem item in selectorVm.Columns)
                    item.IsVisible = current.Contains(item.PropertyName);
            }
        }

        _columnSelectorPopup = new Popup
        {
            Placement = PlacementMode.Absolute,
            StaysOpen = staysOpen,
            AllowsTransparency = true,
            Child = new Border
            {
                Child = new ColumnSelectorPopup(selectorVm)
            }
        };

        PresentationSource? src = PresentationSource.FromVisual(dpiContext as Visual);
        if (src?.CompositionTarget != null)
        {
            Matrix transformFromDevice = src.CompositionTarget.TransformFromDevice;
            Point dip = transformFromDevice.Transform(screenPos);
            _columnSelectorPopup.HorizontalOffset = dip.X;
            _columnSelectorPopup.VerticalOffset = dip.Y;
        }
        else
        {
            _columnSelectorPopup.HorizontalOffset = screenPos.X;
            _columnSelectorPopup.VerticalOffset = screenPos.Y;
        }

        _columnSelectorPopup.IsOpen = true;

        // Attach global outside-click closer while popup is open
        if (Application.Current?.MainWindow != null)
        {
            Application.Current.MainWindow.PreviewMouseDown += MainWindow_PreviewMouseDown_ClosePopup;
        }
    }

    private void CloseColumnPopup()
    {
        if (_columnSelectorPopup != null)
        {
            _columnSelectorPopup.IsOpen = false;
            _columnSelectorPopup = null;
        }

        if (Application.Current?.MainWindow != null)
        {
            Application.Current.MainWindow.PreviewMouseDown -= MainWindow_PreviewMouseDown_ClosePopup;
        }
    }

    private void MainWindow_PreviewMouseDown_ClosePopup(object? sender, MouseButtonEventArgs e)
    {
        if (_columnSelectorPopup?.IsOpen == true)
        {
            // Any click in the main window should close the popup (clicks inside popup do not route here)
            _columnSelectorPopup.IsOpen = false;
            _columnSelectorPopup = null;

            // clear suppression so normal context menus work after closing
            _suppressNextContextMenu = false;

            if (Application.Current?.MainWindow != null)
            {
                Application.Current.MainWindow.PreviewMouseDown -= MainWindow_PreviewMouseDown_ClosePopup;
            }
        }
    }

    private void DataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        => e.Row.Header = (e.Row.GetIndex() + 1).ToString();

    private void AlbumCoverHover_MouseEnter(object sender, MouseEventArgs e)
    {
        // Lazily load the cover so the hover ToolTip has an image to show.
        // The ToolTip opens/closes on its own; no popup tree-walk needed.
        if (sender is FrameworkElement { DataContext: MediaFile mediaFile } &&
            mediaFile.AlbumCover == null &&
            mediaFile.HasEmbeddedCover &&
            mediaFile.UnsupportedCoverFormat == null)
        {
            mediaFile.LoadAlbumCover();
        }
    }

    private void RegenerateCurrentColumns()
    {
        if (GetActiveDataGrid() is DataGrid dg)
            RegenerateColumns(dg);
    }

    private void OnUpdateColumns(UpdateColumnsMessage m)
    {
        MediaTabViewModel? vm = DataContext as MediaTabViewModel;

        // Determine target DataGrid (active) to decide whether to apply to library or playlists
        DataGrid? active = GetActiveDataGrid();
        LibraryTab? libTab = active?.DataContext as LibraryTab;

        if (libTab != null)
        {
            // Apply selection to the library tab and persist immediately
            libTab.VisibleColumns = new List<string>(m.SelectedColumns);
            ISettingsManager? settingsManager = App.AppHost?.Services?.GetService<ISettingsManager>();
            if (settingsManager != null)
            {
                settingsManager.Settings.LibraryVisibleColumns = new List<string>(m.SelectedColumns);
                settingsManager.SaveSettings(nameof(AppSettings.LibraryVisibleColumns));
            }
        }
        else
        {
            if (vm != null)
                vm.ApplySelectedColumns(m.SelectedColumns);
        }

        // Only regenerate columns when there is a live PlaylistTabsViewModel (guard against
        // unit-test scenarios where DataContext is not set but the message handler fires).
        if (vm != null)
            RegenerateCurrentColumns();

        if (_columnSelectorPopup?.IsOpen == true)
            _columnSelectorPopup.IsOpen = false;
    }

    private void PlaylistTabs_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MediaTabViewModel viewModel)
        {
            // LoadPlaylistTabs is now called from LibraryLoaded event when data is ready
        }
        else
        {
            _logger.LogError("PlaylistTabs: DataContext is not PlaylistTabsViewModel, type: {Type}", DataContext?.GetType().FullName ?? "null");
        }
    }

    private void DataGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid dg)
            return;

        if (DataContext is not MediaTabViewModel vm)
            return;

        // With the caching TabControl, the DataGrid stays alive across tab switches.
        // Loaded fires on first creation AND on re-attachment — skip expensive work on re-attachment.
        if (!_initializedGrids.Add(dg))
        {
            // Grid already initialized. Selection persists (we never cleared it), so
            // no need to ScrollIntoView — the row is already where the user left it.
            return;
        }

        // RegenerateColumns needs the template to be applied, which is already done by Loaded event
        RegenerateColumns(dg);
        vm.OnDataGridLoaded(sender, e);

        ISettingsManager? sm = App.AppHost?.Services?.GetService<ISettingsManager>();

        // Restore + wire sort for playlist DataGrids.
        if (dg.DataContext is PlaylistTab playlistTab)
        {
            dg.Sorting -= PlaylistDataGrid_Sorting;
            dg.Sorting += PlaylistDataGrid_Sorting;

            if (sm != null && sm.Settings.PlaylistSortStates.TryGetValue(playlistTab.Name, out AppSettings.PlaylistSortState? sortState))
            {
                List<SortDescription> savedSorts = BuildSortDescriptions(
                    sortState.SortDescriptions,
                    sortState.SortColumn,
                    sortState.SortDirection);

                if (savedSorts.Count > 0)
                {
                    ApplySortDescriptionsToGrid(dg, savedSorts);
                }
            }
        }
        else if (dg.DataContext is LibraryTab libraryTab && sm != null)
        {
            List<SortDescription> savedSorts = BuildSortDescriptions(
                sm.Settings.LibrarySortDescriptions,
                sm.Settings.LibrarySortColumn,
                sm.Settings.LibrarySortDirection);

            if (savedSorts.Count > 0)
            {
                libraryTab.SetSortOrder(savedSorts);
                UpdateColumnSortHeaders(dg, savedSorts);
            }
        }

        // Keep the selected row visible on load without forcing a full centering layout pass.
        if (dg.SelectedItem != null && dg.DataContext is ITabData tabData && tabData.SelectedTrack != null)
        {
            _logger.LogDebug(
                "DataGrid_Loaded: scrolling selected track {SelectedTrackId} into view, DataGrid has {ItemCount} items, TabData={TabName}",
                tabData.SelectedTrack?.Id ?? "null",
                dg.Items.Count,
                tabData is LibraryTab ? "LibraryTab" : (tabData is PlaylistTab pt ? $"PlaylistTab:{pt.Name}" : "Unknown"));

            dg.ScrollIntoView(dg.SelectedItem);
        }

        // Fix header scroll binding issue - deferred to Background priority to let layout settle
        Dispatcher.BeginInvoke(() => FixHeaderScrollShimmy(dg), DispatcherPriority.Background);

        // Attach event handlers for column header and keyboard interactions
        dg.AddHandler(UIElement.PreviewMouseRightButtonUpEvent,
            new MouseButtonEventHandler(DataGrid_PreviewMouseRightButtonUp),
            handledEventsToo: true);

        dg.AddHandler(ContextMenuService.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(DataGrid_ContextMenuOpening),
            handledEventsToo: true);

        // Ensure right-click anywhere on the column header row opens the column selector
        dg.AddHandler(UIElement.PreviewMouseRightButtonDownEvent,
            new MouseButtonEventHandler(DataGrid_PreviewMouseRightButtonDown),
            handledEventsToo: true);

        dg.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(DataGrid_PreviewMouseLeftButtonDown),
            handledEventsToo: true);

        // Keep keyboard focus inside the DataGrid when the selection is at the
        // first/last row so it cannot escape to the ScrollBar.
        dg.AddHandler(UIElement.PreviewKeyDownEvent,
            new KeyEventHandler(DataGrid_PreviewKeyDown),
            handledEventsToo: false);
    }

    private void PlaylistDataGrid_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (!_isExplicitCentering)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Keeps keyboard focus inside the DataGrid when navigation reaches the first or last row.
    /// Without this, pressing ↓ on the last row lets WPF shift focus to the vertical ScrollBar,
    /// after which ↑/↓ moves the scroll thumb instead of the selection.
    /// </summary>
    private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid dg)
            return;

        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End ||
            (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control))
        {
            _allowSelectionSyncFromUserInput = true;
        }

        // Ctrl+Home — select and scroll to first row
        if (e.Key == Key.Home && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (dg.Items.Count > 0)
            {
                dg.SelectedIndex = 0;
                dg.ScrollIntoView(dg.Items[0]);
                DataGridRow? row = GetRowAt(dg, 0);
                row?.Focus();
            }
            e.Handled = true;
            return;
        }

        // Ctrl+End — select and scroll to last row
        if (e.Key == Key.End && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (dg.Items.Count > 0)
            {
                dg.SelectedIndex = dg.Items.Count - 1;
                dg.ScrollIntoView(dg.Items[^1]);
                DataGridRow? row = GetRowAt(dg, dg.Items.Count - 1);
                row?.Focus();
            }
            e.Handled = true;
            return;
        }

        // Ctrl+A — select all rows
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            dg.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Down && e.Key != Key.Up)
            return;

        // If focus has already escaped to a ScrollBar inside the DataGrid, reclaim it.
        if (Keyboard.FocusedElement is ScrollBar sb &&
            FindAncestor<DataGrid>(sb) == dg)
        {
            DataGridRow? row = GetSelectedRow(dg) ?? GetRowAt(dg, 0);
            row?.Focus();
            return;
        }

        // Prevent navigation past the last row (↓) or past the first row (↑).
        if (e.Key == Key.Down && dg.SelectedIndex >= dg.Items.Count - 1)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && dg.SelectedIndex <= 0)
        {
            e.Handled = true;
        }
    }

    private static DataGridRow? GetSelectedRow(DataGrid dg)
    {
        if (dg.SelectedItem == null)
            return null;
        return dg.ItemContainerGenerator.ContainerFromItem(dg.SelectedItem) as DataGridRow;
    }

    private static DataGridRow? GetRowAt(DataGrid dg, int index)
    {
        if (index < 0 || index >= dg.Items.Count)
            return null;
        return dg.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
    }

    private void TracksTable_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        _logger.LogDebug("TracksTable_OnSelectionChanged: EVENT FIRED - AddedCount={Added}, RemovedCount={Removed}", e.AddedItems.Count, e.RemovedItems.Count);

        bool userInitiated = (_allowSelectionSyncFromUserInput || Mouse.LeftButton == MouseButtonState.Pressed) && !_tabSwitchInProgress;
        if (!userInitiated)
        {
            // Keep model-owned selection stable when the grid emits passive deselection during refresh/layout.
            if (dataGrid.DataContext is ITabData tabData && tabData.SelectedTrack != null)
            {
                MediaFile selected = tabData.SelectedTrack;

                // Check if the selected track is in the current items collection
                if (dataGrid.Items.IndexOf(selected) < 0)
                {
                    // Try to find a matching track by ID (remapping for filtered/updated collections)
                    MediaFile? remapped = dataGrid.Items.Cast<object>()
                        .OfType<MediaFile>()
                        .FirstOrDefault(t => string.Equals(t.Id, selected.Id, StringComparison.Ordinal));
                    if (remapped != null)
                    {
                        selected = remapped;
                        tabData.SelectedTrack = remapped;
                        tabData.SelectedIndex = dataGrid.Items.IndexOf(remapped);
                    }
                }

                // If the selected track is in the collection and differs from grid's current selection, sync and scroll
                if (dataGrid.Items.IndexOf(selected) >= 0)
                {
                    if (dataGrid.SelectedItem != selected)
                    {
                        dataGrid.SelectedItem = selected;
                    }
                }
            }

            // Prevent tab-switch/passive events from leaving stale gating flags set,
            // which can cause the next real click selection to be ignored.
            _allowSelectionSyncFromUserInput = false;
            _tabSwitchInProgress = false;
            return;
        }

        _logger.LogDebug("TracksTable_OnSelectionChanged: USER-INITIATED path");
        if (DataContext is MediaTabViewModel viewModel)
        {
            viewModel.OnTrackSelectionChanged(dataGrid, e);
        }

        _allowSelectionSyncFromUserInput = false;
        _tabSwitchInProgress = false;
    }

    // Set by PreviewMouseLeftButtonDown when a double-click is detected, cleared after BeginningEdit consumes it.
    private bool _suppressNextEdit = false;

    // Detect double-click early (before BeginningEdit fires) so we can suppress the edit.
    private void MusicLibraryDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            _suppressNextEdit = true;
        }
    }

    // Edit mode rules to match TagScanner behavior:
    // - Single click on an already-selected row → enters edit mode
    // - F2 → enters edit mode
    // - Double-click → plays the track (edit is suppressed)
    private void MusicLibraryDataGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (_suppressNextEdit)
        {
            _suppressNextEdit = false;
            e.Cancel = true;
        }
    }

    private void MusicLibraryDataGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && DataContext is MediaTabViewModel viewModel)
        {
            // Defer so the binding commits before we check dirty state
            Dispatcher.BeginInvoke(() => viewModel.NotifyDirtyStateChanged(),
                System.Windows.Threading.DispatcherPriority.DataBind);
        }
    }

    private static bool TryParseSortDirection(string? value, out ListSortDirection direction)
    {
        return Enum.TryParse(value, out direction);
    }

    private static List<SortDescription> BuildSortDescriptions(
        IEnumerable<AppSettings.SortDescriptionState>? sortDescriptions,
        string? fallbackColumn,
        string? fallbackDirection)
    {
        List<SortDescription> result = new();

        if (sortDescriptions != null)
        {
            foreach (AppSettings.SortDescriptionState sortState in sortDescriptions)
            {
                if (string.IsNullOrWhiteSpace(sortState.SortColumn) || !TryParseSortDirection(sortState.SortDirection, out ListSortDirection direction))
                    continue;

                result.Add(new SortDescription(sortState.SortColumn, direction));
            }
        }

        if (result.Count == 0 &&
            !string.IsNullOrWhiteSpace(fallbackColumn) &&
            TryParseSortDirection(fallbackDirection, out ListSortDirection fallbackDir))
        {
            result.Add(new SortDescription(fallbackColumn, fallbackDir));
        }

        return result;
    }

    private void ApplySortDescriptionsToGrid(DataGrid dataGrid, IEnumerable<SortDescription> sortDescriptions)
    {
        ICollectionView? view = CollectionViewSource.GetDefaultView(dataGrid.ItemsSource);
        if (view == null)
            return;

        List<SortDescription> sorts = sortDescriptions.ToList();

        view.SortDescriptions.Clear();
        foreach (SortDescription sortDescription in sorts)
        {
            view.SortDescriptions.Add(sortDescription);
        }

        UpdateColumnSortHeaders(dataGrid, sorts);
    }

    /// <summary>
    /// Updates each column's SortDirection glyph and "(n)" multi-sort order indicator
    /// without touching the view's SortDescriptions.
    /// </summary>
    private void UpdateColumnSortHeaders(DataGrid dataGrid, IEnumerable<SortDescription> sortDescriptions)
    {
        List<SortDescription> sorts = sortDescriptions.ToList();

        Dictionary<string, ListSortDirection> sortDirectionByPath = sorts
            .GroupBy(s => s.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Direction, StringComparer.Ordinal);

        HashSet<DataGridColumn> currentColumns = dataGrid.Columns.ToHashSet();
        foreach (DataGridColumn trackedColumn in _baseHeaderByColumn.Keys.Where(c => !currentColumns.Contains(c)).ToList())
        {
            _baseHeaderByColumn.Remove(trackedColumn);
        }

        foreach (DataGridColumn column in dataGrid.Columns)
        {
            if (!_baseHeaderByColumn.ContainsKey(column))
            {
                _baseHeaderByColumn[column] = column.Header;
            }

            if (!string.IsNullOrWhiteSpace(column.SortMemberPath) &&
                sortDirectionByPath.TryGetValue(column.SortMemberPath, out ListSortDirection direction))
            {
                column.SortDirection = direction;
            }
            else
            {
                column.SortDirection = null;
            }
        }

        Dictionary<string, int> sortOrderByPath = sorts
            .Select((sort, index) => new { sort.PropertyName, Order = index + 1 })
            .ToDictionary(x => x.PropertyName, x => x.Order, StringComparer.Ordinal);

        foreach (DataGridColumn column in dataGrid.Columns)
        {
            object? baseHeader = _baseHeaderByColumn.TryGetValue(column, out object? value) ? value : column.Header;
            string baseHeaderText = baseHeader?.ToString() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(column.SortMemberPath) &&
                sortOrderByPath.TryGetValue(column.SortMemberPath, out int order) &&
                !string.IsNullOrWhiteSpace(baseHeaderText))
            {
                column.Header = $"{baseHeaderText} ({order})";
            }
            else
            {
                column.Header = baseHeader;
            }
        }
    }

    private static List<AppSettings.SortDescriptionState> SerializeSortDescriptions(IEnumerable<SortDescription> sortDescriptions)
    {
        return sortDescriptions
            .Select(s => new AppSettings.SortDescriptionState
            {
                SortColumn = s.PropertyName,
                SortDirection = s.Direction.ToString()
            })
            .ToList();
    }

    private void MusicLibraryDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (sender is not DataGrid dataGrid || dataGrid.DataContext is not LibraryTab libraryTab)
            return;

        string sortPath = e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(sortPath))
            return;

        e.Handled = true;

        ListSortDirection newDirection = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        bool isAdditiveSort = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        // Base additive sorts on the LibraryTab's active sort, not the view's SortDescriptions
        // (which is intentionally empty — the sort is applied as a pre-sorted snapshot).
        List<SortDescription> updatedSorts;
        if (isAdditiveSort)
        {
            updatedSorts = libraryTab.ActiveSorts
                .Where(s => !string.Equals(s.PropertyName, sortPath, StringComparison.Ordinal))
                .ToList();
            updatedSorts.Add(new SortDescription(sortPath, newDirection));
        }
        else
        {
            updatedSorts =
            [
                new SortDescription(sortPath, newDirection)
            ];
        }

        libraryTab.SetSortOrder(updatedSorts);
        UpdateColumnSortHeaders(dataGrid, updatedSorts);

        ISettingsManager? settingsManager = App.AppHost?.Services?.GetService<ISettingsManager>();
        if (settingsManager == null)
            return;

        settingsManager.Settings.LibrarySortDescriptions = SerializeSortDescriptions(updatedSorts);

        if (updatedSorts.Count > 0)
        {
            settingsManager.Settings.LibrarySortColumn = updatedSorts[0].PropertyName;
            settingsManager.Settings.LibrarySortDirection = updatedSorts[0].Direction.ToString();
        }
        else
        {
            settingsManager.Settings.LibrarySortColumn = string.Empty;
            settingsManager.Settings.LibrarySortDirection = string.Empty;
        }

        settingsManager.SaveSettings(nameof(AppSettings.LibrarySortDescriptions));
    }

    private void PlaylistDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (sender is not DataGrid dataGrid || dataGrid.DataContext is not PlaylistTab playlistTab)
            return;

        string sortPath = e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(sortPath))
            return;

        ICollectionView? view = CollectionViewSource.GetDefaultView(dataGrid.ItemsSource);
        if (view == null)
            return;

        e.Handled = true;

        ListSortDirection newDirection = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        bool isAdditiveSort = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        List<SortDescription> updatedSorts;
        if (isAdditiveSort)
        {
            updatedSorts = view.SortDescriptions
                .Where(s => !string.Equals(s.PropertyName, sortPath, StringComparison.Ordinal))
                .ToList();
            updatedSorts.Add(new SortDescription(sortPath, newDirection));
        }
        else
        {
            updatedSorts =
            [
                new SortDescription(sortPath, newDirection)
            ];
        }

        ApplySortDescriptionsToGrid(dataGrid, updatedSorts);

        ISettingsManager? settingsManager = App.AppHost?.Services?.GetService<ISettingsManager>();
        if (settingsManager == null)
            return;

        AppSettings.PlaylistSortState playlistSortState = new()
        {
            SortDescriptions = SerializeSortDescriptions(updatedSorts)
        };

        if (updatedSorts.Count > 0)
        {
            playlistSortState.SortColumn = updatedSorts[0].PropertyName;
            playlistSortState.SortDirection = updatedSorts[0].Direction.ToString();
        }

        settingsManager.Settings.PlaylistSortStates[playlistTab.Name] = playlistSortState;
        settingsManager.SaveSettings(nameof(AppSettings.PlaylistSortStates));
    }

    private void PlaylistRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Ignore non-left button and header/column header double-clicks
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None)
        {
            return;
        }

        DependencyObject origin = (DependencyObject)e.OriginalSource;

        DataGridColumnHeader? header = FindAncestor<DataGridColumnHeader>(origin);
        if (header != null)
        {
            return;
        }

        // Ensure the row under the mouse is selected before playing
        DataGrid? dataGrid = sender as DataGrid ?? FindDescendant<DataGrid>(Tabs123);
        if (dataGrid == null)
        {
            return;
        }

        DataGridRow? row = FindAncestor<DataGridRow>(origin);
        if (row == null)
        {
            return;
        }

        try
        {
            dataGrid.SelectedItems.Clear();
            dataGrid.SelectedItem = row.Item;
            dataGrid.ScrollIntoView(row.Item);
            dataGrid.Focus();
        }
        catch { }

        // Synchronously update VM selection and active track BEFORE sending play message
        if (DataContext is MediaTabViewModel viewModel)
        {
            viewModel.OnDoubleClickDataGrid();
        }

        WeakReferenceMessenger.Default.Send(new DataGridPlayMessage(PlaybackState.Playing));
        e.Handled = true;
    }

    private void PlaylistDataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (DataContext is MediaTabViewModel viewModel && viewModel.SelectedTab != null)
        {
            _tabVerticalOffsets[viewModel.SelectedTab] = e.VerticalOffset;
        }
    }

    private void OnGoToActiveTrack(bool value)
    {
        if (!value)
        {
            return;
        }

        if (DataContext is not MediaTabViewModel viewModel)
        {
            return;
        }

        MediaFile? active = viewModel.ActiveTrack;
        if (active == null)
        {
            return;
        }

        int tabIndex = viewModel.SelectedTabIndex;
        int trackIndex = -1;
        MediaFile? activeInTab = null;

        if (tabIndex >= 0 && tabIndex < viewModel.TabList.Count)
        {
            activeInTab = viewModel.TabList[tabIndex].Tracks.FirstOrDefault(t => t.Id == active.Id);
            if (activeInTab != null)
            {
                trackIndex = viewModel.TabList[tabIndex].Tracks.IndexOf(activeInTab);
            }
        }

        if (trackIndex < 0)
        {
            for (int i = 0; i < viewModel.TabList.Count; i++)
            {
                MediaFile? match = viewModel.TabList[i].Tracks.FirstOrDefault(t => t.Id == active.Id);
                if (match != null)
                {
                    tabIndex = i;
                    activeInTab = match;
                    trackIndex = viewModel.TabList[i].Tracks.IndexOf(match);
                    break;
                }
            }
        }

        if (tabIndex < 0 || tabIndex >= viewModel.TabList.Count || trackIndex < 0 || activeInTab == null)
        {
            return;
        }

        viewModel.SelectedTabIndex = tabIndex;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            DataGrid? dataGrid = GetActiveDataGrid();
            if (dataGrid == null)
            {
                return;
            }

            try
            {
                dataGrid.SelectionChanged -= TracksTable_OnSelectionChanged;
                try
                {
                    dataGrid.SelectedItem = activeInTab;
                    dataGrid.SelectedIndex = trackIndex;
                    dataGrid.ScrollIntoView(activeInTab);

                    _isExplicitCentering = true;
                    try
                    {
                        _logger.LogDebug("OnGoToActiveTrack: calling CenterItemInDataGrid");
                        CenterItemInDataGrid(dataGrid, activeInTab);
                    }
                    finally
                    {
                        _isExplicitCentering = false;
                    }

                    dataGrid.Focus();
                }
                finally
                {
                    dataGrid.SelectionChanged += TracksTable_OnSelectionChanged;
                }

                // Ensure the view model selection matches what the grid now shows.
                viewModel.SelectedTrackIndex = trackIndex;
                viewModel.SelectedTrack = activeInTab;
            }
            catch
            {
            }
        }), DispatcherPriority.Render);
    }

    private void OnActiveTrackChanged(MediaFile? track)
    {
        if (track == null)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnActiveTrackChanged(track)), DispatcherPriority.Render);
            return;
        }

        // The active track changed (playback started/changed).
        // Reveal it in the UI without forcing selection to follow.
        // (Selection represents user intent; active track represents playback state.)
        if (DataContext is not MediaTabViewModel viewModel)
        {
            return;
        }

        MediaFile? activeInTab = null;
        int tabIndex = -1;
        int trackIndex = -1;

        string? preferredTabName = viewModel.ActiveTabName;
        if (!string.IsNullOrWhiteSpace(preferredTabName))
        {
            for (int i = 0; i < viewModel.TabList.Count; i++)
            {
                if (!string.Equals(viewModel.TabList[i].Name, preferredTabName, StringComparison.Ordinal))
                {
                    continue;
                }

                tabIndex = i;
                activeInTab = viewModel.TabList[i].Tracks.FirstOrDefault(t => t.Id == track.Id);
                if (activeInTab != null)
                {
                    trackIndex = viewModel.TabList[i].Tracks.IndexOf(activeInTab);
                }

                break;
            }
        }

        if (trackIndex < 0)
        {
            if (tabIndex >= 0 && tabIndex < viewModel.TabList.Count)
            {
                activeInTab = viewModel.TabList[tabIndex].Tracks.FirstOrDefault(t => t.Id == track.Id);
                if (activeInTab != null)
                {
                    trackIndex = viewModel.TabList[tabIndex].Tracks.IndexOf(activeInTab);
                }
            }
        }

        if (trackIndex < 0)
        {
            for (int i = 0; i < viewModel.TabList.Count; i++)
            {
                MediaFile? match = viewModel.TabList[i].Tracks.FirstOrDefault(t => t.Id == track.Id);
                if (match != null)
                {
                    tabIndex = i;
                    activeInTab = match;
                    trackIndex = viewModel.TabList[i].Tracks.IndexOf(match);
                    break;
                }
            }
        }

        if (activeInTab == null || trackIndex < 0 || tabIndex < 0 || tabIndex >= viewModel.TabList.Count)
        {
            return;
        }

        // Switch to the active playback tab if needed
        if (tabIndex != viewModel.SelectedTabIndex)
        {
            viewModel.SelectedTabIndex = tabIndex;
            // Defer scrolling to after tab switch and layout
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Just scroll to reveal the active track; do NOT change selection.
                RevealActiveTrack(activeInTab, trackIndex);
            }), DispatcherPriority.Render);
        }
        else
        {
            // Already on the right tab, reveal immediately
            RevealActiveTrack(activeInTab, trackIndex);
        }
    }

    /// <summary>
    /// Reveals (scrolls into view) the active track without changing selection.
    /// Selection represents user intent; this just makes the active track visible.
    /// </summary>
    private void RevealActiveTrack(MediaFile track, int trackIndex)
    {
        if (DataContext is not MediaTabViewModel viewModel)
        {
            return;
        }

        DataGrid? dataGrid = GetActiveDataGrid();
        if (dataGrid == null)
        {
            return;
        }

        try
        {
            dataGrid.ScrollIntoView(track);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RevealActiveTrack: Exception during ScrollIntoView");
        }
    }

    private static void CenterItemInDataGrid(DataGrid dataGrid, object item)
    {
        dataGrid.UpdateLayout();
        dataGrid.ScrollIntoView(item);
        dataGrid.UpdateLayout();

        ScrollViewer? sv = FindDescendant<ScrollViewer>(dataGrid);
        if (sv == null)
        {
            return;
        }

        bool logicalScroll = ScrollViewer.GetCanContentScroll(dataGrid);
        if (logicalScroll)
        {
            int index = dataGrid.Items.IndexOf(item);
            if (index < 0)
            {
                return;
            }

            int itemsInViewport = (int)Math.Round(sv.ViewportHeight);
            int targetTopIndex = Math.Max(0, index - (itemsInViewport / 2));
            sv.ScrollToVerticalOffset(targetTopIndex);
        }
        else
        {
            DataGridRow? row = dataGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
            if (row == null)
            {
                dataGrid.ScrollIntoView(item);
                dataGrid.UpdateLayout();
                row = dataGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                if (row == null)
                {
                    return;
                }
            }

            GeneralTransform transform = row.TransformToAncestor(sv);
            Point rowPos = transform.Transform(new Point(0, 0));
            double rowCenter = rowPos.Y + (row.ActualHeight / 2.0);
            double targetCenter = sv.ViewportHeight / 2.0;
            double delta = rowCenter - targetCenter;
            sv.ScrollToVerticalOffset(sv.VerticalOffset + delta);
        }
    }

    private void CenterOnTrack(DataGrid dataGrid, MediaFile item)
    {
        if (dataGrid.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
        {
            // If rows aren't ready yet, wait for it
            dataGrid.ItemContainerGenerator.StatusChanged += (object? _, EventArgs __) => CenterOnTrack(dataGrid, item);
            return;
        }

        dataGrid.ScrollIntoView(item);
        dataGrid.UpdateLayout();

        //if (FindDescendant<ScrollViewer>(dataGrid) is not ScrollViewer sv)
        //    return;

        //if (dataGrid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row)
        //    return;

        //bool logicalScroll = ScrollViewer.GetCanContentScroll(dataGrid);

        //if (logicalScroll)
        //{
        //    int index = dataGrid.Items.IndexOf(item);
        //    int itemsInViewport = (int)Math.Round(sv.ViewportHeight);
        //    int targetTopIndex = Math.Max(0, index - (itemsInViewport / 2));
        //    sv.ScrollToVerticalOffset(targetTopIndex);
        //}
        //else
        //{
        //    GeneralTransform transform = row.TransformToAncestor(sv);
        //    Point rowPos = transform.Transform(new Point(0, 0));
        //    double rowCenter = rowPos.Y + (row.ActualHeight / 2.0);
        //    double targetCenter = sv.ViewportHeight / 2.0;
        //    double delta = rowCenter - targetCenter;
        //    sv.ScrollToVerticalOffset(sv.VerticalOffset + delta);
        //}
    }

    internal DataGrid? GetActiveDataGrid()
    {
        // Primary path: visual tree walk (works at runtime when tabs are rendered)
        DataGrid? vt = FindDescendant<DataGrid>(Tabs123);
        if (vt != null)
            return vt;

        // Fallback: logical tree via selected TabItem content (works in unit tests
        // where the TabControl is not part of a rendered visual tree)
        if (Tabs123?.SelectedItem is TabItem selected && selected.Content is DataGrid dg)
            return dg;
        if (Tabs123?.Items.Count > 0 && Tabs123.Items[0] is TabItem first && first.Content is DataGrid dgFirst)
            return dgFirst;

        return null;
    }

    // The filler element inside DataGridColumnHeadersPresenter binds its Width to
    // CellsPanelHorizontalOffset via the DataGrid's default control template.  When
    // the user scrolls horizontally that value briefly goes negative, WPF rejects it
    // (Width < 0 is invalid), fires a binding error, triggers a re-measure and
    // produces the visible shimmy.  We can't reach this element with an implicit
    // style because it lives inside the DataGrid's ControlTemplate.
    //
    // Instead of guessing the element type or name (which varies across WPF themes),
    // we force template application on both the DataGrid and its presenter, then walk
    // every descendant looking for the specific Width binding on CellsPanelHorizontalOffset
    // and replace it with one that clamps the value via NonNegativeConverter.
    private static void FixHeaderScrollShimmy(DataGrid dg)
    {
        dg.ApplyTemplate();

        // The filler Button lives inside the DataGrid's *internal ScrollViewer* template —
        // it is a SIBLING of DataGridColumnHeadersPresenter, not a child of it.
        // Force the ScrollViewer's template too, then walk the entire DataGrid subtree.
        ScrollViewer? sv = dg.Template?.FindName("DG_ScrollViewer", dg) as ScrollViewer;
        sv?.ApplyTemplate();

        ReplaceShimmyBinding(dg);
    }

    private static void ReplaceShimmyBinding(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);

            if (child is FrameworkElement fe)
            {
                BindingExpression? expr = fe.GetBindingExpression(FrameworkElement.WidthProperty);
                if (expr?.ParentBinding.Path?.Path == "CellsPanelHorizontalOffset")
                {
                    Binding clamped = new("CellsPanelHorizontalOffset")
                    {
                        RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1),
                        Converter = new NonNegativeConverter()
                    };
                    fe.SetBinding(FrameworkElement.WidthProperty, clamped);
                    return;
                }
            }

            ReplaceShimmyBinding(child);
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null)
            return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T t)
                return t;
            T? result = FindDescendant<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }

    private static TAncestor? FindAncestor<TAncestor>(DependencyObject? child) where TAncestor : DependencyObject
    {
        int depth = 0;
        const int maxDepth = 100; // Prevent infinite loops

        while (child != null && depth < maxDepth)
        {
            depth++;
            DependencyObject? parent = LogicalTreeHelper.GetParent(child);
            if (parent == null)
            {
                parent = VisualTreeHelper.GetParent(child);
            }

            if (parent == child)
            {
                // Circular reference detected
                return null;
            }

            child = parent;

            if (child is TAncestor ancestor)
            {
                return ancestor;
            }
        }
        return null;
    }

    // --- Tab drag & drop reordering ---
    private void TabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Set flag to suppress mouse-state check during tab switch.
        // This will be cleared at the end of TracksTable_OnSelectionChanged.
        _tabSwitchInProgress = true;

        DependencyObject source = (DependencyObject)sender;
        TabItem? tabItem = source as TabItem ?? FindAncestor<TabItem>(source);
        if (tabItem == null)
        {
            return;
        }

        // Suppress scroll jump when clicking the already-selected tab
        if (tabItem.IsSelected)
        {
            e.Handled = true;
            if (DataContext is MediaTabViewModel vm && vm.SelectedTrack != null)
            {
                // Get the DataGrid from the selected tab's content, not from visual tree search
                if (Tabs123?.SelectedContent != null)
                {
                    DataGrid? dg = FindDescendant<DataGrid>(Tabs123.SelectedContent as DependencyObject ?? Tabs123);
                    if (dg != null)
                    {
                        _logger.LogDebug("TabItem_PreviewMouseLeftButtonDown: centering {TrackId}", vm.SelectedTrack.Id ?? "null");
                        _isExplicitCentering = true;
                        try
                        {
                            CenterItemInDataGrid(dg, vm.SelectedTrack);
                        }
                        finally
                        {
                            _isExplicitCentering = false;
                        }
                    }
                }
            }
            return; // do not initiate drag
        }

        // Only allow dragging PlaylistTab, NOT MusicLibraryTab
        if (tabItem.DataContext is PlaylistTab tab)
        {
            _draggedTab = tab; // only set drag when not the already-selected tab
        }
        else if (tabItem.DataContext is LibraryTab)
        {
            _draggedTab = null; // Library cannot be dragged
        }
    }

    private void TabItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedTab == null)
        {
            return;
        }

        DependencyObject source = (DependencyObject)sender;
        TabItem? tabItem = source as TabItem ?? FindAncestor<TabItem>(source);
        DependencyObject dragSource = tabItem ?? source;

        DataObject data = new DataObject(typeof(PlaylistTab), _draggedTab);
        DragDrop.DoDragDrop(dragSource, data, DragDropEffects.Move);
    }

    private void TabItem_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(PlaylistTab)))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;

        DependencyObject source = (DependencyObject)sender;
        TabItem? targetTabItem = source as TabItem ?? FindAncestor<TabItem>(source);
        if (targetTabItem != null && targetTabItem.DataContext is PlaylistTab)
        {
            ShowDropIndicator(targetTabItem, e);
        }
    }

    private void TabItem_DragLeave(object sender, DragEventArgs e)
    {
        ClearDropIndicator();
    }

    private async void TabItem_Drop(object sender, DragEventArgs e)
    {
        ClearDropIndicator();

        if (!e.Data.GetDataPresent(typeof(PlaylistTab)) || _draggedTab == null)
        {
            return;
        }

        if (DataContext is not MediaTabViewModel viewModel)
        {
            return;
        }

        PlaylistTab droppedTab = (PlaylistTab)e.Data.GetData(typeof(PlaylistTab))!;
        DependencyObject source = (DependencyObject)sender;
        TabItem? targetTabItem = source as TabItem ?? FindAncestor<TabItem>(source);
        if (targetTabItem == null || targetTabItem.DataContext is not PlaylistTab targetTab)
        {
            return;
        }

        int fromIndex = FindTabIndex(droppedTab);
        int targetIndex = FindTabIndex(targetTab);
        if (fromIndex < 0 || targetIndex < 0)
        {
            return;
        }

        // Prevent dropping at index 0 (Music Library tab must always be first)
        if (targetIndex == 0)
        {
            _logger.LogWarning("Cannot drop tab at index 0 - Music Library tab must remain first");
            return;
        }

        // Determine intended insertion position relative to target (before/after)
        Point pos = e.GetPosition(targetTabItem);
        bool insertAfter = pos.X > targetTabItem.ActualWidth / 2.0;
        int count = viewModel.TabList.Count;

        // Intended index in original list (can be equal to count when inserting after last)
        int intended = targetIndex + (insertAfter ? 1 : 0);
        if (intended > count)
        {
            intended = count;
        }

        // Prevent moving to index 0 (before Music Library)
        if (intended == 0)
        {
            intended = 1;
        }

        // Adjust for removal shifting indices when moving forward
        int adjusted = fromIndex < intended ? intended - 1 : intended;

        // Clamp to valid range [1, count-1] (index 0 is reserved for Music Library)
        if (adjusted < 1)
        {
            adjusted = 1;
        }

        if (adjusted >= count)
        {
            adjusted = count - 1;
        }

        if (fromIndex == adjusted)
        {
            _draggedTab = null;
            return;
        }

        await viewModel.ReorderTabsCommand.ExecuteAsync((fromIndex, adjusted));

        _draggedTab = null;
    }

    private int FindTabIndex(PlaylistTab tab)
    {
        if (DataContext is MediaTabViewModel vm)
        {
            for (int i = 0; i < vm.TabList.Count; i++)
            {
                if (ReferenceEquals(vm.TabList[i], tab))
                {
                    return i;
                }
            }
        }
        return -1;
    }

    private void ShowDropIndicator(TabItem target, DragEventArgs e)
    {
        ClearDropIndicator();

        Point pos = e.GetPosition(target);
        bool onLeft = pos.X <= target.ActualWidth / 2.0;
        _dropIndicatorAdorner = new DropIndicatorAdorner(target, onLeft);
        AdornerLayer? adornerLayer = AdornerLayer.GetAdornerLayer(target);
        adornerLayer?.Add(_dropIndicatorAdorner);
    }

    private void ClearDropIndicator()
    {
        if (_dropIndicatorAdorner != null)
        {
            AdornerLayer? adornerLayer = AdornerLayer.GetAdornerLayer(_dropIndicatorAdorner.AdornedElement);
            adornerLayer?.Remove(_dropIndicatorAdorner);
            _dropIndicatorAdorner = null;
        }
    }

    private void DataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dg)
        {
            return;
        }

        _allowSelectionSyncFromUserInput = true;

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None)
        {
            return;
        }

        DependencyObject? origin = e.OriginalSource as DependencyObject;
        if (origin == null)
        {
            return;
        }

        DataGridRow? row = FindAncestor<DataGridRow>(origin);
        if (row == null)
        {
            return;
        }

        if (!dg.SelectedItems.Contains(row.Item) || dg.SelectedItems.Count != 1)
        {
            try
            {
                dg.SelectedItems.Clear();
                dg.SelectedItem = row.Item;
                dg.Focus();
            }
            catch
            {
            }
        }
    }
}

internal class DropIndicatorAdorner : Adorner
{
    private readonly bool _onLeft;

    public DropIndicatorAdorner(UIElement adornedElement, bool onLeft) : base(adornedElement)
    {
        _onLeft = onLeft;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (AdornedElement is TabItem tabItem)
        {
            double x = _onLeft ? 0 : tabItem.ActualWidth;
            Point startPoint = new Point(x, 0);
            Point endPoint = new Point(x, tabItem.ActualHeight);

            Pen pen = new Pen(Brushes.DodgerBlue, 3);
            drawingContext.DrawLine(pen, startPoint, endPoint);
        }
    }
}
