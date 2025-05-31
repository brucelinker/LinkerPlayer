using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestoreWindowPlace;
using Serilog;
using System;
using System.Windows;

namespace LinkerPlayer;

public partial class App
{
    public static IHost AppHost { get; set; } = null!;
    public WindowPlace WindowPlace { get; }

    public App()
    {
        WindowPlace = new WindowPlace("placement.config");

        AppHost = Host.CreateDefaultBuilder()
        .UseSerilog((_, configuration) =>
        {
            configuration
                .WriteTo.Debug()
                .WriteTo.Console()
                .WriteTo.File("Logs/LinkerPlayer-{Date}.txt", rollingInterval: RollingInterval.Day);
        })
        .ConfigureServices((_, services) =>
        {
            services.AddSingleton<SettingsManager>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<PlaylistTabsViewModel>();
            services.AddSingleton<PlayerControlsViewModel>();
            services.AddSingleton<EqualizerWindow>();
            services.AddSingleton<EqualizerViewModel>();
            services.AddSingleton<AudioEngine>();
            services.AddSingleton<OutputDeviceManager>();
            services.AddSingleton<SettingsWindow>();
        })
        .Build();

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File("Logs/LinkerPlayer-.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppHost.Start();

        MainWindow mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var settingsManager = AppHost!.Services.GetRequiredService<SettingsManager>();
        settingsManager.SaveSettings(null!);
        AppHost.StopAsync();
        AppHost.Dispose();
        WindowPlace.Save();
        base.OnExit(e);
    }
}