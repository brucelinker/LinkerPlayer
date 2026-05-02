using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Converters;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using ManagedBass;  // for PlaybackState
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
//using System.Drawing;
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

        WeakReferenceMessenger.Default.Register<GoToActiveTrackMessage>(this, (_, m) => OnGoToActiveTrack(m.Value));
        WeakReferenceMessenger.Default.Register<UpdateColumnsMessage>(this, (_, m) => OnUpdateColumns(m));
        WeakReferenceMessenger.Default.Register<ActiveTrackChangedMessage>(this, (_, m) => OnActiveTrackChanged(m.Value));
    }

    internal void RegenerateColumns(DataGrid dg)
    {
        if (!dg.Dispatcher.CheckAccess())
        {
            dg.Dispatcher.Invoke(() => RegenerateColumns(dg));
            return;
        }

        PlaylistTabsViewModel vm = DataContext as PlaylistTabsViewModel ?? throw new InvalidOperationException("DataContext is not PlaylistTabsViewModel");
        ISettingsManager? settingsManager = App.AppHost?.Services?.GetService<ISettingsManager>();
        Dictionary<string, AppSettings.ColumnInfo> savedInfo = settingsManager?.Settings.ColumnSettings ?? new Dictionary<string, AppSettings.ColumnInfo>();

        // If this DataGrid is hosting the Music Library (its DataContext will be MusicLibraryTab),
        // use the library's VisibleColumns instead of the shared playlist SelectedColumnNames.
        HashSet<string> visibleProps;
        MusicLibraryTab? libTab = dg.DataContext as MusicLibraryTab;
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

        // === 1. Determine how many static columns exist in XAML (play icon and/or # column) ===
        int staticColumnsToPreserve = 0;
        if (dg.Columns.Count > 0 && dg.Columns[0] is DataGridTemplateColumn)
        {
            staticColumnsToPreserve = 1;
            // Library grids also get a cover-indicator column at index 1
            if (libTab != null && dg.Columns.Count > 1 && dg.Columns[1] is DataGridTemplateColumn)
                staticColumnsToPreserve = 2;
        }
        else if (dg.Columns.Count > 1
            && dg.Columns[0] is DataGridTextColumn txt
            && txt.Header?.ToString() == "#"
            && dg.Columns[1] is DataGridTemplateColumn)
        {
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
            ("FileName",    "File Name",    200),
            ("Path",        "Path",         350)
        };

        // Read-only properties that cannot be edited inline
        HashSet<string> readOnlyProps = new()
        {
            "Duration", "Bitrate", "SampleRate", "Channels", "Codec", "FileName", "Path"
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

            double width = savedInfo.TryGetValue(prop, out AppSettings.ColumnInfo? ci) && ci.Width > 10 ? ci.Width : defWidth;
            col.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);

            HookColumnEvents(col, prop);
            dg.Columns.Add(col);
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

    static PlaylistTabs()
    {
        DirtyCellBrush.Freeze();
    }

    private static Style CreateDirtyCellStyle(string propertyName)
    {
        Style style = new Style(typeof(DataGridCell), (Style)Application.Current.FindResource("SharedDataGridCellStyle"));

        // Use a DataTrigger with a MultiBinding to get both DirtyCount (for re-evaluation)
        // and the MediaFile itself (to check per-property dirtiness).
        MultiBinding multiBinding = new MultiBinding
        {
            Converter = new Converters.DirtyCellMultiConverter(propertyName)
        };
        multiBinding.Bindings.Add(new Binding("DirtyCount"));  // triggers re-eval when count changes
        multiBinding.Bindings.Add(new Binding("."));           // provides the MediaFile

        DataTrigger dirtyTrigger = new DataTrigger
        {
            Binding = multiBinding,
            Value = true
        };
        dirtyTrigger.Setters.Add(new Setter(DataGridCell.BackgroundProperty, DirtyCellBrush));
        style.Triggers.Add(dirtyTrigger);

        return style;
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
        }

        // If the active DataGrid belongs to the Music Library, persist to library-specific settings
        if (DataContext is PlaylistTabsViewModel vm && vm.SelectedTab is MusicLibraryTab libTab)
        {
            // Persist library visible columns in display order
            List<string> visible = dg.Columns.Skip(1)
                .OfType<DataGridTextColumn>()
                .Where(c => c.Binding is Binding bind && bind.Path?.Path != null)
                .OrderBy(c => c.DisplayIndex)
                .Select(c => ((Binding)c.Binding).Path.Path)
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
            MusicLibraryTab? libTab = targetGrid.DataContext as MusicLibraryTab;
            if (libTab != null)
            {
                List<string> current = libTab.VisibleColumns ?? new List<string>();
                foreach (ColumnSelectorItem item in selectorVm.Columns)
                    item.IsVisible = current.Contains(item.PropertyName);
            }
            else if (DataContext is PlaylistTabsViewModel vm)
            {
                List<string> current = vm.SelectedColumnNames;
                foreach (ColumnSelectorItem item in selectorVm.Columns)
                    item.IsVisible = current.Contains(item.PropertyName);
            }
        }

        //ColumnSelectorPopup popupContent = new ColumnSelectorPopup(selectorVm)
        //{
        //    Width = 210,
        //    Height = 340
        //};

        //Brush background = (Brush?)Application.Current?.TryFindResource("PanelBackgroundBrush") ?? Brushes.WhiteSmoke;

        _columnSelectorPopup = new Popup
        {
            Placement = PlacementMode.Absolute,
            StaysOpen = staysOpen,
            AllowsTransparency = true,
            Child = new Border
            {
                //Background = background,
                //BorderBrush = Brushes.Gray,
                //BorderThickness = new Thickness(1),
                //CornerRadius = new CornerRadius(8),
                //Padding = new Thickness(8),
                //Effect = new DropShadowEffect { BlurRadius = 20, Opacity = 0.5, ShadowDepth = 5 },
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

    private void RegenerateCurrentColumns()
    {
        if (GetActiveDataGrid() is DataGrid dg)
            RegenerateColumns(dg);
    }

    private void OnUpdateColumns(UpdateColumnsMessage m)
    {
        PlaylistTabsViewModel? vm = DataContext as PlaylistTabsViewModel;

        // Determine target DataGrid (active) to decide whether to apply to library or playlists
        DataGrid? active = GetActiveDataGrid();
        MusicLibraryTab? libTab = active?.DataContext as MusicLibraryTab;

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

        RegenerateCurrentColumns();

        if (_columnSelectorPopup?.IsOpen == true)
            _columnSelectorPopup.IsOpen = false;
    }

    private void PlaylistTabs_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            _logger.LogDebug("PlaylistTabs_Loaded: PHASE 1 - Loading playlist tabs (empty)");
            viewModel.LoadPlaylistTabs();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Tabs123.Items.Count > 0)
                {
                    Tabs123.SelectedIndex = viewModel.SelectedTabIndex;
                }

                // Removed tab row suppression handlers; suppression is scoped to column headers only
            }), DispatcherPriority.Loaded);

            Dispatcher.BeginInvoke(async () =>
            {
                _logger.LogDebug("PlaylistTabs_Loaded: PHASE 2 - Loading selected playlist tracks lazily");
                if (viewModel.TabList.Any())
                {
                    await viewModel.LoadSelectedPlaylistTracksAsync();
                }
                else
                {
                    _logger.LogWarning("PlaylistTabs: No playlists loaded");
                }
            }, DispatcherPriority.Background);

            Dispatcher.BeginInvoke(async () =>
            {
                _logger.LogDebug("PlaylistTabs_Loaded: PHASE 3 - Loading other playlists in background");
                await viewModel.LoadOtherPlaylistTracksAsync();
            }, DispatcherPriority.Background);
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

        Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is PlaylistTabsViewModel vm)
            {
                vm.OnDataGridLoaded(sender, e);
                RegenerateColumns(dg);

                // RegenerateColumns removes/adds columns which clears DataGrid selection — re-apply from the viewmodel.
                if (vm.SelectedTrack != null)
                {
                    dg.SelectedItem = vm.SelectedTrack;

                    Dispatcher.BeginInvoke(() =>
                    {
                        if (dg.SelectedItem != null)
                            dg.ScrollIntoView(dg.SelectedItem);
                    }, DispatcherPriority.Background);
                }

                // Restore library column sort if this is the Music Library DataGrid.
                if (dg.DataContext is MusicLibraryTab)
                {
                    ISettingsManager? sm = App.AppHost?.Services?.GetService<ISettingsManager>();
                    if (sm != null &&
                        !string.IsNullOrWhiteSpace(sm.Settings.LibrarySortColumn) &&
                        !string.IsNullOrWhiteSpace(sm.Settings.LibrarySortDirection))
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            DataGridColumn? col = dg.Columns.FirstOrDefault(c =>
                                string.Equals(c.SortMemberPath, sm.Settings.LibrarySortColumn, StringComparison.Ordinal));

                            if (col != null &&
                                Enum.TryParse(sm.Settings.LibrarySortDirection, out ListSortDirection dir))
                            {
                                ICollectionView? view = CollectionViewSource.GetDefaultView(dg.ItemsSource);
                                if (view != null)
                                {
                                    view.SortDescriptions.Clear();
                                    view.SortDescriptions.Add(new SortDescription(col.SortMemberPath, dir));
                                    col.SortDirection = dir;
                                    // Clear sort glyph from all other columns
                                    foreach (DataGridColumn other in dg.Columns)
                                    {
                                        if (!ReferenceEquals(other, col))
                                            other.SortDirection = null;
                                    }
                                }

                                // Re-apply selection — CollectionView Reset clears DataGrid selection
                                if (vm.SelectedTrack != null)
                                {
                                    dg.SelectedItem = vm.SelectedTrack;
                                    dg.ScrollIntoView(vm.SelectedTrack);
                                }
                            }
                        }, DispatcherPriority.Background);
                    }
                }

                // Restore + wire sort for playlist DataGrids.
                if (dg.DataContext is PlaylistTab playlistTab)
                {
                    // Attach sorting event to save state when user clicks a column header
                    dg.Sorting += PlaylistDataGrid_Sorting;

                    // Restore previously saved sort for this playlist
                    ISettingsManager? sm = App.AppHost?.Services?.GetService<ISettingsManager>();
                    if (sm != null &&
                        sm.Settings.PlaylistSortStates.TryGetValue(playlistTab.Name, out AppSettings.PlaylistSortState? sortState) &&
                        !string.IsNullOrWhiteSpace(sortState.SortColumn) &&
                        !string.IsNullOrWhiteSpace(sortState.SortDirection))
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            DataGridColumn? col = dg.Columns.FirstOrDefault(c =>
                                string.Equals(c.SortMemberPath, sortState.SortColumn, StringComparison.Ordinal));

                            if (col != null &&
                                Enum.TryParse(sortState.SortDirection, out ListSortDirection dir))
                            {
                                ICollectionView? view = CollectionViewSource.GetDefaultView(dg.ItemsSource);
                                if (view != null)
                                {
                                    view.SortDescriptions.Clear();
                                    view.SortDescriptions.Add(new SortDescription(col.SortMemberPath, dir));
                                    col.SortDirection = dir;
                                    foreach (DataGridColumn other in dg.Columns)
                                    {
                                        if (!ReferenceEquals(other, col))
                                            other.SortDirection = null;
                                    }
                                }
                            }
                        }, DispatcherPriority.Background);
                    }
                }

                // Run after all pending layout/render passes from RegenerateColumns have settled.
                Dispatcher.BeginInvoke(() => FixHeaderScrollShimmy(dg), DispatcherPriority.Background);

                // Attach PreviewMouseRightButtonUp and ContextMenuOpening to swallow context menu after header right-click
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
        }, DispatcherPriority.Loaded);
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
        if (e.Key != Key.Down && e.Key != Key.Up)
            return;

        if (sender is not DataGrid dg)
            return;

        // If focus has already escaped to a ScrollBar inside the DataGrid, reclaim it.
        if (Keyboard.FocusedElement is ScrollBar sb &&
            FindAncestor<DataGrid>(sb) == dg)
        {
            // Return focus to the selected row (or the first item as a fallback).
            DataGridRow? row = GetSelectedRow(dg) ?? GetRowAt(dg, 0);
            row?.Focus();
            // Don't eat the key — let DataGrid handle navigation from the refocused row.
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
        if (dg.SelectedItem == null) return null;
        return dg.ItemContainerGenerator.ContainerFromItem(dg.SelectedItem) as DataGridRow;
    }

    private static DataGridRow? GetRowAt(DataGrid dg, int index)
    {
        if (index < 0 || index >= dg.Items.Count) return null;
        return dg.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
    }

    private void TracksTable_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Update selection immediately so context menu actions (like Properties) see multi-select state
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            viewModel.OnTrackSelectionChanged(sender, e);
        }
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
        if (e.EditAction == DataGridEditAction.Commit && DataContext is PlaylistTabsViewModel viewModel)
        {
            // Defer so the binding commits before we check dirty state
            Dispatcher.BeginInvoke(() => viewModel.NotifyDirtyStateChanged(),
                System.Windows.Threading.DispatcherPriority.DataBind);
        }
    }

    private void MusicLibraryDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        // Let WPF apply the sort normally, then persist the sort state after the next render pass.
        ISettingsManager? settingsManager = App.AppHost?.Services?.GetService<ISettingsManager>();
        if (settingsManager == null)
            return;

        // The new direction is the opposite of the current one (WPF toggles on click).
        ListSortDirection newDirection = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        string sortPath = e.Column.SortMemberPath ?? e.Column.Header?.ToString() ?? string.Empty;

        Dispatcher.BeginInvoke(() =>
        {
            settingsManager.Settings.LibrarySortColumn = sortPath;
            settingsManager.Settings.LibrarySortDirection = newDirection.ToString();
            settingsManager.SaveSettings(nameof(AppSettings.LibrarySortColumn));
        }, DispatcherPriority.Background);
    }

    private void PlaylistDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (sender is not DataGrid dg || dg.DataContext is not PlaylistTab playlistTab)
            return;

        ISettingsManager? settingsManager = App.AppHost?.Services?.GetService<ISettingsManager>();
        if (settingsManager == null)
            return;

        ListSortDirection newDirection = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        string sortPath = e.Column.SortMemberPath ?? e.Column.Header?.ToString() ?? string.Empty;

        Dispatcher.BeginInvoke(() =>
        {
            settingsManager.Settings.PlaylistSortStates[playlistTab.Name] = new AppSettings.PlaylistSortState
            {
                SortColumn    = sortPath,
                SortDirection = newDirection.ToString()
            };
            settingsManager.SaveSettings(nameof(AppSettings.PlaylistSortStates));
        }, DispatcherPriority.Background);
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only act when the selected tab actually changes; ignore clicks on the already-selected tab
        bool tabChanged = e.AddedItems.OfType<PlaylistTab>().Any() || e.RemovedItems.OfType<PlaylistTab>().Any();
        if (!tabChanged)
        {
            return;
        }

        Dispatcher.BeginInvoke((Action)delegate
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                viewModel.OnTabSelectionChanged(sender, e);

                // Restore scroll offset for the newly selected tab (if previously recorded)
                DataGrid? dg = GetActiveDataGrid();
                if (dg != null && viewModel.SelectedTab != null)
                {
                    ScrollViewer? sv = FindDescendant<ScrollViewer>(dg);
                    double targetOffset = 0;
                    bool hasSaved = false;
                    if (_tabVerticalOffsets.TryGetValue(viewModel.SelectedTab, out double offset))
                    {
                        targetOffset = offset;
                        hasSaved = true;
                    }

                    if (sv != null)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (hasSaved)
                            {
                                sv.ScrollToVerticalOffset(targetOffset);
                            }
                            else if (viewModel.SelectedTrack != null && !IsItemFullyVisible(dg, viewModel.SelectedTrack))
                            {
                                _isExplicitCentering = true;
                                try
                                {
                                    CenterItemInDataGrid(dg, viewModel.SelectedTrack);
                                }
                                finally
                                {
                                    _isExplicitCentering = false;
                                }
                            }

                            RegenerateCurrentColumns();
                        }), System.Windows.Threading.DispatcherPriority.Render);
                    }
                }

                // After any restore attempt, verify visibility once containers generate
                EnsureSelectedTrackVisible();
            }
        }, null);
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
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            viewModel.OnDoubleClickDataGrid();
        }

        WeakReferenceMessenger.Default.Send(new DataGridPlayMessage(PlaybackState.Playing));
        e.Handled = true;
    }

    // TracksTable sorting is handled by the WPF DataGrid (CollectionView).
    // private void TracksTable_OnSorting(object sender, DataGridSortingEventArgs e)
    // {
    //     if (e.Column is DataGridColumn column)
    //     {
    //         ListSortDirection direction = (column.SortDirection != ListSortDirection.Ascending)
    //         ? ListSortDirection.Ascending
    //         : ListSortDirection.Descending;
    //         string propertyName = (column.SortMemberPath ?? column.Header.ToString())!;
    //
    //         Dispatcher.BeginInvoke((Action)delegate
    //         {
    //             if (DataContext is PlaylistTabsViewModel viewModel)
    //             {
    //                 viewModel.OnDataGridSorted(propertyName, direction);
    //             }
    //         }, null);
    //     }
    // }

    private void PlaylistDataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (DataContext is PlaylistTabsViewModel viewModel && viewModel.SelectedTab != null)
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

        if (DataContext is not PlaylistTabsViewModel viewModel)
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

        // Auto-follow now-playing: select + reveal the playing track (selection should follow now-playing).
        if (DataContext is not PlaylistTabsViewModel viewModel)
        {
            return;
        }

        MediaFile? activeInTab = null;
        int tabIndex = viewModel.SelectedTabIndex;
        int trackIndex = -1;

        if (tabIndex >= 0 && tabIndex < viewModel.TabList.Count)
        {
            activeInTab = viewModel.TabList[tabIndex].Tracks.FirstOrDefault(t => t.Id == track.Id);
            if (activeInTab != null)
            {
                trackIndex = viewModel.TabList[tabIndex].Tracks.IndexOf(activeInTab);
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

        viewModel.SelectedTabIndex = tabIndex;

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
                    CenterItemInDataGrid(dataGrid, activeInTab);
                }
                finally
                {
                    _isExplicitCentering = false;
                }
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
    }

    private void EnsureSelectedTrackVisible()
    {
        if (DataContext is not PlaylistTabsViewModel vm)
        {
            return;
        }
        DataGrid? dg = GetActiveDataGrid();
        if (dg == null || vm.SelectedTrack == null)
        {
            return;
        }
        if (dg.Items.Count == 0)
        {
            return;
        }

        void CenterIfReady()
        {
            if (vm.SelectedTrack == null)
            {
                return;
            }
            if (!IsItemFullyVisible(dg, vm.SelectedTrack))
            {
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

        if (dg.ItemContainerGenerator.ContainerFromItem(vm.SelectedTrack) == null)
        {
            EventHandler? handler = null;
            handler = (object? s, EventArgs e) =>
            {
                if (dg.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                {
                    dg.ItemContainerGenerator.StatusChanged -= handler;
                    dg.Dispatcher.BeginInvoke(new Action(CenterIfReady), System.Windows.Threading.DispatcherPriority.Render);
                }
            };
            dg.ItemContainerGenerator.StatusChanged += handler;
        }
        else
        {
            dg.Dispatcher.BeginInvoke(new Action(CenterIfReady), System.Windows.Threading.DispatcherPriority.Render);
        }
    }

    private static bool IsItemFullyVisible(DataGrid dataGrid, object item)
    {
        ScrollViewer? sv = FindDescendant<ScrollViewer>(dataGrid);
        if (sv == null)
        {
            return false;
        }

        DataGridRow? row = dataGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
        if (row == null || row.ActualHeight <= 0)
        {
            return false;
        }

        GeneralTransform transform = row.TransformToAncestor(sv);
        Point rowPos = transform.Transform(new Point(0, 0));
        double top = rowPos.Y;
        double bottom = top + row.ActualHeight;

        return top >= 0 && bottom <= sv.ViewportHeight;
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

        if (FindDescendant<ScrollViewer>(dataGrid) is not ScrollViewer sv)
            return;

        if (dataGrid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row)
            return;

        bool logicalScroll = ScrollViewer.GetCanContentScroll(dataGrid);

        if (logicalScroll)
        {
            int index = dataGrid.Items.IndexOf(item);
            int itemsInViewport = (int)Math.Round(sv.ViewportHeight);
            int targetTopIndex = Math.Max(0, index - (itemsInViewport / 2));
            sv.ScrollToVerticalOffset(targetTopIndex);
        }
        else
        {
            GeneralTransform transform = row.TransformToAncestor(sv);
            Point rowPos = transform.Transform(new Point(0, 0));
            double rowCenter = rowPos.Y + (row.ActualHeight / 2.0);
            double targetCenter = sv.ViewportHeight / 2.0;
            double delta = rowCenter - targetCenter;
            sv.ScrollToVerticalOffset(sv.VerticalOffset + delta);
        }
    }

    internal DataGrid? GetActiveDataGrid() => FindDescendant<DataGrid>(Tabs123);

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
            if (DataContext is PlaylistTabsViewModel vm && vm.SelectedTrack != null)
            {
                DataGrid? dg = GetActiveDataGrid();
                if (dg != null)
                {
                    dg.ScrollIntoView(vm.SelectedTrack);
                }
            }
            return; // do not initiate drag
        }

        // Only allow dragging PlaylistTab, NOT MusicLibraryTab
        if (tabItem.DataContext is PlaylistTab tab)
        {
            _draggedTab = tab; // only set drag when not the already-selected tab
        }
        else if (tabItem.DataContext is MusicLibraryTab)
        {
            _draggedTab = null; // Music Library cannot be dragged
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

        if (DataContext is not PlaylistTabsViewModel viewModel)
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
        if (DataContext is PlaylistTabsViewModel vm)
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
