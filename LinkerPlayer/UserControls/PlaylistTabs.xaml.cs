using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
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
using System.Windows.Media.Effects;

namespace LinkerPlayer.UserControls;

[ObservableObject]
public partial class PlaylistTabs
{
    private readonly ILogger<PlaylistTabs> _logger;
    private PlaylistTab? _draggedTab;
    private DropIndicatorAdorner? _dropIndicatorAdorner;
    private readonly Dictionary<PlaylistTab, double> _tabVerticalOffsets = new();

    // Flag to allow explicit centering to bypass BringIntoView suppression
    private bool _isExplicitCentering;
    private Popup? _columnSelectorPopup;

    public PlaylistTabs()
    {
        InitializeComponent();

        IServiceProvider? services = App.AppHost?.Services;
        _logger = services != null
            ? services.GetRequiredService<ILogger<PlaylistTabs>>()
            : LoggerFactory.Create(_ => { }).CreateLogger<PlaylistTabs>();

        Loaded += PlaylistTabs_Loaded;

        WeakReferenceMessenger.Default.Register<GoToActiveTrackMessage>(this, (_, m) =>
        {
            OnGoToActiveTrack(m.Value);
        });

        WeakReferenceMessenger.Default.Register<UpdateColumnsMessage>(this, (r, m) =>
        {
            OnUpdateColumns(m);
        });
    }

    internal void RegenerateColumns(DataGrid dg)
    {
        dg.Columns.Clear();

        dg.HeadersVisibility = DataGridHeadersVisibility.Column; // hides row headers completely
        dg.RowHeaderWidth = 0;                                    // extra insurance

        // 1. Play/Pause icon column (always first)
        DataGridTemplateColumn playPauseColumn = new DataGridTemplateColumn
        {
            Header = string.Empty,
            Width = new DataGridLength(36),
            IsReadOnly = true
        };

        // This finds the template even if it's in App.xaml or merged dictionaries
        DataTemplate? tmpl = Application.Current != null
            ? Application.Current.TryFindResource("PlayPauseCellTemplate") as DataTemplate
            : null;
        if (tmpl != null)
        {
            playPauseColumn.CellTemplate = tmpl;
        }
        else
        {
            // Fallback — should never happen, but prevents blank column
            playPauseColumn.CellTemplate = new DataTemplate(); // or throw
        }

        dg.Columns.Insert(0, playPauseColumn);  // Use Insert(0) to guarantee it's first

        // 2. Dynamic tag columns according to global selection
        if (DataContext is PlaylistTabsViewModel vm)
        {
            foreach (string prop in vm.SelectedColumnNames)
            {
                string niceHeader = prop switch
                {
                    "Duration" => "Length",
                    "AlbumArtist" => "Album Artist",
                    "Track" => "#",
                    _ => prop
                };

                DataGridTextColumn col = new DataGridTextColumn
                {
                    Header = niceHeader,
                    Binding = new Binding(prop)
                };
                dg.Columns.Add(col);
            }
        }
    }

