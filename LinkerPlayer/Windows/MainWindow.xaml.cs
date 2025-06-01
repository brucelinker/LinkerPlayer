using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.ViewModels;
using ManagedBass;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Windows;
using System.Windows.Input;

namespace LinkerPlayer.Windows;

public partial class MainWindow
{
    public static MainWindow? Instance { get; private set; }
    private readonly MainViewModel _mainViewModel;
    private readonly AudioEngine _audioEngine;
    private readonly OutputDeviceManager _outputDeviceManager;
    private static int _count;

    public MainWindow(IServiceProvider serviceProvider)
    {
        Instance = this;
        InitializeComponent();

        _mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
        _audioEngine = serviceProvider.GetRequiredService<AudioEngine>();
        _outputDeviceManager = serviceProvider.GetRequiredService<OutputDeviceManager>();
        DataContext = _mainViewModel;

        Log.Information("App started");
        Log.Information($"MAINWINDOW - {++_count}");

        ((App)Application.Current).WindowPlace.Register(this, "MainWindow");
        WinMax.DoSourceInitialized(this);

        try
        {
            _audioEngine.Initialize();
            _outputDeviceManager.InitializeOutputDevice();
            Log.Information("MainWindow: Audio initialization complete");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to initialize audio: {ex.Message}");
            MessageBox.Show($"Audio initialization failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _mainViewModel.OnWindowLoaded();
        WeakReferenceMessenger.Default.Send(new MainWindowLoadedMessage(true));
    }

    private void OnMainWindowClose(object sender, EventArgs e)
    {
        Bass.Free();
        WeakReferenceMessenger.Default.Send(new MainWindowClosingMessage(true));
        _mainViewModel.OnWindowClosing();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            Log.Information("MainWindow: Shutting down application");
            _audioEngine.Dispose();
            _outputDeviceManager.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error($"Error during shutdown: {ex.Message}");
        }
        base.OnClosing(e);
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
    }
}