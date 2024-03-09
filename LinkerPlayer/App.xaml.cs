using LinkerPlayer.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestoreWindowPlace;
using Serilog;
using System.Windows;
using LinkerPlayer.Audio;
using LinkerPlayer.ViewModels;

namespace LinkerPlayer;

public partial class App
{
    public static IHost? AppHost { get; set; }
    public WindowPlace WindowPlace { get; }

    public App()
    {
        this.WindowPlace = new WindowPlace("placement.config");

        AppHost = Host.CreateDefaultBuilder()
            .UseSerilog((host, configuration) =>
            {
                configuration
                    .WriteTo.Debug()
                    .WriteTo.Console()
                    .WriteTo.File("Logs/LinkerPlayer-{Date}.txt");
            })
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<MainWindow>();
                services.AddSingleton<PlaylistTabsViewModel>();
                services.AddSingleton<PlayerControlsViewModel>();
            })
            .Build();

        // Global logger
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File("Logs/LinkerPlayer-.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await AppHost!.StartAsync();

        MainWindow mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await AppHost!.StopAsync();
        AppHost.Dispose();
        base.OnExit(e);
        this.WindowPlace.Save();
    }
}