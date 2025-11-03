using System.Windows;
using System.Windows.Threading;

namespace LinkerPlayer.Windows;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void CloseSplash()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            Close();
        }));
    }
}
