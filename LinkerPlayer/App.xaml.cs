using LinkerPlayer.Audio;
using LinkerPlayer.BassLibs;
using LinkerPlayer.Core;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.ViewModels;
using LinkerPlayer.ViewModels.Properties.Loaders;
using LinkerPlayer.Windows; // restore windows namespace for window types
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestoreWindowPlace;
using System.IO;
using System.Windows;
using System.Windows.Threading;

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
                logging.ClearProviders();
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
                services.AddSingleton<MainWindow>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<IMusicLibrary, MusicLibrary>();
                services.AddSingleton<IFileImportService, FileImportService>();
                services.AddSingleton<IPlaylistManagerService, PlaylistManagerService>();
                services.AddSingleton<ITrackNavigationService, TrackNavigationService>();
                services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
                services.AddSingleton<IUiNotifier, WpfUiNotifier>();
                services.AddSingleton<IMediaFileHelper, MediaFileHelper>();
                services.AddSingleton<IOutputDeviceManager, OutputDeviceManager>();
                services.AddSingleton<ISettingsManager, SettingsManager>();
                services.AddSingleton<IDatabaseSaveService, DatabaseSaveService>();
                services.AddSingleton<IBpmDetector, BpmDetector>();
                services.AddSingleton<IReplayGainCalculator, ReplayGainCalculator>();
                services.AddSingleton<PlaylistTabsViewModel>();
                services.AddSingleton<PlayerControlsViewModel>();
                services.AddSingleton<EqualizerWindow>();
                services.AddSingleton<EqualizerViewModel>();
                services.AddSingleton<AudioEngine>();
                services.AddSingleton<BassAudioEngine>();
                services.AddSingleton<SettingsWindow>();
                services.AddSingleton<SharedDataModel>();
                services.AddSingleton<ISharedDataModel>(sp => sp.GetRequiredService<SharedDataModel>());
                services.AddSingleton<ISelectionService, SelectionService>(); // new selection service
                services.AddTransient<CoreMetadataLoader>();
                services.AddTransient<CustomMetadataLoader>();
                services.AddTransient<FilePropertiesLoader>();
                services.AddTransient<ReplayGainLoader>();
                services.AddTransient<PictureInfoLoader>();
                services.AddTransient<LyricsCommentLoader>();
                services.AddTransient<PropertiesViewModel>();
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
            _logger.LogInformation("Starting AppHost (no splash timing test)");
            Task.Run(async () => await InitializeApplicationAsync());
            base.OnStartup(e);
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

    private async Task InitializeApplicationAsync()
    {
        try
        {
            _logger.LogInformation("Background init started");
            await AppHost.StartAsync();
            _logger.LogInformation("Host started");

            // Show MainWindow immediately after host start
            await Dispatcher.BeginInvoke(new Action(() =>
            {
                MainWindow mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
                MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
                _logger.LogInformation("MainWindow shown early (splash disabled)");
            }));

            // Fire off background tasks (do not await before showing UI)
            BassAudioEngine bassEngine = AppHost.Services.GetRequiredService<BassAudioEngine>();
            IMusicLibrary library = AppHost.Services.GetRequiredService<IMusicLibrary>();

            Task bassInit = Task.Run(() =>
            {
                try
                {
                    bassEngine.Initialize(new BassInitializationOptions());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "BASS init failed");
                }
            });
            Task libLoad = Task.Run(async () =>
            {
                try
                {
                    await library.LoadFromDatabaseAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Library load failed");
                }
            });
            await Task.WhenAll(bassInit, libLoad);
            _logger.LogInformation("Background init complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialization error");
            await Dispatcher.BeginInvoke(new Action(() =>
            {
                MessageBox.Show($"Failed to initialize: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            try
            {
                ISettingsManager settingsManager = AppHost.Services.GetRequiredService<ISettingsManager>();
                IDatabaseSaveService databaseSaveService = AppHost.Services.GetRequiredService<IDatabaseSaveService>();
                databaseSaveService.SaveImmediately();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving during shutdown");
            }
            try
            {
                AudioEngine audioEngine = AppHost.Services.GetRequiredService<AudioEngine>();
                audioEngine.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing AudioEngine");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shutdown error");
        }
        try
        {
            AppHost.StopAsync().GetAwaiter().GetResult();
            AppHost.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Host dispose error");
        }
        base.OnExit(e);
    }
}
