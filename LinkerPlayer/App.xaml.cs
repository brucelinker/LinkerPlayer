using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestoreWindowPlace;
using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace LinkerPlayer;

public partial class App
{
    public static IHost AppHost { get; set; } = null!;
    public WindowPlace WindowPlace { get; }
    private readonly ILogger<App> _logger;

    public App()
    {
        WindowPlace = new WindowPlace("placement.config");

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        AppHost = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders(); // Clear default providers to avoid duplicate logs
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddSimpleConsole(options =>
                {
                    options.IncludeScopes = false;
                    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
                    options.UseUtcTimestamp = false;
                });
                logging.AddDebug();
                logging.AddFile($"Logs/LinkerPlayer-{timestamp}.txt", options =>
                {
                    options.FormatLogEntry = entry =>
                        $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.LogLevel}] {entry.Message}{Environment.NewLine}{entry.Exception}";
                });
            })
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<SettingsManager>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MusicLibrary>();
                
                // Add the new services
                services.AddSingleton<IFileImportService, FileImportService>();
                services.AddSingleton<IPlaylistManagerService, PlaylistManagerService>();
                services.AddSingleton<ITrackNavigationService, TrackNavigationService>();
                services.AddSingleton<IUIDispatcher, WpfUIDispatcher>();
                services.AddSingleton<IMediaFileHelper, MediaFileHelper>();
                
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

        _logger = AppHost.Services.GetRequiredService<ILogger<App>>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        using Mutex mutex = new Mutex(true, "LinkerPlayer", out bool createdNew);
        if (!createdNew)
        {
            _logger.LogError("Another instance is already running");
            Current.Shutdown();
            return;
        }

        try
        {
            _logger.LogInformation("Starting AppHost");
            AppHost.StartAsync();
            _logger.LogInformation("AppHost started successfully");
            MainWindow mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
            MusicLibrary musicLibrary = AppHost.Services.GetRequiredService<MusicLibrary>();
            _logger.LogInformation("Showing MainWindow");
            mainWindow.Show();
            _logger.LogInformation("Loading MetadataCache");
            musicLibrary.LoadMetadataCacheAsync().GetAwaiter().GetResult();
            _logger.LogInformation("MetadataCache loaded");
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO Exception during startup: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during startup: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            SettingsManager settingsManager = AppHost.Services.GetRequiredService<SettingsManager>();
            settingsManager.SaveSettings(null!);
            MusicLibrary musicLibrary = AppHost.Services.GetRequiredService<MusicLibrary>();
            musicLibrary.SaveMetadataCacheAsync().GetAwaiter().GetResult(); // Save metadata cache on exit
            _logger.LogInformation("Application shutdown complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during application shutdown");
        }

        AppHost.StopAsync().GetAwaiter().GetResult();
        AppHost.Dispose();
        WindowPlace.Save();
        base.OnExit(e);
    }
}