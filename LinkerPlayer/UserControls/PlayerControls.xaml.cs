using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LinkerPlayer.Messages;
using LinkerPlayer.Models;
using LinkerPlayer.ViewModels;
using LinkerPlayer.Windows;
using MaterialDesignThemes.Wpf;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LinkerPlayer.UserControls;

[ObservableObject]
public partial class PlayerControls
{
    private readonly PlayerControlsViewModel _playerControlsViewModel = new();
    public bool Rendering;

    public PlayerControls()
    {
        InitializeComponent();

        DataContext = _playerControlsViewModel;

        ShuffleModeButton.IsChecked = Properties.Settings.Default.ShuffleMode;

        WeakReferenceMessenger.Default.Register<SelectedTrackChangedMessage>(this, (r, m) =>
        {
            OnSelectedTrackChanged(m.Value);
        });

        WeakReferenceMessenger.Default.Register<DataGridPlayMessage>(this, (r, m) =>
        {
            OnDataGridPlay(m.Value);
        });

    }

    public void ShowSeekBarHideBorders()
    {
        // reset SeekBar scaling to 1
        SeekBar.RenderTransformOrigin = new Point(0.5, 0.5);
        SeekBar.RenderTransform = new ScaleTransform() { ScaleY = 1 };

        // animate SeekBar opacity from 0 to SeekBar.Opacity
        DoubleAnimation seekBarOpacityAnimation = new DoubleAnimation
        {
            From = SeekBar.Opacity,
            To = 1,
            Duration = TimeSpan.FromSeconds(1),
            EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
        };

        Storyboard storyboardSeekBarOpacity = new Storyboard();

        Storyboard.SetTarget(seekBarOpacityAnimation, SeekBar);
        Storyboard.SetTargetProperty(seekBarOpacityAnimation, new PropertyPath(OpacityProperty));
        storyboardSeekBarOpacity.Children.Add(seekBarOpacityAnimation);

        storyboardSeekBarOpacity.Begin();

        // animate UniGrid scaleY from 1 to 0
        UniGrid.RenderTransformOrigin = new Point(0.5, 0.5);
        UniGrid.RenderTransform = new ScaleTransform() { ScaleY = 1 };

        DoubleAnimation uniGridScaleAnimation = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
        };

        Storyboard storyboardUniGridScale = new Storyboard();

        Storyboard.SetTarget(uniGridScaleAnimation, UniGrid);
        Storyboard.SetTargetProperty(uniGridScaleAnimation,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        storyboardUniGridScale.Children.Add(uniGridScaleAnimation);
        storyboardUniGridScale.Begin();
    }

    public void HideSeekBarShowBorders()
    {
        SeekBar.RenderTransformOrigin = new Point(0.5, 0.5);
        SeekBar.RenderTransform = new ScaleTransform() { ScaleY = 1 };

        // animate SeekBar opacity from 1 to 0
        DoubleAnimation seekBarOpacityAnimation = new DoubleAnimation
        {
            From = SeekBar.Opacity,
            To = 0,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
        };

        Storyboard storyboardSeekBarOpacity = new Storyboard();

        Storyboard.SetTarget(seekBarOpacityAnimation, SeekBar);
        Storyboard.SetTargetProperty(seekBarOpacityAnimation, new PropertyPath(OpacityProperty));
        storyboardSeekBarOpacity.Children.Add(seekBarOpacityAnimation);
        storyboardSeekBarOpacity.Begin();

        // animate UniGrid scaleY from 0 to 1
        UniGrid.RenderTransformOrigin = new Point(0.5, 0.5);
        UniGrid.RenderTransform = new ScaleTransform() { ScaleY = 1 };
        DoubleAnimation uniGridScaleAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
        };

        uniGridScaleAnimation.Completed += (_, _) =>
        {
            // set SeekBar scaling to 2
            SeekBar.RenderTransformOrigin = new Point(0.5, 0.5);
            SeekBar.RenderTransform = new ScaleTransform() { ScaleY = 2 };
        };

        Storyboard storyboardUniGridScale = new Storyboard();

        Storyboard.SetTarget(uniGridScaleAnimation, UniGrid);
        Storyboard.SetTargetProperty(uniGridScaleAnimation,
            new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        storyboardUniGridScale.Children.Add(uniGridScaleAnimation);
        storyboardUniGridScale.Begin();
    }

    public async void VisualizeAudio(string? path)
    {
        if (Rendering)
        {
            Rendering = false;
            await Task.Delay(10);
        }
        else
        {
            ShowSeekBarHideBorders();
        }

        List<float> peaks = new List<float>();

        await Render(path, peaks);

        if (peaks.Count == 0)
        {
            return;
        }

        UniGrid.Children.Clear();

        foreach (float peak in peaks)
        {
            UniGrid.Children.Add(new Border()
            {
                CornerRadius = new CornerRadius(2),
                Height = peak,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#673ab7")),
                Opacity = 0.4,
                Margin = new Thickness(1)
            });
        }

        UniGrid_SizeChanged(null, null);
        SeekBar_ValueChanged(null, null);

        HideSeekBarShowBorders();
    }

    private void OnSelectedTrackChanged(MediaFile? selectedTrack)
    {
        if(selectedTrack == null) return;

        CurrentSongName.Text = selectedTrack.Title;
        TimeSpan ts = selectedTrack.Duration;
        TotalTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        CurrentTime.Text = "0:00";
    }

