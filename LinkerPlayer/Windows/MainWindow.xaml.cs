using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Core;
using LinkerPlayer.Messages;
using LinkerPlayer.ViewModels;
using Serilog;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

        InitializeComponent();

        // Remember window placement
        ((App)Application.Current).WindowPlace.Register(this, "MainWindow");
        WinMax.DoSourceInitialized(this);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _mainViewModel.OnWindowLoaded();
        WeakReferenceMessenger.Default.Send(new MainWindowLoadedMessage(true));
    }
    
    private void Window_Closing(object sender, EventArgs e)
    {
        _mainViewModel.OnWindowClosing();
        WeakReferenceMessenger.Default.Send(new MainWindowClosingMessage(true));
    }
    
    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            Uri uri = new("/Images/restore.png", UriKind.Relative);
            ImageSource imgSource = new BitmapImage(uri);
            TitlebarButtons.MaximizeButtonImage.Source = imgSource;
        }
        else if (WindowState == WindowState.Normal)
        {
            Uri uri = new("/Images/maximize.png", UriKind.Relative);
            ImageSource imgSource = new BitmapImage(uri);
            TitlebarButtons.MaximizeButtonImage.Source = imgSource;
        }
    }
}