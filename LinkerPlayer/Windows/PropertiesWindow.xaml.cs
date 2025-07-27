using LinkerPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace LinkerPlayer.Windows;
/// <summary>
/// Interaction logic for PropertiesWindow.xaml
/// </summary>
public partial class PropertiesWindow : Window
{
    public PropertiesWindow()
    {
        InitializeComponent();
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
}
