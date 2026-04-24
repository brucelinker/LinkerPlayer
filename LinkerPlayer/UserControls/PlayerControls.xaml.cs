using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.Services;
using LinkerPlayer.Services.Playback;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Windows;
using ManagedBass;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly IAudioEngine _audioEngine;
    private readonly EqualizerWindow _equalizerWindow;
    private readonly IPlayerControlsViewModel _vm;
    private readonly ILogger<PlayerControls> _logger;
    private readonly IPlaybackCoordinator _playbackCoordinator;
    private readonly IImportCancellationService _importCancellationService;

    private bool _isUserSeeking;
    private bool _isStopped = true;
    private readonly DispatcherTimer _statusClearTimer;

    public PlayerControls()
    {
        _audioEngine = App.AppHost.Services.GetRequiredService<IAudioEngine>();
        _playbackCoordinator = App.AppHost.Services.GetRequiredService<IPlaybackCoordinator>();
        _importCancellationService = App.AppHost.Services.GetRequiredService<IImportCancellationService>();

        _vm = App.AppHost.Services.GetRequiredService<IPlayerControlsViewModel>();
        DataContext = _vm;

        _logger = App.AppHost.Services.GetRequiredService<ILogger<PlayerControls>>();

        //_logger.LogInformation($"{DataContext} has been set to DataContext");

        _vm.UpdateSelectedTrack += OnSelectedTrackChanged;

        _statusClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusClearTimer.Tick += (_, _) =>
        {
            _statusClearTimer.Stop();
            ProgressInfo.Text = string.Empty;
            TheProgressBar.Value = 0;
        };

        InitializeComponent();

        _seekBarTimer.Interval = TimeSpan.FromMilliseconds(50);
        _seekBarTimer.Tick += timer_Tick!;
        SeekBar.PreviewMouseLeftButtonDown += SeekBar_PreviewMouseLeftButtonDown;
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

        WeakReferenceMessenger.Default.Register<ActiveTrackChangedMessage>(this, (_, m) =>
        {
            OnActiveTrackChanged(m.Value);
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
            TimeSpan ts = TimeSpan.FromSeconds(_vm.SelectedTrack.Duration);
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
        return channels switch
        {
            1 => "mono",
            2 => "stereo",
            4 => "quad",
            6 => "5.1 surround",
            8 => "7.1 surround",
            _ => $"{channels} channels"
        };
    }

    private void OnPlaybackStateChanged(PlaybackState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnPlaybackStateChanged(state)), DispatcherPriority.Background);
            return;
        }

        switch (state)
        {
            case PlaybackState.Playing:
                _seekBarTimer.Start();
                // Initialize seekbar from engine (single source of truth)
                if (_audioEngine.CurrentTrackLength > 0)
                {
                    double percentage = (_audioEngine.CurrentTrackPosition / _audioEngine.CurrentTrackLength) * 100d;
                    if (!double.IsNaN(percentage) && !double.IsInfinity(percentage))
                    {
                        SeekBar.Value = Math.Clamp(percentage, 0d, 100d);
                    }
                }
                else
                {
                    SeekBar.Value = 0;
                }
                _isStopped = false;

                if (_audioEngine.CurrentTrackLength > 0)
                {
                    TimeSpan total = TimeSpan.FromSeconds(_audioEngine.CurrentTrackLength);
                    TotalTime.Text = $"{(int)total.TotalMinutes}:{total.Seconds:D2}";
                }

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

        // Show hint in the Info area during imports; restore output mode when done
        if (progressData.IsProcessing)
        {
            _statusClearTimer.Stop();
            Info.Text = "Press ESC to cancel";
        }
        else
        {
            _statusClearTimer.Stop();
            _statusClearTimer.Start();
            OnOutputModeChanged(_audioEngine.GetCurrentOutputMode());
        }
    }

    private void OnDataGridPlay(PlaybackState value)
    {
        _seekBarTimer.Stop();
        SeekBar.Value = 0;

        // DataGrid play must go through the ViewModel/coordinator path so PlaybackCursor,
        // ActiveTrack, and PlaybackState messages are established correctly.
        _vm.PlayTrack();
    }

    private void SeekBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isUserSeeking = true;
    }

    private void SeekBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (_audioEngine.CurrentTrackLength <= 0 || string.IsNullOrEmpty(_audioEngine.LoadedTrackPath))
            {
                _logger.LogWarning("Seek ignored: no loaded track/length (LoadedTrackPath={LoadedTrackPath}, Length={Length})", _audioEngine.LoadedTrackPath, _audioEngine.CurrentTrackLength);
                return;
            }

            double clickPosition = e.GetPosition(SeekBar).X;
            double seekPercentage = clickPosition / SeekBar.ActualWidth;
            seekPercentage = Math.Clamp(seekPercentage, 0d, 1d);

            double posInSeconds = seekPercentage * _audioEngine.CurrentTrackLength;
            if (double.IsNaN(posInSeconds) || double.IsInfinity(posInSeconds))
            {
                _logger.LogWarning("Seek ignored: computed position invalid (SeekPercentage={SeekPercentage}, Length={Length})", seekPercentage, _audioEngine.CurrentTrackLength);
                return;
            }

            // Move the thumb immediately.
            SeekBar.Value = seekPercentage * 100d;

            _playbackCoordinator.Seek(posInSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeekBar click seek failed");
        }
        finally
        {
            _isUserSeeking = false;
        }
    }

    private void timer_Tick(object sender, EventArgs e)
    {
        if (_isUserSeeking)
        {
            return;
        }

        if (!(SeekBar.IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed))
        {
            double length = _audioEngine.CurrentTrackLength;
            double position = _audioEngine.CurrentTrackPosition;

            if (length > 0 && !double.IsNaN(position) && !double.IsInfinity(position))
            {
                double percentage = (position / length) * 100d;
                if (!double.IsNaN(percentage) && !double.IsInfinity(percentage))
                {
                    SeekBar.Value = Math.Clamp(percentage, 0d, 100d);
                }
            }
        }
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_vm!.SelectedTrack == null)
        {
            return;
        }

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

    private void StatusText_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm.ActiveTrack == null || string.IsNullOrEmpty(StatusText.Text))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            // Handle double-click event on StatusText
            WeakReferenceMessenger.Default.Send(new GoToActiveTrackMessage(true));
        }
    }

    private void PlayerControls_ShutdownStarted(object sender, EventArgs e)
    {
        _vm.SaveSettingsOnShutdown(VolumeSlider.Value, SeekBar.Value);
    }

    private void OnActiveTrackChanged(MediaFile? track)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnActiveTrackChanged(track)), DispatcherPriority.Background);
            return;
        }

        if (track == null)
        {
            TotalTime.Text = "0:00";
            return;
        }

        // Prefer engine length when available; otherwise fall back to metadata duration.
        double engineLengthSeconds = _audioEngine.CurrentTrackLength;
        TimeSpan total = engineLengthSeconds > 0 ? TimeSpan.FromSeconds(engineLengthSeconds) : TimeSpan.FromSeconds(track.Duration);
        TotalTime.Text = $"{(int)total.TotalMinutes}:{total.Seconds:D2}";
    }
}
