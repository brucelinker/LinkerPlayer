using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Windows;
using ManagedBass;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
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
    private PlayerControlsViewModel? vm;

    private bool isStopped = true;
    private MediaFile? currentMediaFile;

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

        //WeakReferenceMessenger.Default.Register<SelectedTrackChangedMessage>(this, (_, m) =>
        //{
        //    OnSelectedTrackChanged(m.Value);
        //});
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
        Serilog.Log.Information("PlayerControls Loaded, DataContext type: {Type}", DataContext?.GetType().FullName ?? "null");

        if (DataContext is PlayerControlsViewModel viewModel)
        {
            vm = viewModel;
            vm.UpdateSelectedTrack += OnSelectedTrackChanged;
            Serilog.Log.Information("PlayerControlsViewModel set successfully");
        }

        SetTrackStatus();

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

    private void OnSelectedTrackChanged()
    {
        //if (mediaFile == null) return;
        SetTrackStatus();
    }

    private void SetTrackStatus()
    {
        if (vm.SelectedTrack == null)
        {
            //CurrentTrackName.Text = "";
            TotalTime.Text = "0:00";
            CurrentTime.Text = "0:00";
            StatusText.Text = "Playback stopped.";
            currentMediaFile = null;
            return;
        }

        string extension = Path.GetExtension(vm.SelectedTrack.FileName).Substring(1).ToUpper();
        string channels = GetChannelsString(vm.SelectedTrack.Channels);

        if (vm.SelectedTrack == vm.ActiveTrack)
        {
            //CurrentTrackName.Text = selectedTrack.Title;
            TimeSpan ts = vm.SelectedTrack.Duration;
            TotalTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            CurrentTime.Text = "0:00";
        }

        if (isStopped)
        {
            StatusText.Text = "Playback stopped";
        }
        else
        {
            StatusText.Text = $"{extension} | {vm.SelectedTrack.Bitrate} kbps | {vm.SelectedTrack.SampleRate} Hz | {channels}";
        }
    }

    private string GetChannelsString(int channels)
    {
        if (channels == 1) return "Mono";
        if (channels == 2) return "stereo";
        if (channels > 2) return "multichannel";

        return "";
    }

    private void OnPlaybackStateChanged(PlaybackState state)
    {
        switch (state)
        {
            case PlaybackState.Playing:
                _seekBarTimer.Start();
                isStopped = false;
                break;
            case PlaybackState.Paused:
                _seekBarTimer.Stop();
                isStopped = false;
                break;
            case PlaybackState.Stopped:
                _seekBarTimer.Stop();
                SeekBar.Value = 0;
                isStopped = true;
                break;
        }

        SetTrackStatus();
    }

    private void OnDataGridPlay(PlaybackState value)
    {
        vm!.StopTrack();
        _seekBarTimer.Stop();
        SeekBar.Value = 0;
        PlayButton.Command.Execute(value);
        _seekBarTimer.Start();
        isStopped = false;
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
            SeekBar.Value = vm!.CurrentSeekbarPosition();
        }
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (vm!.SelectedTrack == null) return;
        double posInSeekBar = (SeekBar.Value * _audioEngine.CurrentTrackLength) / 100;
        TimeSpan ts = TimeSpan.FromSeconds(posInSeekBar);
        CurrentTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    private void OnMuteChanged(bool isMuted)
    {
        if (vm == null) return;

        double targetValue = isMuted ? 0 : vm.GetVolumeBeforeMute();
        AnimateVolumeSliderValue(VolumeSlider, targetValue, isMuted);
    }

    private void AnimateVolumeSliderValue(Slider slider, double position, bool isMuted)
    {
        double fromValue = slider.Value;
        DoubleAnimation animation = new()
        {
            From = fromValue,
            To = position,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };

        // Update audio volume during animation
        animation.CurrentTimeInvalidated += (_, _) =>
        {
            if (vm != null)
            {
                double currentValue = slider.Value;
                _audioEngine.MusicVolume = (float)currentValue / 100;
                //Serilog.Log.Debug("Animation tick: Slider.Value={Value}, MusicVolume={Volume}", currentValue, _audioEngine.MusicVolume);
            }
        };

        animation.Completed += (_, _) =>
        {
            slider.BeginAnimation(RangeBase.ValueProperty, null);
            slider.Value = position;
            if (vm != null)
            {
                vm.UpdateVolumeAfterAnimation(position, isMuted);
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
        if (vm != null)
        {
            vm.SaveSettingsOnShutdown(VolumeSlider.Value, SeekBar.Value);
        }
    }
}