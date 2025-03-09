using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Audio;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Windows;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LinkerPlayer.UserControls;

[ObservableObject]
public partial class PlayerControls
{
    private readonly PlayerControlsViewModel _playerControlsViewModel;
    private readonly DispatcherTimer _seekBarTimer = new();
    private readonly AudioEngine _audioEngine;

    private readonly EqualizerWindow _equalizerWindow;

    public PlayerControls()
    {
        InitializeComponent();

        _audioEngine = AudioEngine.Instance;
        _playerControlsViewModel = PlayerControlsViewModel.Instance;
        DataContext = PlayerControlsViewModel.Instance;

        _seekBarTimer.Interval = TimeSpan.FromMilliseconds(50);
        _seekBarTimer.Tick += timer_Tick!;

        SeekBar.PreviewMouseLeftButtonUp += SeekBar_PreviewMouseLeftButtonUp;
        SeekBar.ValueChanged += SeekBar_ValueChanged;
        Dispatcher.ShutdownStarted += PlayerControls_ShutdownStarted!;


        VolumeSlider.Value = Properties.Settings.Default.VolumeSliderValue;
        ShuffleModeButton.IsChecked = Properties.Settings.Default.ShuffleMode;

        _equalizerWindow = new();
        _equalizerWindow.Hide();
        //{
        //    Owner = Window.GetWindow(this),
        //    WindowStartupLocation = WindowStartupLocation.CenterOwner,
        //    Visibility = Visibility.Hidden
        //};

        // Remember window placement
        ((App)Application.Current).WindowPlace.Register(_equalizerWindow, "EqualizerWindow");

        //WeakReferenceMessenger.Default.Register<SelectedTrackChangedMessage>(this, (_, m) =>
        //{
        //    OnSelectedTrackChanged(m.Value);
        //});

        WeakReferenceMessenger.Default.Register<ActiveTrackChangedMessage>(this, (_, m) =>
        {
            OnActiveTrackChanged(m.Value);
        });

        WeakReferenceMessenger.Default.Register<DataGridPlayMessage>(this, (_, m) =>
        {
            OnDataGridPlay(m.Value);
        });

        WeakReferenceMessenger.Default.Register<MuteMessage>(this, (_, m) =>
        {
            OnMuteChanged(m.Value);
        });

        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (_, m) =>
        {
            OnPlaybackStateChanged(m.Value);
        });
    }

    private void OnActiveTrackChanged(MediaFile? mediaFile)
    {
        if (mediaFile == null) return;

        SetTrackStatus(mediaFile);
    }

    private void OnSelectedTrackChanged(MediaFile? mediaFile)
    {
        if (mediaFile != null)
        {
            _audioEngine.PathToMusic = mediaFile.Path;
            _audioEngine.LoadAudioFile(mediaFile.Path);
        }

        SetTrackStatus(mediaFile ?? new MediaFile());
    }

    private void SetTrackStatus(MediaFile selectedTrack)
    {
        CurrentTrackName.Text = selectedTrack.Title;
        TimeSpan ts = selectedTrack.Duration;
        TotalTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        CurrentTime.Text = "0:00";

        string extension = Path.GetExtension(selectedTrack.FileName).Substring(1).ToUpper();
        string channels = GetChannelsString(selectedTrack.Channels);

        StatusText.Text = $"{extension} | {selectedTrack.Bitrate} kbps | {selectedTrack.SampleRate} Hz | {channels}";
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

    private string GetChannelsString(int channels)
    {
        if (channels == 1) return "Mono";
        if (channels == 2) return "stereo";
        if (channels > 2) return "multichannel";

        return "";
    }

    private void OnDataGridPlay(PlayerState value)
    {
        _playerControlsViewModel.StopTrack();
        _seekBarTimer.Stop();
        SeekBar.Value = 0;

        PlayButton.Command.Execute(value);
        _seekBarTimer.Start();
    }

    private void UniGrid_SizeChanged(object? sender, SizeChangedEventArgs? e)
    {
        int n = UniGrid.Children.Count;

        if (n == 0)
        {
            return;
        }

        int k = (int)UniGrid.ActualWidth / 6;

        List<int> numbers = new();
        for (int i = 0; i < n; i++)
        {
            numbers.Add(i);
        }

        List<int> reducedList = numbers.EvenlySpacedSubset(k);

        for (int i = 0; i < UniGrid.Children.Count; i++)
        {
            if (UniGrid.Children[i] is Border border)
            {
                border.Visibility = reducedList.Contains(i) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void SeekBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        double posInSeekBar = (SeekBar.Value * _audioEngine.CurrentTrackLength) / 100;

        if (_audioEngine.PathToMusic != null &&
            Math.Abs(_audioEngine.CurrentTrackPosition - posInSeekBar) > 0 &&
            !_audioEngine.IsPaused)
        {
            _audioEngine.StopAndPlayFromPosition(posInSeekBar);

            //_playerControlsViewModel.State = PlaybackState.Playing;
            _seekBarTimer.Start();
        }
    }

    private void VolumeSlider_ValueChanged(object sender, EventArgs e)
    {
        _audioEngine.MusicVolume = (float)VolumeSlider.Value / 100;

        Properties.Settings.Default.VolumeSliderValue = VolumeSlider.Value;
    }

    private void timer_Tick(object sender, EventArgs e)
    {
        if (!(SeekBar.IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed))
        {
            SeekBar.Value = _playerControlsViewModel.CurrentSeekbarPosition();
        }
    }

    private void SeekBar_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double>? e)
    {
        if (_playerControlsViewModel.SelectedTrack == null) return;

        double posInSeekBar = (SeekBar.Value * _audioEngine.CurrentTrackLength) / 100;
        TimeSpan ts = TimeSpan.FromSeconds(posInSeekBar);
        CurrentTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    double _mainVolumeSliderBeforeMuteValue;

    private void OnMuteChanged(bool isMuted)
    {
        if (isMuted)
        {
            _mainVolumeSliderBeforeMuteValue = VolumeSlider.Value;

            AnimateVolumeSliderValue(VolumeSlider, 0);
        }
        else
        {
            AnimateVolumeSliderValue(VolumeSlider, _mainVolumeSliderBeforeMuteValue);
        }
    }

    private void AnimateVolumeSliderValue(Slider slider, double newVal)
    {
        DoubleAnimation doubleAnimation = new()
        {
            From = slider.Value,
            To = newVal,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
        };

        slider.BeginAnimation(RangeBase.ValueProperty, doubleAnimation);
    }

    private void OnEqualizerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_equalizerWindow is { IsVisible: true }) return;

        _equalizerWindow.Show();
    }

    private void PlayerControls_ShutdownStarted(object sender, EventArgs e)
    {
        Properties.Settings.Default.VolumeSliderValue = VolumeSlider.Value;
        Properties.Settings.Default.LastSeekBarValue = SeekBar.Value;
    }
}

public static class ListExtensions
{
    public static List<T> EvenlySpacedSubset<T>(this List<T> list, int count)
    {
        int length = list.Count;
        int[] indices = Enumerable.Range(0, count)
            .Select(i => (int)Math.Round((double)(i * (length - 1)) / (count - 1)))
            .ToArray();
        return indices.Select(i => list[i]).ToList();
    }
}