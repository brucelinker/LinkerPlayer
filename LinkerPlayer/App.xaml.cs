using LinkerPlayer.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Windows;

namespace LinkerPlayer;

public partial class App
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ServiceCollection serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
        MainWindow mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void ConfigureServices(ServiceCollection serviceCollection)
    {
        serviceCollection.AddTransient<MainWindow>();
    }
}