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
    public readonly PlayerControlsViewModel playerControlsViewModel;
    public readonly DispatcherTimer SeekBarTimer = new();
    public readonly AudioEngine audioEngine;

    public PlayerControls()
    {
        InitializeComponent();

        audioEngine = AudioEngine.Instance;
        playerControlsViewModel = PlayerControlsViewModel.Instance;
        DataContext = PlayerControlsViewModel.Instance;

        SeekBarTimer.Interval = TimeSpan.FromMilliseconds(50);
        SeekBarTimer.Tick += timer_Tick!;

        SeekBar.PreviewMouseLeftButtonUp += SeekBar_PreviewMouseLeftButtonUp;
        SeekBar.ValueChanged += SeekBar_ValueChanged;
        VolumeSlider.Value = Properties.Settings.Default.VolumeSliderValue;


        ShuffleModeButton.IsChecked = Properties.Settings.Default.ShuffleMode;

        WeakReferenceMessenger.Default.Register<SelectedTrackChangedMessage>(this, (_, m) =>
        {
            OnSelectedTrackChanged(m.Value);
        });

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

        //WeakReferenceMessenger.Default.Register<PlaybackStoppedMessage>(this, (_, _) =>
        //{
        //    OnAudioStopped();
        //});

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

        SetTrackStatus(mediaFile ?? new MediaFile());
    }

    private void SetTrackStatus(MediaFile selectedTrack)
    {
        CurrentSongName.Text = selectedTrack.Title;
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
                SeekBarTimer.Start();
                break;
            case PlaybackState.Paused:
                SeekBarTimer.Stop();
                break;
            case PlaybackState.Stopped:
                SeekBarTimer.Stop();
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
        playerControlsViewModel.StopTrack();
        SeekBarTimer.Stop();
        SeekBar.Value = 0;

        PlayButton.Command.Execute(value);
        SeekBarTimer.Start();
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
        double posInSeekBar = (SeekBar.Value * audioEngine.CurrentTrackLength) / 100;

        if (audioEngine.PathToMusic != null &&
            Math.Abs(audioEngine.CurrentTrackPosition - posInSeekBar) > 0 &&
            !audioEngine.IsPaused)
        {
            audioEngine.StopAndPlayFromPosition(posInSeekBar);

            //_playerControlsViewModel.State = PlaybackState.Playing;
            SeekBarTimer.Start();
        }
    }

    private void VolumeSlider_ValueChanged(object sender, EventArgs e)
    {
        audioEngine.MusicVolume = (float)VolumeSlider.Value / 100;
    }

    private void timer_Tick(object sender, EventArgs e)
    {
        if (!(SeekBar.IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed))
        {
            SeekBar.Value = playerControlsViewModel.CurrentSeekbarPosition();
        }
    }

    private void SeekBar_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double>? e)
    {
        if (playerControlsViewModel.SelectedTrack == null) return;

        double posInSeekBar = (SeekBar.Value * audioEngine.CurrentTrackLength) / 100;
        TimeSpan ts = TimeSpan.FromSeconds(posInSeekBar);
        CurrentTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    //private void SeekBar_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double>? e)
    //{
    //    double val = SeekBar.Value;
    //    UIElementCollection borders = UniGrid.Children;

    //    int before = (int)(borders.Count * val / 100);

    //    for (int i = 0; i < borders.Count; i++)
    //    {
    //        if (i < before)
    //            ((borders[i] as Border)!).Opacity = 1;
    //        else
    //            ((borders[i] as Border)!).Opacity = 0.4;
    //    }
    //}

    //private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    //{
    //    PackIcon? icon = MuteButton.Content as PackIcon;
    //    double val = VolumeSlider.Value;

    //    if (val == 0)
    //    {
    //        if (icon != null) icon.Kind = PackIconKind.VolumeMute;
    //    }
    //    else if (val < 50)
    //    {
    //        if (icon != null) icon.Kind = PackIconKind.VolumeMedium;
    //    }
    //    else if (val >= 50)
    //    {
    //        if (icon != null) icon.Kind = PackIconKind.VolumeHigh;
    //    }
    //}

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

    //private void OnAudioStopped()
    //{
    //    if ((audioEngine.CurrentTrackPosition + 0.3) >= audioEngine.CurrentTrackLength)
    //    {
    //        SeekBarTimer.Stop();

    //        _playerControlsViewModel.NextTrack();
    //    }
    //    else
    //    {
    //        audioEngine.Pause();

    //        SeekBarTimer.Stop();
    //    }

    //}

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
        EqualizerWindow equalizerWindow = new()
        {
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        equalizerWindow.Show();

        MainWindow win = (MainWindow)Window.GetWindow(this)!;

        equalizerWindow.StartStopText.Content = audioEngine.IsEqualizerInitialized ? "Stop" : "Start";

        equalizerWindow.LoadSelectedBand(win.SelectedEqualizerProfile);

        if (Properties.Settings.Default.EqualizerOnStartEnabled)
        {
            equalizerWindow.ButtonsSetEnabledState(false);
            equalizerWindow.SliderSetEnabledState(false);
        }
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