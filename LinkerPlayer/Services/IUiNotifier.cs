using System.Windows;
using System.Windows.Threading;

namespace LinkerPlayer.Services;

public interface IUiNotifier
{
    void ShowWarning(string title, string message);
    void ShowError(string title, string message);
}

public class WpfUiNotifier : IUiNotifier
{
    public void ShowWarning(string title, string message)
    {
        Show(title, message, MessageBoxImage.Warning);
    }

    public void ShowError(string title, string message)
    {
        Show(title, message, MessageBoxImage.Error);
    }

    private static void Show(string title, string message, MessageBoxImage image)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            // Fallback if no dispatcher available
            MessageBox.Show(message, title, MessageBoxButton.OK, image);
            return;
        }

        if (dispatcher.CheckAccess())
        {
            Window? owner = Application.Current?.MainWindow;
            if (owner != null)
            {
                MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
            }
            else
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, image);
            }
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() =>
            {
                Window? owner = Application.Current?.MainWindow;
                if (owner != null)
                {
                    MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
                }
                else
                {
                    MessageBox.Show(message, title, MessageBoxButton.OK, image);
                }
            }));
        }
    }
}
