using LinkerPlayer.ViewModels;
using System.Windows;
using System.Windows.Input;

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
}
