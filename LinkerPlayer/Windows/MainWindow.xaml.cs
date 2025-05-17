using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.ViewModels;
using ManagedBass;
using Serilog;
using System;
using System.Windows;
using System.Windows.Input;

namespace LinkerPlayer.Windows;

public partial class MainWindow
{
    public static MainWindow? Instance { get; private set; }
    private readonly MainViewModel _mainViewModel;
    private static int _count;

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();

        _mainViewModel = new MainViewModel();
        DataContext = _mainViewModel;

        Log.Information("App started");
        Log.Information($"MAINWINDOW - {++_count}");

        // Remember window placement
        ((App)Application.Current).WindowPlace.Register(this, "MainWindow");
        WinMax.DoSourceInitialized(this);

        InitializeComponent();
        try
        {
            AudioEngine.Initialize();
            OutputDeviceManager.InitializeOutputDevice();
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

    private void Window_Closing(object sender, EventArgs e)
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
            AudioEngine.Instance.Dispose();
            OutputDeviceManager.Dispose();
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