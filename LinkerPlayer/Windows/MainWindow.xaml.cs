using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.ViewModels;
using ManagedBass;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace LinkerPlayer.Windows;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }
    private readonly MainViewModel _mainViewModel;
    private readonly AudioEngine _audioEngine;
    private readonly OutputDeviceManager _outputDeviceManager;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(IServiceProvider serviceProvider, ILogger<MainWindow> logger)
    {
        _logger = logger;

        try
        {
            Instance = this;
            InitializeComponent();

            _logger.LogInformation("MainWindow: Regular WPF Window initialized");

            _mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
            _audioEngine = serviceProvider.GetRequiredService<AudioEngine>();
            _outputDeviceManager = serviceProvider.GetRequiredService<OutputDeviceManager>();
            DataContext = _mainViewModel;

            ((App)Application.Current).WindowPlace.Register(this, "MainWindow");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error in MainWindow constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in MainWindow constructor: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            throw;
        }

        try
        {
            _audioEngine.InitializeBass();
            _outputDeviceManager.InitializeOutputDevice();
            _logger.LogInformation("MainWindow: Audio initialization complete");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to initialize audio: {ex.Message}");
            MessageBox.Show($"Audio initialization failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _logger.LogInformation("MainWindow: Window_Loaded event fired");
        
        // Initialize the view model
        _mainViewModel.OnWindowLoaded();
        WeakReferenceMessenger.Default.Send(new MainWindowLoadedMessage(true));
        
        _logger.LogInformation("MainWindow: Regular WPF Window loaded successfully");
    }

    private void OnMainWindowClose(object sender, EventArgs e)
    {
        _logger.LogInformation("MainWindow: OnMainWindowClose called");
        // Remove Bass.Free() from here - it will be handled by AudioEngine.Dispose()
        WeakReferenceMessenger.Default.Send(new MainWindowClosingMessage(true));
        _mainViewModel.OnWindowClosing();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            _logger.LogInformation("MainWindow: Shutting down application");
            // Remove manual disposal from here - it will be handled by App.OnExit()
            //_audioEngine.Dispose();
            //_outputDeviceManager.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error during shutdown: {ex.Message}");
        }
        base.OnClosing(e);
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        _logger.LogInformation("MainWindow: Window state changed to: {State}", WindowState);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}