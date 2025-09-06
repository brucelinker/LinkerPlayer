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
using System.Threading.Tasks;
using System.Windows;

namespace LinkerPlayer;

public partial class App
{
    public static IHost AppHost { get; set; } = null!;
    public WindowPlace WindowPlace { get; }
    private readonly ILogger<App> _logger;
    private SplashWindow? _splashWindow;

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
                services.AddSingleton<IMusicLibrary, MusicLibrary>();
                
                // Add the new services
                services.AddSingleton<IFileImportService, FileImportService>();
                services.AddSingleton<IPlaylistManagerService, PlaylistManagerService>();
                services.AddSingleton<ITrackNavigationService, TrackNavigationService>();
                services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
                services.AddSingleton<IMediaFileHelper, MediaFileHelper>();
                
                services.AddSingleton<PlaylistTabsViewModel>();
                services.AddSingleton<PlayerControlsViewModel>(); // Fixed: Added missing >
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
            
            // Show splash screen first
            _splashWindow = new SplashWindow();
            _splashWindow.Show();
            _logger.LogInformation("Splash screen shown");
            
            // Start async initialization
            Task.Run(async () =>
            {
                await InitializeApplicationAsync();
            });
            
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
            _logger.LogInformation("Starting background initialization");
            
            // Start the host
            await AppHost.StartAsync();
            _logger.LogInformation("AppHost started successfully");
            
            // Load metadata cache
            IMusicLibrary musicLibrary = AppHost.Services.GetRequiredService<IMusicLibrary>();
            _logger.LogInformation("Loading MetadataCache");
            await musicLibrary.LoadMetadataCacheAsync();
            _logger.LogInformation("MetadataCache loaded");
            
            // Switch to UI thread to create and show main window
            await Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _logger.LogInformation("Creating MainWindow");
                    MainWindow mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
                    
                    // IMPORTANT: Set MainWindow and ShutdownMode BEFORE showing the window
                    MainWindow = mainWindow;
                    ShutdownMode = ShutdownMode.OnMainWindowClose;
                    _logger.LogInformation("MainWindow set as application MainWindow");
                    
                    // Show main window (it starts hidden and will show itself when ready)
                    mainWindow.Show();
                    _logger.LogInformation("MainWindow.Show() called");
                    
                    // Wait longer before closing splash to ensure main window is fully rendered
                    var timer = new System.Windows.Threading.DispatcherTimer 
                    { 
                        Interval = TimeSpan.FromMilliseconds(2000) // Increased to 2 seconds
                    };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        try
                        {
                            _logger.LogInformation("Closing splash screen");
                            _splashWindow?.CloseSplash();
                            _splashWindow = null;
                            _logger.LogInformation("Splash screen closed, MainWindow should be visible");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error closing splash screen");
                        }
                    };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating MainWindow");
                    _splashWindow?.CloseSplash();
                    throw;
                }
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during background initialization");
            await Dispatcher.BeginInvoke(new Action(() =>
            {
                _splashWindow?.CloseSplash();
                MessageBox.Show($"Failed to initialize application: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Save settings and data BEFORE stopping the host
            try
            {
                SettingsManager settingsManager = AppHost.Services.GetRequiredService<SettingsManager>();
                _logger.LogInformation("SettingsManager retrieved for shutdown");
                
                IMusicLibrary musicLibrary = AppHost.Services.GetRequiredService<IMusicLibrary>();
                musicLibrary.SaveMetadataCacheAsync().GetAwaiter().GetResult(); // Save metadata cache on exit
                _logger.LogInformation("Metadata cache saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving data during shutdown - continuing with cleanup");
            }
            
            // Cleanup AudioEngine first to avoid RPC timeouts
            try
            {
                AudioEngine audioEngine = AppHost.Services.GetRequiredService<AudioEngine>();
                audioEngine.Dispose();
                _logger.LogInformation("AudioEngine disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing AudioEngine - continuing with cleanup");
            }
            
            _logger.LogInformation("Application shutdown complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during application shutdown");
        }

        try
        {
            // Stop and dispose the host
            AppHost.StopAsync().GetAwaiter().GetResult();
            AppHost.Dispose();
            _logger.LogInformation("AppHost stopped and disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing AppHost");
        }
        
        try
        {
            WindowPlace.Save();
            _logger.LogInformation("Window placement saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving window placement");
        }
        
        base.OnExit(e);
    }
}