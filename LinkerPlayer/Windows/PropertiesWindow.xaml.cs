using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LinkerPlayer.Windows;

/// <summary>
/// Interaction logic for PropertiesWindow.xaml
/// </summary>
public partial class PropertiesWindow
{
    public PropertiesWindow()
    {
        InitializeComponent();

        ((App)Application.Current).WindowPlace.Register(this, "PropertiesWindow");
        this.Loaded += PropertiesWindow_Loaded;
    }

    private void PropertiesWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PropertiesViewModel vm)
        {
            vm.CloseRequested += PropertiesViewModel_CloseRequested;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Properly unsubscribe from VM events when window closes. Do not dispose singleton VM here.
        if (DataContext is PropertiesViewModel vm)
        {
            vm.CloseRequested -= PropertiesViewModel_CloseRequested;
            // Do not call vm.Dispose() for singleton VM; host will dispose at shutdown
        }

        base.OnClosed(e);
    }

    private const int ScrollAmount = 20; // At the top of the class

    private void PropertiesViewModel_CloseRequested(object? sender, bool result)
    {
        ClosePropertiesWindow();
    }

    private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Forward the mouse wheel event to the parent ScrollViewer
        if (WindowScrollViewer != null)
        {
            MouseWheelEventArgs eventArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            WindowScrollViewer.RaiseEvent(eventArgs);
            e.Handled = true;
        }
    }

    private void PictureDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        // Cancel edit if the row is not editable
        if (e.Row.Item is TagItem tagItem && !tagItem.IsEditable)
        {
            e.Cancel = true;
        }
    }

    private void MetadataDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        // Cancel edit if the row is not editable (e.g., custom tags with angle brackets)
        if (e.Row.Item is TagItem tagItem && !tagItem.IsEditable)
        {
            e.Cancel = true;
        }
    }

    private void ReplayGainDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        // Cancel edit if the row is not editable (e.g., Peak values)
        if (e.Row.Item is TagItem tagItem && !tagItem.IsEditable)
        {
            e.Cancel = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        ClosePropertiesWindow();
    }

    private void ClosePropertiesWindow()
    {
        // Hide instead of closing to allow reuse of single instance
        Window? win = GetWindow(this);
        if (win != null)
        {
            win.Hide();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // During application shutdown allow close; otherwise convert close to hide so the window can be reused
        if (Application.Current?.Dispatcher.HasShutdownStarted == true || Application.Current?.Dispatcher.HasShutdownFinished == true)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        this.Hide();
    }

    private void LyricsTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-click to enter edit mode
        if (DataContext is PropertiesViewModel vm && vm.LyricsItem.IsEditable)
        {
            LyricsTextBox.IsReadOnly = false;
            LyricsTextBox.Focus();
            LyricsTextBox.SelectAll();
        }
    }

    private void CommentTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-click to enter edit mode
        if (DataContext is PropertiesViewModel vm && vm.CommentItem.IsEditable)
        {
            CommentTextBox.IsReadOnly = false;
            CommentTextBox.Focus();
            CommentTextBox.SelectAll();
        }
    }

    private void LyricsTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // Clear all DataGrid selections when Lyrics TextBox is focused
        ClearAllDataGridSelections();
    }

    private void CommentTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // Clear all DataGrid selections when Comment TextBox is focused
        ClearAllDataGridSelections();
    }

    private void ClearAllDataGridSelections()
    {
        MetadataDataGrid?.UnselectAll();
        PropertiesDataGrid?.UnselectAll();
        ReplayGainDataGrid?.UnselectAll();
        PictureDataGrid?.UnselectAll();
    }

    private void EditableTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // Select all text when an editable TextBox gets focus (like in Foobar2000)
        if (sender is TextBox textBox && !textBox.IsReadOnly)
        {
            // Use Dispatcher to ensure the text is selected after the TextBox is fully loaded
            textBox.Dispatcher.BeginInvoke(new Action(() =>
                 {
                     textBox.SelectAll();
                 }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void LyricsTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !LyricsTextBox.IsReadOnly)
        {
            // Escape to cancel editing
            LyricsTextBox.IsReadOnly = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyboardDevice.Modifiers == ModifierKeys.Control && !LyricsTextBox.IsReadOnly)
        {
            // Ctrl+Enter to save and exit editing
            LyricsTextBox.IsReadOnly = true;
            e.Handled = true;
        }
    }

    private void CommentTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !CommentTextBox.IsReadOnly)
        {
            // Escape to cancel editing
            CommentTextBox.IsReadOnly = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyboardDevice.Modifiers == ModifierKeys.Control && !CommentTextBox.IsReadOnly)
        {
            // Ctrl+Enter to save and exit editing
            CommentTextBox.IsReadOnly = true;
            e.Handled = true;
        }
    }

    private void LyricsTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Auto-save when focus is lost
        if (!LyricsTextBox.IsReadOnly)
        {
            LyricsTextBox.IsReadOnly = true;
        }
    }

    private void CommentTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Auto-save when focus is lost
        if (!CommentTextBox.IsReadOnly)
        {
            CommentTextBox.IsReadOnly = true;
        }
    }

    private void LyricsTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TextBox? textBox = sender as TextBox;
        if (textBox != null)
        {
            ScrollViewer? scrollViewer = GetScrollViewer(textBox);
            if (scrollViewer != null)
            {
                // Only handle scrolling if the TextBox has content that can be scrolled
                bool canScrollUp = scrollViewer.VerticalOffset > 0;
                bool canScrollDown = scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;

                // If scrolling up and we're at the top, or scrolling down and we're at the bottom,
                // let the event bubble to the parent ScrollViewer
                if ((e.Delta > 0 && !canScrollUp) || (e.Delta < 0 && !canScrollDown))
                {
                    return; // Don't handle, let it bubble up
                }

                // Otherwise, handle the scroll internally
                e.Handled = true;
                if (e.Delta > 0)
                {
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - ScrollAmount);
                }
                else
                {
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + ScrollAmount);
                }
            }
        }
    }

    private void CommentTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TextBox? textBox = sender as TextBox;
        if (textBox != null)
        {
            ScrollViewer? scrollViewer = GetScrollViewer(textBox);
            if (scrollViewer != null)
            {
                // Only handle scrolling if the TextBox has content that can be scrolled
                bool canScrollUp = scrollViewer.VerticalOffset > 0;
                bool canScrollDown = scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;

                // If scrolling up and we're at the top, or scrolling down and we're at the bottom,
                // let the event bubble to the parent ScrollViewer
                if ((e.Delta > 0 && !canScrollUp) || (e.Delta < 0 && !canScrollDown))
                {
                    return; // Don't handle, let it bubble up
                }

                // Otherwise, handle the scroll internally
                e.Handled = true;
                if (e.Delta > 0)
                {
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - ScrollAmount);
                }
                else
                {
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + ScrollAmount);
                }
            }
        }
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer scrollViewer)
        {
            return scrollViewer;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(element, i);
            ScrollViewer? result = GetScrollViewer(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only handle additions (when a row is selected), not removals
        if (e.AddedItems.Count == 0)
        {
            return;
        }

        DataGrid? currentDataGrid = sender as DataGrid;
        if (currentDataGrid == null)
        {
            return;
        }

        // Clear selection in all other DataGrids
        if (currentDataGrid != MetadataDataGrid)
        {
            MetadataDataGrid?.UnselectAll();
        }

        if (currentDataGrid != PropertiesDataGrid)
        {
            PropertiesDataGrid?.UnselectAll();
        }

        if (currentDataGrid != ReplayGainDataGrid)
        {
            ReplayGainDataGrid?.UnselectAll();
        }

        if (currentDataGrid != PictureDataGrid)
        {
            PictureDataGrid?.UnselectAll();
        }
    }
}
