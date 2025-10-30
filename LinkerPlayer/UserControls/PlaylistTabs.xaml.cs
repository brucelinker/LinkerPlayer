using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.BassLibs;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Windows;
using ManagedBass;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LinkerPlayer.UserControls;

[ObservableObject]
public partial class PlaylistTabs
{
    private EditableTabHeaderControl? _selectedEditableTabHeaderControl;
    private readonly ILogger<PlaylistTabs> _logger;
    private PlaylistTab? _draggedTab;
    private int _draggedTabIndex = -1;
    private DropIndicatorAdorner? _dropIndicatorAdorner;
    private TabItem? _lastHighlightedTab;


    public PlaylistTabs()
    {
        InitializeComponent();

        _logger = App.AppHost.Services.GetRequiredService<ILogger<PlaylistTabs>>();

        Loaded += PlaylistTabs_Loaded;
    }

    private void PlaylistTabs_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            viewModel.LoadPlaylistTabs();
            //_logger.LogInformation("PlaylistTabs: Loaded {Count} playlists", viewModel.TabList.Count);
            
            if (viewModel.TabList.Any())
            {
                // Get the saved tab index and set it if valid
                int savedTabIndex = App.AppHost.Services.GetRequiredService<ISettingsManager>().Settings.SelectedTabIndex;
                
                if (savedTabIndex >= 0 && savedTabIndex < viewModel.TabList.Count)
                {
                    viewModel.SelectedTabIndex = savedTabIndex;
                    //_logger.LogInformation("PlaylistTabs: Set SelectedTabIndex to saved value {Index}", savedTabIndex);
                }
                else
                {
                    viewModel.SelectedTabIndex = 0;
                    //_logger.LogInformation("PlaylistTabs: Set SelectedTabIndex to default 0");
                }
            }
            else
            {
                _logger.LogWarning("PlaylistTabs: No playlists loaded");
            }
        }
        else
        {
            _logger.LogError("PlaylistTabs: DataContext is not PlaylistTabsViewModel, type: {Type}", DataContext?.GetType().FullName ?? "null");
        }
    }

    private void DataGrid_Loaded(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                viewModel.OnDataGridLoaded(sender, e);
            }
        }, null);
    }

    private void TracksTable_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            if (DataContext is PlaylistTabsViewModel viewModel)
            {
                viewModel.OnTrackSelectionChanged(sender, e);
            }
        }, null);
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl && (e.AddedItems.Count > 0 || e.RemovedItems.Count > 0))
        {
            bool isTabChange = e.AddedItems.OfType<PlaylistTab>().Any() || e.RemovedItems.OfType<PlaylistTab>().Any();
            if (isTabChange)
            {
                this.Dispatcher.BeginInvoke((Action)delegate
                {
                    if (DataContext is PlaylistTabsViewModel viewModel)
                    {
                        viewModel.OnTabSelectionChanged(sender, e);
                    }
                }, null);
                Console.WriteLine("Tab selection changed.");
            }
        }
        //else
        //{
        //    Console.WriteLine($"Event from child control {e.OriginalSource}, ignoring.");
        //}
    }

    private void PlaylistRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
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
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            if (_selectedEditableTabHeaderControl != null)
            {
                _selectedEditableTabHeaderControl.SetEditMode(true);
                // Pass PlaylistTab to enable command binding
                if (_selectedEditableTabHeaderControl.DataContext is PlaylistTab && DataContext is PlaylistTabsViewModel viewModel)
                {
                    _selectedEditableTabHeaderControl.Tag = viewModel; // Store view model for command access
                }
            }
        }, null);
    }

    private void MenuItem_RemovePlaylist(object sender, RoutedEventArgs e)
    {
        this.Dispatcher.BeginInvoke(async () =>
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
        this.Dispatcher.BeginInvoke((Action)delegate
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
      // Use DI to create PropertiesViewModel with all its dependencies
 PropertiesViewModel propertiesViewModel = App.AppHost.Services.GetRequiredService<PropertiesViewModel>();
       
        PropertiesWindow dialog = new PropertiesWindow
        {
      DataContext = propertiesViewModel
        };
        dialog.Show();
    }

    private void TabHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        this.Dispatcher.BeginInvoke((Action)delegate
        {
            _selectedEditableTabHeaderControl = (EditableTabHeaderControl)sender;
        }, null);
    }

    private void TabHeader_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _selectedEditableTabHeaderControl = (EditableTabHeaderControl)sender;
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            viewModel.RightMouseDownTabSelect((string)_selectedEditableTabHeaderControl.Content);
        }
    }

    private void TracksTable_OnSorting(object sender, DataGridSortingEventArgs e)
    {
        if (e.Column is DataGridColumn column)
        {
            ListSortDirection direction = (column.SortDirection != ListSortDirection.Ascending)
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;
            string propertyName = (column.SortMemberPath ?? column.Header.ToString())!;
            this.Dispatcher.BeginInvoke((Action)delegate
            {
                if (DataContext is PlaylistTabsViewModel viewModel)
                {
                    viewModel.OnDataGridSorted(propertyName, direction);
                }
            }, null);
        }
    }

    private void TabHeader_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is EditableTabHeaderControl tabHeader)
        {
            if (tabHeader.DataContext is PlaylistTab playlistTab)
            {
                _draggedTab = playlistTab;
                _draggedTabIndex = GetTabIndex(playlistTab);
                
                if (_draggedTabIndex >= 0)
                {
                    // Set custom cursor for drag operation
                    Mouse.SetCursor(Cursors.Hand);
                    
                    DragDropEffects effects = DragDrop.DoDragDrop(tabHeader, playlistTab, DragDropEffects.Move);
                    
                    // Reset after drag completes
                    RemoveDropIndicator();
                    _draggedTab = null;
                    _draggedTabIndex = -1;
                    Mouse.SetCursor(Cursors.Arrow);
                }
            }
        }
    }

    private void TabHeader_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(PlaylistTab)))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        // Find the TabItem that contains this header
        if (sender is EditableTabHeaderControl targetHeader)
        {
            TabItem? targetTabItem = FindParent<TabItem>(targetHeader);
            if (targetTabItem != null && targetTabItem.DataContext is PlaylistTab targetTab)
            {
                // Get mouse position relative to the tab
                Point mousePos = e.GetPosition(targetTabItem);
                double tabWidth = targetTabItem.ActualWidth;
                
                // Determine if we're on the left or right half of the tab
                bool dropOnLeft = mousePos.X < tabWidth / 2;
                
                int targetIndex = GetTabIndex(targetTab);
                
                // Show drop indicator
                ShowDropIndicator(targetTabItem, dropOnLeft);
                
                // Highlight the target tab
                if (_lastHighlightedTab != targetTabItem)
                {
                    ClearTabHighlight();
                    _lastHighlightedTab = targetTabItem;
                    HighlightTab(targetTabItem, true);
                }
            }
        }
    }

    private void TabHeader_DragLeave(object sender, DragEventArgs e)
    {
        // Only clear if we're actually leaving the tab control area
        if (sender is EditableTabHeaderControl header)
        {
            TabItem? tabItem = FindParent<TabItem>(header);
            if (tabItem != null)
            {
                Point position = e.GetPosition(tabItem);
                if (position.X < 0 || position.X > tabItem.ActualWidth || 
                    position.Y < 0 || position.Y > tabItem.ActualHeight)
                {
                    ClearTabHighlight();
                }
            }
        }
        e.Handled = true;
    }

    private void TabItem_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(PlaylistTab)))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        if (sender is TabItem targetTabItem && targetTabItem.DataContext is PlaylistTab targetTab)
        {
            // Get mouse position relative to the tab
            Point mousePos = e.GetPosition(targetTabItem);
            double tabWidth = targetTabItem.ActualWidth;
            
            // Determine if we're on the left or right half of the tab
            bool dropOnLeft = mousePos.X < tabWidth / 2;
            
            // Show drop indicator
            ShowDropIndicator(targetTabItem, dropOnLeft);
            
            // Highlight the target tab
            if (_lastHighlightedTab != targetTabItem)
            {
                ClearTabHighlight();
                _lastHighlightedTab = targetTabItem;
                HighlightTab(targetTabItem, true);
            }
        }
    }

    private void TabItem_Drop(object sender, DragEventArgs e)
    {
        ProcessDrop(sender as TabItem, e);
    }

    private void TabControl_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(PlaylistTab)))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        // Find which tab we're over based on mouse position
        if (sender is TabControl tabControl)
        {
            Point mousePos = e.GetPosition(tabControl);
            TabItem? targetTabItem = GetTabItemAtPosition(tabControl, mousePos);
            
            if (targetTabItem != null && targetTabItem.DataContext is PlaylistTab)
            {
                // Get mouse position relative to the tab
                Point tabMousePos = e.GetPosition(targetTabItem);
                double tabWidth = targetTabItem.ActualWidth;
                
                // Determine if we're on the left or right half of the tab
                bool dropOnLeft = tabMousePos.X < tabWidth / 2;
                
                // Show drop indicator
                ShowDropIndicator(targetTabItem, dropOnLeft);
                
                // Highlight the target tab
                if (_lastHighlightedTab != targetTabItem)
                {
                    ClearTabHighlight();
                    _lastHighlightedTab = targetTabItem;
                    HighlightTab(targetTabItem, true);
                }
            }
        }
    }

    private void TabControl_Drop(object sender, DragEventArgs e)
    {
        if (sender is TabControl tabControl)
        {
            Point mousePos = e.GetPosition(tabControl);
            TabItem? targetTabItem = GetTabItemAtPosition(tabControl, mousePos);
            ProcessDrop(targetTabItem, e);
        }
    }

    private TabItem? GetTabItemAtPosition(TabControl tabControl, Point position)
    {
        HitTestResult? result = VisualTreeHelper.HitTest(tabControl, position);
        if (result != null)
        {
            DependencyObject? hitElement = result.VisualHit;
            while (hitElement != null && hitElement != tabControl)
            {
                if (hitElement is TabItem tabItem)
                    return tabItem;
                hitElement = VisualTreeHelper.GetParent(hitElement);
            }
        }
        return null;
    }

    private void TabHeader_Drop(object sender, DragEventArgs e)
    {
        if (sender is EditableTabHeaderControl targetHeader)
        {
            TabItem? targetTabItem = FindParent<TabItem>(targetHeader);
            ProcessDrop(targetTabItem, e);
        }
    }

    private void ProcessDrop(TabItem? targetTabItem, DragEventArgs e)
    {
        RemoveDropIndicator();
        ClearTabHighlight();
        
        if (!e.Data.GetDataPresent(typeof(PlaylistTab)) || targetTabItem == null)
        {
            e.Handled = true;
            return;
        }

        if (targetTabItem.DataContext is PlaylistTab targetTab && _draggedTab != null)
        {
            // Get mouse position relative to the tab to determine drop position
            Point mousePos = e.GetPosition(targetTabItem);
            double tabWidth = targetTabItem.ActualWidth;
            bool dropOnLeft = mousePos.X < tabWidth / 2;
            
            int targetIndex = GetTabIndex(targetTab);
            
            if (targetIndex >= 0 && _draggedTabIndex >= 0 && _draggedTabIndex != targetIndex)
            {
                // Adjust target index based on drop position
                // If dropping on the right side, we want to insert after this tab
                if (!dropOnLeft)
                {
                    targetIndex++;
                }
                
                // If we're moving from left to right, adjust for the removal
                if (_draggedTabIndex < targetIndex)
                {
                    targetIndex--;
                }
                
                if (_draggedTabIndex != targetIndex && DataContext is PlaylistTabsViewModel viewModel)
                {
                    viewModel.ReorderTabsCommand.Execute((_draggedTabIndex, targetIndex));
                }
            }
        }
        
        e.Handled = true;
    }

    private void ShowDropIndicator(TabItem tabItem, bool onLeft)
    {
        RemoveDropIndicator();

        AdornerLayer? adornerLayer = AdornerLayer.GetAdornerLayer(tabItem);
        if (adornerLayer != null)
        {
            _dropIndicatorAdorner = new DropIndicatorAdorner(tabItem, onLeft);
            adornerLayer.Add(_dropIndicatorAdorner);
        }
    }

    private void RemoveDropIndicator()
    {
        if (_dropIndicatorAdorner != null)
        {
            AdornerLayer? adornerLayer = AdornerLayer.GetAdornerLayer(_dropIndicatorAdorner.AdornedElement);
            adornerLayer?.Remove(_dropIndicatorAdorner);
            _dropIndicatorAdorner = null;
        }
    }

    private void HighlightTab(TabItem tabItem, bool highlight)
    {
        if (highlight)
        {
            tabItem.Opacity = 0.7;
        }
        else
        {
            tabItem.Opacity = 1.0;
        }
    }

    private void ClearTabHighlight()
    {
        if (_lastHighlightedTab != null)
        {
            HighlightTab(_lastHighlightedTab, false);
            _lastHighlightedTab = null;
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parentObject = VisualTreeHelper.GetParent(child);

        if (parentObject == null)
            return null;

        if (parentObject is T parent)
            return parent;

        return FindParent<T>(parentObject);
    }

    private int GetTabIndex(PlaylistTab tab)
    {
        if (DataContext is PlaylistTabsViewModel viewModel)
        {
            return viewModel.TabList.IndexOf(tab);
        }
        return -1;
    }
}

// Adorner class for the drop indicator
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