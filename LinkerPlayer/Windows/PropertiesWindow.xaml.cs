using LinkerPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LinkerPlayer.Windows;
/// <summary>
/// Interaction logic for PropertiesWindow.xaml
/// </summary>
public partial class PropertiesWindow : Window
{
    public PropertiesWindow()
    {
        InitializeComponent();

        ((App)Application.Current).WindowPlace.Register(this);
        this.Loaded += PropertiesWindow_Loaded;
    }

    private const int SCROLL_AMOUNT = 20; // At the top of the class

    private void PropertiesWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PropertiesViewModel vm)
        {
            vm.CloseRequested += PropertiesViewModel_CloseRequested;
        }
    }

    private void PropertiesViewModel_CloseRequested(object? sender, bool result)
    {
        ClosePropertiesWindow();
    }

    private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Forward the mouse wheel event to the parent ScrollViewer
        if (WindowScrollViewer != null)
        {
            var eventArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            WindowScrollViewer.RaiseEvent(eventArgs);
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        ClosePropertiesWindow();
    }

    private void ClosePropertiesWindow()
    {
        Window? win = GetWindow(this);
        if (win != null) win.Close();
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

    private void LyricsTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Auto-save when focus is lost
        if (!LyricsTextBox.IsReadOnly)
        {
            LyricsTextBox.IsReadOnly = true;
        }
    }

    private void LyricsTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Always prevent bubbling when mouse is over the TextBox
        // This lets the TextBox handle scrolling internally without affecting the parent
        e.Handled = true;

        // Manually scroll the TextBox
        var textBox = sender as TextBox;
        if (textBox != null)
        {
            var scrollViewer = GetScrollViewer(textBox);
            if (scrollViewer != null)
            {
                // Scroll the TextBox content
                if (e.Delta > 0)
                {
                    // Scroll up
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - SCROLL_AMOUNT);
                }
                else
                {
                    // Scroll down
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + SCROLL_AMOUNT);
                }
            }
        }
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer scrollViewer)
            return scrollViewer;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            var result = GetScrollViewer(child);
            if (result != null)
                return result;
        }
        return null;
    }
}