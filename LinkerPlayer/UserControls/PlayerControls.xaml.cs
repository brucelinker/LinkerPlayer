using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Windows;
using ManagedBass;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Runtime;
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
    private readonly PlayerControlsViewModel _vm;
    private readonly ILogger<PlayerControls> _logger;


    private bool _isStopped = true;

    public PlayerControls()
    {
        _audioEngine = App.AppHost.Services.GetRequiredService<AudioEngine>();

        _vm = App.AppHost.Services.GetRequiredService<PlayerControlsViewModel>();
        DataContext = _vm;

        _logger = App.AppHost.Services.GetRequiredService<ILogger<PlayerControls>>();

        //_logger.LogInformation($"{DataContext} has been set to DataContext");

        _vm.UpdateSelectedTrack += OnSelectedTrackChanged;

        InitializeComponent();

        _seekBarTimer.Interval = TimeSpan.FromMilliseconds(50);
        _seekBarTimer.Tick += timer_Tick!;
        SeekBar.PreviewMouseLeftButtonUp += SeekBar_PreviewMouseLeftButtonUp;
        SeekBar.ValueChanged += SeekBar_ValueChanged;
        Dispatcher.ShutdownStarted += PlayerControls_ShutdownStarted!;

        _equalizerWindow = App.AppHost.Services.GetRequiredService<EqualizerWindow>();
        _equalizerWindow.Hide();

        SetTrackStatus();

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

        WeakReferenceMessenger.Default.Register<ProgressValueMessage>(this, (_, m) =>
        {
            OnProgressDataChanged(m.Value);
        });

        WeakReferenceMessenger.Default.Register<OutputModeChangedMessage>(this, (_, m) =>
        {
            OnOutputModeChanged(m.Value);
        });

        OnOutputModeChanged(_audioEngine.GetCurrentOutputMode());

        _logger.LogInformation("PlayerControls Loaded, DataContext type: {Type}", DataContext?.GetType().FullName ?? "null");
    }

    private void OnSelectedTrackChanged()
    {
        SetTrackStatus();
    }

    private void SetTrackStatus()
    {
        if (_vm.SelectedTrack == null)
        {
            TotalTime.Text = "0:00";
            CurrentTime.Text = "0:00";
            StatusText.Text = "Playback stopped.";
            return;
        }

        string extension = Path.GetExtension(_vm.SelectedTrack.FileName).Substring(1).ToUpper();
        string channels = GetChannelsString(_vm.SelectedTrack.Channels);

        if (_vm.SelectedTrack == _vm.ActiveTrack)
        {
            TimeSpan ts = _vm.SelectedTrack.Duration;
            TotalTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            CurrentTime.Text = "0:00";
        }

        if (_isStopped)
        {
            StatusText.Text = "Playback stopped";
        }
        else
        {
            StatusText.Text = $"{extension} | {_vm.SelectedTrack.Bitrate} kbps | {_vm.SelectedTrack.SampleRate} Hz | {channels}";
        }
    }

    private void OnOutputModeChanged(OutputMode mode)
    {
        switch (mode)
        {
            case OutputMode.DirectSound:
                Info.Text = "DirectSound";
                break;
            case OutputMode.WasapiShared:
                Info.Text = "Wasapi Shared";
                break;
            case OutputMode.WasapiExclusive:
                Info.Text = "Wasapi Exclusive";
                break;
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
                _isStopped = false;
                break;
            case PlaybackState.Paused:
                _seekBarTimer.Stop();
                _isStopped = false;
                break;
            case PlaybackState.Stopped:
                _seekBarTimer.Stop();
                SeekBar.Value = 0;
                _isStopped = true;
                break;
        }

        SetTrackStatus();
    }

    private void OnProgressDataChanged(ProgressData progressData)
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => OnProgressDataChanged(progressData));
            return;
        }

        TheProgressBar.Maximum = progressData.TotalTracks > 0 ? progressData.TotalTracks : 1;
        TheProgressBar.Value = progressData.ProcessedTracks;
        ProgressInfo.Text = progressData.IsProcessing
            ? $"{progressData.Status} ({progressData.ProcessedTracks}/{progressData.TotalTracks})"
            : progressData.Status;
    }

    private void OnDataGridPlay(PlaybackState value)
    {
        _vm.StopTrack();
        _seekBarTimer.Stop();
        SeekBar.Value = 0;
        PlayButton.Command.Execute(value);
        _seekBarTimer.Start();
        _isStopped = false;
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
            _logger.LogWarning("Seek conditions not met: PathToMusic or position invalid");
        }
    }

    private void timer_Tick(object sender, EventArgs e)
    {
        if (!(SeekBar.IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed))
        {
            SeekBar.Value = _vm!.CurrentSeekbarPosition();
        }
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_vm!.SelectedTrack == null) return;
        double posInSeekBar = (SeekBar.Value * _audioEngine.CurrentTrackLength) / 100;
        TimeSpan ts = TimeSpan.FromSeconds(posInSeekBar);
        CurrentTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    private void OnMuteChanged(bool isMuted)
    {
        double targetValue = isMuted ? 0 : _vm.GetVolumeBeforeMute();
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
            double currentValue = slider.Value;
            _audioEngine.MusicVolume = (float)currentValue / 100;
        };

        animation.Completed += (_, _) =>
        {
            slider.BeginAnimation(RangeBase.ValueProperty, null);
            slider.Value = position;

            _vm.UpdateVolumeAfterAnimation(position, isMuted);
        };

        slider.BeginAnimation(RangeBase.ValueProperty, animation);
    }

    private void OnEqualizerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_equalizerWindow is { IsVisible: true })
        {
            _equalizerWindow.Hide();
        }
        else
        {
            _equalizerWindow.Show();
        }
    }

    private void PlayerControls_ShutdownStarted(object sender, EventArgs e)
    {
        _vm.SaveSettingsOnShutdown(VolumeSlider.Value, SeekBar.Value);
    }
}