    private void OnDataGridPlay(PlayerState value)
    {
        _playerControlsViewModel.StopTrack();
        PlayButton.Command.Execute(value);
    }

    private async Task Render(string? path, List<float> peaks)
    {
        await Task.Run(() =>
        {
            Rendering = true;

            using Mp3FileReader mp3 = new Mp3FileReader(path);
            int peakCount = 300;

            int bytesPerSample = (mp3.WaveFormat.BitsPerSample / 8) * mp3.WaveFormat.Channels;
            int samplesPerPeak = (int)(mp3.Length / (double)(peakCount * bytesPerSample));
            int bytesPerPeak = bytesPerSample * samplesPerPeak;

            byte[] buffer = new byte[bytesPerPeak];

            for (int x = 0; x < peakCount; x++)
            {
                if (!Rendering)
                {
                    peaks.Clear();

                    return;
                }

                int bytesRead = mp3.Read(buffer, 0, bytesPerPeak);
                if (bytesRead == 0)
                    break;

                float sum = 0;

                for (int n = 0; n < bytesRead; n += 2)
                {
                    if (!Rendering)
                    {
                        peaks.Clear();

                        return;
                    }

                    sum += Math.Abs(BitConverter.ToInt16(buffer, n));
                }

                // ReSharper disable once PossibleLossOfFraction
                float average = sum / (bytesRead / 2);

                peaks.Add(average);
            }

            if (peaks.Count != 0)
            {
                float peaksMax = peaks.Max();
                for (int i = 0; i < peaks.Count; i++)
                {
                    if (!Rendering)
                    {
                        peaks.Clear();

                        return;
                    }

                    peaks[i] = (peaks[i] / peaksMax) * (int)(UniGrid.ActualHeight * 0.95); // peak height

                    if (peaks[i] < 2)
                    {
                        peaks[i] = 2;
                    }
                }
            }

            Rendering = false;
        });
    }

    private void UniGrid_SizeChanged(object? sender, SizeChangedEventArgs? e)
    {
        int n = UniGrid.Children.Count;

        if (n == 0)
        {
            return;
        }

        int k = (int)UniGrid.ActualWidth / 6;

        List<int> numbers = new List<int>();
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

    private void SeekBar_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double>? e)
    {
        double val = SeekBar.Value;
        UIElementCollection borders = UniGrid.Children;

        int before = (int)(borders.Count * val / 100);

        for (int i = 0; i < borders.Count; i++)
        {
            if (i < before)
                ((borders[i] as Border)!).Opacity = 1;
            else
                ((borders[i] as Border)!).Opacity = 0.4;
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        PackIcon? icon = MuteButton.Content as PackIcon;
        double val = VolumeSlider.Value;

        if (val == 0)
        {
            if (icon != null) icon.Kind = PackIconKind.VolumeMute;
        }
        else if (val < 50)
        {
            if (icon != null) icon.Kind = PackIconKind.VolumeMedium;
        }
        else if (val >= 50)
        {
            if (icon != null) icon.Kind = PackIconKind.VolumeHigh;
        }
    }

    //double _mainVolumeSliderBeforeMuteValue;

    //private void MainVolumeButton_Click(object sender, RoutedEventArgs e)
    //{
    //    if (VolumeSlider.Value != 0)
    //    {
    //        _mainVolumeSliderBeforeMuteValue = VolumeSlider.Value;

    //        AnimateVolumeSliderValue(VolumeSlider, 0);
    //    }
    //    else
    //    {
    //        AnimateVolumeSliderValue(VolumeSlider, _mainVolumeSliderBeforeMuteValue);
    //    }
    //}

    //private void ShuffleModeButton_Click(object sender, RoutedEventArgs e)
    //{
    //    _playerControlsViewModel.ShuffleMode = !_playerControlsViewModel.ShuffleMode;
    //}

    //private void AnimateVolumeSliderValue(Slider slider, double newVal)
    //{
    //    DoubleAnimation doubleAnimation = new DoubleAnimation
    //    {
    //        From = slider.Value,
    //        To = newVal,
    //        Duration = TimeSpan.FromMilliseconds(300),
    //        EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
    //    };

    //    slider.BeginAnimation(RangeBase.ValueProperty, doubleAnimation);
    //}

    private void OnEqualizerButton_Click(object sender, RoutedEventArgs e)
    {
        //bool isEqualizerWindowOpen = false;

        //if (_isEqualizerWindowOpen)
        //{
        //    if (_equalizerWin.WindowState == WindowState.Minimized)
        //    {
        //        _equalizerWin.WindowState = WindowState.Normal;
        //    }
        //    return;
        //}

        EqualizerWindow equalizerWindow = new EqualizerWindow
        {
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        //equalizerWindow.Closed += (_, _) => { isEqualizerWindowOpen = false; };
        //equalizerWindow.Closing += (_, _) => { equalizerWindow.Owner = null; };
        //isEqualizerWindowOpen = true;

        equalizerWindow.Show();

        MainWindow win = (MainWindow)Window.GetWindow(this)!;

        if (win.AudioStreamControl.MainMusic!.IsEqualizerWorking)
        {
            equalizerWindow.StartStopText.Content = "Stop";
        }
        else
        {
            equalizerWindow.StartStopText.Content = "Start";
        }

        equalizerWindow.LoadSelectedBand(win.SelectedBandsSettings);

        if (equalizerWindow.StartStopText.Content.Equals("Start"))
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