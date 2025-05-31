using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Windows;
using ManagedBass;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LinkerPlayer.UserControls;

public partial class PlayerControls
{
    private readonly DispatcherTimer _seekBarTimer = new();
    private readonly AudioEngine _audioEngine;
    private readonly EqualizerWindow _equalizerWindow;
    private PlayerControlsViewModel? _playerControlsViewModel;

    public PlayerControls()
    {
        InitializeComponent();

        _audioEngine = App.AppHost.Services.GetRequiredService<AudioEngine>();

        _seekBarTimer.Interval = TimeSpan.FromMilliseconds(50);
        _seekBarTimer.Tick += timer_Tick!;
        SeekBar.PreviewMouseLeftButtonUp += SeekBar_PreviewMouseLeftButtonUp;
        SeekBar.ValueChanged += SeekBar_ValueChanged;
        Dispatcher.ShutdownStarted += PlayerControls_ShutdownStarted!;

        _equalizerWindow = App.AppHost.Services.GetRequiredService<EqualizerWindow>();
        _equalizerWindow.Hide();
        ((App)Application.Current).WindowPlace.Register(_equalizerWindow, "EqualizerWindow");

        WeakReferenceMessenger.Default.Register<ActiveTrackChangedMessage>(this, (_, m) =>
        {
            OnActiveTrackChanged(m.Value);
        });
        WeakReferenceMessenger.Default.Register<DataGridPlayMessage>(this, (_, m) =>
        {
            OnDataGridPlay(m.Value);
        });
        WeakReferenceMessenger.Default.Register<IsMutedMessage>(this, (_, m) =>
        {
            OnMuteChanged(m.Value);
        });
        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (_, m) =>
        {
            OnPlaybackStateChanged(m.Value);
        });

        Loaded += PlayerControls_Loaded;
    }

    private void PlayerControls_Loaded(object sender, RoutedEventArgs e)
    {
        Serilog.Log.Information("PlayerControls Loaded, DataContext type: {Type}", DataContext?.GetType()?.FullName ?? "null");

        if (DataContext is PlayerControlsViewModel viewModel)
        {
            _playerControlsViewModel = viewModel;
            Serilog.Log.Information("PlayerControlsViewModel set successfully");
        }

        LogCommandBinding(PlayButton, "PlayPauseCommand");
        LogCommandBinding(PrevButton, "PrevCommand");
        LogCommandBinding(NextButton, "NextCommand");
        LogCommandBinding(StopButton, "StopCommand");
    }

    private void LogCommandBinding(ButtonBase button, string commandName)
    {
        if (button.Command == null)
        {
            Serilog.Log.Error("{ButtonName} Command is null for {CommandName}", button.Name, commandName);
        }
        else
        {
            Serilog.Log.Information("{ButtonName} Command bound to {CommandType}", button.Name, button.Command.GetType().FullName);
        }
    }

    private void OnActiveTrackChanged(MediaFile? mediaFile)
    {
        if (mediaFile == null) return;
        SetTrackStatus(mediaFile);
    }

    private void SetTrackStatus(MediaFile selectedTrack)
    {
        CurrentTrackName.Text = selectedTrack.Title;
        TimeSpan ts = selectedTrack.Duration;
        TotalTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        CurrentTime.Text = "0:00";
    }

    private void OnPlaybackStateChanged(PlaybackState state)
    {
        switch (state)
        {
            case PlaybackState.Playing:
                _seekBarTimer.Start();
                break;
            case PlaybackState.Paused:
                _seekBarTimer.Stop();
                break;
            case PlaybackState.Stopped:
                _seekBarTimer.Stop();
                SeekBar.Value = 0;
                break;
        }
    }

    private void OnDataGridPlay(PlaybackState value)
    {
        _playerControlsViewModel!.StopTrack();
        _seekBarTimer.Stop();
        SeekBar.Value = 0;
        PlayButton.Command.Execute(value);
        _seekBarTimer.Start();
    }

    private void SeekBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        double clickPosition = e.GetPosition(SeekBar).X;
        double seekPercentage = clickPosition / SeekBar.ActualWidth;
        double posInSeekBar = seekPercentage * _audioEngine.CurrentTrackLength;

        if (!string.IsNullOrEmpty(_audioEngine.PathToMusic) && !double.IsNaN(posInSeekBar))
        {
            _audioEngine.SeekAudioFile(posInSeekBar);
            SeekBar.Value = seekPercentage * 100;
            _seekBarTimer.Start();
        }
        else
        {
            Serilog.Log.Warning("Seek conditions not met: PathToMusic or position invalid");
        }
    }

    private void timer_Tick(object sender, EventArgs e)
    {
        if (!(SeekBar.IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed))
        {
            SeekBar.Value = _playerControlsViewModel!.CurrentSeekbarPosition();
        }
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_playerControlsViewModel!.SelectedTrack == null) return;
        double posInSeekBar = (SeekBar.Value * _audioEngine.CurrentTrackLength) / 100;
        TimeSpan ts = TimeSpan.FromSeconds(posInSeekBar);
        CurrentTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    private void OnMuteChanged(bool isMuted)
    {
        if (_playerControlsViewModel == null) return;

        double targetValue = isMuted ? 0 : _playerControlsViewModel.GetVolumeBeforeMute();
        AnimateVolumeSliderValue(VolumeSlider, targetValue, isMuted);
    }

    private void AnimateVolumeSliderValue(Slider slider, double position, bool isMuted)
    {
        double fromValue = slider.Value;
        DoubleAnimation animation = new()
        {
            From = fromValue,
            To = position,
            Duration = TimeSpan.FromMilliseconds(700),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };

        // Update audio volume during animation
        animation.CurrentTimeInvalidated += (s, e) =>
        {
            if (_playerControlsViewModel != null)
            {
                double currentValue = slider.Value;
                _audioEngine.MusicVolume = (float)currentValue / 100;
                //Serilog.Log.Debug("Animation tick: Slider.Value={Value}, MusicVolume={Volume}", currentValue, _audioEngine.MusicVolume);
            }
        };

        animation.Completed += (s, e) =>
        {
            slider.BeginAnimation(RangeBase.ValueProperty, null);
            slider.Value = position;
            if (_playerControlsViewModel != null)
            {
                _playerControlsViewModel.UpdateVolumeAfterAnimation(position, isMuted);
            }
            //Serilog.Log.Information("VolumeSlider animation completed, Value={Value}, IsMuted={IsMuted}", position, isMuted);
        };

        slider.BeginAnimation(RangeBase.ValueProperty, animation);
    }

    private void OnEqualizerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_equalizerWindow is { IsVisible: true }) return;
        _equalizerWindow.Show();
    }

    private void PlayerControls_ShutdownStarted(object sender, EventArgs e)
    {
        if (_playerControlsViewModel != null)
        {
            _playerControlsViewModel.SaveSettingsOnShutdown(VolumeSlider.Value, SeekBar.Value);
        }
    }
}