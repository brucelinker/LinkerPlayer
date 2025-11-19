using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Windows;
using ManagedBass;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace LinkerPlayer.UserControls;

[ObservableObject]
public partial class PlaylistTabs
{
    private EditableTabHeaderControl? _selectedEditableTabHeaderControl;
    private readonly ILogger<PlaylistTabs> _logger;
    private PlaylistTab? _draggedTab;
    private DropIndicatorAdorner? _dropIndicatorAdorner;
    private readonly Dictionary<PlaylistTab, double> _tabVerticalOffsets = new();

    // Flag to allow explicit centering to bypass BringIntoView suppression
    private bool _isExplicitCentering;

    public PlaylistTabs()
    {
        InitializeComponent();

        _logger = App.AppHost.Services.GetRequiredService<ILogger<PlaylistTabs>>();

        Loaded += PlaylistTabs_Loaded;

        WeakReferenceMessenger.Default.Register<GoToActiveTrackMessage>(this, (_, m) =>
        {
            OnGoToActiveTrack(m.Value);
        });
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
                    // Prefer restoring prior offset for this tab; only center if no known offset
                    if (viewModel.SelectedTab != null && _tabVerticalOffsets.TryGetValue(viewModel.SelectedTab, out double savedOffset))
                    {
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

    private void PlaylistDataGrid_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (!_isExplicitCentering)
        {
            e.Handled = true;
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

        // Apply target scroll immediately to avoid showing previous tab's offset
        if (DataContext is PlaylistTabsViewModel vmImmediate)
        {
            DataGrid? dgImmediate = GetActiveDataGrid();
            if (dgImmediate != null && vmImmediate.SelectedTab != null)
            {
                ScrollViewer? svImmediate = FindDescendant<ScrollViewer>(dgImmediate);
                if (svImmediate != null)
                {
                    if (_tabVerticalOffsets.TryGetValue(vmImmediate.SelectedTab, out double immOffset))
                    {
                        svImmediate.ScrollToVerticalOffset(immOffset);
                    }
                    else if (vmImmediate.SelectedTrack != null)
                    {
                        _isExplicitCentering = true;
                        try { CenterItemInDataGrid(dgImmediate, vmImmediate.SelectedTrack); }
                        finally { _isExplicitCentering = false; }
                    }
                }
            }
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

    private void MenuItem_NewPlaylist(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            viewModel.NewPlaylistCommand.Execute(null);
        }
    }

    private void MenuItem_LoadPlaylistAsync(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            viewModel.LoadPlaylistCommand.Execute(null);
        }
    }

    private void MenuItem_RenamePlaylist(object sender, RoutedEventArgs e)
    {
        EditableTabHeaderControl? targetHeader = null;
        if (sender is MenuItem mi)
        {
            ContextMenu? ctx = mi.Parent as ContextMenu;
            if (ctx == null)
            {
                ctx = FindAncestor<ContextMenu>(mi);
            }
            if (ctx != null && ctx.PlacementTarget is EditableTabHeaderControl header)
            {
                targetHeader = header;
            }
        }

        if (targetHeader == null)
        {
            targetHeader = _selectedEditableTabHeaderControl;
        }

        if (targetHeader != null)
        {
            if (targetHeader.Tag is not PlaylistTabsViewModel && DataContext is PlaylistTabsViewModel vm)
            {
                targetHeader.Tag = vm;
            }
            targetHeader.SetEditMode(true);
        }
    }

    private void MenuItem_RemovePlaylist(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                await viewModel.RemovePlaylistAsync(sender);
            }
        });
    }

    private void MenuItem_AddFolder(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            viewModel.AddFolderCommand.Execute(null);
        }
    }

    private void MenuItem_AddFiles(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            viewModel.AddFilesCommand.Execute(null);
        }
    }

    private void MenuItem_PlayTrack(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke((Action)delegate
        {
            if (DataContext is PlayerControlsViewModel viewModel)
            {
                viewModel.PlayTrack();
            }
        }, null);
    }

    private void MenuItem_RemoveTrack(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            viewModel.RemoveTrackCommand.Execute(null);
        }
    }

    private void MenuItem_NewPlaylistFromFolder(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            viewModel.NewPlaylistFromFolderCommand.Execute(null);
        }
    }

    private void MenuItem_Properties(object sender, RoutedEventArgs e)
    {
        PropertiesViewModel propertiesViewModel = App.AppHost.Services.GetRequiredService<PropertiesViewModel>();

        PropertiesWindow dialog = new PropertiesWindow
        {
            DataContext = propertiesViewModel
        };
        dialog.Show();
    }

    private void TabHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Removed: allow normal bubbling so EditableTabHeaderControl.MouseDoubleClick works
    }

    private void TabHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        EditableTabHeaderControl header = (EditableTabHeaderControl)sender;
        _selectedEditableTabHeaderControl = header;

        if (header.Tag is not PlaylistTabsViewModel && DataContext is PlaylistTabsViewModel vmTag)
        {
            header.Tag = vmTag; // ensure rename works from context menu
        }
        // No same-tab scroll logic here; handled in TabItem_PreviewMouseLeftButtonDown
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

    private DataGrid? GetActiveDataGrid()
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
                    dg.ScrollIntoView(vm.SelectedTrack); // rely on DataGrid layout
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
            intended = count;

        // Adjust for removal shifting indices when moving forward
        int adjusted = fromIndex < intended ? intended - 1 : intended;

        // Clamp to valid range [0, count-1]
        if (adjusted < 0)
            adjusted = 0;
        if (adjusted >= count)
            adjusted = count - 1;

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
