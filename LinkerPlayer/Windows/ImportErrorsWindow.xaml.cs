using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using LinkerPlayer.Services;

namespace LinkerPlayer.Windows;

public partial class ImportErrorsWindow : Window
{
    private readonly IImportErrorLogger _logger;

    public ImportErrorsWindow()
    {
        _logger = App.AppHost.Services.GetRequiredService<IImportErrorLogger>();
        InitializeComponent();
        DataContext = _logger;

        // Keep window hidden initially; it will be shown when errors are logged
        this.Visibility = Visibility.Hidden;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