    private void DataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = (e.Row.GetIndex() + 1).ToString();
    }

    private void RegenerateCurrentColumns()
    {
        DataGrid? dg = GetActiveDataGrid();
        if (dg != null)
            RegenerateColumns(dg);
    }

    private void OnUpdateColumns(UpdateColumnsMessage m)
    {
        if (DataContext is PlaylistTabsViewModel vm)
        {
            vm.ApplySelectedColumns(m.SelectedColumns);
        }

        RegenerateCurrentColumns();

        // Close the popup when OK is pressed
        if (_columnSelectorPopup != null && _columnSelectorPopup.IsOpen)
        {
            _columnSelectorPopup.IsOpen = false;
        }
    }

    private void PlaylistTabs_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            _logger.LogInformation("PlaylistTabs_Loaded: PHASE 1 - Loading playlist tabs (empty)");
            viewModel.LoadPlaylistTabs();

            // Rely on binding to apply SelectedTabIndex; no manual SelectionChanged invocation
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Tabs123.Items.Count > 0)
                {
                    Tabs123.SelectedIndex = viewModel.SelectedTabIndex;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            Dispatcher.BeginInvoke(async () =>
            {
                _logger.LogInformation("PlaylistTabs_Loaded: PHASE 2 - Loading selected playlist tracks lazily");
                if (viewModel.TabList.Any())
                {
                    await viewModel.LoadSelectedPlaylistTracksAsync();
                }
                else
                {
                    _logger.LogWarning("PlaylistTabs: No playlists loaded");
                }
            }, System.Windows.Threading.DispatcherPriority.Background);

            Dispatcher.BeginInvoke(async () =>
            {
                _logger.LogInformation("PlaylistTabs_Loaded: PHASE 3 - Loading other playlists in background");
                await viewModel.LoadOtherPlaylistTracksAsync();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
        else
        {
            _logger.LogError("PlaylistTabs: DataContext is not PlaylistTabsViewModel, type: {Type}", DataContext?.GetType().FullName ?? "null");
        }
    }

    private void DataGrid_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke((Action)delegate
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                viewModel.OnDataGridLoaded(sender, e);

                if (sender is DataGrid dg)
                {
                    RegenerateColumns(dg);

                    // Prefer restoring prior offset for this tab; only center if no known offset
                    if (viewModel.SelectedTab != null && _tabVerticalOffsets.TryGetValue(viewModel.SelectedTab, out double savedOffset))
                    {
                        // Attach column header right-click handlers
                        dg.AddHandler(DataGridColumnHeader.PreviewMouseRightButtonDownEvent,
                            new MouseButtonEventHandler(DataGridColumnHeader_PreviewMouseRightButtonDown), true);

                        dg.AddHandler(DataGridColumnHeader.PreviewMouseRightButtonUpEvent,
                            new MouseButtonEventHandler(DataGridColumnHeader_PreviewMouseRightButtonUp), true);

                        // Attach DataGrid-level right mouse up handler
                        dg.AddHandler(UIElement.PreviewMouseRightButtonUpEvent,
                            new MouseButtonEventHandler(DataGrid_PreviewMouseRightButtonUp), true);

                        ScrollViewer? sv = FindDescendant<ScrollViewer>(dg);
                        if (sv != null)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                sv.ScrollToVerticalOffset(savedOffset);
                            }), System.Windows.Threading.DispatcherPriority.Render);
                        }
                    }
                    else if (viewModel.SelectedTrack != null)
                    {
                        // Force centering explicitly against this DataGrid after layout
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                _isExplicitCentering = true;
                                CenterItemInDataGrid(dg, viewModel.SelectedTrack);
                            }
                            finally
                            {
                                _isExplicitCentering = false;
                            }
                        }), System.Windows.Threading.DispatcherPriority.Render);
                    }

                    // Ensure ultimately visible once containers are generated
                    EnsureSelectedTrackVisible();
                }
            }
        }, null);
    }

    private void DataGridColumnHeader_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject depObj)
            return;

        DataGridColumnHeader? header = FindAncestor<DataGridColumnHeader>(depObj);
        if (header == null)
            return;

        e.Handled = true;
        Mouse.Capture(header);

        DataGrid? dataGrid = GetActiveDataGrid();
        if (dataGrid == null)
            return;

        ColumnSelectorViewModel selectorVm = new ColumnSelectorViewModel();

        // Initialise checkboxes to exactly match what is currently visible (global state)
        if (DataContext is PlaylistTabsViewModel mainVm)
        {
            List<string> current = mainVm.SelectedColumnNames;
            foreach (ColumnSelectorItem item in selectorVm.Columns)
            {
                item.IsVisible = current.Contains(item.PropertyName);
            }
        }

        ColumnSelectorPopup content = new ColumnSelectorPopup(selectorVm)
        {
            Width = 220,
            Height = 400
        };

        Border border = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Child = content,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.4,
                BlurRadius = 10,
                ShadowDepth = 3
            }
        };

        _columnSelectorPopup = new Popup
        {
            PlacementTarget = header,
            Placement = PlacementMode.Bottom,
            Child = border,
            StaysOpen = false,
            HorizontalOffset = 0,
            VerticalOffset = 1
        };

        _columnSelectorPopup.IsOpen = true;
    }

    private void DataGridColumnHeader_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_columnSelectorPopup != null && _columnSelectorPopup.IsOpen)
        {
            if (Mouse.Captured is DataGridColumnHeader)
            {
                Mouse.Capture(null); // Release mouse capture
                e.Handled = true;
            }
        }
    }

    private void DataGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_columnSelectorPopup != null && _columnSelectorPopup.IsOpen)
        {
            e.Handled = true;
        }
    }

    private void PlaylistDataGrid_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (!_isExplicitCentering)
        {
            e.Handled = true;
        }
    }

    private void DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ensure right-click selects the row under the mouse so context menu commands act on it
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        DependencyObject source = (DependencyObject)e.OriginalSource;
        DataGridRow? row = FindAncestor<DataGridRow>(source);
        if (row != null)
        {
            try
            {
                // Select the row and focus it
                dataGrid.SelectedItem = row.Item;
                row.IsSelected = true;
                row.Focus();
            }
            catch { }
        }
    }

    private void TracksTable_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Update selection immediately so context menu actions (like Properties) see multi-select state
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            viewModel.OnTrackSelectionChanged(sender, e);
        }
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
        Dispatcher.BeginInvoke((Action)delegate
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                viewModel.OnDoubleClickDataGrid();
            }
        }, null);

        WeakReferenceMessenger.Default.Send(new DataGridPlayMessage(PlaybackState.Playing));
    }

    private void TracksTable_OnSorting(object sender, DataGridSortingEventArgs e)
    {
        if (e.Column is DataGridColumn column)
        {
            ListSortDirection direction = (column.SortDirection != ListSortDirection.Ascending)
            ? ListSortDirection.Ascending
            : ListSortDirection.Descending;
            string propertyName = (column.SortMemberPath ?? column.Header.ToString())!;

            Dispatcher.BeginInvoke((Action)delegate
            {
                if (DataContext is PlaylistTabsViewModel viewModel)
                {
                    viewModel.OnDataGridSorted(propertyName, direction);
                }
            }, null);
        }
    }

    private void PlaylistDataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (DataContext is PlaylistTabsViewModel viewModel && viewModel.SelectedTab != null)
        {
            _tabVerticalOffsets[viewModel.SelectedTab] = e.VerticalOffset;
        }
    }

    private void OnGoToActiveTrack(bool value)
    {
        // Explicit user action: allow forced centering
        CenterSelectedTrack();
    }

    private void CenterSelectedTrack()
    {
        if (DataContext is PlaylistTabsViewModel viewModel && viewModel.SelectedTrack != null)
        {
            DataGrid? dataGrid = GetActiveDataGrid();
            if (dataGrid != null)
            {
                if (!IsItemFullyVisible(dataGrid, viewModel.SelectedTrack))
                {
                    try
                    {
                        _isExplicitCentering = true;
                        CenterItemInDataGrid(dataGrid, viewModel.SelectedTrack);
                    }
                    finally
                    {
                        _isExplicitCentering = false;
                    }
                }
            }
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
            handler = (s, e) =>
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

    internal DataGrid? GetActiveDataGrid()
    {
        return FindDescendant<DataGrid>(Tabs123);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null)
        {
            return null;
        }
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T t)
            {
                return t;
            }
            T? result = FindDescendant<T>(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private static TAncestor? FindAncestor<TAncestor>(DependencyObject? child) where TAncestor : DependencyObject
    {
        DependencyObject? current = child;
        while (current != null)
        {
            DependencyObject? parent = LogicalTreeHelper.GetParent(current) ?? VisualTreeHelper.GetParent(current);
            if (parent is TAncestor ancestor)
            {
                return ancestor;
            }
            current = parent;
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

        if (tabItem.DataContext is PlaylistTab tab)
        {
            _draggedTab = tab; // only set drag when not the already-selected tab
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

        // Adjust for removal shifting indices when moving forward
        int adjusted = fromIndex < intended ? intended - 1 : intended;

        // Clamp to valid range [0, count-1]
        if (adjusted < 0)
        {
            adjusted = 0;
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
}

// Adorner class for the drop indicator (unused with Dragablz, kept for reference)
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
