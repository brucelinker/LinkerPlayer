using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestoreWindowPlace;
using Serilog;
using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace LinkerPlayer;

public partial class App
{
    public static IHost AppHost { get; set; } = null!;
    public WindowPlace WindowPlace { get; }

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log.Error(e.ExceptionObject as Exception, "Unhandled exception: {Message}\n{StackTrace}",
                e.ExceptionObject.ToString(), (e.ExceptionObject as Exception)?.StackTrace);
            Log.CloseAndFlush();
        };
        DispatcherUnhandledException += (s, e) =>
        {
            Log.Error(e.Exception, "Dispatcher unhandled exception: {Message}\n{StackTrace}",
                e.Exception.Message, e.Exception.StackTrace);
            e.Handled = true;
            Log.CloseAndFlush();
        };

        WindowPlace = new WindowPlace("placement.config");

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        AppHost = Host.CreateDefaultBuilder()
        .UseSerilog((_, configuration) =>
        {
            configuration
                .MinimumLevel.Debug()
                .WriteTo.Debug()
                .WriteTo.Console()
                .WriteTo.File($"Logs/Serilog-{timestamp}.txt",
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message}{NewLine}{Exception}",
                    buffered: false);
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
            services.AddSingleton<SharedDataModel>();
        })
        .Build();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File($"Logs/LinkerPlayer-{timestamp}.txt",
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message}{NewLine}{Exception}",
                buffered: false)
            .CreateLogger();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        using var mutex = new Mutex(true, "LinkerPlayer", out bool createdNew);
        if (!createdNew)
        {
            Log.Error("Another instance is already running");
            Current.Shutdown();
            return;
        }

        try
        {
            Log.Information("Starting AppHost");
            await AppHost.StartAsync();
            Log.Information("AppHost started successfully");
            MainWindow mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
            Log.Information("Showing MainWindow");
            mainWindow.Show();
            Log.Information("Loading MetadataCache");
            await MusicLibrary.LoadMetadataCacheAsync();
            Log.Information("MetadataCache loaded");
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (IOException ex)
        {
            Log.Error(ex, "IO Exception during startup: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error during startup: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            SettingsManager settingsManager = AppHost.Services.GetRequiredService<SettingsManager>();
            settingsManager.SaveSettings(null!);
            await MusicLibrary.SaveMetadataCacheAsync(); // Save metadata cache on exit
            Log.Information("Application shutdown complete");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during application shutdown");
        }

        await AppHost.StopAsync();
        AppHost.Dispose();
        WindowPlace.Save();
        base.OnExit(e);
    }
}